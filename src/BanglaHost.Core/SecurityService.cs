using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public class SecurityScanResult
{
    public string FilePath { get; set; } = "";
    public string ThreatType { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "";

    public SecurityScanResult() { }

    public SecurityScanResult(string FilePath, string ThreatType, string Description, string Severity)
    {
        this.FilePath = FilePath; this.ThreatType = ThreatType; this.Description = Description; this.Severity = Severity;
    }
}

public static class SecurityService
{
    // A simple file scanner looking for common PHP malware patterns or vulnerabilities.
    private static readonly Dictionary<string, string> MalwarePatterns = new()
    {
        { "eval(base64_decode(", "Obfuscated PHP Code (eval base64)" },
        { "shell_exec(", "Potentially dangerous function (shell_exec)" },
        { "system(", "Potentially dangerous function (system)" },
        { "passthru(", "Potentially dangerous function (passthru)" },
        { "<?php $_POST", "Suspicious raw POST handling (potential backdoor)" }
    };

    public static async Task<List<SecurityScanResult>> ScanDirectoryAsync(string directoryPath, Action<string> log)
    {
        var results = new List<SecurityScanResult>();
        
        if (!Directory.Exists(directoryPath))
        {
            log($"Directory not found: {directoryPath}");
            return results;
        }

        try
        {
            var files = Directory.GetFiles(directoryPath, "*.php", SearchOption.AllDirectories);
            log($"Scanning {files.Length} PHP files...");

            int count = 0;
            foreach (var file in files)
            {
                count++;
                if (count % 100 == 0) log($"Scanned {count} / {files.Length} files...");
                
                var content = await File.ReadAllTextAsync(file);
                
                foreach (var pattern in MalwarePatterns)
                {
                    if (content.Contains(pattern.Key))
                    {
                        var severity = pattern.Key.StartsWith("eval") ? "High" : "Medium";
                        results.Add(new SecurityScanResult(file, "Suspicious Pattern", pattern.Value, severity));
                    }
                }
            }

            log($"Scan complete. Found {results.Count} potential issues.");
        }
        catch (Exception ex)
        {
            log($"Scan error: {ex.Message}");
        }

        return results;
    }

    public static async Task<bool> IsWafEnabledAsync()
    {
        // Check if ModSecurity or similar WAF is enabled in config.
        // For now, simulate check.
        await Task.Delay(100);
        return false;
    }

    public static async Task ToggleWafAsync(bool enable, Action<string> log)
    {
        log(enable ? "Enabling Web Application Firewall..." : "Disabling Web Application Firewall...");
        // Simulation
        await Task.Delay(1000);
        log(enable ? "WAF enabled." : "WAF disabled.");
    }
}
