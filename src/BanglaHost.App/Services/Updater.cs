using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BanglaHost.App.Services;

/// <summary>
/// Store-backed update checker. BanglaHost ships through the Microsoft Store now,
/// so we ask the Store product page for the currently listed version and compare it
/// to this build. If there is a newer version we surface it in the UI and open the
/// Store deep link so the user can Update from there.
/// </summary>
public static class Updater
{
    // Microsoft Store product identity for BanglaHost Local Web Server.
    private const string StoreProductId = "9MWFKR8D8318";
    private const string StoreDeepLink  = "ms-windows-store://pdp/?productid=" + StoreProductId;
    private const string StoreWebUrl    = "https://apps.microsoft.com/detail/" + StoreProductId;

    /// <summary>
    /// The version we compare against the Store listing.
    ///
    /// Two bugs lived in the old one-liner:
    /// <list type="bullet">
    /// <item>It returned three components ("1.6.0"). The Store always publishes four ("1.6.0.0"),
    /// and <see cref="Version"/> treats an absent revision as −1, so 1.6.0.0 &gt; 1.6.0 — the app told
    /// every user an update was available immediately after they had just updated, forever.</item>
    /// <item>It read the *assembly* version. For a Store install the authoritative number is the
    /// MSIX package version from the manifest, which is what the Store compares against; the two
    /// diverge whenever the manifest is bumped without a rebuild.</item>
    /// </list>
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            // Packaged (Store/MSIX): the manifest version is what the Store listing reports.
            try
            {
                var pv = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{pv.Major}.{pv.Minor}.{pv.Build}.{pv.Revision}";
            }
            catch { /* unpackaged — Package.Current throws; fall through */ }

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}.{Math.Max(v.Revision, 0)}";
        }
    }

    public sealed record Result(bool UpdateAvailable, string Latest, string? AssetUrl, string? Notes, string? Error);

    // ── automatic-check throttle ──────────────────────────────────────────────
    // Store queries are cheap but we still don't want to hammer the endpoint on
    // every launch. Same 30-min gate as before; manual "Check for updates"
    // ignores this.
    private static string StampFile => System.IO.Path.Combine(BanglaHost.Core.Paths.Home, "run", "update-check.txt");
    private static readonly TimeSpan MinAutoInterval = TimeSpan.FromMinutes(30);

    public static bool AutomaticCheckDue()
    {
        try
        {
            if (File.Exists(StampFile) &&
                DateTime.TryParse(File.ReadAllText(StampFile).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
                DateTime.UtcNow - last < MinAutoInterval)
                return false;
        }
        catch { }
        return true;
    }

    public static void StampAutomaticCheck()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StampFile)!);
            File.WriteAllText(StampFile, DateTime.UtcNow.ToString("o"));
        }
        catch { }
    }

    public static string StoreLink => StoreDeepLink;
    public static string StoreWebLink => StoreWebUrl;

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10)
    }) { Timeout = TimeSpan.FromSeconds(15) };
    static Updater()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BanglaHost-Updater/1.0");
    }

    /// <summary>Query the Store for the latest published version of BanglaHost.</summary>
    public static async Task<Result> Check()
    {
        try
        {
            var http = _http;

            var latest = await TryDisplayCatalog(http) ?? await TryStorefrontApi(http) ?? await TryScrapeStorePage(http);
            if (string.IsNullOrEmpty(latest))
                return new Result(false, CurrentVersion, null, null, "Couldn't read the Store version right now.");

            var available = Compare(latest, CurrentVersion) > 0;
            return new Result(available, latest, available ? StoreDeepLink : null, null, null);
        }
        catch (Exception ex) { return new Result(false, CurrentVersion, null, null, ex.Message); }
    }

    // Storefront DisplayCatalog — authoritative source for Store listings.
    private static async Task<string?> TryDisplayCatalog(HttpClient http)
    {
        try
        {
            var url = $"https://displaycatalog.mp.microsoft.com/v7.0/products/{StoreProductId}" +
                      "?market=US&languages=en-US&fieldsTemplate=details";
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            // Iterate all package versions in the product and pick the highest.
            string? best = null;
            if (doc.RootElement.TryGetProperty("Product", out var product) &&
                product.TryGetProperty("DisplaySkuAvailabilities", out var skus))
            {
                foreach (var sku in skus.EnumerateArray())
                {
                    if (!sku.TryGetProperty("Sku", out var s)) continue;
                    if (!s.TryGetProperty("Properties", out var props)) continue;
                    if (!props.TryGetProperty("Packages", out var pkgs)) continue;
                    foreach (var pkg in pkgs.EnumerateArray())
                    {
                        if (!pkg.TryGetProperty("Version", out var ver)) continue;
                        var v = ver.GetString();
                        if (string.IsNullOrEmpty(v)) continue;
                        if (best is null || Compare(v, best) > 0) best = v;
                    }
                }
            }
            return best;
        }
        catch { return null; }
    }

    // Fallback: the storeedgefd API used by the Store client.
    private static async Task<string?> TryStorefrontApi(HttpClient http)
    {
        try
        {
            var url = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/pdp/productdetails/{StoreProductId}?market=US&locale=en-US&deviceFamily=Windows.Desktop";
            var json = await http.GetStringAsync(url);
            var m = Regex.Match(json, "\"Version\"\\s*:\\s*\"([0-9.]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    // Last-resort HTML scrape of the public Store page.
    private static async Task<string?> TryScrapeStorePage(HttpClient http)
    {
        try
        {
            var html = await http.GetStringAsync(StoreWebUrl);
            var m = Regex.Match(html, "\"version\"\\s*:\\s*\"([0-9.]+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Compare two version strings with missing components treated as 0.
    /// <c>Version.CompareTo</c> treats an absent Build/Revision as −1, so a plain CompareTo makes
    /// "1.6" &lt; "1.6.0" &lt; "1.6.0.0" — three spellings of the same release.
    /// </summary>
    internal static int Compare(string a, string b) => Norm(a).CompareTo(Norm(b));

    private static Version Norm(string s)
    {
        if (!Version.TryParse(Trim(s), out var v)) return new Version(0, 0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    private static string Trim(string s) => (s ?? "").Trim().TrimStart('v', 'V');

    /// <summary>Open the Microsoft Store product page (deep link, falls back to the web URL).</summary>
    public static void OpenStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = StoreDeepLink, UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo { FileName = StoreWebUrl, UseShellExecute = true }); } catch { }
        }
    }
}
