using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>Shared start/stop/running for the simple TCP cache daemons (Redis, Memcached).</summary>
internal static class CacheProc
{
    public static bool PortOpen(int port)
    {
        return NetUtils.IsListening(port, 500);
    }

    public static bool Start(string runName, string? exe, string args, int port)
    {
        if (exe is null) return false;
        if (PortOpen(port)) return true;
        if (!NetUtils.IsPortAvailable(port)) throw new BhException($"Port {port} is already in use by another application. Please stop it before starting {runName}.");
        var psi = new ProcessStartInfo
        {
            FileName = exe, Arguments = args,
            UseShellExecute = false, CreateNoWindow = true,
            // Do NOT redirect daemon pipes: nobody drains them and the child blocks
            // once the 4KB pipe fills (same reason DbServer avoids redirect).
            RedirectStandardOutput = false, RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        var p = Process.Start(psi);
        if (p is null) return false;
        JobManager.Add(p);
        // Don't dispose — let JobManager own the process lifetime
        Directory.CreateDirectory(Paths.Run);
        File.WriteAllText(Path.Combine(Paths.Run, $"{runName}.json"), JsonSerializer.Serialize(new { pid = p.Id, port }));
        for (var i = 0; i < 12 && !PortOpen(port); i++) System.Threading.Thread.Sleep(250);
        return PortOpen(port);
    }

    public static void Stop(string runName)
    {
        var f = Path.Combine(Paths.Run, $"{runName}.json");
        try
        {
            if (File.Exists(f))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                BanglaHost.Core.ProcessUtils.KillSafeChecked(
                    doc.RootElement.GetProperty("pid").GetInt32(),
                    runName, runName + ".exe", "redis-server", "memcached", "valkey-server");
            }
        }
        catch { }
        try { File.Delete(f); } catch { }
    }
}

/// <summary>Redis (port 6379). Dev mode: no persistence (--save ""). Bound to loopback only
/// (+ protected-mode) so an unauthenticated Redis is never reachable from the LAN.</summary>
public static class Redis
{
    public const int Port = 6379;
    public static bool Running() => CacheProc.PortOpen(Port);
    public static bool Start() => CacheProc.Start("redis", Tools.RedisServerExe(),
        $"--bind 127.0.0.1 --protected-mode yes --port {Port} --save \"\"", Port);
    public static void Stop() => CacheProc.Stop("redis");
}

/// <summary>Memcached (port 11211). Bound to loopback (-l) with UDP disabled (-U 0) so the
/// unauthenticated cache isn't LAN-readable or usable as a UDP amplification reflector.</summary>
public static class Memcached
{
    public const int Port = 11211;
    public static bool Running() => CacheProc.PortOpen(Port);
    public static bool Start() => CacheProc.Start("memcached", Tools.MemcachedExe(),
        $"-l 127.0.0.1 -U 0 -p {Port} -m 64", Port);
    public static void Stop() => CacheProc.Stop("memcached");
}

/// <summary>Valkey (port 6380) — Redis-compatible fork. Bound to loopback only.</summary>
public static class Valkey
{
    public const int Port = 6380;
    public static bool Running() => CacheProc.PortOpen(Port);
    public static bool Start() => CacheProc.Start("valkey", Tools.ValkeyExe(),
        $"--bind 127.0.0.1 --protected-mode yes --port {Port} --save \"\"", Port);
    public static void Stop() => CacheProc.Stop("valkey");
}
