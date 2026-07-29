using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public enum QueueDriver { Database, Redis, Beanstalkd }

public record QueueJob(string Id, string Queue, string Payload, string Status, DateTime CreatedAt);

public static class QueueService
{
    public static async Task<bool> IsRedisRunningAsync()
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "ping",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output.Trim().Equals("PONG", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static async Task<string> GetRedisInfoAsync()
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return "Redis CLI not found.";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "Failed to start redis-cli.";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    public static async Task<List<string>> GetRedisQueuesAsync()
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return new List<string>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "keys queues:*",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return new List<string>();
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return new List<string>(lines);
        }
        catch { return new List<string>(); }
    }

    public static async Task<long> GetQueueLengthAsync(string queueName)
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return 0;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"llen {queueName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return 0;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return long.TryParse(output.Trim(), out var len) ? len : 0;
        }
        catch { return 0; }
    }

    public static async Task<bool> FlushQueueAsync(string queueName)
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"del {queueName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
