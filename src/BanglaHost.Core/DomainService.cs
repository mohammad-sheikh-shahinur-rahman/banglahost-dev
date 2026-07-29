using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record HostEntry(string IpAddress, string Domain, bool IsActive);

public static class DomainService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string HostsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers\\etc\\hosts");

    public static List<HostEntry> GetHostsEntries()
    {
        var list = new List<HostEntry>();
        if (!File.Exists(HostsFilePath)) return list;

        var lines = File.ReadAllLines(HostsFilePath);
        foreach (var line in lines)
        {
            var l = line.Trim();
            var isActive = !l.StartsWith("#");
            if (!isActive) l = l.TrimStart('#').Trim();

            var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                // Simple validation for IP
                if (System.Net.IPAddress.TryParse(parts[0], out _))
                {
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (!parts[i].StartsWith("#"))
                        {
                            list.Add(new HostEntry(parts[0], parts[i], isActive));
                        }
                    }
                }
            }
        }
        return list;
    }

    public static async Task<bool> UpdateHostEntryAsync(string ip, string domain, bool add)
    {
        // This requires elevation. We use the BanglaHost.Elevate helper.
        var exe = Path.Combine(AppContext.BaseDirectory, "BanglaHost.Elevate.exe");
        if (!File.Exists(exe)) throw new Exception("Elevate helper not found.");

        var action = add ? "add" : "remove";
        var args = $"hosts {action} {domain} {ip}";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false; // User denied UAC or failed
        }
    }

    public static async Task<string> CheckDnsResolutionAsync(string domain)
    {
        try
        {
            var ips = await System.Net.Dns.GetHostAddressesAsync(domain);
            return ips.Length > 0 ? ips[0].ToString() : "No records found.";
        }
        catch (Exception ex)
        {
            return $"DNS Error: {ex.Message}";
        }
    }

    public static async Task<bool> UpdateCloudflareDnsAsync(string apiToken, string zoneId, string domain, string ip)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records?name={domain}");
            req.Headers.Add("Authorization", $"Bearer {apiToken}");

            // 1. Get existing record
            var getResRaw = await _http.SendAsync(req);
            var getRes = await getResRaw.Content.ReadAsStringAsync();
            using var getDoc = JsonDocument.Parse(getRes);
            
            var result = getDoc.RootElement.GetProperty("result");
            string? recordId = null;
            if (result.GetArrayLength() > 0)
            {
                recordId = result[0].GetProperty("id").GetString();
            }

            var payload = new
            {
                type = "A",
                name = domain,
                content = ip,
                ttl = 1, // Auto
                proxied = true
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            if (recordId != null)
            {
                // 2. Update existing
                var putUrl = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records/{recordId}";
                var putRes = await _http.PutAsync(putUrl, content);
                return putRes.IsSuccessStatusCode;
            }
            else
            {
                // 3. Create new
                var postUrl = $"https://api.cloudflare.com/client/v4/zones/{zoneId}/dns_records";
                var postRes = await _http.PostAsync(postUrl, content);
                return postRes.IsSuccessStatusCode;
            }
        }
        catch
        {
            return false;
        }
    }
}
