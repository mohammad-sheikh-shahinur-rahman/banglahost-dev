using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.Core;

/// <summary>Outcome of a child-process run.</summary>
public sealed record ProcResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Ok => !TimedOut && ExitCode == 0;

    /// <summary>Combined output, for logging. StdErr last so the error is the thing you see.</summary>
    public string All => string.Join("\n", new[] { StdOut, StdErr }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// The one correct way to run a child process and collect its output.
///
/// Two bugs this exists to make unrepeatable:
///
/// 1. <b>Unbounded waits.</b> 26 of 31 <c>WaitForExit()</c> calls passed no timeout. Any child that
///    hung — a php-cgi with a broken ini, mysqldump on a locked table, curl against a black-holed
///    host — hung BanglaHost with it, forever, with no way out but Task Manager.
///
/// 2. <b>Pipe deadlock.</b> The old pattern read <c>StandardError.ReadToEnd()</c> to completion and
///    only then read stdout. A child that fills the 4 KB stdout pipe buffer blocks in <c>write()</c>
///    and never exits; the parent is blocked reading stderr and never drains stdout. Neither side
///    moves. Both streams must be drained concurrently, which is what BeginOutputReadLine-style
///    async reads do here.
///
/// Always uses <see cref="ProcessStartInfo.ArgumentList"/> — the runtime applies Windows CRT
/// quoting per element, so no caller can accidentally create an injectable command line.
/// </summary>
public static class ProcRunner
{
    public const int DefaultTimeoutMs = 60_000;

    /// <summary>Run a process to completion, or kill it at <paramref name="timeoutMs"/>.</summary>
    /// <param name="stdin">Written to the child's stdin then closed. Use this instead of a shell
    /// <c>&lt;</c> redirect — with UseShellExecute=false there is no shell, so <c>&lt;</c> is passed
    /// to the program as a literal argument (which is why SQL Studio never ran a query).</param>
    public static async Task<ProcResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        string? workingDir = null,
        int timeoutMs = DefaultTimeoutMs,
        string? stdin = null,
        IDictionary<string, string>? env = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exe)) throw new ArgumentException("exe required", nameof(exe));

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
        foreach (var a in args ?? Array.Empty<string>()) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        // Signalled when the child closes each stream. Waiting on these (not just on exit)
        // guarantees we have every byte — Process.Exited can fire before the readers drain.
        var outDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { outDone.TrySetResult(true); return; }
            stdout.Append(e.Data).Append('\n');
            if (onOutputLine is not null) { try { onOutputLine(e.Data); } catch { } }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { errDone.TrySetResult(true); return; }
            stderr.Append(e.Data).Append('\n');
            if (onErrorLine is not null) { try { onErrorLine(e.Data); } catch { } }
        };

        if (!proc.Start())
            return new ProcResult(-1, "", $"failed to start {exe}", false);

        // Both readers started before any wait — this is the fix for the deadlock.
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin is not null)
        {
            try
            {
                await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
                await proc.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            catch { /* child may have exited already */ }
            finally { try { proc.StandardInput.Close(); } catch { } }
        }

        var timedOut = false;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            if (timeoutMs > 0) timeoutCts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                KillTree(proc);
                if (ct.IsCancellationRequested)
                {
                    await DrainBriefly(outDone, errDone).ConfigureAwait(false);
                    throw;
                }
            }
        }

        // Give the readers a moment to flush whatever the child managed to emit. Bounded, because
        // a grandchild inheriting the pipe can hold it open after the parent dies.
        await DrainBriefly(outDone, errDone).ConfigureAwait(false);

        var exit = -1;
        try { exit = proc.ExitCode; } catch { }
        if (timedOut) stderr.Append($"[timeout] {exe} killed after {timeoutMs} ms\n");

        return new ProcResult(exit, stdout.ToString().TrimEnd('\n'), stderr.ToString().TrimEnd('\n'), timedOut);
    }

    /// <summary>Synchronous wrapper, for the CLI and other genuinely synchronous callers.
    /// Never call from the UI thread.</summary>
    public static ProcResult Run(
        string exe,
        IEnumerable<string> args,
        string? workingDir = null,
        int timeoutMs = DefaultTimeoutMs,
        string? stdin = null,
        IDictionary<string, string>? env = null)
        => RunAsync(exe, args, workingDir, timeoutMs, stdin, env).GetAwaiter().GetResult();

    /// <summary>Wait for a process the caller started elsewhere, with a real timeout.
    /// Returns false if the timeout elapsed (and the tree was killed).</summary>
    public static async Task<bool> WaitOrKillAsync(Process proc, int timeoutMs, CancellationToken ct = default)
    {
        if (proc is null) return true;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            KillTree(proc);
            return false;
        }
        catch { return true; }
    }

    /// <summary>Kill a process and everything it spawned. A bare Kill() leaves grandchildren
    /// running — which is how orphaned php-cgi and mysqld processes survived a "stop".</summary>
    public static void KillTree(Process proc)
    {
        if (proc is null) return;
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* already gone, or access denied on a process we no longer own */ }
    }

    private static async Task DrainBriefly(TaskCompletionSource<bool> outDone, TaskCompletionSource<bool> errDone)
    {
        try
        {
            await Task.WhenAny(
                Task.WhenAll(outDone.Task, errDone.Task),
                Task.Delay(2_000)).ConfigureAwait(false);
        }
        catch { }
    }
}
