using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record CacheStats(long KeyCount, long MemoryUsedBytes, long HitCount, long MissCount, double HitRatio, string Uptime);

public static class CacheService
{
    // ─── Redis Cache ─────────────────────────────────────
    public static async Task<CacheStats> GetRedisCacheStatsAsync()
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return new CacheStats(0, 0, 0, 0, 0, "N/A");

        try
        {
            var info = await RunRedisCommandAsync("info");
            long keys = ParseInfoLong(info, "db0:keys=");
            long mem = ParseInfoLong(info, "used_memory:");
            long hits = ParseInfoLong(info, "keyspace_hits:");
            long misses = ParseInfoLong(info, "keyspace_misses:");
            var uptime = ParseInfoString(info, "uptime_in_seconds:");
            double hitRatio = (hits + misses) > 0 ? (double)hits / (hits + misses) * 100.0 : 0;
            
            return new CacheStats(keys, mem, hits, misses, hitRatio, $"{int.Parse(uptime) / 3600}h {int.Parse(uptime) % 3600 / 60}m");
        }
        catch { return new CacheStats(0, 0, 0, 0, 0, "Error"); }
    }

    public static async Task<bool> FlushRedisCacheAsync()
    {
        var result = await RunRedisCommandAsync("flushall");
        return result.Contains("OK");
    }

    public static async Task<string> GetRedisKeyValueAsync(string key)
    {
        return await RunRedisCommandAsync($"get {key}");
    }

    public static async Task<List<string>> GetRedisKeysAsync(string pattern = "*")
    {
        var result = await RunRedisCommandAsync($"keys {pattern}");
        var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return new List<string>(lines);
    }

    public static async Task<bool> DeleteRedisKeyAsync(string key)
    {
        var result = await RunRedisCommandAsync($"del {key}");
        return result.Trim() != "0";
    }

    // ─── OPCache (PHP) ───────────────────────────────────
    public static async Task<string> GetOpcacheStatusAsync()
    {
        // Call php -r to get opcache status
        var php = Tools.PhpExe(Config.Load().DefaultPhp);
        if (string.IsNullOrEmpty(php) || !File.Exists(php)) return "PHP not found.";

        var psi = new ProcessStartInfo
        {
            FileName = php,
            Arguments = "-r \"echo json_encode(opcache_get_status(false));\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "Failed to start PHP.";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    public static async Task<bool> ResetOpcacheAsync()
    {
        var php = Tools.PhpExe(Config.Load().DefaultPhp);
        if (string.IsNullOrEmpty(php) || !File.Exists(php)) return false;

        var psi = new ProcessStartInfo
        {
            FileName = php,
            Arguments = "-r \"opcache_reset(); echo 'OK';\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output.Contains("OK");
        }
        catch { return false; }
    }

    // ─── Helpers ─────────────────────────────────────────
    private static async Task<string> RunRedisCommandAsync(string command)
    {
        var exe = Path.Combine(Paths.Bin, "redis", "redis-cli.exe");
        if (!File.Exists(exe)) return "";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    private static long ParseInfoLong(string info, string key)
    {
        var idx = info.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        var start = idx + key.Length;
        var end = info.IndexOfAny(new[] { '\r', '\n', ',' }, start);
        if (end < 0) end = info.Length;
        return long.TryParse(info[start..end], out var val) ? val : 0;
    }

    private static string ParseInfoString(string info, string key)
    {
        var idx = info.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "0";
        var start = idx + key.Length;
        var end = info.IndexOfAny(new[] { '\r', '\n' }, start);
        if (end < 0) end = info.Length;
        return info[start..end].Trim();
    }
}
