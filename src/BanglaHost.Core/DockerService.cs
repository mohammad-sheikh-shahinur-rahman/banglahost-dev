using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record DockerContainer(string Id, string Name, string Image, string Status, string Ports, string Created);
public record DockerImage(string Id, string Repository, string Tag, string Size, string Created);

public static class DockerService
{
    private static string DockerExe => FindDockerExe();

    private static string FindDockerExe()
    {
        // Check common paths
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Docker", "Docker", "resources", "bin", "docker.exe"),
            "docker" // fallback to PATH
        };
        return paths.FirstOrDefault(File.Exists) ?? "docker";
    }

    public static async Task<bool> IsDockerInstalledAsync()
    {
        try
        {
            var output = await RunDockerAsync("version --format '{{.Server.Version}}'");
            return !string.IsNullOrWhiteSpace(output) && !output.Contains("error", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static async Task<bool> IsDockerRunningAsync()
    {
        try
        {
            var output = await RunDockerAsync("info --format '{{.ServerVersion}}'");
            return !string.IsNullOrWhiteSpace(output) && !output.Contains("error", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static async Task<List<DockerContainer>> ListContainersAsync(bool all = true)
    {
        var args = all ? "ps -a --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}|{{.CreatedAt}}\"" 
                       : "ps --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}|{{.CreatedAt}}\"";
        var output = await RunDockerAsync(args);
        var list = new List<DockerContainer>();
        
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length >= 5)
            {
                list.Add(new DockerContainer(
                    Id: parts[0],
                    Name: parts[1],
                    Image: parts[2],
                    Status: parts[3],
                    Ports: parts[4],
                    Created: parts.Length > 5 ? parts[5] : ""
                ));
            }
        }
        return list;
    }

    public static async Task<List<DockerImage>> ListImagesAsync()
    {
        var output = await RunDockerAsync("images --format \"{{.ID}}|{{.Repository}}|{{.Tag}}|{{.Size}}|{{.CreatedAt}}\"");
        var list = new List<DockerImage>();
        
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length >= 4)
            {
                list.Add(new DockerImage(
                    Id: parts[0],
                    Repository: parts[1],
                    Tag: parts[2],
                    Size: parts[3],
                    Created: parts.Length > 4 ? parts[4] : ""
                ));
            }
        }
        return list;
    }

    public static async Task<string> StartContainerAsync(string nameOrId)
        => await RunDockerAsync($"start {nameOrId}");

    public static async Task<string> StopContainerAsync(string nameOrId)
        => await RunDockerAsync($"stop {nameOrId}");

    public static async Task<string> RemoveContainerAsync(string nameOrId)
        => await RunDockerAsync($"rm -f {nameOrId}");

    public static async Task<string> GetContainerLogsAsync(string nameOrId, int tail = 100)
        => await RunDockerAsync($"logs --tail {tail} {nameOrId}");

    public static async Task<string> PullImageAsync(string image, Action<string> log)
    {
        log($"Pulling image: {image}...");
        var output = await RunDockerAsync($"pull {image}");
        log(output);
        return output;
    }

    public static async Task<string> RunContainerAsync(string image, string name, string ports = "", string extraArgs = "")
    {
        var portArg = string.IsNullOrEmpty(ports) ? "" : $"-p {ports}";
        var nameArg = string.IsNullOrEmpty(name) ? "" : $"--name {name}";
        return await RunDockerAsync($"run -d {nameArg} {portArg} {extraArgs} {image}");
    }

    public static async Task<string> ComposeUpAsync(string composePath, Action<string> log)
    {
        if (!File.Exists(composePath))
        {
            log($"docker-compose.yml not found at {composePath}");
            return "File not found.";
        }

        var dir = Path.GetDirectoryName(composePath) ?? ".";
        log($"Running docker compose up in {dir}...");

        var psi = new ProcessStartInfo
        {
            FileName = DockerExe,
            Arguments = "compose up -d",
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "Failed to start docker.";
            
            p.OutputDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? "Success" : "Failed";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static async Task<string> RunDockerAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = DockerExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "Failed to start docker.";
            var output = await p.StandardOutput.ReadToEndAsync();
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return string.IsNullOrEmpty(output) ? err : output;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
