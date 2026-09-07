using System.Diagnostics;

namespace BanglaHost.Core;

/// <summary>nginx process lifecycle on Windows (start/stop/reload/test) — analog of the
/// mac engine's <c>nginx_start</c>/<c>nginx_stop</c>/<c>nginx_reload</c>.</summary>
public static class Nginx
{
    private static string NginxDir => Path.Combine(Paths.Home, "nginx");
    private static string ConfPath => Path.Combine(NginxDir, "nginx.conf");
    private static string PidFile  => Path.Combine(Paths.Run, "nginx.pid");

    public static bool Running()
    {
        // pid file is authoritative when valid, but it goes stale across restarts — so
        // fall back to "is any nginx.exe process alive?" (avoids a stale pid skipping reloads).
        try
        {
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid))
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited) return true;
            }
        }
        catch { }
        try { return Process.GetProcessesByName("nginx").Length > 0; } catch { return false; }
    }

    private static (int code, string output) Run(string exe, string[] args, bool wait = true)
    {
        if (!wait)
        {
            // Detached daemon: spawn without redirected-stream collection — nginx
            // daemonizes and never exits, so ReadToEnd-style collection would block
            // forever. The process is job-tracked so it dies with the host.
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Do NOT redirect pipes: nginx daemonizes and nobody drains them.
                // A redirected pipe fills at 4KB and blocks nginx in write() forever.
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var proc = Process.Start(psi)!;
            JobManager.Add(proc);
            return (0, "");
        }
        var res = ProcRunner.Run(exe, args, workingDir: Path.GetDirectoryName(exe), timeoutMs: 60_000);
        return (res.TimedOut ? -1 : res.ExitCode, res.TimedOut ? $"timed out\n{res.All}" : res.All);
    }

    /// <summary>nginx -t: returns (ok, message). Treats "syntax is ok" as success even if the bind probe fails.</summary>
    public static (bool ok, string msg) Test(string exe)
    {
        var (_, outp) = Run(exe, new[] { "-t", "-p", NginxConfig.Fwd(NginxDir), "-c", NginxConfig.Fwd(ConfPath) });
        return (outp.Contains("syntax is ok"), outp);
    }

    public static (bool ok, string msg) Start(Config cfg)
    {
        NginxConfig.RenderMain(cfg);
        var exe = Tools.NginxExe();
        if (exe is null) return (false, "nginx not installed — run: banglahost install nginx");
        if (Running()) return (true, "nginx already running");

        if (!NetUtils.IsPortAvailable(cfg.HttpPort))
            throw new BhException($"Port {cfg.HttpPort} is already in use by another application. Please stop it before starting Nginx.");
        if (cfg.HttpsPort > 0 && !NetUtils.IsPortAvailable(cfg.HttpsPort))
            throw new BhException($"Port {cfg.HttpsPort} (HTTPS) is already in use by another application. Please stop it before starting Nginx.");

        var (ok, msg) = Test(exe);
        if (!ok) return (false, "nginx config test failed:\n" + msg);

        // Launch detached. nginx daemonizes on Windows and writes its own pid file.
        Run(exe, new[] { "-p", NginxConfig.Fwd(NginxDir), "-c", NginxConfig.Fwd(ConfPath) }, wait: false);
        for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(300);
        return Running() ? (true, "nginx started") : (false, "nginx failed to start (see logs/nginx-error.log)");
    }

    public static void Stop()
    {
        var exe = Tools.NginxExe();
        if (exe is not null && Running())
            Run(exe, new[] { "-s", "stop", "-p", NginxConfig.Fwd(NginxDir), "-c", NginxConfig.Fwd(ConfPath) });
        // Fallback: kill by pid if -s stop didn't clear it.
        try
        {
            if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid))
                BanglaHost.Core.ProcessUtils.KillSafeChecked(pid, "nginx", "nginx.exe");
        }
        catch { }

        // `-s stop` is asynchronous and depends on a valid pid file — if either falls short the
        // old master keeps running, Stop() returns "done", and the next Start() sees Running()==true
        // and no-ops. Then a cert/config change never takes effect (only a manual stop-all +
        // start-all worked). So GUARANTEE nginx is actually gone: wait briefly for a graceful exit,
        // then force-kill any BanglaHost nginx.exe still alive, and block until none remain.
        for (var i = 0; i < 20 && Running(); i++) System.Threading.Thread.Sleep(150);
        KillStrayNginx(exe);
        for (var i = 0; i < 20 && Running(); i++) System.Threading.Thread.Sleep(100);
        try { File.Delete(PidFile); } catch { }
    }

    /// <summary>Force-kill any lingering BanglaHost nginx.exe (matched by install path so an unrelated
    /// nginx elsewhere on the machine is left alone; if the path can't be read it's almost certainly
    /// ours, so kill it).</summary>
    private static void KillStrayNginx(string? ourExe)
    {
        foreach (var p in Process.GetProcessesByName("nginx"))
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                var ours = ourExe is null || path is null
                    || string.Equals(path, ourExe, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(Paths.Home, StringComparison.OrdinalIgnoreCase);
                if (ours) p.Kill(true);
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    public static void Reload(Config cfg)
    {
        NginxConfig.RenderMain(cfg);
        var exe = Tools.NginxExe();
        if (exe is null || !Running()) return;
        Run(exe, new[] { "-s", "reload", "-p", NginxConfig.Fwd(NginxDir), "-c", NginxConfig.Fwd(ConfPath) });
    }
}
