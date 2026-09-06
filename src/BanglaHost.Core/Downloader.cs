using System.IO.Compression;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>
/// Fetches portable Windows builds into <c>bin\&lt;tool&gt;\â€¦</c> â€” the Windows analog of
/// the mac engine's <c>brew install</c>. No package manager: we pull the official
/// portable zips/exes and extract them ourselves.
/// </summary>
public static class Downloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private const string NginxPinned = "1.27.4";
    private const string MysqlPinned = "9.7.1";        // latest Oracle MySQL innovation (keeps --initialize-insecure)

    // Ã¢â€â‚¬Ã¢â€â‚¬ downloads go through Windows' built-in, Microsoft-SIGNED tools Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    // curl.exe fetches files and tar.exe extracts them, so the process that pulls
    // executables off the internet and writes them to disk is curl/tar (trusted),
    // NOT banglahost.exe. That keeps antivirus behavioral scanners from flagging banglahost
    // as a "dropper" â€” without bundling and without a code-signing certificate.
    private static string CurlExe => Path.Combine(Environment.SystemDirectory, "curl.exe");
    private static string TarExe  => Path.Combine(Environment.SystemDirectory, "tar.exe");
    private const string UA = "BanglaHost/0.1 (+https://apps.microsoft.com/store/detail/9MWFKR8D8318?cid=DevShareMCLPCB)";

    // ArgumentList (not a single string) so every arg is escaped by the runtime â€” a URL or path
    // can never break out and inject extra curl/tar flags.
    private static void Shell(string exe, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"could not start {Path.GetFileName(exe)}");
        // Drain both pipes concurrently — sequential ReadToEnd deadlocks once the child
        // fills the 4KB pipe (composer/laravel are chatty). Bounded 10 min wait, then kill.
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(600_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"{Path.GetFileName(exe)} timed out after 10 minutes");
        }
        var err = errTask.GetAwaiter().GetResult();
        outTask.GetAwaiter().GetResult();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} failed ({p.ExitCode}): {err.Trim()}");
    }

    /// <summary>Live download progress (0â€“100), set by the host during a tracked install.</summary>
    public static Action<double>? OnProgress;

    /// <summary>Download a file to <paramref name="dest"/> via the signed system curl.exe, reporting
    /// live progress (curl's --progress-bar on stderr is parsed for the percentage).</summary>
    private static async Task CurlTo(string url, string dest, string? ua = UA)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = CurlExe, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        // --progress-bar (instead of -s) gives a parseable "####  45.2%" on stderr.
        var args = new List<string> { "-fL", "--progress-bar", "--show-error", "--retry", "5", "--retry-delay", "2",
                                      "--speed-limit", "2048", "--speed-time", "20", "--connect-timeout", "30" };
        // Some CDNs (notably dev.mysql.com) 403 our custom User-Agent but serve curl's default fine,
        // so callers can pass ua: null to send no -A and let curl use its own UA.
        if (!string.IsNullOrEmpty(ua)) { args.Add("-A"); args.Add(ua); }
        args.Add("-o"); args.Add(dest); args.Add(url);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi)!;
        var tail = new System.Text.StringBuilder();
        var buf = new char[256];
        int n;
        while ((n = await p.StandardError.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            tail.Append(buf, 0, n);
            if (tail.Length > 4000) tail.Remove(0, tail.Length - 1000);   // keep the tail only
            var m = System.Text.RegularExpressions.Regex.Matches(tail.ToString(), @"(\d+(?:\.\d+)?)%");
            if (m.Count > 0 && double.TryParse(m[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                OnProgress?.Invoke(pct);
        }
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"curl failed ({p.ExitCode}): {tail.ToString().Trim()}");
        if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
            throw new InvalidOperationException($"download produced no file: {url}");
    }

    private static async Task<string> DownloadToTmp(string url, string fileName, string? ua = UA)
    {
        // Unique per call — two concurrent installs must never share "mysql.zip" etc.
        var unique = $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var dest = Path.Combine(Paths.Tmp, unique);
        await CurlTo(url, dest, ua);
        return dest;
    }

    /// <summary>Extract a zip via the signed system tar.exe (so tar, not banglahost, creates the exes).
    /// Junk helper exes that trip generic AV heuristics (e.g. mingw's sizes.exe) are excluded so
    /// they never touch disk and can't be flagged.</summary>
    private static void ExtractZip(string zip, string destDir)
    {
        Directory.CreateDirectory(destDir);
        // bsdtar: --exclude must precede -f, and its * does NOT cross '/', so match the basename.
        Shell(TarExe, "--exclude", "sizes.exe", "-xf", zip, "-C", destDir);
        try { File.Delete(zip); } catch { }
    }

    /// <summary>Fetches a PECL extension DLL by scraping windows.php.net for the correct build.</summary>
    public static async Task InstallPeclExtension(string phpVersion, string vc, string name, string extDir)
    {
        var url = $"https://windows.php.net/downloads/pecl/releases/{name}/";
        string html;
        try { html = await Http.GetStringAsync(url); }
        catch { throw new BhException($"PECL extension '{name}' not found on windows.php.net."); }

        // Match e.g. php_redis-6.0.2-8.4-nts-vs17-x64.zip
        var pattern = $@"php_{name}-([\d\.]+)-{phpVersion}-nts-{vc}-x64\.zip";
        var matches = System.Text.RegularExpressions.Regex.Matches(html, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (matches.Count == 0)
            throw new BhException($"No matching PECL build found for {name} on PHP {phpVersion} ({vc}).");

        var best = matches.Cast<System.Text.RegularExpressions.Match>()
            .OrderByDescending(m => Version.TryParse(m.Groups[1].Value, out var v) ? v : new Version(0, 0))
            .First().Value;

        var downloadUrl = url + best;
        var tmpZip = await DownloadToTmp(downloadUrl, best);
        
        var extractDir = Path.Combine(Paths.Tmp, $"pecl_{name}_{phpVersion}");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ExtractZip(tmpZip, extractDir);

        var dll = Path.Combine(extractDir, $"php_{name}.dll");
        if (File.Exists(dll))
        {
            Directory.CreateDirectory(extDir);
            File.Copy(dll, Path.Combine(extDir, $"php_{name}.dll"), true);
        }
        else throw new BhException($"Downloaded ZIP did not contain php_{name}.dll");
        
        try { Directory.Delete(extractDir, true); File.Delete(tmpZip); } catch { }
    }

    /// <summary>Delete every *.exe under <paramref name="dir"/> except the named keepers (case-insensitive).</summary>
    private static void PruneExes(string dir, params string[] keep)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
                if (!keep.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    /// <summary>Install nginx â€” resolves the latest version from nginx.org, pin as fallback. Only the
    /// binaries in bin\nginx are replaced; the running config lives in Home\nginx and is regenerated
    /// on every Start, so an update never loses site config.</summary>
    public static async Task<string> InstallNginx()
    {
        var ver = await LatestNginx();
        try { return DoInstallNginx(ver); }
        catch when (ver != NginxPinned) { return DoInstallNginx(NginxPinned); }
    }

    private static async Task<string> LatestNginx()
    {
        try
        {
            var html = await ApiGet("https://nginx.org/en/download.html");
            var best = System.Text.RegularExpressions.Regex.Matches(html, @"nginx-(\d+\.\d+\.\d+)")
                .Select(m => m.Groups[1].Value).OrderByDescending(Vparse).FirstOrDefault();
            return best ?? NginxPinned;
        }
        catch { return NginxPinned; }
    }

    private static string DoInstallNginx(string ver)
    {
        var url = $"https://nginx.org/download/nginx-{ver}.zip";
        var zip = DownloadToTmp(url, $"nginx-{ver}.zip").GetAwaiter().GetResult();
        var dir = Path.Combine(Paths.Bin, "nginx");
        // The zip contains nginx-<ver>\, so extract ALONGSIDE rather than deleting bin\nginx first â€”
        // a still-running old nginx.exe would lock its dir and block the delete. The newest version
        // wins in Tools.NginxExe; old version dirs are best-effort pruned (a locked one is skipped).
        ExtractZip(zip, dir);
        PruneOldVersionDirs(dir, $"nginx-{ver}");
        return Tools.NginxExe() ?? throw new InvalidOperationException("nginx.exe not found after extract");
    }

    /// <summary>Best-effort removal of sibling version dirs under <paramref name="parent"/>, keeping
    /// <paramref name="keep"/>. A dir locked by a running process is silently skipped.</summary>
    private static void PruneOldVersionDirs(string parent, string keep)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(parent))
                if (!string.Equals(Path.GetFileName(d), keep, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }

    public static async Task<string> InstallPhp(string version)
    {
        // Resolve the current patch + the NTS x64 zip path from the official manifest.
        var json = await Http.GetStringAsync("https://windows.php.net/downloads/releases/releases.json");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(version, out var branch))
            throw new InvalidOperationException($"PHP {version} not in releases.json (try an active branch)");

        string? relPath = null;
        foreach (var prop in branch.EnumerateObject())
        {
            // keys look like "nts-vs16-x64", "nts-vs17-x64", "ts-vs16-x64", â€¦
            if (prop.Name.StartsWith("nts-") && prop.Name.EndsWith("-x64") &&
                prop.Value.TryGetProperty("zip", out var zipEl) &&
                zipEl.TryGetProperty("path", out var pathEl))
            {
                relPath = pathEl.GetString();
                break;
            }
        }
        if (relPath is null)
            throw new InvalidOperationException($"no NTS x64 build listed for PHP {version}");

        var zip = await DownloadToTmp($"https://windows.php.net/downloads/releases/{relPath}", relPath);
        var dest = Path.Combine(Paths.Bin, "php", version);
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        ExtractZip(zip, dest);
        SeedPhpIni(dest, version);
        return Tools.PhpCgiExe(version) ?? throw new InvalidOperationException("php-cgi.exe not found after extract");
    }

    /// <summary>If the build ships only php.ini-development, copy it to php.ini (php-cgi needs one).</summary>
    private static void SeedPhpIni(string phpDir, string version)
    {
        var ini = Path.Combine(phpDir, "php.ini");
        if (File.Exists(ini)) return;
        var seed = Path.Combine(phpDir, "php.ini-development");
        if (File.Exists(seed))
        {
            var text = File.ReadAllText(seed);
            // enable the extensions BanglaHost sites commonly need
            text = text.Replace(";extension_dir = \"ext\"", "extension_dir = \"ext\"");
            foreach (var ext in new[] { "curl", "mbstring", "openssl", "mysqli", "pdo_mysql", "gd", "fileinfo", "zip", "intl", "exif" })
                text = text.Replace($";extension={ext}", $"extension={ext}");

            // Setup error logging but disable display_errors (crucial for CGI otherwise 500 errors happen)
            text = text.Replace("error_reporting = E_ALL & ~E_DEPRECATED & ~E_STRICT", "error_reporting = E_ALL");
            text = text.Replace("display_errors = On", "display_errors = Off"); // sometimes On in dev
            text = text.Replace("display_startup_errors = On", "display_startup_errors = Off");
            text = text.Replace(";log_errors = On", "log_errors = On");
            
            // Required for php-cgi to run under Apache mod_cgi
            text = text.Replace(";cgi.force_redirect = 1", "cgi.force_redirect = 0");
            
            var logPath = Path.Combine(Paths.Logs, $"php-{version}-error.log").Replace("\\", "/");
            text += $"\nerror_log = \"{logPath}\"\n";

            File.WriteAllText(ini, text);
        }
    }

    public static async Task<string> InstallMkcert()
    {
        // Latest mkcert release asset for windows amd64.
        var rel = await Http.GetStringAsync("https://api.github.com/repos/FiloSottile/mkcert/releases/latest");
        using var doc = JsonDocument.Parse(rel);
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").Contains("windows-amd64"));
        var url = asset.GetProperty("browser_download_url").GetString()
                  ?? throw new InvalidOperationException("mkcert windows asset not found");
        var dir = Path.Combine(Paths.Bin, "mkcert");
        var dest = Path.Combine(dir, "mkcert.exe");
        await CurlTo(url, dest);
        return dest;
    }

    private static async Task<string> GithubAsset(string repo, Func<string, bool> match)
    {
        // /releases/latest 404s when every release is marked pre-release (e.g. nono303/memcached),
        // so fall back to the full releases list and scan newest-first.
        string json;
        try { json = await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"); }
        catch (HttpRequestException) { json = "[]"; }

        using (var doc = JsonDocument.Parse(json))
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("assets", out var a1))
                foreach (var a in a1.EnumerateArray())
                    if (match(a.GetProperty("name").GetString() ?? "")) return a.GetProperty("browser_download_url").GetString()!;

        var list = await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=20");
        using var doc2 = JsonDocument.Parse(list);
        foreach (var rel in doc2.RootElement.EnumerateArray())
            if (rel.TryGetProperty("assets", out var a2))
                foreach (var a in a2.EnumerateArray())
                    if (match(a.GetProperty("name").GetString() ?? "")) return a.GetProperty("browser_download_url").GetString()!;

        throw new InvalidOperationException($"no matching asset in {repo} releases");
    }

    public static async Task<string> InstallMailpit()
    {
        var url = await GithubAsset("axllent/mailpit",
            n => n.Contains("windows", StringComparison.OrdinalIgnoreCase) && n.Contains("amd64") && n.EndsWith(".zip"));
        var zip = await DownloadToTmp(url, "mailpit.zip");
        var dir = Path.Combine(Paths.Bin, "mailpit");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.MailpitExe() ?? throw new InvalidOperationException("mailpit.exe not found after extract");
    }

    // Pins are the OFFLINE FALLBACK only. Install/Update resolve the real latest from the
    // vendors' own release APIs, so when MariaDB 13 / MySQL 9.8 ship, "Reinstall (update)"
    // picks them up automatically â€” no code change needed. The pin is used only if the API
    // is unreachable.
    private const string MariadbPinned = "12.3.2";

    /// <summary>A short-timeout client for the tiny version-resolution API calls (the download
    /// client has a 15-min timeout that's wrong for a quick JSON GET).</summary>
    private static readonly HttpClient ApiHttp = new() { Timeout = TimeSpan.FromSeconds(12) };

    private static async Task<string> ApiGet(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UA);
        using var resp = await ApiHttp.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    private static Version Vparse(string s) => Version.TryParse(s, out var v) ? v : new Version(0, 0);

    /// <summary>Latest MariaDB STABLE point release from the official release API; pin on failure.</summary>
    private static async Task<string> LatestMariadb()
    {
        try
        {
            using var idx = JsonDocument.Parse(await ApiGet("https://downloads.mariadb.org/rest-api/mariadb/"));
            string? major = null;
            foreach (var mr in idx.RootElement.GetProperty("major_releases").EnumerateArray())
                if (mr.GetProperty("release_status").GetString() == "Stable") { major = mr.GetProperty("release_id").GetString(); break; }
            if (major is null) return MariadbPinned;
            using var rel = JsonDocument.Parse(await ApiGet($"https://downloads.mariadb.org/rest-api/mariadb/{major}/"));
            var latest = rel.RootElement.GetProperty("releases").EnumerateObject()
                            .Select(p => p.Name).OrderByDescending(Vparse).FirstOrDefault();
            return latest ?? MariadbPinned;
        }
        catch { return MariadbPinned; }
    }

    /// <summary>Latest MySQL GA point release (endoflife.date catalog); pin on failure.</summary>
    private static async Task<string> LatestMysql()
    {
        try
        {
            using var doc = JsonDocument.Parse(await ApiGet("https://endoflife.date/api/mysql.json"));
            var latest = doc.RootElement.EnumerateArray()
                .Select(c => c.TryGetProperty("latest", out var l) ? l.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderByDescending(s => Vparse(s!)).FirstOrDefault();
            return latest ?? MysqlPinned;
        }
        catch { return MysqlPinned; }
    }

    /// <summary>Download MariaDB (portable winx64 zip) into bin\mariadb â€” latest stable, pin fallback.
    /// Only the bin\mariadb binaries are replaced; the data dir (data-mariadb) is left untouched, so
    /// existing databases survive an update.</summary>
    public static async Task<string> InstallMariadb()
    {
        var ver = await LatestMariadb();
        try { return DoInstallMariadb(ver); }
        catch when (ver != MariadbPinned) { return DoInstallMariadb(MariadbPinned); }   // bad/missing winx64 zip Ã¢â€ â€™ known-good pin
    }

    private static string DoInstallMariadb(string ver)
    {
        var url = $"https://archive.mariadb.org/mariadb-{ver}/winx64-packages/mariadb-{ver}-winx64.zip";
        var zip = DownloadToTmp(url, "mariadb.zip").GetAwaiter().GetResult();
        var dir = Path.Combine(Paths.Bin, "mariadb");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);   // binaries only â€” data-mariadb is a separate dir
        ExtractZip(zip, dir);
        // Validate the engine we just installed (NOT the ambiguous MysqldExe(), which prefers MariaDB
        // and would mask a failed extract when the other engine is present).
        return Tools.MysqldExe("mariadb")
            ?? throw new InvalidOperationException("MariaDB mysqld.exe not found after extract (download blocked by antivirus, or incomplete).");
    }

    /// <summary>Download Oracle MySQL (portable winx64 zip) into bin\mysql â€” latest GA, pin fallback.
    /// Replaces only the bin\mysql binaries; the data dir is untouched so databases survive.</summary>
    public static async Task<string> InstallDb()
    {
        var ver = await LatestMysql();
        try { return DoInstallDb(ver); }
        catch when (ver != MysqlPinned) { return DoInstallDb(MysqlPinned); }
    }

    private static string DoInstallDb(string ver)
    {
        // dev.mysql.com/get/ 302-redirects to the CDN; curl follows it. Series subdir (MySQL-9.7)
        // is derived from the version.
        var series = string.Join('.', ver.Split('.').Take(2));
        var url = $"https://dev.mysql.com/get/Downloads/MySQL-{series}/mysql-{ver}-winx64.zip";
        // dev.mysql.com's CDN 403s our custom User-Agent (but serves curl's default UA) â€” send no -A.
        var zip = DownloadToTmp(url, "mysql.zip", ua: null).GetAwaiter().GetResult();
        var dir = Path.Combine(Paths.Bin, "mysql");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        // Validate MySQL specifically (NOT the ambiguous MysqldExe(), which prefers MariaDB and would
        // falsely report success â€” finding MariaDB's mysqld.exe â€” when the MySQL extract produced nothing).
        return Tools.MysqldExe("mysql")
            ?? throw new InvalidOperationException("MySQL mysqld.exe not found after extract (download blocked by antivirus, or incomplete).");
    }

    private const string PgPinned = "16.4-1";

    /// <summary>Install PostgreSQL (EDB portable binaries) â€” latest version via endoflife.date, pin
    /// fallback. NOTE: a major-version bump (e.g. 16 Ã¢â€ â€™ 18) can't read an existing pgdata in place
    /// (PostgreSQL needs pg_upgrade), so an update is best on a fresh data dir.</summary>
    public static async Task<string> InstallPostgres()
    {
        var ver = await LatestPostgres();
        try { return DoInstallPostgres(ver); }
        catch when (ver != PgPinned) { return DoInstallPostgres(PgPinned); }
    }

    private static async Task<string> LatestPostgres()
    {
        try
        {
            using var doc = JsonDocument.Parse(await ApiGet("https://endoflife.date/api/postgresql.json"));
            var latest = doc.RootElement.EnumerateArray()
                .Select(c => c.TryGetProperty("latest", out var l) ? l.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => Vparse(s!)).FirstOrDefault();
            return latest is null ? PgPinned : $"{latest}-1";   // EDB appends a build number; -1 is the first/standard build
        }
        catch { return PgPinned; }
    }

    private static string DoInstallPostgres(string ver)
    {
        var url = $"https://get.enterprisedb.com/postgresql/postgresql-{ver}-windows-x64-binaries.zip";
        var zip = DownloadToTmp(url, "postgresql.zip").GetAwaiter().GetResult();
        var dir = Path.Combine(Paths.Bin, "postgresql");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);   // Ã¢â€ â€™ bin\postgresql\pgsql\bin\postgres.exe
        return Tools.PostgresExe() ?? throw new InvalidOperationException("postgres.exe not found after extract");
    }

    // Fallback only â€” Apache Lounge URLs carry a build date + VS toolset that change over time
    // (it has moved VS17 Ã¢â€ â€™ VS18), so we scrape the current latest and keep this as last resort.
    private const string ApachePinned = "https://www.apachelounge.com/download/VS18/binaries/httpd-2.4.68-260617-Win64-VS18.zip";

    /// <summary>Install Apache (Apache Lounge build) â€” scrapes the current latest Win64 zip from the
    /// download page (its URLs carry a build date + VS toolset), pin as last resort.</summary>
    public static async Task<string> InstallApache()
    {
        var url = await LatestApacheUrl() ?? ApachePinned;
        var zip = await DownloadToTmp(url, "httpd.zip");
        var dir = Path.Combine(Paths.Bin, "apache");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);   // Ã¢â€ â€™ bin\apache\Apache24\bin\httpd.exe
        return Tools.HttpdExe() ?? throw new InvalidOperationException("httpd.exe not found after extract");
    }

    private static async Task<string?> LatestApacheUrl()
    {
        try
        {
            var html = await ApiGet("https://www.apachelounge.com/download/");
            // hrefs look like: VS18/binaries/httpd-2.4.68-260617-Win64-VS18.zip
            var best = System.Text.RegularExpressions.Regex.Matches(
                    html, @"VS\d+/binaries/httpd-2\.4\.\d+-\d+-Win64-VS\d+\.zip",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(m => m.Value).Distinct()
                .OrderByDescending(s =>
                {
                    var n = System.Text.RegularExpressions.Regex.Match(s, @"httpd-2\.4\.(\d+)-(\d+)-Win64-VS(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    // newest by (httpd patch, build date, VS toolset)
                    return (int.Parse(n.Groups[1].Value), long.Parse(n.Groups[2].Value), int.Parse(n.Groups[3].Value));
                })
                .FirstOrDefault();
            return best is null ? null : "https://www.apachelounge.com/download/" + best;
        }
        catch { return null; }
    }

    public static async Task<string> InstallRedis()
    {
        // tporadowski/redis ships Redis-x64-<ver>.zip (redis-server.exe inside)
        var url = await GithubAsset("tporadowski/redis",
            n => n.StartsWith("Redis-x64", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var zip = await DownloadToTmp(url, "redis.zip");
        var dir = Path.Combine(Paths.Bin, "redis");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.RedisServerExe() ?? throw new InvalidOperationException("redis-server.exe not found after extract");
    }

    public static async Task<string> InstallMemcached()
    {
        // jefyt/memcached-windows ships memcached-<ver>-win64-mingw.zip (memcached.exe + mingw deps,
        // no cygwin). Skip the sibling libevent/libressl zips in the same release.
        var url = await GithubAsset("jefyt/memcached-windows",
            n => n.StartsWith("memcached", StringComparison.OrdinalIgnoreCase)
              && n.Contains("win64") && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var zip = await DownloadToTmp(url, "memcached.zip");
        var dir = Path.Combine(Paths.Bin, "memcached");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        // The mingw build bundles throwaway helper exes (e.g. sizes.exe) that trip generic
        // AV heuristics. Keep only memcached.exe + its DLLs so nothing junk ships to users.
        PruneExes(dir, keep: "memcached.exe");
        return Tools.MemcachedExe() ?? throw new InvalidOperationException("memcached.exe not found after extract");
    }

    public static async Task<string> InstallCloudflared()
    {
        var url = await GithubAsset("cloudflare/cloudflared",
            n => n.Equals("cloudflared-windows-amd64.exe", StringComparison.OrdinalIgnoreCase));
        var dest = Path.Combine(Paths.Bin, "cloudflared", "cloudflared.exe");
        await CurlTo(url, dest);
        return dest;
    }

    // â”€â”€ MongoDB (Community Server, portable zip) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const string MongoPinned = "7.0.14";
    public static async Task<string> InstallMongodb()
    {
        var ver = MongoPinned;
        var url = $"https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-{ver}.zip";
        var zip = await DownloadToTmp(url, $"mongodb-{ver}.zip");
        var dir = Path.Combine(Paths.Bin, "mongodb");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.MongodExe() ?? throw new InvalidOperationException("mongod.exe not found after extract");
    }

    // â”€â”€ Meilisearch (single-file windows exe from GitHub releases) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallMeilisearch()
    {
        var url = await GithubAsset("meilisearch/meilisearch",
            n => n.Equals("meilisearch-windows-amd64.exe", StringComparison.OrdinalIgnoreCase));
        var dest = Path.Combine(Paths.Bin, "meilisearch", "meilisearch.exe");
        await CurlTo(url, dest);
        return dest;
    }

    // â”€â”€ Composer (single-file composer.phar; needs a working PHP) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallComposer()
    {
        var dir = Path.Combine(Paths.Bin, "composer");
        var dest = Path.Combine(dir, "composer.phar");
        await CurlTo("https://getcomposer.org/composer-stable.phar", dest);
        // convenience wrapper: composer.bat -> php composer.phar
        var php = Tools.PhpExe(Config.Load().DefaultPhp);
        var bat = Path.Combine(dir, "composer.bat");
        var line = php is not null
            ? $"@echo off\r\n\"{php}\" \"{dest}\" %*\r\n"
            : $"@echo off\r\nphp \"{dest}\" %*\r\n";
        File.WriteAllText(bat, line);
        return dest;
    }

    // â”€â”€ Go (portable zip from go.dev) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const string GoPinned = "1.23.4";
    public static async Task<string> InstallGo()
    {
        var ver = GoPinned;
        var url = $"https://go.dev/dl/go{ver}.windows-amd64.zip";
        var zip = await DownloadToTmp(url, $"go-{ver}.zip");
        var dir = Path.Combine(Paths.Bin, "go");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.GoExe() ?? throw new InvalidOperationException("go.exe not found after extract");
    }

    // â”€â”€ Rust (rustup-init bootstrap; installs cargo/rustc into bin\rust) â”€â”€â”€â”€
    public static async Task<string> InstallRust()
    {
        var dir = Path.Combine(Paths.Bin, "rust");
        Directory.CreateDirectory(dir);
        var init = Path.Combine(Paths.Tmp, "rustup-init.exe");
        await CurlTo("https://win.rustup.rs/x86_64", init);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = init,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.Environment["CARGO_HOME"] = dir;
        psi.Environment["RUSTUP_HOME"] = Path.Combine(dir, "rustup");
        foreach (var a in new[] { "-y", "--no-modify-path", "--default-toolchain", "stable", "--profile", "minimal" })
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"rustup-init failed ({p.ExitCode}): {err.Trim()}");
        return Tools.CargoExe() ?? throw new InvalidOperationException("cargo.exe not found after rustup");
    }

    // â”€â”€ Ollama (single-file windows installer; grab the standalone exe) â”€â”€â”€â”€â”€
    public static async Task<string> InstallOllama()
    {
        var url = await GithubAsset("ollama/ollama",
            n => n.Equals("ollama-windows-amd64.zip", StringComparison.OrdinalIgnoreCase));
        var zip = await DownloadToTmp(url, "ollama.zip");
        var dir = Path.Combine(Paths.Bin, "ollama");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.OllamaExe() ?? throw new InvalidOperationException("ollama.exe not found after extract");
    }

    // â”€â”€ Valkey (Redis-compatible fork) â€” Windows build via memurai-compatible port â”€â”€
    public static async Task<string> InstallValkey()
    {
        var url = await GithubAsset("valkey-io/valkey",
            n => n.Contains("windows", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip"));
        var zip = await DownloadToTmp(url, "valkey.zip");
        var dir = Path.Combine(Paths.Bin, "valkey");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.ValkeyExe() ?? throw new InvalidOperationException("valkey-server.exe not found after extract");
    }

    // â”€â”€ MailHog (single-file exe from GitHub releases) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallMailhog()
    {
        var url = await GithubAsset("mailhog/MailHog",
            n => n.Contains("windows", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe"));
        var dest = Path.Combine(Paths.Bin, "mailhog", "MailHog.exe");
        await CurlTo(url, dest);
        return dest;
    }

    // â”€â”€ SQLite (official sqlite-tools zip; single sqlite3.exe) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallSqlite()
    {
        // sqlite.org publishes a stable "sqlite-tools-win-x64" zip â€” resolve the current name from the DL page.
        var html = await ApiGet("https://sqlite.org/download.html");
        var m = System.Text.RegularExpressions.Regex.Match(html, @"(\d{4}/sqlite-tools-win-x64-\d+\.zip)");
        var relPath = m.Success ? m.Groups[1].Value : throw new InvalidOperationException("SQLite tools zip not found on sqlite.org");
        var url = $"https://sqlite.org/{relPath}";
        var zip = await DownloadToTmp(url, "sqlite.zip");
        var dir = Path.Combine(Paths.Bin, "sqlite");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.SqliteExe() ?? throw new InvalidOperationException("sqlite3.exe not found after extract");
    }

    // â”€â”€ pnpm (single-file exe) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallPnpm()
    {
        var url = await GithubAsset("pnpm/pnpm",
            n => n.Equals("pnpm-win-x64.exe", StringComparison.OrdinalIgnoreCase));
        var dest = Path.Combine(Paths.Bin, "pnpm", "pnpm.exe");
        await CurlTo(url, dest);
        return dest;
    }

    // â”€â”€ Yarn (Berry release-yarn-<ver>.js standalone; also drop a batch wrapper) â”€â”€
    public static async Task<string> InstallYarn()
    {
        // Yarn Berry is distributed as a single JS file, executed by any node. Fetch the latest release JS.
        var url = await GithubAsset("yarnpkg/berry",
            n => n.StartsWith("yarn-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".js"));
        var dir = Path.Combine(Paths.Bin, "yarn");
        var dest = Path.Combine(dir, "yarn.js");
        await CurlTo(url, dest);
        // Simple wrapper that invokes node with the JS â€” Yarn resolves node via PATH (fnm keeps it there).
        File.WriteAllText(Path.Combine(dir, "yarn.bat"), "@echo off\r\nnode \"%~dp0yarn.js\" %*\r\n");
        return dest;
    }

    // â”€â”€ Bun (single-file exe inside the release zip) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallBun()
    {
        var url = await GithubAsset("oven-sh/bun",
            n => n.Equals("bun-windows-x64.zip", StringComparison.OrdinalIgnoreCase));
        var zip = await DownloadToTmp(url, "bun.zip");
        var dir = Path.Combine(Paths.Bin, "bun");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.BunExe() ?? throw new InvalidOperationException("bun.exe not found after extract");
    }

    // â”€â”€ Deno (single-file exe inside the release zip) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallDeno()
    {
        var url = await GithubAsset("denoland/deno",
            n => n.Equals("deno-x86_64-pc-windows-msvc.zip", StringComparison.OrdinalIgnoreCase));
        var zip = await DownloadToTmp(url, "deno.zip");
        var dir = Path.Combine(Paths.Bin, "deno");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.DenoExe() ?? throw new InvalidOperationException("deno.exe not found after extract");
    }

    // â”€â”€ Java (Adoptium Temurin JDK, LTS 21 portable zip) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static async Task<string> InstallJava()
    {
        // Adoptium API resolves the latest LTS JDK 21 zip for windows-x64.
        var api = "https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse";
        var json = await ApiGet(api);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        string? url = null;
        foreach (var e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("binary", out var b) && b.TryGetProperty("package", out var p) && p.TryGetProperty("link", out var l))
            { url = l.GetString(); break; }
        if (url is null) throw new InvalidOperationException("Adoptium API returned no JDK 21 win-x64 asset");
        var zip = await DownloadToTmp(url, "temurin-jdk.zip");
        var dir = Path.Combine(Paths.Bin, "java");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.JavaExe() ?? throw new InvalidOperationException("java.exe not found after extract");
    }

    // â”€â”€ Docker (delegates to Docker Desktop installer â€” user runs it, we detect after) â”€â”€
    public static async Task<string> InstallDocker()
    {
        // We don't silently install Docker Desktop (needs UAC + reboot); we download the installer
        // and launch it so the user can complete the wizard themselves. Once installed we detect
        // `docker.exe` on PATH via `where docker`.
        var dest = Path.Combine(Paths.Tmp, "DockerDesktopInstaller.exe");
        await CurlTo("https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe", dest);
        var psi = new System.Diagnostics.ProcessStartInfo { FileName = dest, UseShellExecute = true };
        using (System.Diagnostics.Process.Start(psi)) { }
        return dest;
    }

    // â”€â”€ Git for Windows (PortableGit â€” no admin, no shell integration) â”€â”€â”€
    public static async Task<string> InstallGit()
    {
        var url = await GithubAsset("git-for-windows/git",
            n => n.StartsWith("PortableGit-", StringComparison.OrdinalIgnoreCase)
              && n.Contains("64-bit", StringComparison.OrdinalIgnoreCase)
              && n.EndsWith(".7z.exe", StringComparison.OrdinalIgnoreCase));
        // PortableGit is a self-extracting 7z that runs silently with -y -o<dir>.
        var installer = await DownloadToTmp(url, "PortableGit.7z.exe");
        var dir = Path.Combine(Paths.Bin, "git");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installer, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-y", "-gm2", $"-o{dir}" }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        try { File.Delete(installer); } catch { }
        return Tools.GitExe() ?? throw new InvalidOperationException("git.exe not found after PortableGit extract");
    }

    private const string PyVersionPrefix = "cpython-3.13";
    // A pinned, known-good direct CDN URL (release download, NOT api.github.com) â€” so the common case
    // never touches GitHub's 60/hr/IP rate-limited API. Falls back to the API-resolved latest if this 404s.
    private const string PyPinnedUrl =
        "https://github.com/astral-sh/python-build-standalone/releases/download/20250604/cpython-3.13.4+20250604-x86_64-pc-windows-msvc-install_only.tar.gz";

    /// <summary>Install a portable, relocatable CPython (astral-sh/python-build-standalone â€” the same
    /// builds uv uses) into bin\python. The "install_only" archive includes pip + venv and needs no
    /// installer/UAC. Download with curl's DEFAULT UA (no custom BanglaHost UA Ã¢â€ â€™ avoids any CDN 403).</summary>
    public static async Task<string> InstallPython()
    {
        try { return DoInstallPython(PyPinnedUrl); }                  // pinned direct URL â€” no API call
        catch
        {
            // fallback: resolve the newest via the API (subject to GitHub's rate limit)
            var url = await GithubAsset("astral-sh/python-build-standalone",
                n => n.StartsWith(PyVersionPrefix, StringComparison.OrdinalIgnoreCase)
                  && n.EndsWith("x86_64-pc-windows-msvc-install_only.tar.gz", StringComparison.OrdinalIgnoreCase));
            return DoInstallPython(url);
        }
    }

    private static string DoInstallPython(string url)
    {
        var tgz = DownloadToTmp(url, "python.tar.gz", ua: null).GetAwaiter().GetResult();
        var dir = Path.Combine(Paths.Bin, "python");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(tgz, dir);   // tar.exe handles .tar.gz too
        return Tools.PythonExe() ?? throw new InvalidOperationException("python.exe not found after extract");
    }

    public static async Task<string> InstallFnm()
    {
        // fnm ships fnm-windows.zip (contains fnm.exe)
        var url = await GithubAsset("Schniz/fnm",
            n => n.Contains("windows", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip"));
        var zip = await DownloadToTmp(url, "fnm.zip");
        var dir = Path.Combine(Paths.Bin, "fnm");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ExtractZip(zip, dir);
        return Tools.FnmExe() ?? throw new InvalidOperationException("fnm.exe not found after extract");
    }

    /// <summary>Download + extract the Windows ionCube loaders for a VC build (cached per vc).
    /// We fetch the NON-thread-safe (nonts) bundle because BanglaHost's php-cgi builds are NTS â€” its
    /// loader imports php8.dll. The plain `ioncube_loaders_win_<vc>_x86-64.zip` is the TS bundle,
    /// whose loader imports php8ts.dll and fails to load into NTS PHP (LoadLibrary error 126).
    /// Cached under a `nonts-<vc>` dir so an old (TS) `<vc>` cache is never reused.</summary>
    public static async Task<string> InstallIoncube(string vc)
    {
        var dir = Path.Combine(Paths.Bin, "ioncube", $"nonts-{vc}");
        if (Directory.Exists(Path.Combine(dir, "ioncube"))) return dir;   // already extracted
        var url = $"https://downloads.ioncube.com/loader_downloads/ioncube_loaders_win_nonts_{vc}_x86-64.zip";
        var zip = await DownloadToTmp(url, $"ioncube_nonts_{vc}.zip");
        // A 404 returns an HTML page, not a zip â€” ExtractZip throws, surfaced to the caller.
        ExtractZip(zip, dir);
        return dir;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(src, dst));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), overwrite: true);
    }

    /// <summary>Generic Composer create-project runner. Wipes the target root, runs
    /// `composer create-project --prefer-dist &lt;package&gt; &lt;root&gt;`, and if a .env file
    /// is produced (Laravel/Symfony/CakePHP) rewrites DB_* / DATABASE_URL to point at
    /// BanglaHost's MySQL as passwordless root.</summary>
    public static async Task InstallComposerProject(string root, string package, string dbName)
    {
        var php = Tools.PhpExe(Config.Load().DefaultPhp)
                  ?? throw new InvalidOperationException("PHP not installed - install a PHP version first");
        var composer = Tools.ComposerPhar()
                       ?? throw new InvalidOperationException("Composer not installed - install Composer first");

        Directory.CreateDirectory(root);
        foreach (var f in Directory.EnumerateFileSystemEntries(root))
            try { if (Directory.Exists(f)) Directory.Delete(f, true); else File.Delete(f); } catch { }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = php,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(root)!,
        };
        foreach (var a in new[] { composer, "create-project", "--prefer-dist", "--no-interaction", package, root })
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"composer create-project {package} failed ({p.ExitCode}): {err.Trim()}");

        // Rewrite .env DB_* if present (Laravel / Symfony style).
        var envFile = Path.Combine(root, ".env");
        if (File.Exists(envFile))
        {
            var lines = await File.ReadAllLinesAsync(envFile);
            var rootPw = Config.Load().RootPassword;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (l.StartsWith("DB_CONNECTION=", StringComparison.Ordinal)) lines[i] = "DB_CONNECTION=mysql";
                else if (l.StartsWith("DB_HOST=",     StringComparison.Ordinal)) lines[i] = "DB_HOST=127.0.0.1";
                else if (l.StartsWith("DB_PORT=",     StringComparison.Ordinal)) lines[i] = "DB_PORT=3306";
                else if (l.StartsWith("DB_DATABASE=", StringComparison.Ordinal)) lines[i] = $"DB_DATABASE={dbName}";
                else if (l.StartsWith("DB_USERNAME=", StringComparison.Ordinal)) lines[i] = "DB_USERNAME=root";
                else if (l.StartsWith("DB_PASSWORD=", StringComparison.Ordinal)) lines[i] = $"DB_PASSWORD={rootPw}";
                else if (l.StartsWith("DATABASE_URL=", StringComparison.Ordinal))
                    lines[i] = $"DATABASE_URL=\"mysql://root:{rootPw}@127.0.0.1:3306/{dbName}\"";
            }
            await File.WriteAllLinesAsync(envFile, lines);
        }
    }

    /// <summary>Scaffold a fresh Laravel app at <paramref name="root"/> using composer create-project.
    /// Requires composer.phar and a working PHP (installed on demand by the caller).</summary>
    public static async Task InstallLaravel(string root, string dbName)
        => await InstallComposerProject(root, "laravel/laravel", dbName);

    /// <summary>Download the latest WordPress into <paramref name="root"/> and pre-write wp-config.php.</summary>
    public static async Task InstallWordPress(string root, string db)
    {
        var zip = await DownloadToTmp("https://wordpress.org/latest.zip", "wordpress.zip");
        var tmp = Path.Combine(Paths.Tmp, "wp-extract-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ExtractZip(zip, tmp);
        CopyDir(Path.Combine(tmp, "wordpress"), root);   // merge into the (possibly placeholder) root

        var sample = Path.Combine(root, "wp-config-sample.php");
        var cfg = Path.Combine(root, "wp-config.php");
        if (File.Exists(sample) && !File.Exists(cfg))
        {
            var txt = await File.ReadAllTextAsync(sample);
            var rootPw = Config.Load().RootPassword;
            txt = txt.Replace("database_name_here", db)
                     .Replace("username_here", "root")
                     .Replace("'password_here'", $"'{rootPw}'")
                     .Replace("localhost", "127.0.0.1");
            try
            {
                var salts = await Http.GetStringAsync("https://api.wordpress.org/secret-key/1.1/salt/");
                var saltBlock = salts.Trim();
                if (saltBlock.Length > 0)
                    // MatchEvaluator Ã¢â€ â€™ the replacement is inserted LITERALLY. A plain string replacement
                    // would treat $ in the salts (WP salts include $, and sequences like $', $`, $&) as
                    // .NET substitution patterns and inject parts of the file Ã¢â€ â€™ broken wp-config.php
                    // ("Parse error: syntax error â€¦ in wp-config.php").
                    txt = System.Text.RegularExpressions.Regex.Replace(
                        txt, @"define\(\s*'AUTH_KEY'.*?'NONCE_SALT'[^;]*\);",
                        _ => saltBlock, System.Text.RegularExpressions.RegexOptions.Singleline);
            }
            catch { /* keep sample salts if the API is unreachable */ }
            await File.WriteAllTextAsync(cfg, txt);
        }
    }

    /// <summary>Download + extract the latest phpMyAdmin into <paramref name="root"/> and write config.inc.php.</summary>
    public static async Task InstallPhpMyAdmin(string root)
    {
        var verJson = await Http.GetStringAsync("https://www.phpmyadmin.net/home_page/version.json");
        using var doc = JsonDocument.Parse(verJson);
        var ver = doc.RootElement.GetProperty("version").GetString()
                  ?? throw new InvalidOperationException("could not resolve phpMyAdmin version");
        var url = $"https://files.phpmyadmin.net/phpMyAdmin/{ver}/phpMyAdmin-{ver}-all-languages.zip";
        var zip = await DownloadToTmp(url, "phpmyadmin.zip");

        var tmp = Path.Combine(Paths.Tmp, "pma-extract-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ExtractZip(zip, tmp);
        var inner = Directory.GetDirectories(tmp).FirstOrDefault() ?? tmp;   // phpMyAdmin-<ver>-all-languages\

        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        
        try
        {
            Directory.Move(inner, root);
        }
        catch (IOException)
        {
            // Fallback for cross-volume move
            CopyDir(inner, root);
            Directory.Delete(inner, true);
        }

        // config.inc.php: connect to BanglaHost's MySQL as passwordless root.
        var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var allowNoPw = Config.Load().RootPassword.Length == 0 ? "true" : "false";
        File.WriteAllText(Path.Combine(root, "config.inc.php"),
            "<?php\n" +
            $"$cfg['blowfish_secret'] = '{secret}';\n" +
            "$i = 0; $i++;\n" +
            "$cfg['Servers'][$i]['host'] = '127.0.0.1';\n" +
            "$cfg['Servers'][$i]['port'] = '3306';\n" +
            "$cfg['Servers'][$i]['auth_type'] = 'cookie';\n" +
            $"$cfg['Servers'][$i]['AllowNoPassword'] = {allowNoPw};\n");
    }

    /// <summary>Download the latest single-file Adminer to <paramref name="dest"/>.</summary>
    public static async Task InstallAdminer(string dest)
        => await CurlTo("https://www.adminer.org/latest.php", dest);

    static Downloader()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("BanglaHost/0.1 (+https://apps.microsoft.com/store/detail/9MWFKR8D8318?cid=DevShareMCLPCB)");
    }
}

