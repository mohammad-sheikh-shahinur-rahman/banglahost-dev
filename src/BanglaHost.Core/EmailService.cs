using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record SmtpProfile(string Id, string Name, string Host, int Port, string Username, string Password, bool UseSsl);
public record EmailLogEntry(DateTime Timestamp, string To, string Subject, string Status);

public static class EmailService
{
    private static string ProfileDir => Path.Combine(Paths.Home, "email_profiles");
    private static string LogFile => Path.Combine(Paths.Home, "email_log.json");

    public static void Init()
    {
        if (!Directory.Exists(ProfileDir)) Directory.CreateDirectory(ProfileDir);
    }

    public static SmtpProfile[] GetProfiles()
    {
        Init();
        var files = Directory.GetFiles(ProfileDir, "*.json");
        var list = new List<SmtpProfile>();
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var p = JsonSerializer.Deserialize<SmtpProfile>(json);
                if (p != null) list.Add(p);
            }
            catch { }
        }
        return list.ToArray();
    }

    public static void SaveProfile(SmtpProfile p)
    {
        Init();
        var path = Path.Combine(ProfileDir, $"{p.Id}.json");
        var json = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static void DeleteProfile(string id)
    {
        var path = Path.Combine(ProfileDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static async Task<bool> SendTestEmailAsync(SmtpProfile profile, string toAddress, Action<string> log)
    {
        log($"Sending test email via {profile.Host}:{profile.Port}...");
        try
        {
            using var client = new SmtpClient(profile.Host, profile.Port)
            {
                Credentials = new NetworkCredential(profile.Username, profile.Password),
                EnableSsl = profile.UseSsl,
                Timeout = 15000
            };

            var msg = new MailMessage
            {
                From = new MailAddress(profile.Username, "BanglaHost Test"),
                Subject = "BanglaHost SMTP Test",
                Body = $"This is a test email from BanglaHost sent at {DateTime.Now}.\n\nIf you received this, your SMTP configuration is working correctly.",
                IsBodyHtml = false
            };
            msg.To.Add(toAddress);

            await client.SendMailAsync(msg);
            log("Email sent successfully!");
            AppendLog(new EmailLogEntry(DateTime.Now, toAddress, msg.Subject, "Sent"));
            return true;
        }
        catch (Exception ex)
        {
            log($"Failed: {ex.Message}");
            AppendLog(new EmailLogEntry(DateTime.Now, toAddress, "Test Email", $"Failed: {ex.Message}"));
            return false;
        }
    }

    public static List<EmailLogEntry> GetLog()
    {
        if (!File.Exists(LogFile)) return new List<EmailLogEntry>();
        try
        {
            var json = File.ReadAllText(LogFile);
            return JsonSerializer.Deserialize<List<EmailLogEntry>>(json) ?? new List<EmailLogEntry>();
        }
        catch { return new List<EmailLogEntry>(); }
    }

    private static void AppendLog(EmailLogEntry entry)
    {
        var log = GetLog();
        log.Add(entry);
        // Keep only last 100 entries
        if (log.Count > 100) log.RemoveRange(0, log.Count - 100);
        File.WriteAllText(LogFile, JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ─── Built-in Mailhog / MailPit ─────────────────────
    public static bool IsMailhogRunning()
    {
        var procs = Process.GetProcessesByName("mailhog");
        return procs.Length > 0;
    }

    public static async Task<bool> StartMailhogAsync(Action<string> log)
    {
        var exe = Path.Combine(Paths.Bin, "mailhog", "MailHog.exe");
        if (!File.Exists(exe))
        {
            log("MailHog not found. Install it via the Marketplace.");
            return false;
        }

        log("Starting MailHog...");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            Process.Start(psi);
            await Task.Delay(1000);
            log("MailHog started on http://localhost:8025");
            return true;
        }
        catch (Exception ex)
        {
            log($"Failed to start MailHog: {ex.Message}");
            return false;
        }
    }

    public static void StopMailhog()
    {
        foreach (var p in Process.GetProcessesByName("mailhog"))
        {
            try { p.Kill(); } catch { }
        }
    }
}
