using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
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

    /// <summary>Let's Encrypt issuance is not implemented. See <see cref="GenerateLetsEncryptAsync"/>.</summary>
    public static bool LetsEncryptSupported => false;

    public static void Init()
    {
        if (!Directory.Exists(CertsDir))
            Directory.CreateDirectory(CertsDir);
    }

    /// <summary>
    /// A domain name reaches the filesystem (as <c>&lt;domain&gt;.pem</c>) and the mkcert command line.
    /// Unvalidated, <c>..\..\Windows\System32\x</c> wrote outside the certs directory, and a name
    /// containing a quote broke out of the argument string.
    /// Accepts a hostname or a single-label wildcard (<c>*.example.test</c>), which is what mkcert
    /// takes for a SAN.
    /// </summary>
    public static string ValidDomain(string domain)
    {
        domain = (domain ?? "").Trim().ToLowerInvariant();
        if (domain.Length == 0) throw new BhException("Domain is required.");
        if (domain.Length > 253) throw new BhException("Domain is too long.");
        if (!Regex.IsMatch(domain, @"^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            throw new BhException($"Invalid domain '{domain}'.");
        return domain;
    }

    /// <summary>Wildcard certs are stored with the '*' replaced, because '*' is not a legal
    /// Windows filename character.</summary>
    private static string FileStem(string domain) => domain.Replace("*", "_wildcard");

    public static List<SslCertInfo> GetLocalCertificates()
    {
        Init();
        var list = new List<SslCertInfo>();
        string[] files;
        try { files = Directory.GetFiles(CertsDir, "*.pem"); }
        catch { return list; }

        foreach (var file in files)
        {
            if (file.EndsWith("-key.pem", StringComparison.OrdinalIgnoreCase)) continue;   // private keys aren't certs
            try
            {
                // X509Certificate2 holds an unmanaged CNG/CAPI key handle. Without the using, one
                // handle leaked per certificate per page visit; the SSL page refreshes on every
                // navigation, so a long session accumulated hundreds.
                using var cert = new X509Certificate2(file);
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

    public static async Task<bool> GenerateLocalCertAsync(
        string domain, Action<string> log, CancellationToken ct = default)
    {
        log ??= _ => { };
        try { domain = ValidDomain(domain); }
        catch (Exception ex) { log(ex.Message); return false; }

        log($"Generating local certificate for {domain} via mkcert…");

        // Was hardcoded to bin\mkcert\mkcert.exe, which misses the bundled install root shipped in
        // the installer — so a fresh install reported "mkcert not found" with mkcert right there.
        var exe = Tools.MkcertExe();
        if (exe is null || !File.Exists(exe))
        {
            log("mkcert not found. Install it from the Tools page and try again.");
            return false;
        }

        Init();
        var stem = FileStem(domain);
        var keyFile = Path.Combine(CertsDir, $"{stem}-key.pem");
        var certFile = Path.Combine(CertsDir, $"{stem}.pem");

        try
        {
            var res = await ProcRunner.RunAsync(
                exe,
                new[] { "-key-file", keyFile, "-cert-file", certFile, domain },
                workingDir: Path.GetDirectoryName(exe),
                timeoutMs: 120_000,
                onOutputLine: log,
                onErrorLine: log,          // mkcert writes its normal progress to stderr
                ct: ct).ConfigureAwait(false);

            if (res.TimedOut) { log("mkcert timed out after 120 seconds."); return false; }
            if (res.ExitCode != 0)
            {
                log($"mkcert failed (exit {res.ExitCode}).");
                return false;
            }
            if (!File.Exists(certFile) || !File.Exists(keyFile))
            {
                // Exit code 0 with no files means the local CA is not installed yet.
                log("mkcert reported success but produced no files — run 'mkcert -install' once.");
                return false;
            }
            log($"Certificate written to {certFile}");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log($"Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// NOT IMPLEMENTED. Kept so the UI has something to bind to, but it no longer pretends to work:
    /// the previous body slept two seconds and logged "Simulation complete", which read to users as a
    /// real certificate request that had failed for some unexplained reason.
    ///
    /// Issuing a real Let's Encrypt certificate needs an ACME client (win-acme or certbot), a
    /// publicly reachable port 80 or a DNS-01 provider credential, and a renewal scheduler. None of
    /// that is shipped. <see cref="LetsEncryptSupported"/> is false and the button is disabled.
    /// </summary>
    public static Task<bool> GenerateLetsEncryptAsync(string domain, string email, Action<string> log)
    {
        log?.Invoke("Let's Encrypt certificates are not supported in this version of BanglaHost. "
                  + "Use mkcert for local domains, or issue a public certificate with win-acme/certbot "
                  + "and drop the .pem files into " + CertsDir + ".");
        return Task.FromResult(false);
    }

    public static void DeleteCert(string domain)
    {
        // The domain arrives from a UI Tag; validate before it becomes a path.
        var stem = FileStem(ValidDomain(domain));
        foreach (var f in new[] { Path.Combine(CertsDir, $"{stem}-key.pem"), Path.Combine(CertsDir, $"{stem}.pem") })
        {
            // Belt and braces: never delete outside CertsDir even if validation is ever loosened.
            var full = Path.GetFullPath(f);
            if (!full.StartsWith(Path.GetFullPath(CertsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new BhException("Refusing to delete outside the certificates folder.");
            if (File.Exists(full)) File.Delete(full);
        }
    }
}
