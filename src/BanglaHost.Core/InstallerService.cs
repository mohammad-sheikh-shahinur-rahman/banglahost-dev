using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public static class InstallerService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    public static async Task<bool> InstallAppAsync(string appName, string targetDir, Action<string> log)
    {
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        switch (appName.ToLower())
        {
            case "wordpress":
                return await InstallWordPressAsync(targetDir, log);
            case "laravel":
                return await InstallLaravelAsync(targetDir, log);
            case "whmcs":
                return InstallWhmcsMock(targetDir, log); // requires manual download usually, we mock the stub
            case "next.js":
                return await InstallNextJsAsync(targetDir, log);
            case "vue.js":
                return await InstallVueAsync(targetDir, log);
            case "prestashop":
                log("PrestaShop installer via Composer...");
                return await RunCommandAsync("composer", $"create-project prestashop/prestashop \"{targetDir}\"", targetDir, log);
            default:
                log($"No installer defined for {appName}.");
                return false;
        }
    }

    private static async Task<bool> InstallWordPressAsync(string dir, Action<string> log)
    {
        log("Downloading WordPress...");
        try
        {
            var bytes = await _http.GetByteArrayAsync("https://wordpress.org/latest.zip");
            var zipPath = Path.Combine(dir, "wp.zip");
            await File.WriteAllBytesAsync(zipPath, bytes);
            log("Extracting WordPress...");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dir, true);
            File.Delete(zipPath);
            
            // Move contents from 'wordpress' folder to root
            var wpDir = Path.Combine(dir, "wordpress");
            if (Directory.Exists(wpDir))
            {
                foreach (var f in Directory.GetFiles(wpDir)) File.Move(f, Path.Combine(dir, Path.GetFileName(f)), true);
                foreach (var d in Directory.GetDirectories(wpDir)) Directory.Move(d, Path.Combine(dir, Path.GetFileName(d)));
                Directory.Delete(wpDir);
            }
            log("WordPress installed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            log($"Failed to install WordPress: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> InstallLaravelAsync(string dir, Action<string> log)
    {
        log("Installing Laravel via Composer...");
        return await RunCommandAsync("composer", $"create-project laravel/laravel \"{dir}\" --no-interaction", dir, log);
    }

    private static async Task<bool> InstallNextJsAsync(string dir, Action<string> log)
    {
        log("Installing Next.js via npx...");
        return await RunCommandAsync("npx", $"-y create-next-app@latest \"{dir}\" --use-npm --js --tailwind --eslint --app --src-dir --import-alias \"@/*\"", dir, log);
    }

    private static async Task<bool> InstallVueAsync(string dir, Action<string> log)
    {
        log("Installing Vue via npm...");
        return await RunCommandAsync("npm", $"create -y vue@latest \"{dir}\" -- --default", dir, log);
    }

    private static bool InstallWhmcsMock(string dir, Action<string> log)
    {
        log("WHMCS requires a commercial license and manual download of the zip file.");
        log("Please download WHMCS from their website and extract it here.");
        log("Once extracted, ensure the ionCube loader is enabled in the PHP Extensions Manager.");
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "Extract WHMCS zip here.");
        return true;
    }

    private static async Task<bool> RunCommandAsync(string cmd, string args, string cwd, Action<string> log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            // Fallback for Windows cmd if the executable isn't directly resolvable
            if (cmd == "npm" || cmd == "npx" || cmd == "composer")
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c {cmd} {args}";
            }

            using var p = Process.Start(psi);
            if (p == null) return false;

            p.OutputDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
            
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            log($"Command failed: {ex.Message}");
            return false;
        }
    }
}
