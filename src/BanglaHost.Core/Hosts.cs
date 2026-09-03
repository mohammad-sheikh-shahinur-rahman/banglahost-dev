using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace BanglaHost.Core;

/// <summary>
/// Windows hosts-file management (the analog of mac dnsmasq/resolver, since the
/// Windows hosts file can't do wildcards). Each managed line is tagged so we can
/// add/remove cleanly. Writing requires Administrator — callers check
/// <see cref="IsElevated"/> and surface an elevation hint when false.
/// </summary>
public static class Hosts
{
    private const string Tag = "# BanglaHost";

    /// <summary>A strict hostname: lowercase labels of [a-z0-9-] joined by dots, no leading/trailing
    /// hyphen, max 253 chars. Critically rejects whitespace/newlines so the value can never inject
    /// extra lines into the hosts file (this code runs elevated).</summary>
    public static bool IsValidDomain(string domain) =>
        !string.IsNullOrEmpty(domain) && domain.Length <= 253 &&
        Regex.IsMatch(domain, @"^(?=.{1,253}$)([a-z0-9](-?[a-z0-9])*)(\.[a-z0-9](-?[a-z0-9])*)+$");

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool Has(string domain)
    {
        try
        {
            return File.Exists(Paths.HostsFile) &&
                   File.ReadLines(Paths.HostsFile)
                       .Any(l => l.Contains(Tag) && l.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                                     .Contains(domain));
        }
        catch { return false; }
    }

    /// <summary>Append "<paramref name="ip"/> <paramref name="domain"/> # BanglaHost" if absent. Returns false (no-throw) when not elevated.</summary>
    /// <summary>
    /// Parse an IP and re-serialise the parsed value, so only a canonical address
    /// can ever reach the hosts file. A raw string could carry newlines or extra
    /// tokens into a file the elevated helper writes (B5) — the parsed form cannot.
    /// </summary>
    public static string CanonicalIp(string raw)
    {
        if (!IPAddress.TryParse((raw ?? "").Trim(), out var ip))
            throw new BhException($"not a valid IP address: '{raw}'");
        if (ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new BhException($"unsupported address family for '{raw}'");
        return ip.ToString();
    }

    public static bool Add(string domain, string ip = "127.0.0.1")
    {
        if (!IsValidDomain(domain)) return false;   // never write unvalidated input to the hosts file
        string safeIp;
        try { safeIp = CanonicalIp(ip); }
        catch { return false; }
        if (Has(domain)) return true;
        if (!IsElevated()) return false;
        try
        {
            File.AppendAllText(Paths.HostsFile, $"{safeIp} {domain} {Tag}{Environment.NewLine}");
            return true;
        }
        catch (UnauthorizedAccessException) { throw new BhException("Failed to modify hosts file. Your antivirus may be blocking it, or the file is read-only."); }
        catch (IOException) { throw new BhException("Failed to modify hosts file. Your antivirus may be blocking it, or the file is read-only."); }
        catch { return false; }
    }

    /// <summary>
    /// Remove our tagged line(s) for exactly <paramref name="domain"/>.
    /// Hostnames are compared with ordinal-ignore-case equality on the parsed
    /// name fields — never substring matching, so removing "app.test" cannot
    /// take "myapp.test" or "app.test.local" with it (B6). Lines outside our
    /// managed block are never touched.
    /// </summary>
    public static bool Remove(string domain)
    {
        if (!IsValidDomain(domain)) return false;
        if (!Has(domain)) return true;
        if (!IsElevated()) return false;
        try
        {
            // A hosts line is "<addr> <name> [name…] [# comment]". Only a line we
            // tagged AND whose name list contains exactly this hostname is dropped.
            bool IsOursFor(string line)
            {
                if (!line.Contains(Tag, StringComparison.Ordinal)) return false;
                var payload = line.Split('#', 2)[0];
                var fields = payload.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2) return false;
                return fields.Skip(1).Any(n => string.Equals(n, domain, StringComparison.OrdinalIgnoreCase));
            }
            var kept = File.ReadAllLines(Paths.HostsFile).Where(l => !IsOursFor(l));
            File.WriteAllLines(Paths.HostsFile, kept);
            return true;
        }
        catch { return false; }
    }
}
