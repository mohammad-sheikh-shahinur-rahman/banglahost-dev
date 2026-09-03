namespace BanglaHost.Core;

/// <summary>
/// Absolute paths to system executables.
///
/// Launching "powershell", "cmd.exe" or "wt.exe" by bare name resolves through
/// the current directory and then PATH order — a planted executable in an
/// earlier PATH entry runs instead of the system one, with our token. Resolve
/// everything to an absolute path first (B11). Downloader already did this for
/// curl/tar; this makes it uniform.
/// </summary>
public static class SystemExe
{
    public static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static string PowerShell
    {
        get
        {
            // 64-bit PowerShell on 64-bit Windows; Sysnative when running 32-bit on 64-bit.
            var sys = Environment.SystemDirectory;
            var p1 = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(p1)) return p1;
            try
            {
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var p2 = Path.Combine(win, "Sysnative", "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(p2)) return p2;
            }
            catch { }
            return p1;
        }
    }

    public static string Curl => Path.Combine(Environment.SystemDirectory, "curl.exe");
    public static string Tar => Path.Combine(Environment.SystemDirectory, "tar.exe");

    /// <summary>Windows Terminal packaged alias, or null when not installed.</summary>
    public static string? WindowsTerminal()
    {
        try
        {
            var p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "wt.exe");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve a tool (node, npm, composer…) to an absolute path so the child
    /// is not resolved via a caller-controlled PATH search at spawn time.
    /// Returns the original name when it cannot be resolved — the spawn will
    /// then fail loudly instead of silently picking something unexpected.
    /// </summary>
    public static string ResolveTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        try
        {
            // Already absolute.
            if (Path.IsPathRooted(name)) return name;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var d = dir.Trim().Trim('"');
                    if (!Directory.Exists(d)) continue;
                    foreach (var ext in new[] { ".exe", ".cmd", ".bat", "" })
                    {
                        var c = Path.Combine(d, name + ext);
                        if (File.Exists(c)) return c;
                    }
                }
                catch { }
            }
        }
        catch { }
        return name;
    }
}
