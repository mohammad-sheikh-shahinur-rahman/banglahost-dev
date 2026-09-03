using System.Text.Json;
using System.Text.Json.Serialization;

namespace BanglaHost.Core;

/// <summary>
/// Typed view of <c>config\banglahost.json</c> — the Windows analog of the mac
/// engine's <c>jget</c> reads.
///
/// Load is fail-soft for a *missing* config (fresh install → defaults, so the CLI never dies on a
/// config problem) but NOT for a corrupt one: a torn write used to silently reset every setting,
/// including the database root password, with no way for the user to tell. A bad primary now falls
/// back to the <c>.bak</c> written by every save, and an unrecoverable one is reported via
/// <see cref="LastLoadOutcome"/> instead of being disguised as a fresh install.
/// </summary>
public sealed class Config
{
    [JsonPropertyName("tld")]          public string Tld { get; set; } = "test";
    [JsonPropertyName("http_port")]    public int HttpPort { get; set; } = 80;
    [JsonPropertyName("https_port")]   public int HttpsPort { get; set; } = 443;
    [JsonPropertyName("default_php")]  public string DefaultPhp { get; set; } = "8.4";
    [JsonPropertyName("default_web")]  public string DefaultWeb { get; set; } = "nginx";
    [JsonPropertyName("sites_root")]   public string SitesRoot { get; set; } = @"C:\BanglaHost\www";
    [JsonPropertyName("autostart")]    public bool Autostart { get; set; } = false;
    [JsonPropertyName("minimize_to_tray")] public bool MinimizeToTray { get; set; } = true;
    [JsonPropertyName("dashboard_page_size")] public int DashboardPageSize { get; set; } = 10;
    [JsonPropertyName("sites_page_size")]     public int SitesPageSize { get; set; } = 15;
    [JsonPropertyName("databases_page_size")] public int DatabasesPageSize { get; set; } = 15;
    [JsonPropertyName("apps_page_size")]      public int AppsPageSize { get; set; } = 15;
    [JsonPropertyName("auto_update")]              public bool AutoUpdate { get; set; } = true;
    [JsonPropertyName("start_services_on_launch")] public bool StartServicesOnLaunch { get; set; } = false;
    /// <summary>
    /// On-disk form of the root credential. New saves write "dpapi:&lt;base64&gt;"
    /// (DPAPI CurrentUser — B10); a legacy clear-text value is still accepted on
    /// load and silently re-protected on the next save.
    /// </summary>
    [JsonPropertyName("root_password")]            public string RootPasswordStorage { get; set; } = "";
    [JsonIgnore]
    public string RootPassword
    {
        get
        {
            var s = RootPasswordStorage ?? "";
            if (s.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                        return Dpapi.Unprotect(s["dpapi:".Length..]) ?? "";
                }
                catch { }
                return "";   // machine/profile changed — treat as unset, prompt the user
            }
            return s;   // legacy plaintext — migrated on next Save()
        }
        set
        {
            if (string.IsNullOrEmpty(value)) { RootPasswordStorage = ""; return; }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    RootPasswordStorage = "dpapi:" + Dpapi.Protect(value);
                    return;
                }
            }
            catch { }
            RootPasswordStorage = value;   // non-Windows / DPAPI unavailable: plaintext fallback
        }
    }
    [JsonPropertyName("install_path")]             public string InstallPath { get; set; } = @"C:\BanglaHost";
    [JsonPropertyName("language")]                 public string Language { get; set; } = "en";

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // ── cache ────────────────────────────────────────────────────────────────────────────
    // Load() was called on every dashboard tick, every page navigation and inside tight loops,
    // re-reading and re-deserialising the same ~700 bytes each time. The cache is invalidated by
    // (path, last-write-time, length) so an external edit is still picked up, and by Save().
    private static readonly object _cacheGate = new();
    private static Config? _cached;
    private static string _cachedKey = "";

    /// <summary>How the last load went. Anything other than Absent/Primary means the user should
    /// be told — silently running on defaults is how settings "disappear".</summary>
    public static AtomicFile.ReadOutcome LastLoadOutcome { get; private set; } = AtomicFile.ReadOutcome.Absent;

    /// <summary>Read config (missing → defaults, corrupt → .bak → defaults). Expands %ENV% in sites_root.</summary>
    public static Config Load()
    {
        try
        {
            var key = CacheKey();
            lock (_cacheGate)
            {
                if (_cached is not null && _cachedKey == key) return _cached.Clone();
            }

            var text = AtomicFile.ReadWithBackup(
                Paths.ConfigJson,
                validate: static s =>
                {
                    if (string.IsNullOrWhiteSpace(s)) return false;
                    // Must be parseable AND produce an object — a truncated write often yields
                    // valid-but-partial JSON, which is exactly what we must not accept silently.
                    try { return JsonSerializer.Deserialize<Config>(s, Opts) is not null; }
                    catch { return false; }
                },
                out var outcome);

            LastLoadOutcome = outcome;

            var cfg = new Config();
            if (text is not null)
            {
                var c = JsonSerializer.Deserialize<Config>(text, Opts);
                if (c is not null) cfg = c;
            }

            cfg.SitesRoot = Environment.ExpandEnvironmentVariables(cfg.SitesRoot ?? "");

            // Recovered from the backup: put the good content back so the next reader gets the
            // primary, and so a second corruption doesn't take the last copy with it.
            if (outcome == AtomicFile.ReadOutcome.RecoveredFromBackup)
            {
                try { cfg.Save(); } catch { }
            }

            lock (_cacheGate)
            {
                _cached = cfg.Clone();
                _cachedKey = CacheKey();
            }
            return cfg;
        }
        catch
        {
            // Never throw from Load — but do not cache this, so a transient IO error doesn't
            // pin defaults for the rest of the session.
            LastLoadOutcome = AtomicFile.ReadOutcome.Corrupt;
            return new Config();
        }
    }

    public void Save()
    {
        // Migrate a legacy clear-text password to DPAPI on write (B10).
        try
        {
            if (!string.IsNullOrEmpty(RootPasswordStorage)
                && !RootPasswordStorage.StartsWith("dpapi:", StringComparison.Ordinal)
                && OperatingSystem.IsWindows())
                RootPasswordStorage = "dpapi:" + Dpapi.Protect(RootPasswordStorage);
        }
        catch { }
        Directory.CreateDirectory(Paths.Config);
        AtomicFile.WriteAllText(Paths.ConfigJson, JsonSerializer.Serialize(this, Opts));
        lock (_cacheGate)
        {
            _cached = Clone();
            _cachedKey = CacheKey();
        }
    }

    /// <summary>Drop the cache — for tests and for the settings page after an external edit.</summary>
    public static void InvalidateCache()
    {
        lock (_cacheGate) { _cached = null; _cachedKey = ""; }
    }

    private Config Clone() => (Config)MemberwiseClone();

    private static string CacheKey()
    {
        try
        {
            var fi = new FileInfo(Paths.ConfigJson);
            return fi.Exists ? $"{fi.FullName}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}" : "absent";
        }
        catch { return Guid.NewGuid().ToString(); }   // unreadable → never serve a stale cache
    }
}
