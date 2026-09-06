using System;
using System.Diagnostics;

namespace BanglaHost.Core
{
    public static class ProcessUtils
    {
        public static bool IsRunning(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static void KillSafe(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                if (!p.HasExited) p.Kill(true);
            }
            catch (ArgumentException)
            {
                // Process is already gone
            }
            catch (InvalidOperationException)
            {
                // Process is already gone
            }
            catch (Exception)
            {
                // Ignore other errors like AccessDenied
            }
        }

        /// <summary>Kill only when the PID still belongs to one of the expected process names.
        /// Windows recycles PIDs aggressively — a stale pidfile can otherwise kill an unrelated
        /// app (e.g. a recycled node PID now owned by notepad). Name check is case-insensitive,
        /// ".exe" suffix optional.</summary>
        public static void KillSafeChecked(int pid, params string[] expectedNames)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return;
                if (expectedNames is { Length: > 0 })
                {
                    string actual;
                    try { actual = p.ProcessName; } catch { return; }
                    var ok = false;
                    foreach (var want in expectedNames)
                    {
                        var w = (want ?? "").Trim();
                        if (w.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) w = w[..^4];
                        if (actual.Equals(w, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    }
                    if (!ok) return;
                }
                p.Kill(true);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Exception) { }
        }
    }
}
