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

    // Tools that are only available as .cmd shims on Windows (npm, npx) must be
    // driven through cmd.exe — but NEVER via string-concatenated "cmd /c {cmd} {args}"
    // with a PATH-resolved cmd (B13/B11). The cmd path is absolute, the tool is
    // allow-listed, and the target dir is passed through ArgumentList-style quoting.
    private static readonly HashSet<string> ShellTools =
        new(StringComparer.OrdinalIgnoreCase) { "npm", "npx", "composer" };

    private static async Task<bool> RunCommandAsync(string cmd, string args, string cwd, Action<string> log)
    {
        try
        {
            if (!Directory.Exists(cwd)) Directory.CreateDirectory(cwd);
            var tool = SystemExe.ResolveTool(cmd);
            
            string exeToRun;
            var runArgs = new System.Collections.Generic.List<string>();

            if (ShellTools.Contains(cmd))
            {
                exeToRun = SystemExe.Cmd;
                runArgs.Add("/d");
                runArgs.Add("/c");
                runArgs.Add(tool);
                runArgs.AddRange(SplitArgs(args));
            }
            else
            {
                exeToRun = tool;
                runArgs.AddRange(SplitArgs(args));
            }

            var result = await ProcRunner.RunAsync(
                exe: exeToRun,
                args: runArgs,
                workingDir: cwd,
                onOutputLine: line => { if (line != null) log(line); },
                onErrorLine: line => { if (line != null) log(line); }
            );

            return result.Ok;
        }
        catch (Exception ex)
        {
            log($"Command failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Split an argument string on whitespace, honouring double quotes.</summary>
    private static IEnumerable<string> SplitArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) yield break;
        var cur = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in args)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); }
                continue;
            }
            cur.Append(c);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }
}
