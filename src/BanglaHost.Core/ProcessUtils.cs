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
                if (!p.HasExited) p.Kill();
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
    }
}
