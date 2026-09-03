using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BanglaHost.App.Services;

/// <summary>
/// Unified global exception logger with rolling file support.
/// Keeps the last 5 crash logs, max 10MB each.
///
/// Diagnostics note: because the app catches almost everything, Windows Error Reporting never
/// sees a fault and therefore never produces a minidump — which is why Store telemetry shows
/// "Uncategorized" with no stack. <see cref="DiagnosticMode"/> deliberately re-enables the
/// crash so WER can bucket it; <see cref="Breadcrumb"/> records what the app was doing so a
/// log without a stack is still actionable.
/// </summary>
public static class CrashLogger
{
    private static readonly string LogDir = Path.Combine(BanglaHost.Core.Paths.Home, "logs");
    private static readonly string CrashLogPath = Path.Combine(LogDir, "appcrash.log");
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private const int MaxLogFiles = 5;
    /// <summary>Marker left by the terminal (AppDomain) handler. The next launch
    /// finds it and can offer to report — the only forensic channel for a fault
    /// that kills the process outright (E5/C9).</summary>
    public static string BreadcrumbPath => Path.Combine(LogDir, "last-crash.json");    private static readonly object _lock = new();

    // Last N things the app did, oldest first. Written into every crash entry so an exception
    // with a useless stack (async continuation, XAML layout) still says what triggered it.
    private const int MaxBreadcrumbs = 24;
    private static readonly Queue<string> _breadcrumbs = new();

    /// <summary>When on, a crash is logged AND rethrown on a background thread so the process
    /// faults for real — WER captures a minidump and Partner Center gets a categorised bucket
    /// instead of "Uncategorized". Off by default: users get a working app, not a crashing one.
    /// Toggle in Settings → Advanced, or set BANGLAHOST_DIAGNOSTIC=1.</summary>
    public static bool DiagnosticMode { get; set; } =
        Environment.GetEnvironmentVariable("BANGLAHOST_DIAGNOSTIC") == "1";

    /// <summary>Record what the app is about to do. Cheap, bounded, never throws.</summary>
    public static void Breadcrumb(string what)
    {
        if (string.IsNullOrEmpty(what)) return;
        try
        {
            lock (_lock)
            {
                _breadcrumbs.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {what}");
                while (_breadcrumbs.Count > MaxBreadcrumbs) _breadcrumbs.Dequeue();
            }
        }
        catch { }
    }

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

                var sb = new StringBuilder();
                sb.Append("=== APP CRASH (").Append(type).Append(") @ ")
                  .Append(DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")).Append(" ===\n");
                sb.Append("Version: ").Append(Updater.CurrentVersion).Append('\n');
                sb.Append("Packaged: ").Append(IsPackaged()).Append('\n');
                sb.Append("OS: ").Append(Environment.OSVersion.VersionString)
                  .Append("  CLR: ").Append(Environment.Version).Append('\n');
                sb.Append("Thread: ").Append(Environment.CurrentManagedThreadId)
                  .Append("  Pool: ").Append(ThreadPoolSummary()).Append('\n');

                // Full exception chain — the top-level message is usually a wrapper
                // ("One or more errors occurred"), and the actual cause is 2-3 levels down.
                var depth = 0;
                for (var e = ex; e != null && depth < 8; e = e.InnerException, depth++)
                {
                    sb.Append(depth == 0 ? "Exception: " : $"  Inner[{depth}]: ")
                      .Append(e.GetType().FullName).Append(": ").Append(e.Message).Append('\n');
                    if (e is System.Runtime.InteropServices.COMException com)
                        sb.Append("    HRESULT: 0x").Append(com.HResult.ToString("X8")).Append('\n');
                    if (!string.IsNullOrEmpty(e.StackTrace))
                        sb.Append("    at:\n").Append(Indent(e.StackTrace)).Append('\n');
                }

                if (ex is AggregateException agg && agg.InnerExceptions.Count > 1)
                {
                    sb.Append("AggregateException with ").Append(agg.InnerExceptions.Count).Append(" inner:\n");
                    foreach (var inner in agg.InnerExceptions.Take(8))
                        sb.Append("  - ").Append(inner.GetType().Name).Append(": ").Append(inner.Message).Append('\n');
                }

                if (_breadcrumbs.Count > 0)
                {
                    sb.Append("Breadcrumbs (oldest first):\n");
                    foreach (var b in _breadcrumbs) sb.Append("  ").Append(b).Append('\n');
                }

                sb.Append("==========================================================\n\n");

                File.AppendAllText(CrashLogPath, sb.ToString());
            }
        }
        catch
        {
            // Absolute worst case fallback - do nothing, we are already crashing
        }

        // Diagnostic mode: make the process actually fault so WER produces a minidump.
        // Done on a pool thread so the log write above has already completed.
        if (DiagnosticMode)
        {
            var captured = ex;
            System.Threading.Tasks.Task.Run(() => Environment.FailFast($"BanglaHost diagnostic mode ({type})", captured));
        }
    }

    /// <summary>Barrier that guarantees any in-flight log write has landed on disk. Called from the
    /// AppDomain handler, where the runtime kills the process the moment the handler returns.</summary>
    public static void Flush()
    {
        try { lock (_lock) { } } catch { }
    }

    /// <summary>Write a crash breadcrumb for the next launch to find (E5/C9).
    /// Never throws — called from the terminal handler on the way down.</summary>
    public static void WriteCrashBreadcrumb(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var crumb = new System.Collections.Generic.Dictionary<string, string?>
            {
                ["whenUtc"] = DateTime.UtcNow.ToString("o"),
                ["version"] = Updater.CurrentVersion,
                ["type"] = ex?.GetType().FullName,
                ["message"] = ex?.Message,
                ["stack"] = ex?.StackTrace,
            };
            File.WriteAllText(BreadcrumbPath,
                System.Text.Json.JsonSerializer.Serialize(crumb));
        }
        catch { }
    }

    /// <summary>Read and clear the breadcrumb from a previous session, if any.</summary>
    public static string? TakeBreadcrumb()
    {
        try
        {
            if (!File.Exists(BreadcrumbPath)) return null;
            var text = File.ReadAllText(BreadcrumbPath);
            try { File.Delete(BreadcrumbPath); } catch { }
            return text;
        }
        catch { return null; }
    }

    private static string Indent(string s) =>
        string.Join("\n", s.Split('\n').Select(l => "      " + l.TrimEnd()));

    private static string ThreadPoolSummary()
    {
        try
        {
            System.Threading.ThreadPool.GetAvailableThreads(out var w, out var io);
            return $"avail={w}/{io} queue={System.Threading.ThreadPool.PendingWorkItemCount} threads={System.Threading.ThreadPool.ThreadCount}";
        }
        catch { return "n/a"; }
    }

    private static string IsPackaged()
    {
        try { return Windows.ApplicationModel.Package.Current?.Id?.Version is { } v ? $"yes {v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : "no"; }
        catch { return "no"; }
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
