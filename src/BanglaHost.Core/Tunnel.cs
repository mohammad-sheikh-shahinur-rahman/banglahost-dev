using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BanglaHost.Core;

/// <summary>
/// Public sharing via Cloudflare quick tunnels (no account needed) � the analog of
/// the mac engine's <c>tunnel</c>. cloudflared writes to a log file (via cmd redirect
/// so it survives the CLI exiting); we poll it for the https://*.trycloudflare.com URL.
/// </summary>
public static class Tunnel
{
    private static string PidFile(string n) => Path.Combine(Paths.Run, $"tunnel-{n}.pid");
    private static string LogFile(string n) => Path.Combine(Paths.Run, $"tunnel-{n}.log");
    private static string UrlFile(string n) => Path.Combine(Paths.Run, $"tunnel-{n}.url");

    public static bool Running(string name)
    {
        try
        {
            if (!File.Exists(PidFile(name))) return false;
            if (!int.TryParse(File.ReadAllText(PidFile(name)).Trim(), out var pid)) return false;
            if (!BanglaHost.Core.ProcessUtils.IsRunning(pid)) return false;
            // Windows reuses PIDs aggressively — a stale pidfile can match some unrelated
            // process. Confirm this PID is actually cloudflared before believing it.
            try
            {
                var p = Process.GetProcessById(pid);
                var n = p.ProcessName;
                return n.Equals("cloudflared", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        catch { }
        return false;
    }

    public static string? Url(string name)
    {
        try { return File.Exists(UrlFile(name)) ? File.ReadAllText(UrlFile(name)).Trim() : null; }
        catch { return null; }
    }

    /// <summary>Read a file that another process holds open for writing (cloudflared's log).
    /// The default File.ReadAllText opens with FileShare.Read, which on Windows throws because
    /// cloudflared (via the `cmd > log` redirect) holds the log open for writing � so the URL is
    /// never seen and the tunnel reports "no URL yet". Opening with FileShare.ReadWrite fixes it.
    /// (POSIX allows the concurrent read, which is why the mac engine never hit this.)</summary>
    private static string ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return ""; }
    }

    public static (bool ok, string msg) Start(string name, string domain, string origin)
    {
        var cf = Tools.CloudflaredExe();
        if (cf is null) return (false, "cloudflared not installed � banglahost tunnel install");
        if (Running(name))
        {
            // If cloudflared is genuinely still up AND we have a URL from the previous run,
            // reuse it. Otherwise the previous session is a zombie (URL file missing, or the
            // pid file is stale but the name check happened to pass) — tear it down and start
            // fresh instead of returning "already running" with no URL.
            var existing = Url(name);
            if (!string.IsNullOrEmpty(existing)) return (true, existing);
            Stop(name);
        }
        // Earlier builds ran cloudflared through `cmd /c cloudflared > log` and stored cmd.exe's
        // PID — so the orphaned cloudflared child could survive a Stop() and keep the log file
        // locked (Windows opens files exclusive-write by default). Sweep any cloudflared.exe
        // whose module path is *our* installed copy before we touch the log.
        KillOrphanCloudflared(cf);
        // Belt-and-braces: even after Stop, wipe leftover state so nothing lingers.
        try { File.Delete(PidFile(name)); } catch { }
        Directory.CreateDirectory(Paths.Run);
        try { File.Delete(UrlFile(name)); } catch { }
        var logPath = LogFile(name);
        // Log may still be locked briefly after the kill — retry a few times, then fall back
        // to a fresh timestamped filename so a stubborn handle never blocks a share attempt.
        if (!TryResetLog(ref logPath, name))
            return (false, "log file locked and could not be released: " + logPath);

        // Spawn cloudflared directly (NOT through `cmd > log`) — the cmd redirect buffers with
        // ~4 KiB flushes, so the quick-tunnel URL banner doesn't hit the log for many seconds
        // and our poll times out with "no URL yet". Pumping stdout/stderr ourselves through
        // OutputDataReceived flushes each line immediately and lets us capture the URL from
        // memory the moment cloudflared prints it. Everything still lands in the log for
        // diagnostics.
        var args = new List<string>
        {
            "tunnel", "--no-autoupdate",
            "--url", origin,
            "--http-host-header", domain,
        };
        if (origin.StartsWith("https", StringComparison.OrdinalIgnoreCase)) args.Add("--no-tls-verify");

        var psi = new ProcessStartInfo
        {
            FileName = cf,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding  = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var urlRx = new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled);
        var urlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logLock = new object();
        void OnLine(string? s)
        {
            if (s is null) return;
            try { lock (logLock) File.AppendAllText(logPath, s + Environment.NewLine); } catch { }
            var m = urlRx.Match(s);
            if (m.Success) urlTcs.TrySetResult(m.Value);
        }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, ev) => OnLine(ev.Data);
        proc.ErrorDataReceived  += (_, ev) => OnLine(ev.Data);
        proc.Exited += (_, _) => urlTcs.TrySetException(new Exception("cloudflared exited"));
        try
        {
            if (!proc.Start()) return (false, "failed to launch cloudflared");
        }
        catch (Exception ex) { return (false, "failed to launch cloudflared: " + ex.Message); }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        File.WriteAllText(PidFile(name), proc.Id.ToString());

        try
        {
            if (urlTcs.Task.Wait(TimeSpan.FromSeconds(45)))
            {
                var url = urlTcs.Task.Result;
                File.WriteAllText(UrlFile(name), url);
                return (true, url);
            }
        }
        catch (AggregateException ae) when (ae.InnerException is not null)
        {
            return (false, "tunnel exited early — see " + logPath);
        }
        // Timed out waiting for the URL but the process is still alive — leave it running so
        // the UI can pick up the URL on the next refresh, and re-scan the on-disk log once in
        // case the URL landed between the last stream flush and our timeout.
        try
        {
            var late = urlRx.Match(ReadShared(logPath));
            if (late.Success)
            {
                File.WriteAllText(UrlFile(name), late.Value);
                return (true, late.Value);
            }
        }
        catch { }
        return (true, "tunnel up but no URL yet — check " + logPath);
    }

