using System;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Linq;
using BanglaHost.Core;

namespace BanglaHost.Core;

public static class CloneService
{
    public static void CloneSite(string srcName, string destName, Config cfg, Engine engine, Action<string> log)
    {
        var srcRoot = Path.Combine(cfg.SitesRoot, srcName);
        var destRoot = Path.Combine(cfg.SitesRoot, destName);

        if (!Directory.Exists(srcRoot))
            throw new BhException($"Source site '{srcName}' does not exist.");
        if (Directory.Exists(destRoot))
            throw new BhException($"Destination site '{destName}' already exists.");

        log($"[Clone] Copying files from {srcName} to {destName}...");
        CopyDirectory(srcRoot, destRoot);

        // Try to clone database if it's a known CMS/framework
        CloneDatabaseIfDetected(srcName, destName, destRoot, cfg, log);

        log($"[Clone] Provisioning vhost for {destName}...");
        
        var phpVer = ProjectDetector.DetectPhpVersion(destRoot);
        if (string.IsNullOrEmpty(phpVer)) phpVer = cfg.DefaultPhp;

        // Add the site
        engine.SiteAdd(destName, phpVer, destRoot, cfg.DefaultWeb);

        log($"[Clone] Successfully cloned {srcName} to {destName}!");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            // Skip large directories that can be regenerated or are not essential for staging
            if (subDir.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private static void CloneDatabaseIfDetected(string srcName, string destName, string destRoot, Config cfg, Action<string> log)
    {
        var envPath = Path.Combine(destRoot, ".env");
        var wpConfigPath = Path.Combine(destRoot, "wp-config.php");

        string dbName = "";

        if (File.Exists(wpConfigPath))
        {
            var content = File.ReadAllText(wpConfigPath);
            var match = Regex.Match(content, @"define\(\s*'DB_NAME'\s*,\s*'([^']+)'\s*\)");
            if (match.Success)
            {
                dbName = match.Groups[1].Value;
                
                // Rewrite DB_NAME in new config
                var newDbName = Regex.Replace(destName, "[^a-zA-Z0-9_]", "_");
                content = Regex.Replace(content, @"(define\(\s*'DB_NAME'\s*,\s*')[^']+('\s*\))", $"${{1}}{newDbName}${{2}}");
                
                // Rewrite URLs if defined
                content = Regex.Replace(content, @"(define\(\s*'WP_HOME'\s*,\s*')[^']+('\s*\))", $"${{1}}http://{destName}.{cfg.Tld}${{2}}");
                content = Regex.Replace(content, @"(define\(\s*'WP_SITEURL'\s*,\s*')[^']+('\s*\))", $"${{1}}http://{destName}.{cfg.Tld}${{2}}");
                
                File.WriteAllText(wpConfigPath, content);
                PerformDatabaseClone(dbName, newDbName, cfg, log);
            }
        }
        else if (File.Exists(envPath))
        {
            var content = File.ReadAllText(envPath);
            var match = Regex.Match(content, @"^DB_DATABASE=([^\r\n]+)", RegexOptions.Multiline);
            if (match.Success)
            {
                dbName = match.Groups[1].Value;
                
                // Rewrite DB_DATABASE in new config
                var newDbName = Regex.Replace(destName, "[^a-zA-Z0-9_]", "_");
                content = Regex.Replace(content, @"^DB_DATABASE=[^\r\n]+", $"DB_DATABASE={newDbName}", RegexOptions.Multiline);
                
                // Rewrite APP_URL
                content = Regex.Replace(content, @"^APP_URL=[^\r\n]+", $"APP_URL=http://{destName}.{cfg.Tld}", RegexOptions.Multiline);
                
                File.WriteAllText(envPath, content);
                if (dbName != null && newDbName != null)
                {
                    PerformDatabaseClone(dbName, newDbName, cfg, log);
                }
            }
        }
        
        log($"[Clone] Successfully cloned {srcName} to {destName}!");
    }

    private static void PerformDatabaseClone(string srcDb, string destDb, Config cfg, Action<string> log)
    {
        log($"[Clone] Cloning database '{srcDb}' to '{destDb}'...");
        
        var dumpExe = Path.Combine(Paths.Bin, "mysql", "bin", "mysqldump.exe");
        if (!File.Exists(dumpExe)) dumpExe = Path.Combine(Paths.Bin, "mariadb", "bin", "mysqldump.exe");
        var mysqlExe = Tools.MysqlClientExe();
        
        if (!File.Exists(dumpExe) || mysqlExe == null)
        {
            log($"[Clone] MySQL tools not found. Skipping DB clone.");
            return;
        }

        var tempSql = Path.Combine(Path.GetTempPath(), $"{srcDb}_clone_{Guid.NewGuid():N}.sql");
        var user = "root";
        var pass = cfg.RootPassword;

        Action<ProcessStartInfo> addAuth = (psi) => {
            psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(user);
            if (!string.IsNullOrEmpty(pass)) psi.ArgumentList.Add($"-p{pass}");
            psi.ArgumentList.Add("-P"); psi.ArgumentList.Add("3306");
            psi.ArgumentList.Add("-h"); psi.ArgumentList.Add("127.0.0.1");
        };

        try
        {
            // 1. Dump safely
            var psiDump = new ProcessStartInfo { FileName = dumpExe, UseShellExecute = false, CreateNoWindow = true };
            psiDump.ArgumentList.Add("--opt");
            addAuth(psiDump);
            psiDump.ArgumentList.Add(srcDb);
            psiDump.ArgumentList.Add($"--result-file={tempSql}");
            using (var p = Process.Start(psiDump)) { p?.WaitForExit(); if (p?.ExitCode != 0) throw new BhException($"Dump failed for {srcDb}"); }
            
            // 2. Create new DB safely
            var psiCreate = new ProcessStartInfo { FileName = mysqlExe, UseShellExecute = false, CreateNoWindow = true };
            addAuth(psiCreate);
            psiCreate.ArgumentList.Add("-e");
            psiCreate.ArgumentList.Add($"CREATE DATABASE IF NOT EXISTS `{destDb}`;");
            using (var p = Process.Start(psiCreate)) { p?.WaitForExit(); if (p?.ExitCode != 0) throw new BhException($"Failed to create database {destDb}"); }
            
            // 3. Import safely via Stream
            var psiImport = new ProcessStartInfo { FileName = mysqlExe, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            addAuth(psiImport);
            psiImport.ArgumentList.Add(destDb);
            using (var p = Process.Start(psiImport))
            {
                if (p != null)
                {
                    using (var fs = File.OpenRead(tempSql)) { fs.CopyTo(p.StandardInput.BaseStream); p.StandardInput.Close(); }
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new BhException($"Import failed for {destDb}");
                }
            }
        }
        catch (Exception ex)
        {
            log($"[Clone] Database clone failed: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempSql)) try { File.Delete(tempSql); } catch { }
        }
    }
}
