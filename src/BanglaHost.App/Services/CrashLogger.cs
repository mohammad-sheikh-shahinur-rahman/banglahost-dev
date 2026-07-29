using System;
using System.IO;
using System.Linq;

namespace BanglaHost.App.Services;

/// <summary>
/// Unified global exception logger with rolling file support.
/// Keeps the last 5 crash logs, max 10MB each.
/// </summary>
public static class CrashLogger
{
    private static readonly string LogDir = Path.Combine(BanglaHost.Core.Paths.Home, "logs");
    private static readonly string CrashLogPath = Path.Combine(LogDir, "appcrash.log");
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private const int MaxLogFiles = 5;
    private static readonly object _lock = new();

    public static void Log(Exception? ex, string type = "UnhandledException")
    {
        if (ex == null) return;
        
        try
        {
            lock (_lock)
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }

                RotateLogsIfNeeded();

                var msg = $"=== APP CRASH ({type}) @ {DateTime.Now:yyyy-MM-dd hh:mm:ss tt} ===\n" +
                          $"Version: {Updater.CurrentVersion}\n" +
                          $"Exception: {ex.GetType().FullName}\n" +
                          $"Message: {ex.Message}\n\n" +
                          $"StackTrace:\n{ex.StackTrace}\n" +
                          $"==========================================================\n\n";
                          
                File.AppendAllText(CrashLogPath, msg);
            }
        }
        catch 
        { 
            // Absolute worst case fallback - do nothing, we are already crashing
        }
    }

    private static void RotateLogsIfNeeded()
    {
        if (!File.Exists(CrashLogPath)) return;

        var info = new FileInfo(CrashLogPath);
        if (info.Length < MaxFileSize) return;

        // Shift existing logs: appcrash.3.log -> appcrash.4.log
        for (int i = MaxLogFiles - 1; i >= 1; i--)
        {
            string oldPath = i == 1 ? CrashLogPath : Path.Combine(LogDir, $"appcrash.{i - 1}.log");
            string newPath = Path.Combine(LogDir, $"appcrash.{i}.log");

            if (File.Exists(oldPath))
            {
                if (File.Exists(newPath)) File.Delete(newPath);
                File.Move(oldPath, newPath);
            }
        }
    }
}
