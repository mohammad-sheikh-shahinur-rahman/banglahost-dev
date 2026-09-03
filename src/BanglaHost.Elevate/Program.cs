using System.Diagnostics;
using BanglaHost.Core;

// banglahost-elevate — runs elevated (requireAdministrator). Does ONLY the admin-only
// steps so the main CLI/GUI can stay unprivileged. Invoked via runas by Core.Elevation.
//   banglahost-elevate hosts-add <domain>
//   banglahost-elevate hosts-remove <domain>
//   banglahost-elevate mkcert-install

if (args.Length == 0) return 1;
var verb = args[0];

try
{
    switch (verb)
    {
        case "hosts-add":
            if (args.Length < 2 || !Hosts.IsValidDomain(args[1])) return 1;
            // The privileged side re-validates INDEPENDENTLY of its caller (B5):
            // an ip with newlines/tokens would become extra hosts lines machine-wide.
            // Belt-and-braces: refuse control characters in every argument.
            if (args.Any(a => a.Any(char.IsControl))) { Console.Error.WriteLine("refusing: control character in argument"); return 3; }
            var ip = args.Length > 2 ? args[2] : "127.0.0.1";
            try { ip = Hosts.CanonicalIp(ip); }
            catch (Exception ex) { Console.Error.WriteLine($"refusing: {ex.Message}"); return 2; }
            return Hosts.Add(args[1], ip) ? 0 : 1;

        case "hosts-remove":
            if (args.Length < 2 || !Hosts.IsValidDomain(args[1])) return 1;
            return Hosts.Remove(args[1]) ? 0 : 1;

        case "mkcert-install":
        {
            var mkc = Tools.MkcertExe();
            if (mkc is null) { Console.Error.WriteLine("mkcert not installed"); return 1; }
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = mkc,
                Arguments = "-install",
                UseShellExecute = false,
            })!;
            if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } }

            // mkcert -install only trusts the CA for the CURRENT USER (CurrentUser\Root).
            // HTTPS-scanning security software (ESET, Kaspersky, Avast…) validates server certs
            // against the MACHINE store — without the CA there it re-signs every local site with
            // an "untrusted" placeholder â†’ ERR_CERT_AUTHORITY_INVALID in every browser. We're
            // already elevated here, so add the CA machine-wide too.
            try
            {
                var caOut = Process.Start(new ProcessStartInfo
                { FileName = mkc, Arguments = "-CAROOT", UseShellExecute = false, RedirectStandardOutput = true })!;
                var caroot = caOut.StandardOutput.ReadToEnd().Trim();
                if (!caOut.WaitForExit(30_000)) { try { caOut.Kill(true); } catch { } }
                var rootPem = Path.Combine(caroot, "rootCA.pem");
                if (File.Exists(rootPem))
                {
                var cu = Process.Start(new ProcessStartInfo
                {
                    // Absolute path: this process is elevated, so a PATH-resolved
                    // certutil would be a privilege-escalation primitive (B11).
                    FileName = Path.Combine(Environment.SystemDirectory, "certutil.exe"),
                    Arguments = $"-addstore -f Root \"{rootPem}\"", UseShellExecute = false, CreateNoWindow = true
                })!;
                if (!cu.WaitForExit(60_000)) { try { cu.Kill(true); } catch { } }
                    if (cu.ExitCode != 0) Console.Error.WriteLine("certutil machine-store add failed (user-store trust still installed)");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"machine-store CA add failed: {ex.Message}"); }

            return p.ExitCode;
        }

        default:
            Console.Error.WriteLine($"unknown verb: {verb}");
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
