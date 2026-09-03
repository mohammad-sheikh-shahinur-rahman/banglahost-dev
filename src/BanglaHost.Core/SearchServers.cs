using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>Meilisearch (port 7700) — instant search engine. No auth in dev mode.</summary>
public static class Meilisearch
{
    public const int Port = 7700;
    private static string RunFile => Path.Combine(Paths.Run, "meilisearch.json");

    public static bool Running()
    {
        return NetUtils.IsListening(Port, 500);
    }

    public static bool Start()
    {
        if (Running()) return true;
        var exe = Tools.MeilisearchExe();
        if (exe is null) return false;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--http-addr 127.0.0.1:{Port} --env development",
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        var p = Process.Start(psi);

        if (p is null) return false;

        JobManager.Add(p);

        Directory.CreateDirectory(Paths.Run);
        File.WriteAllText(RunFile, JsonSerializer.Serialize(new { pid = p.Id, port = Port }));
        for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(300);
        return Running();
    }

    public static void Stop()
    {
        try
        {
            if (File.Exists(RunFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(RunFile));
                BanglaHost.Core.ProcessUtils.KillSafe(doc.RootElement.GetProperty("pid").GetInt32());
            }
        }
        catch { }
        try { File.Delete(RunFile); } catch { }
    }
}
