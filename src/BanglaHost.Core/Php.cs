using System.Diagnostics;

namespace BanglaHost.Core;

/// <summary>php.ini editing, ionCube loader install, and per-version status — the
/// Windows analog of the mac engine's <c>php ini</c> / <c>php ioncube</c> / <c>php status</c>.</summary>
public static class Php
{
    private static (int code, string output) Run(string exe, string args, (string k, string v)? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe, Arguments = args,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        if (env is { } e) psi.Environment[e.k] = e.v;
        var p = Process.Start(psi)!;
        var outp = ((Func<string>)(() => { var _errT = p.StandardError.ReadToEndAsync(); var _out = p.StandardOutput.ReadToEnd(); return _out + _errT.Result; }))();
        p.WaitForExit();
        return (p.ExitCode, outp);
    }

    /// <summary>Resolve (seeding if needed) the loaded php.ini for a version.</summary>
    public static string IniPath(string version)
    {
        var exe = Tools.PhpExe(version) ?? throw new BhException($"php {version} not installed");
        var (_, loaded) = Run(exe, "-r \"echo php_ini_loaded_file();\"");
        loaded = loaded.Trim();
        if (loaded.Length > 0 && File.Exists(loaded)) return loaded;

        var dir = Path.GetDirectoryName(exe)!;
        var ini = Path.Combine(dir, "php.ini");
        if (!File.Exists(ini))
        {
            var seed = new[] { "php.ini-development", "php.ini-production" }
                .Select(f => Path.Combine(dir, f)).FirstOrDefault(File.Exists);
            if (seed is not null) 
            {
                var text = File.ReadAllText(seed);
                text = text.Replace("extension_dir = \"ext\"", $"extension_dir = \"{Path.Combine(dir, "ext").Replace('\\', '/')}\"");
                text += "\n; BanglaHost applied fixes\ncgi.force_redirect=0\ndisplay_errors=Off\n";
                File.WriteAllText(ini, text);
            }
            else File.WriteAllText(ini, $"; BanglaHost-created php.ini for {version}\n; Add your directives below.\n\n; BanglaHost applied fixes\ncgi.force_redirect=0\ndisplay_errors=Off\n");
        }
        return ini;
    }

    /// <summary>Restart the version's php-cgi (only if running) so an edited php.ini takes effect.</summary>
    public static bool IniReload(string version)
    {
        if (!PhpCgi.Running(version)) return false;
        PhpCgi.Stop(version);
        return PhpCgi.Start(version);
    }

    /// <summary>The MSVC toolset windows.php.net builds each PHP branch with — it selects the matching
    /// ionCube loader bundle. BanglaHost flattens PHP into bin\php\&lt;version&gt; (no vs## token in the path),
    /// so sniffing the exe path is unreliable; map by version NUMBER, which is fixed per branch:
    ///   7.x â†’ VC15,  8.0–8.3 â†’ VS16 (vc16),  8.4+ â†’ VS17 (vc17).</summary>
    public static string VcFor(string version)
    {
        var parts = version.Split('.');
        var maj = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 8;
        var min = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        if (maj <= 7) return "vc15";
        if (maj == 8 && min <= 3) return "vc16";
        return "vc17";   // 8.4, 8.5, 8.6, future
    }

