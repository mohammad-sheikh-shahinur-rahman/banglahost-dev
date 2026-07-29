using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>Ollama (port 11434) — local LLM runner.</summary>
public static class Ollama
{
    public const int Port = 11434;
    private static string RunFile => Path.Combine(Paths.Run, "ollama.json");

    public static bool Running()
    {
        try { using var c = new TcpClient(); return c.ConnectAsync("127.0.0.1", Port).Wait(500) && c.Connected; }
        catch { return false; }
    }

    public static bool Start()
    {
        if (Running()) return true;
        var exe = Tools.OllamaExe();
        if (exe is null) return false;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "serve",
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["OLLAMA_HOST"] = $"127.0.0.1:{Port}";
        var p = Process.Start(psi);
        if (p is null) return false;
        JobManager.Add(p);
        Directory.CreateDirectory(Paths.Run);
        File.WriteAllText(RunFile, JsonSerializer.Serialize(new { pid = p.Id, port = Port }));
        for (var i = 0; i < 15 && !Running(); i++) System.Threading.Thread.Sleep(400);
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