    public static void Stop(string name)
    {
        try
        {
            if (File.Exists(PidFile(name)) && int.TryParse(File.ReadAllText(PidFile(name)).Trim(), out var pid))
                BanglaHost.Core.ProcessUtils.KillSafe(pid);
        }
        catch { }
        try { File.Delete(PidFile(name)); File.Delete(UrlFile(name)); } catch { }
    }

    /// <summary>Kill any cloudflared.exe whose module path is our installed copy — catches the
    /// zombies left behind by pre-1.4.4 builds that wrapped cloudflared in cmd /c and lost
    /// track of the child PID. Best-effort; failures are swallowed because they usually mean
    /// "already dead" or "no permission" (in which case Windows will still deny our write).</summary>
    private static void KillOrphanCloudflared(string ourExe)
    {
        try
        {
            var target = Path.GetFullPath(ourExe);
            foreach (var p in Process.GetProcessesByName("cloudflared"))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path is null) continue;
                    if (!string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
                catch { /* access-denied or race; keep going */ }
                finally { p.Dispose(); }
            }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // Pre-1.4.4 spawned `cmd.exe /c cloudflared ... > log`. The cmd process can outlive
                    // cloudflared if it was terminated abruptly, holding a lock on the log file.
                    var script = $"Get-CimInstance Win32_Process -Filter \\\"Name='cmd.exe' AND CommandLine LIKE '%cloudflared.exe%'\\\" | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}";
                    var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    };
                    Process.Start(psi)?.WaitForExit(3000);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Delete + recreate the log file. If Windows still has the old handle open we
    /// retry briefly, then swap to a fresh timestamped filename so an orphaned handle can't
    /// permanently break sharing.</summary>
    private static bool TryResetLog(ref string logPath, string name)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(logPath)) File.Delete(logPath);
                File.WriteAllText(logPath, "");
                return true;
            }
            catch (IOException) { System.Threading.Thread.Sleep(150); }
            catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(150); }
        }
        try
        {
            var alt = Path.Combine(Paths.Run,
                $"tunnel-{name}-{DateTime.Now:yyyyMMddHHmmss}.log");
            File.WriteAllText(alt, "");
            logPath = alt;
            return true;
        }
        catch { return false; }
    }

    public static IEnumerable<(string name, string? url)> List()
    {
        if (!Directory.Exists(Paths.Run)) yield break;
        foreach (var f in Directory.EnumerateFiles(Paths.Run, "tunnel-*.pid"))
        {
            var name = Path.GetFileNameWithoutExtension(f)["tunnel-".Length..];
            if (Running(name)) yield return (name, Url(name));
        }
    }
}
