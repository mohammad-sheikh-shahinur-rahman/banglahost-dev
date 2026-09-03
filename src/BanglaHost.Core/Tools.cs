namespace BanglaHost.Core;

/// <summary>
/// Resolves managed binaries from BanglaHost's own portable installs — in two roots:
/// (1) the user's <c>%LOCALAPPDATA%\BanglaHost\bin\</c> (on-demand downloads/updates), and
/// (2) the bundled <c>&lt;app&gt;\bin\</c> shipped inside the installer. The bundled root
/// means a fresh install runs with ZERO runtime executable downloads — which keeps
/// antivirus behavioral scanners from flagging banglahost.exe as a "dropper".
/// BanglaHost never borrows binaries from Laragon/XAMPP/etc.
/// </summary>
public static class Tools
{
    /// <summary>Search roots, in priority order: user downloads first, then the bundled install.</summary>
    private static IEnumerable<string> BinRoots()
    {
        yield return Paths.Bin;
        var appBin = Path.Combine(AppContext.BaseDirectory, "bin");
        if (!string.Equals(appBin, Paths.Bin, StringComparison.OrdinalIgnoreCase)) yield return appBin;
    }

    /// <summary>Highest version embedded in a path (e.g. …\nginx-1.31.2\… â†’ 1.31.2), else 0.0 — so the
    /// newest of several coexisting version dirs is preferred when a locked old one can't be removed.</summary>
    private static Version PathVersion(string path)
    {
        Version best = new(0, 0);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(path.Replace('\\', '/'), @"(\d+\.\d+(?:\.\d+)?)"))
            if (Version.TryParse(m.Groups[1].Value, out var v) && v > best) best = v;
        return best;
    }

    /// <summary>First match for <paramref name="fileName"/> under <c>&lt;root&gt;\&lt;tool&gt;\…</c> across both roots.</summary>
    private static string? Find(string tool, string fileName)
    {
        foreach (var root in BinRoots())
        {
            var dir = Path.Combine(root, tool);
            if (!Directory.Exists(dir)) continue;
            try
            {
                var hit = Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
                                   .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                                   .ThenByDescending(PathVersion)   // when a stale + new version dir coexist, the newest wins
                                   .FirstOrDefault();
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    public static string? PhpExe(string version)    => Find(Path.Combine("php", version), "php.exe");
    public static string? PhpCgiExe(string version) => Find(Path.Combine("php", version), "php-cgi.exe");

    public static string? NginxExe() => Find("nginx", "nginx.exe");
    /// <summary>Installed nginx version parsed from its dir (…\nginx-1.31.2\…), or null.</summary>
    public static string? NginxVersion()
    {
        if (NginxExe() is not { } e) return null;
        var m = System.Text.RegularExpressions.Regex.Match(e.Replace('\\', '/'), @"/nginx-(\d+\.\d+\.\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }
    public static string? NginxPrefix() => NginxExe() is { } exe ? Path.GetDirectoryName(exe) : null;

    public static string? MkcertExe() => Find("mkcert", "mkcert.exe");

    // MySQL â†’ bin\mysql, MariaDB â†’ bin\mariadb. MysqldExe prefers MariaDB if both are present
    // (only one DB runs on :3306 at a time; each engine keeps its own data dir).
    public static string? MysqldExe()      => Find("mariadb", "mysqld.exe") ?? Find("mysql", "mysqld.exe");
    public static string? MysqldExe(string engine) => engine == "mariadb" ? Find("mariadb", "mysqld.exe") : Find("mysql", "mysqld.exe");

    /// <summary>The ACTUAL installed version of a DB engine, parsed from its versioned extract dir
    /// (…\mariadb\mariadb-12.3.2-winx64\…), or null if not installed. Lets the UI show the real
    /// version instead of a hardcoded label.</summary>
    public static string? DbVersionFor(string engine)
    {
        if (MysqldExe(engine) is not { } exe) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            exe.Replace('\\', '/'), @"/(?:mariadb|mysql)-(\d+\.\d+(?:\.\d+)?)");
        return m.Success ? m.Groups[1].Value : null;
    }
    public static string? MysqlClientExe() => Find("mariadb", "mysql.exe")  ?? Find("mysql", "mysql.exe");
    /// <summary>The command-line client for a SPECIFIC engine (falls back to the other). Critical when
    /// both are installed: a MariaDB client against a MySQL server can't load MySQL's caching_sha2_password
    /// auth plugin (ERROR 1156/2059), so the running engine must be talked to by its own client.</summary>
    public static string? MysqlClientFor(string engine) => engine == "mariadb"
        ? (Find("mariadb", "mariadb.exe") ?? Find("mariadb", "mysql.exe") ?? Find("mysql", "mysql.exe"))
        : (Find("mysql", "mysql.exe") ?? Find("mariadb", "mariadb.exe") ?? Find("mariadb", "mysql.exe"));
    public static string? MysqldumpExe(string engine) => engine == "mariadb"
        ? (Find("mariadb", "mariadb-dump.exe") ?? Find("mariadb", "mysqldump.exe") ?? Find("mysql", "mysqldump.exe"))
        : (Find("mysql", "mysqldump.exe") ?? Find("mariadb", "mariadb-dump.exe") ?? Find("mariadb", "mysqldump.exe"));
    public static bool MysqlInstalled   => Find("mysql", "mysqld.exe") is not null;
    public static bool MariadbInstalled => Find("mariadb", "mysqld.exe") is not null;
    public static string? MariadbInstallDbExe() => Find("mariadb", "mariadb-install-db.exe") ?? Find("mariadb", "mysql_install_db.exe");
    public static string? MariadbUpgradeExe()   => Find("mariadb", "mariadb-upgrade.exe")    ?? Find("mariadb", "mysql_upgrade.exe");

    public static string? PostgresExe() => Find("postgresql", "postgres.exe");
    public static string? PgCtlExe()    => Find("postgresql", "pg_ctl.exe");
    public static string? PsqlExe()     => Find("postgresql", "psql.exe");
    public static string? InitdbExe()   => Find("postgresql", "initdb.exe");
    public static string? CreatedbExe() => Find("postgresql", "createdb.exe");

    public static string? MailpitExe() => Find("mailpit", "mailpit.exe");

    public static string? HttpdExe() => Find("apache", "httpd.exe");
    public static string? ApacheRoot() =>
        HttpdExe() is { } exe ? Path.GetDirectoryName(Path.GetDirectoryName(exe)!) : null;

    public static string? RedisServerExe() => Find("redis", "redis-server.exe");

    public static string? MemcachedExe()
    {
        // prefer a 64-bit build if the extract has several
        foreach (var root in BinRoots())
        {
            var dir = Path.Combine(root, "memcached");
            if (!Directory.Exists(dir)) continue;
            try
            {
                var hit = Directory.EnumerateFiles(dir, "memcached.exe", SearchOption.AllDirectories)
                                   .OrderByDescending(p => p.Contains("win64") || p.Contains("x64"))
                                   .FirstOrDefault();
                if (hit is not null) return hit;
            }
            catch { }
        }
        return null;
    }

    public static string? CloudflaredExe() => Find("cloudflared", "cloudflared.exe");

    public static string? FnmExe() => Find("fnm", "fnm.exe");

    public static string? MongodExe() => Find("mongodb", "mongod.exe");
    public static string? MongoshExe() => Find("mongodb", "mongosh.exe") ?? Find("mongodb", "mongo.exe");

    public static string? MeilisearchExe() => Find("meilisearch", "meilisearch.exe");

    public static string? ComposerPhar()
    {
        foreach (var root in BinRoots())
        {
            var p = Path.Combine(root, "composer", "composer.phar");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static string? GoExe() => Find("go", "go.exe");
    public static string? GoRoot() => GoExe() is { } exe ? Path.GetDirectoryName(Path.GetDirectoryName(exe)!) : null;

    public static string? CargoExe() => Find("rust", "cargo.exe");
    public static string? RustcExe() => Find("rust", "rustc.exe");

    public static string? OllamaExe() => Find("ollama", "ollama.exe");

    // Cache / KV alternates
    public static string? ValkeyExe() => Find("valkey", "valkey-server.exe") ?? Find("valkey", "redis-server.exe");

    // Mail
    public static string? MailhogExe() => Find("mailhog", "MailHog.exe") ?? Find("mailhog", "mailhog.exe");

    // SQLite CLI (single exe)
    public static string? SqliteExe() => Find("sqlite", "sqlite3.exe");

    // Node package managers / alt runtimes
    public static string? PnpmExe() => Find("pnpm", "pnpm.exe");
    public static string? YarnCli()
    {
        foreach (var root in BinRoots())
        {
            var cmd = Path.Combine(root, "yarn", "bin", "yarn.cmd");
            if (File.Exists(cmd)) return cmd;
            var js = Path.Combine(root, "yarn", "bin", "yarn.js");
            if (File.Exists(js)) return js;
        }
        return null;
    }
    public static string? BunExe() => Find("bun", "bun.exe");
    public static string? DenoExe() => Find("deno", "deno.exe");

    // Java (Adoptium Temurin JRE portable)
    public static string? JavaExe() => Find("java", "java.exe");
    public static string? JavaHome() => JavaExe() is { } exe ? Path.GetDirectoryName(Path.GetDirectoryName(exe)!) : null;

    // Docker (detect-only: system-wide install, we don't manage it)
    public static string? DockerExe()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var p in new[] {
            Path.Combine(pf, "Docker", "Docker", "resources", "bin", "docker.exe"),
            Path.Combine(pf, "Docker", "Docker", "resources", "docker.exe"),
        }) if (File.Exists(p)) return p;
        return null;
    }

    // Git (detect system-wide install; we do not vendor)
    public static string? GitExe()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var p in new[] {
            Path.Combine(pf, "Git", "cmd", "git.exe"),
            Path.Combine(pfx86, "Git", "cmd", "git.exe"),
        }) if (File.Exists(p)) return p;
        return null;
    }

    // -- Extra Node-family package managers --------------------------
    public static string? NpmCmd()  => Find("fnm",  "npm.cmd") ?? Find("nodejs", "npm.cmd");

    // -- Extra caches / search / DB --------------------------------
    public static string? ValkeyServerExe() => Find("valkey", "valkey-server.exe");

    // -- Terminals / VCS / container tooling --------------------------

    // â”€â”€ Python (portable CPython for Python-app sites) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public static string? PythonExe() => Find("python", "python.exe");
    public static bool PythonInstalled => PythonExe() is not null;
    /// <summary>Directory holding the managed python.exe (prepended to a Python app's PATH).</summary>
    public static string? PythonBinDir() => PythonExe() is { } e ? Path.GetDirectoryName(e) : null;
    private static string? _pyVer, _pyVerForExe;
    /// <summary>The installed CPython version (e.g. "3.13.4"), from running <c>python --version</c>
    /// (cached per exe). The python-build-standalone "install_only" tree extracts to a plain
    /// <c>python\</c> folder with no version in the path, so the path can't be parsed.</summary>
    public static string? PythonVersion()
    {
        if (PythonExe() is not { } exe) return null;
        if (_pyVerForExe == exe && _pyVer is not null) return _pyVer;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe, Arguments = "--version",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            var p = System.Diagnostics.Process.Start(psi)!;
            var outp = ((Func<string>)(() => { var _errT = p.StandardError.ReadToEndAsync(); var _out = p.StandardOutput.ReadToEnd(); return _out + _errT.Result; }))();
            p.WaitForExit(4000);
            var m = System.Text.RegularExpressions.Regex.Match(outp, @"(\d+\.\d+\.\d+)");
            if (m.Success) { _pyVer = m.Groups[1].Value; _pyVerForExe = exe; return _pyVer; }
        }
        catch { }
        var pm = System.Text.RegularExpressions.Regex.Match(exe.Replace('\\', '/'), @"cpython-(\d+\.\d+(?:\.\d+)?)");
        return pm.Success ? pm.Groups[1].Value : null;
    }

    /// <summary>Directory holding the fnm-managed default node.exe + npm (for Node-app sites).</summary>
    public static string? NodeBinDir()
    {
        var root = Path.Combine(Paths.Home, "node");   // FNM_DIR
        if (!Directory.Exists(root)) return null;
        try
        {
            // prefer the 'default' alias, else the newest installed version
            var node = Directory.EnumerateFiles(root, "node.exe", SearchOption.AllDirectories)
                                 .OrderByDescending(p => p.Contains("default"))
                                 .ThenByDescending(p => p)
                                 .FirstOrDefault();
            return node is null ? null : Path.GetDirectoryName(node);
        }
        catch { return null; }
    }
}

