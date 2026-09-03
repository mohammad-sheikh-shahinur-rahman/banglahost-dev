using System;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.App.Services;

/// <summary>
/// The single sanctioned way to start fire-and-forget background work.
///
/// Why this exists: <c>AppDomain.CurrentDomain.UnhandledException</c> cannot mark an exception
/// handled on .NET Core — the runtime tears the process down as soon as the handler returns.
/// So a throw on a raw <see cref="Thread"/>, or inside a timer callback, or in a
/// <c>Task.Run</c> nobody awaits, is fatal no matter how good the global handlers are. Routing
/// every background operation through here means the catch is *inside* the work, where it can
/// actually stop the process from dying.
///
/// Replaces the ~167 hand-rolled <c>async void</c> + <c>catch { }</c> pairs, which swallowed the
/// exception without recording it (so the failure was invisible in the crash log too).
/// </summary>
public static class BackgroundWork
{
    /// <summary>Run <paramref name="work"/> on the thread pool. Never throws, always logs.</summary>
    public static Task RunGuarded(Action work, string what)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(() =>
        {
            CrashLogger.Breadcrumb($"bg start: {what}");
            try { work(); }
            catch (OperationCanceledException) { /* normal cancellation */ }
            catch (Exception ex) { CrashLogger.Log(ex, $"BackgroundWork:{what}"); }
        });
    }

    /// <summary>Async variant. Never throws, always logs.</summary>
    public static Task RunGuarded(Func<Task> work, string what)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(async () =>
        {
            CrashLogger.Breadcrumb($"bg start: {what}");
            try { await work().ConfigureAwait(false); }
            catch (OperationCanceledException) { /* normal cancellation */ }
            catch (Exception ex) { CrashLogger.Log(ex, $"BackgroundWork:{what}"); }
        });
    }

    /// <summary>Wrap the body of an <c>async void</c> event handler. Handlers are the only place
    /// <c>async void</c> is legitimate, and every one of them needs exactly this guard — having it
    /// in one place means it cannot be copy-pasted wrong.</summary>
    public static async void Handler(Func<Task> body, string what)
    {
        try { await body().ConfigureAwait(true); }
        catch (OperationCanceledException) { /* normal cancellation */ }
        catch (Exception ex) { CrashLogger.Log(ex, $"Handler:{what}"); }
    }

    /// <summary>A long-running loop that must survive its own failures: logs, backs off, continues.
    /// Used for watchdogs and pollers, which previously died silently on the first exception.</summary>
    public static Task LoopGuarded(Func<CancellationToken, Task> iteration, TimeSpan interval,
                                   string what, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        return Task.Run(async () =>
        {
            var consecutiveFailures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await iteration(ct).ConfigureAwait(false);
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    // Log the first few, then only every 10th — a permanently broken loop must not
                    // fill the disk, but it also must not go completely silent.
                    if (consecutiveFailures <= 3 || consecutiveFailures % 10 == 0)
                        CrashLogger.Log(ex, $"LoopGuarded:{what} (failure #{consecutiveFailures})");
                }

                // Exponential backoff while failing, capped at 30x the normal interval.
                var wait = consecutiveFailures == 0
                    ? interval
                    : TimeSpan.FromMilliseconds(Math.Min(interval.TotalMilliseconds * Math.Pow(2, Math.Min(consecutiveFailures, 5)),
                                                         interval.TotalMilliseconds * 30));
                try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }, ct);
    }
}
