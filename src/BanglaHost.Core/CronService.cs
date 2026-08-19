using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace BanglaHost.Core;

public static class CronService
{
    private static readonly ConcurrentDictionary<string, Process> _workers = new();

    public static bool IsRunning(string siteName, string type)
    {
        var key = $"{siteName}_{type}";
        if (_workers.TryGetValue(key, out var p))
        {
            if (!p.HasExited) return true;
            _workers.TryRemove(key, out _);
        }
        return false;
    }

    public static void StartWorker(string siteName, string root, string phpExe, string type)
    {
        var key = $"{siteName}_{type}";
        if (IsRunning(siteName, type)) return;

        var cmd = type == "queue" ? "queue:work" : "schedule:work";
        
        var psi = new ProcessStartInfo
        {
            FileName = phpExe,
            Arguments = $"artisan {cmd}",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();
        _workers[key] = p;
    }

    public static void StopWorker(string siteName, string type)
    {
        var key = $"{siteName}_{type}";
        if (_workers.TryGetValue(key, out var p))
        {
            try { p.Kill(true); } catch { }
            _workers.TryRemove(key, out _);
        }
    }
}
