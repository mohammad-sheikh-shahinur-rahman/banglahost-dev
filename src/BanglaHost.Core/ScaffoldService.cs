using System;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using BanglaHost.Core;

namespace BanglaHost.Core;

public static class ScaffoldService
{
    public static void CreateWordPress(string siteName, Config cfg, Engine engine, Action<string> log)
    {
        var root = Path.Combine(cfg.SitesRoot, siteName);
        if (Directory.Exists(root)) throw new BhException($"Directory {root} already exists.");
        Directory.CreateDirectory(root);

        log($"[Scaffold] Downloading WordPress latest...");
        var zipPath = Path.Combine(Path.GetTempPath(), $"wp_{Guid.NewGuid():N}.zip");
        
        var psi = new ProcessStartInfo
        {
            FileName = "curl.exe",
            Arguments = $"-L \"https://wordpress.org/latest.zip\" -o \"{zipPath}\"",
            UseShellExecute = false, CreateNoWindow = true
        };
        var p = Process.Start(psi);
        p?.WaitForExit();
        if (p?.ExitCode != 0) throw new BhException("Failed to download WordPress.");

        log($"[Scaffold] Extracting WordPress...");
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, Path.GetTempPath(), overwriteFiles: true);
        
        var wpFolder = Path.Combine(Path.GetTempPath(), "wordpress");
        foreach (var file in Directory.GetFiles(wpFolder))
            File.Move(file, Path.Combine(root, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(wpFolder))
            Directory.Move(dir, Path.Combine(root, Path.GetFileName(dir)));
            
        Directory.Delete(wpFolder);
        File.Delete(zipPath);

        log($"[Scaffold] Creating database '{siteName}'...");
        var mysqlExe = Tools.MysqlClientExe();
        if (mysqlExe != null)
        {
            var user = "root";
            var pass = cfg.RootPassword;
            var port = 3306;
            var auth = $"-u {user}" + (string.IsNullOrEmpty(pass) ? "" : $" -p\"{pass}\"") + $" -P {port} -h 127.0.0.1";
            try { using (var pm = Process.Start(new ProcessStartInfo { FileName = mysqlExe, Arguments = $"{auth} -e \"CREATE DATABASE IF NOT EXISTS \\\"{siteName}\\\";\"", UseShellExecute = false, CreateNoWindow = true })) { pm?.WaitForExit(); } } catch { }

            var configSample = Path.Combine(root, "wp-config-sample.php");
            var configFinal = Path.Combine(root, "wp-config.php");
            if (File.Exists(configSample))
            {
                var content = File.ReadAllText(configSample);
                content = content.Replace("database_name_here", siteName);
                content = content.Replace("username_here", user);
                content = content.Replace("password_here", pass);
                content = content.Replace("localhost", $"127.0.0.1:{port}");
                File.WriteAllText(configFinal, content);
            }
        }

        var phpVer = ProjectDetector.DetectPhpVersion(root);
        if (string.IsNullOrEmpty(phpVer)) phpVer = cfg.DefaultPhp;
        engine.SiteAdd(siteName, phpVer, root, cfg.DefaultWeb);
        log($"[Scaffold] WordPress '{siteName}' is ready!");
    }

    public static void CreateLaravel(string siteName, Config cfg, Engine engine, Action<string> log)
    {
        var root = Path.Combine(cfg.SitesRoot, siteName);
        if (Directory.Exists(root)) throw new BhException($"Directory {root} already exists.");

        var composer = Tools.ComposerPhar();
        var php = Tools.PhpExe(cfg.DefaultPhp);
        
        if (composer == null || php == null)
            throw new BhException("PHP and Composer are required to install Laravel. Please install them from the Services page.");

        log($"[Scaffold] Running composer create-project laravel/laravel {siteName}...");
        
        var psi = new ProcessStartInfo
        {
            FileName = php,
            Arguments = $"\"{composer}\" create-project laravel/laravel \"{siteName}\"",
            WorkingDirectory = cfg.SitesRoot,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        var p = Process.Start(psi);
        p?.WaitForExit();
        if (p?.ExitCode != 0) throw new BhException($"Composer failed: {p?.StandardError.ReadToEnd()}");

        log($"[Scaffold] Creating database '{siteName}'...");
        var mysqlExe = Tools.MysqlClientExe();
        if (mysqlExe != null)
        {
            var user = "root";
            var pass = cfg.RootPassword;
            var port = 3306;
            var auth = $"-u {user}" + (string.IsNullOrEmpty(pass) ? "" : $" -p\"{pass}\"") + $" -P {port} -h 127.0.0.1";
            try { using (var pm = Process.Start(new ProcessStartInfo { FileName = mysqlExe, Arguments = $"{auth} -e \"CREATE DATABASE IF NOT EXISTS \\\"{siteName}\\\";\"", UseShellExecute = false, CreateNoWindow = true })) { pm?.WaitForExit(); } } catch { }

            var envPath = Path.Combine(root, ".env");
            if (File.Exists(envPath))
            {
                var content = File.ReadAllText(envPath);
                content = Regex.Replace(content, @"^DB_DATABASE=.*", $"DB_DATABASE={siteName}", RegexOptions.Multiline);
                content = Regex.Replace(content, @"^DB_USERNAME=.*", $"DB_USERNAME={user}", RegexOptions.Multiline);
                content = Regex.Replace(content, @"^DB_PASSWORD=.*", $"DB_PASSWORD={pass}", RegexOptions.Multiline);
                content = Regex.Replace(content, @"^DB_PORT=.*", $"DB_PORT={port}", RegexOptions.Multiline);
                content = Regex.Replace(content, @"^APP_URL=.*", $"APP_URL=http://{siteName}.{cfg.Tld}", RegexOptions.Multiline);
                File.WriteAllText(envPath, content);
            }
        }

        // Add site. Laravel serves from /public
        var phpVer = ProjectDetector.DetectPhpVersion(root);
        if (string.IsNullOrEmpty(phpVer)) phpVer = cfg.DefaultPhp;
        engine.SiteAdd(siteName, phpVer, Path.Combine(root, "public"), cfg.DefaultWeb);
        log($"[Scaffold] Laravel '{siteName}' is ready!");
    }
}