    /// <summary>Download + enable the ionCube loader for a PHP version.
    /// ionCube must be the FIRST zend_extension (before OPcache) or it aborts with "The Loader must
    /// appear as the first entry". The PHP_INI_SCAN_DIR (conf.d) always loads AFTER the main php.ini —
    /// where windows.php.net enables OPcache — so a conf.d entry would load second and fail. We instead
    /// insert the loader line into the MAIN php.ini, immediately before the opcache zend_extension.</summary>
    public static void Ioncube(string version, Action<string> log)
    {
        var exe = Tools.PhpExe(version) ?? throw new BhException($"php {version} not installed");
        var vc = VcFor(version);
        string loadersDir;
        try { loadersDir = Downloader.InstallIoncube(vc).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            throw new BhException(
                $"ionCube loader download/extract failed for {vc} ({ex.Message}). " +
                "ionCube's URLs change per VC build — grab ioncube_loaders_win_nonts_<vc>_x86-64.zip manually " +
                $"and extract into {Path.Combine(Paths.Bin, "ioncube", "nonts-" + vc)}.");
        }

        // NTS loader (our php-cgi builds are NTS): ioncube_loader_win_<mm>.dll (NOT *_ts.dll)
        var dll = Directory.EnumerateFiles(loadersDir, $"ioncube_loader_win_{version}.dll", SearchOption.AllDirectories)
                           .FirstOrDefault(d => !d.Contains("_ts.dll", StringComparison.OrdinalIgnoreCase));
        if (dll is null)
            throw new BhException($"no NTS ionCube loader for PHP {version} in the {vc} bundle");

        var line = $"zend_extension={dll.Replace('\\', '/')}";
        var ini = IniPath(version);
        var lines = File.ReadAllLines(ini).ToList();
        // Idempotent: drop any prior ionCube line we (or anyone) added.
        lines.RemoveAll(l => l.TrimStart().StartsWith("zend_extension", StringComparison.OrdinalIgnoreCase)
                          && l.Contains("ioncube_loader", StringComparison.OrdinalIgnoreCase));
        // Insert before the first ACTIVE opcache zend_extension (commented ;… lines are skipped); else at top.
        var idx = lines.FindIndex(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("zend_extension", StringComparison.OrdinalIgnoreCase)
                && t.Contains("opcache", StringComparison.OrdinalIgnoreCase);
        });
        lines.Insert(idx < 0 ? 0 : idx, line);
        File.WriteAllLines(ini, lines);

        // Retire the old conf.d approach (a stray scan-dir copy would load second and re-trigger the error).
        try { File.Delete(Path.Combine(PhpCgi.ConfDir(version), "00-ioncube.ini")); } catch { }

