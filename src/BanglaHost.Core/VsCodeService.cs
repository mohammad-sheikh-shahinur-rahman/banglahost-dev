using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public static class VsCodeService
{
    private static string FindCodeExe()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            "code"
        };
        foreach (var p in paths)
        {
            if (p == "code" || File.Exists(p)) return p;
        }
        return "code";
    }

    public static async Task<bool> IsInstalledAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindCodeExe(),
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && output.Contains(".");
        }
        catch { return false; }
    }

    public static bool OpenProject(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindCodeExe(),
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> InstallExtensionAsync(string extensionId, Action<string> log)
    {
        log($"Installing VS Code extension: {extensionId}...");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindCodeExe(),
                Arguments = $"--install-extension {extensionId}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            log(output);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            log($"Failed to install {extensionId}: {ex.Message}");
            return false;
        }
    }
}
