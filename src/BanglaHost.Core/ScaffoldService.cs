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

        // Absolute curl path (B11); bounded wait (was unbounded, C2).
        var curl = ProcRunner.Run(SystemExe.Curl,
            new[] { "-L", "https://wordpress.org/latest.zip", "-o", zipPath },
            timeoutMs: 600_000);
        if (!curl.Ok) throw new BhException("Failed to download WordPress.");

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
        EnsureDatabase(siteName, cfg, log);
        {
            var user = "root";
            var pass = cfg.RootPassword;
            var port = 3306;

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

        // ArgumentList (never a pasted string) + bounded wait with concurrent pipe
        // reads. Reading StandardError AFTER WaitForExit, as before, deadlocks when
        // composer fills the stderr pipe — and the wait itself was unbounded (C2/C5).
        var comp = ProcRunner.Run(php,
            new[] { composer, "create-project", "laravel/laravel", siteName },
            workingDir: cfg.SitesRoot, timeoutMs: 600_000);
        if (!comp.Ok) throw new BhException($"Composer failed: {comp.StdErr.Split('\n').FirstOrDefault()}");

        log($"[Scaffold] Creating database '{siteName}'...");
        EnsureDatabase(siteName, cfg, log);
        {
            var user = "root";
            var pass = cfg.RootPassword;
            var port = 3306;

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

    public static void CreateReact(string siteName, Config cfg, Engine engine, Action<string> log)
    {
        var root = Path.Combine(cfg.SitesRoot, siteName);
        if (Directory.Exists(root)) throw new BhException($"Directory {root} already exists.");

        var nodeBin = Tools.NodeBinDir();
        var npm = nodeBin != null ? Path.Combine(nodeBin, "npm.cmd") : null;
        if (npm == null || !File.Exists(npm)) throw new BhException("Node.js/NPM is required to install React. Please install Node.js from the Services page.");

        log($"[Scaffold] Running npm create vite@latest {siteName} -- --template react ...");

        // Absolute cmd (B11); bounded waits with concurrent pipe reads (C2/C5).
        // (ReadToEnd-after-WaitForExit, as before, deadlocks on chatty output.)
        var vite = ProcRunner.Run(SystemExe.Cmd,
            new[] { "/d", "/c", $"\"{npm}\" create vite@latest \"{siteName}\" --yes -- --template react" },
            workingDir: cfg.SitesRoot, timeoutMs: 600_000);
        if (!vite.Ok) throw new BhException($"Vite failed: {vite.StdErr.Split('\n').FirstOrDefault()}");

        log($"[Scaffold] Installing React dependencies (npm install)...");
        var inst = ProcRunner.Run(SystemExe.Cmd,
            new[] { "/d", "/c", $"\"{npm}\" install" },
            workingDir: root, timeoutMs: 600_000);
        if (!inst.Ok) log($"[Scaffold] npm install reported: {inst.StdErr.Split('\n').FirstOrDefault()}");

        log($"[Scaffold] Note: React apps are typically run with 'npm run dev' or built for static hosting. You can start it from the Terminal.");
        engine.SiteAdd(siteName, cfg.DefaultPhp, root, cfg.DefaultWeb);
        log($"[Scaffold] React '{siteName}' is ready!");
    }

    /// <summary>
    /// Create the scaffold database. Credentials travel in a locked-down defaults
    /// file (never -p on the command line, B3) and the name is validated before it
    /// reaches SQL — backtick-quoting alone is not a defence (B9). Best-effort:
    /// scaffolding must not fail just because the DB server is down.
    /// </summary>
    private static void EnsureDatabase(string siteName, Config cfg, Action<string> log)
    {
        try
        {
            MySqlAuthFile.ValidIdentifier(siteName, "site name");
            var mysqlExe = Tools.MysqlClientExe();
            if (mysqlExe is null) return;
            using var auth = MySqlAuthFile.Create("root", cfg.RootPassword, DbServer.Port);
            var args = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
            args.Add("-e");
            args.Add($"CREATE DATABASE IF NOT EXISTS `{siteName}`;");
            var res = ProcRunner.Run(mysqlExe, args, timeoutMs: 60_000);
            if (!res.Ok) log($"[Scaffold] database '{siteName}' not created: {res.StdErr.Split('\n').FirstOrDefault()}");
        }
        catch (Exception ex) { log($"[Scaffold] database '{siteName}' not created: {ex.Message}"); }
    }
}
