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

    /// <summary>Cleans up the temporary directory to prevent storage leaks.</summary>
    public static void CleanTmp()
    {
        try
        {
            if (System.IO.Directory.Exists(Tmp))
            {
                foreach (var f in System.IO.Directory.GetFiles(Tmp)) try { System.IO.File.Delete(f); } catch { }
                foreach (var d in System.IO.Directory.GetDirectories(Tmp)) try { System.IO.Directory.Delete(d, true); } catch { }
            }
            else
            {
                System.IO.Directory.CreateDirectory(Tmp);
            }
        }
        catch { }
    }
}