        log($"ionCube enabled for php {version} (before opcache in php.ini): {dll}");
        if (IniReload(version)) log($"php-cgi {version} reloaded with ionCube");
        else log("restart this PHP to load it: banglahost restart all");
    }

    /// <summary>Common bundled DLL extensions we surface in the PHP Manager UI. Only ones that ship
    /// with the official windows.php.net PHP builds (php\ext\php_*.dll) are listed; anything missing
    /// on disk is hidden by <see cref="ListExtensions"/> automatically.</summary>
    public static readonly string[] CommonExtensions =
    {
        // bundled with windows.php.net builds
        "bz2", "curl", "exif", "fileinfo", "ftp", "gd", "gettext", "gmp", "intl", "imap",
        "ldap", "mbstring", "mysqli", "odbc", "opcache", "openssl", "pdo_mysql", "pdo_pgsql",
        "pdo_sqlite", "pgsql", "shmop", "soap", "sockets", "sodium", "sqlite3", "sysvshm",
        "tidy", "xsl", "zip",
        // pecl extensions we can auto-fetch on demand (php\ext\php_*.dll)
        "xdebug", "redis", "memcached", "imagick", "swoole", "mongodb", "amqp", "apcu",
        // custom special case
        "ioncube"
    };

    /// <summary>Extensions loaded via zend_extension= rather than plain extension= — Xdebug + opcache
    /// are the two that windows.php.net-style INI files use. Everything else is a regular extension.</summary>
    private static readonly HashSet<string> ZendExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "opcache", "xdebug",
    };

    /// <summary>PECL builds we know how to auto-fetch when a user toggles the extension on but the
    /// DLL isn't bundled with the PHP build. Maps ext name → PECL package name (usually the same).</summary>
    private static readonly Dictionary<string, string> PeclPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xdebug"] = "xdebug", ["redis"] = "redis", ["memcached"] = "memcached",
        ["imagick"] = "imagick", ["swoole"] = "swoole", ["mongodb"] = "mongodb",
        ["amqp"] = "amqp", ["apcu"] = "apcu",
    };

    /// <summary>Runtime dependency requirements — if extension A needs B enabled too, we surface it.
    /// Only *hard* runtime deps that would cause PHP to fail to load the module.</summary>
    private static readonly Dictionary<string, string[]> Dependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdo_mysql"] = new[] { "pdo" },
        ["pdo_pgsql"] = new[] { "pdo" },
        ["pdo_sqlite"] = new[] { "pdo" },
        ["mysqli"]    = new[] { "mysqlnd" },
    };

    public record ExtensionInfo(string Name, bool Available, bool Enabled, bool IsZend, string[] MissingDeps);

    /// <summary>Whether an ext DLL is present in this PHP build's ext/ directory.</summary>
    public static bool HasDll(string version, string name)
    {
        if (name == "ioncube") return true; // Handled specially
        var exe = Tools.PhpExe(version);
        if (exe is null) return false;
        var extDir = Path.Combine(Path.GetDirectoryName(exe)!, "ext");
        return File.Exists(Path.Combine(extDir, $"php_{name}.dll"));
    }

    /// <summary>Is this extension loaded via zend_extension= (opcache, xdebug) rather than plain extension=?</summary>
    public static bool IsZendExtension(string name) => ZendExtensions.Contains(name);

    /// <summary>Which extensions must also be enabled for this one to load (returns names that are OFF).</summary>
    public static string[] MissingDependencies(string version, string name)
    {
        if (!Dependencies.TryGetValue(name, out var deps)) return Array.Empty<string>();
        var ini = IniPath(version);
        var lines = File.ReadAllLines(ini);
        bool Enabled(string n) => lines.Any(l =>
        {
            var t = l.TrimStart(); if (t.StartsWith(";", StringComparison.Ordinal)) return false;
            return t.StartsWith("extension=", StringComparison.OrdinalIgnoreCase) &&
                   t["extension=".Length..].Trim('"', ' ').Equals(n, StringComparison.OrdinalIgnoreCase);
        });
        return deps.Where(d => !Enabled(d)).ToArray();
    }

    /// <summary>List extensions installed in this PHP build (from `<php>/ext/php_<name>.dll`),
    /// with each one's current on/off state from `php.ini` (a live `extension=<name>` line, not commented).
    /// Extensions like `opcache` come as `zend_extension=...opcache` — both forms are recognized.</summary>
    public static IReadOnlyList<ExtensionInfo> ListExtensions(string version)
    {
        var exe = Tools.PhpExe(version) ?? throw new BhException($"php {version} not installed");
        var dir = Path.GetDirectoryName(exe)!;
        var extDir = Path.Combine(dir, "ext");
        var ini = IniPath(version);
        var lines = File.ReadAllLines(ini);
        bool IsEnabled(string name)
        {
            foreach (var raw in lines)
            {
                var l = raw.TrimStart();
                if (l.StartsWith(";", StringComparison.Ordinal)) continue;
                if (l.StartsWith("extension=", StringComparison.OrdinalIgnoreCase) &&
                    l["extension=".Length..].Trim('"', ' ').Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (l.StartsWith("zend_extension=", StringComparison.OrdinalIgnoreCase) &&
                    l["zend_extension=".Length..].Contains(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        var list = new List<ExtensionInfo>();
        foreach (var e in CommonExtensions)
        {
            var available = e == "opcache" || HasDll(version, e);
            var isZend = ZendExtensions.Contains(e);
            var missing = Dependencies.TryGetValue(e, out var deps) ? deps.Where(d => !IsEnabled(d)).ToArray() : Array.Empty<string>();
            list.Add(new ExtensionInfo(e, available, IsEnabled(e), isZend, missing));
        }
        return list.OrderBy(x => x.Name).ToList();
    }

    /// <summary>Enable or disable a bundled extension in the version's php.ini (idempotent).
    /// Handles both plain extensions (<c>extension=name</c>) and zend extensions (<c>zend_extension=name</c>
    /// — opcache, xdebug). Auto-restarts php-cgi so the change takes effect right away.</summary>
    public static async Task SetExtension(string version, string name, bool enable, Action<string>? log = null)
    {
        if (name == "ioncube")
        {
            if (enable) Ioncube(version, log ?? (m => { }));
            else IoncubeDisable(version);
            return;
        }

        if (enable && !HasDll(version, name) && PeclPackages.TryGetValue(name, out var peclName))
        {
            log?.Invoke($"Fetching {name} PECL build for PHP {version}...");
            var exe = Tools.PhpExe(version) ?? throw new BhException($"php {version} not installed");
            var extDir = Path.Combine(Path.GetDirectoryName(exe)!, "ext");
            await Downloader.InstallPeclExtension(version, VcFor(version), peclName, extDir);
            log?.Invoke($"Downloaded {name} DLL successfully.");
        }

        var ini = IniPath(version);
        var lines = File.ReadAllLines(ini).ToList();
        var isZend = ZendExtensions.Contains(name);
        var directive = isZend ? "zend_extension" : "extension";

        bool IsMatch(string raw)
        {
            var l = raw.TrimStart().TrimStart(';').Trim();
            if (!l.StartsWith(directive + "=", StringComparison.OrdinalIgnoreCase)) return false;
            var value = l[(directive.Length + 1)..].Trim('"', ' ');
            // Match on the extension name — handles both `zend_extension=opcache` and `zend_extension=path/to/xdebug.dll`.
            return value.Equals(name, StringComparison.OrdinalIgnoreCase)
                || value.Contains($"{name}.dll", StringComparison.OrdinalIgnoreCase)
                || value.Contains($"php_{name}.dll", StringComparison.OrdinalIgnoreCase);
        }

        // Rewrite existing lines that reference this extension, commented or not.
        bool touched = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!IsMatch(lines[i])) continue;
            lines[i] = enable ? $"{directive}={name}" : $";{directive}={name}";
            touched = true;
        }
        if (!touched && enable) lines.Add($"{directive}={name}");

        if (name == "xdebug")
        {
            lines.RemoveAll(l => l.TrimStart().StartsWith("xdebug.", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith(";xdebug.", StringComparison.OrdinalIgnoreCase));
            if (enable)
            {
                lines.Add("xdebug.mode=profile");
                lines.Add($"xdebug.output_dir=\"{Paths.Tmp.Replace('\\', '/')}\"");
                lines.Add("xdebug.profiler_output_name=\"cachegrind.out.%p\"");
            }
        }
        File.WriteAllLines(ini, lines);
        IniReload(version);
    }

    /// <summary>Remove the ionCube loader line from php.ini for a version (idempotent). Restarts php-cgi.</summary>
    public static void IoncubeDisable(string version)
    {
        var ini = IniPath(version);
        var lines = File.ReadAllLines(ini).ToList();
        var removed = lines.RemoveAll(l =>
            l.TrimStart().StartsWith("zend_extension", StringComparison.OrdinalIgnoreCase)
         && l.Contains("ioncube_loader", StringComparison.OrdinalIgnoreCase));
        if (removed > 0) File.WriteAllLines(ini, lines);
        IniReload(version);
    }

    public record PhpInfo(string Version, bool Installed, bool Running, string Ioncube);

    public static IReadOnlyList<PhpInfo> Status()
    {
        var list = new List<PhpInfo>();
        foreach (var v in Services.PhpVersions)
        {
            var exe = Tools.PhpExe(v);
            if (exe is null) continue;
            var ini = IniPath(v);
            var configured = File.ReadAllText(ini).Contains("ioncube_loader", StringComparison.OrdinalIgnoreCase);
            var (_, vout) = Run(exe, "-v");
            // Match the SUCCESS banner ("with the ionCube PHP Loader"); the failure messages
            // ("Failed loading …ioncube…", "[ionCube Loader] The Loader must appear…") must NOT count as loaded.
            var loaded = vout.Contains("ionCube PHP Loader", StringComparison.OrdinalIgnoreCase);
            list.Add(new PhpInfo(v, true, PhpCgi.Running(v),
                                 loaded ? "loaded" : configured ? "configured" : "no"));
        }
        return list;
    }
}
