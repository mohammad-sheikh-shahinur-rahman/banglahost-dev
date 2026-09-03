namespace BanglaHost.Core;

/// <summary>
/// Filesystem layout for BanglaHost on Windows — the analog of the mac engine's
/// <c>~/.banglahost</c> root. Everything lives under <c>%LOCALAPPDATA%\BanglaHost</c>.
/// </summary>
public static class Paths
{
    /// <summary>Root data dir, overridable via the BANGLAHOST_HOME env var, or Registry.</summary>
    public static string Home
    {
        get
        {
            if (Environment.GetEnvironmentVariable("BANGLAHOST_HOME") is { Length: > 0 } h) return h;
            if (GetRegistryInstallPath() is { Length: > 0 } reg) return reg;
            
            var primary = @"C:\BanglaHost";
            try
            {
                if (!System.IO.Directory.Exists(primary))
                    System.IO.Directory.CreateDirectory(primary);
                return primary;
            }
            catch
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BanglaHost");
            }
        }
    }

    private static string? GetRegistryInstallPath()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\BanglaHost");
                if (key?.GetValue("InstallPath") is string path && !string.IsNullOrWhiteSpace(path))
                    return path;
            }
            catch { }
        }
        return null;
    }

    public static string Config     => Sub("config");
    public static string Bin        => Sub("bin");          // downloaded portable php/nginx/mariadb/...
    public static string NginxSites => Sub("nginx", "sites");
    public static string Run        => Sub("run");          // pid/port json per service
    public static string Logs       => Sub("logs");
    public static string Sites      => Sub("sites");        // default web roots
    public static string Certs      => Sub("certs");        // mkcert output
    public static string Tmp        => Sub("tmp");

    public static string ConfigJson => System.IO.Path.Combine(Config, "banglahost.json");

    /// <summary>The Windows hosts file (writing it requires elevation).</summary>
    public static string HostsFile =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");

    private static string Sub(params string[] parts) =>
        System.IO.Path.Combine(new[] { Home }.Concat(parts).ToArray());

    /// <summary>Create the full directory skeleton (idempotent) — the `init` step.</summary>
    public static void EnsureSkeleton()
    {
        foreach (var d in new[] { Config, Bin, NginxSites, Run, Logs, Sites, Certs, Tmp })
            System.IO.Directory.CreateDirectory(d);
    }

    /// <summary>
    /// Delete staging files older than <paramref name="maxAge"/>, oldest-first, until the
    /// directory is under <paramref name="maxBytes"/> (D5). Temp holds installer archives,
    /// extraction staging and backup staging — without this it grows without bound and no
    /// uninstaller reclaims it. Never throws: cleanup must not break startup. Files still
    /// locked by a running process are skipped and retried next launch.
    /// </summary>
    public static void CleanTmp(TimeSpan? maxAge = null, long maxBytes = 512L * 1024 * 1024)
    {
        var age = maxAge ?? TimeSpan.FromDays(2);
        try
        {
            var dir = new DirectoryInfo(Tmp);
            if (!dir.Exists) { dir.Create(); return; }

            var files = dir.GetFiles("*", SearchOption.AllDirectories)
                           .OrderBy(f => f.LastWriteTimeUtc)
                           .ToList();
            var cutoff = DateTime.UtcNow - age;
            long total = 0;
            foreach (var f in files) { try { total += f.Length; } catch { } }

            foreach (var f in files)
            {
                bool stale, over;
                try
                {
                    f.Refresh();
                    stale = f.LastWriteTimeUtc < cutoff;
                    over = total > maxBytes;
                }
                catch { continue; }
                if (!stale && !over) break;   // ordered oldest-first: nothing later qualifies
                try { var n = f.Length; f.Delete(); total -= n; }
                catch (IOException) { }                 // still in use — try again next launch
                catch (UnauthorizedAccessException) { }
            }
        }
        catch { /* best-effort: cleanup must never break startup */ }
    }
}
