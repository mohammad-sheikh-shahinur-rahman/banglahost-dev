using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public class SslCertInfo
{
    public string Domain { get; set; } = "";
    public string Issuer { get; set; } = "";
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public string Thumbprint { get; set; } = "";
    public bool IsValid { get; set; }

    public SslCertInfo() { }

    public SslCertInfo(string Domain, string Issuer, DateTime ValidFrom, DateTime ValidTo, string Thumbprint, bool IsValid)
    {
        this.Domain = Domain; this.Issuer = Issuer; this.ValidFrom = ValidFrom; this.ValidTo = ValidTo; this.Thumbprint = Thumbprint; this.IsValid = IsValid;
    }
}

public static class SslService
{
    private static string CertsDir => Path.Combine(Paths.Home, "certs");

    public static void Init()
    {
        if (!Directory.Exists(CertsDir))
            Directory.CreateDirectory(CertsDir);
    }

    public static List<SslCertInfo> GetLocalCertificates()
    {
        Init();
        var list = new List<SslCertInfo>();
        var files = Directory.GetFiles(CertsDir, "*.pem");
        foreach (var file in files)
        {
            try
            {
                var cert = new X509Certificate2(file);
                list.Add(new SslCertInfo(
                    Domain: Path.GetFileNameWithoutExtension(file),
                    Issuer: cert.Issuer,
                    ValidFrom: cert.NotBefore,
                    ValidTo: cert.NotAfter,
                    Thumbprint: cert.Thumbprint,
                    IsValid: DateTime.Now >= cert.NotBefore && DateTime.Now <= cert.NotAfter
                ));
            }
            catch { }
        }
        return list;
    }

    public static async Task<bool> GenerateLocalCertAsync(string domain, Action<string> log)
    {
        log($"Generating local certificate for {domain} via mkcert...");
        var exe = Path.Combine(Paths.Bin, "mkcert", "mkcert.exe");
        if (!File.Exists(exe))
        {
            log("mkcert not found. Please wait for it to be installed.");
            return false;
        }

        var keyFile = Path.Combine(CertsDir, $"{domain}-key.pem");
        var certFile = Path.Combine(CertsDir, $"{domain}.pem");
        
        var args = $"-key-file \"{keyFile}\" -cert-file \"{certFile}\" {domain}";
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
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
            log($"Error: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> GenerateLetsEncryptAsync(string domain, string email, Action<string> log)
    {
        log($"Starting Let's Encrypt generation for {domain}...");
        log("Note: This requires port 80 to be publicly accessible, or DNS validation setup.");
        // We'll use a bundled win-acme or certbot. For now, simulate the request.
        await Task.Delay(2000);
        log("Let's Encrypt feature requires `win-acme` CLI integration. Simulation complete.");
        return false;
    }

    public static void DeleteCert(string domain)
    {
        var keyFile = Path.Combine(CertsDir, $"{domain}-key.pem");
        var certFile = Path.Combine(CertsDir, $"{domain}.pem");
        if (File.Exists(keyFile)) File.Delete(keyFile);
        if (File.Exists(certFile)) File.Delete(certFile);
    }
}
