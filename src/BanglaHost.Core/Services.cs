namespace BanglaHost.Core;

/// <summary>A managed service definition (Windows registry analog of the bash <c>services()</c> table).</summary>
public sealed record ServiceDef(string Key, ServiceRole Role);

/// <summary>
/// The static catalog of services BanglaHost can manage on Windows, plus the
/// "enabled" (auto-start) set persisted at <c>config\enabled</c>.
/// </summary>
public static class Services
{
    /// <summary>PHP minor versions we support as managed php-cgi pools.</summary>
    public static readonly string[] PhpVersions = { "8.6", "8.5", "8.4", "8.3", "8.2", "8.1", "7.4" };

    public static IReadOnlyList<ServiceDef> All { get; } = Build();

    private static List<ServiceDef> Build()
    {
        var list = new List<ServiceDef> { new("php", ServiceRole.Php) };
        foreach (var v in PhpVersions) list.Add(new($"php@{v}", ServiceRole.Php));
        // Web servers
        list.Add(new("nginx",       ServiceRole.Web));
        list.Add(new("apache",      ServiceRole.Web));
        // Databases
        list.Add(new("mysql",       ServiceRole.Db));
        list.Add(new("mariadb",     ServiceRole.Db));
        list.Add(new("postgresql",  ServiceRole.Db));
        list.Add(new("mongodb",     ServiceRole.Db));
        list.Add(new("sqlite",      ServiceRole.Db));
        // Cache / KV
        list.Add(new("redis",       ServiceRole.Cache));
        list.Add(new("memcached",   ServiceRole.Cache));
        list.Add(new("valkey",      ServiceRole.Cache));
        // Search
        list.Add(new("meilisearch", ServiceRole.Search));
        // Mail catchers
        list.Add(new("mailpit",     ServiceRole.Mail));
        list.Add(new("mailhog",     ServiceRole.Mail));
        // Node ecosystem
        list.Add(new("fnm",         ServiceRole.Node));
        list.Add(new("pnpm",        ServiceRole.Node));
        list.Add(new("yarn",        ServiceRole.Node));
        list.Add(new("bun",         ServiceRole.Node));
        list.Add(new("deno",        ServiceRole.Node));
        // Python
        list.Add(new("python",      ServiceRole.Python));
        // Java
        list.Add(new("java",        ServiceRole.Java));
        // Runtimes
        list.Add(new("go",          ServiceRole.Runtime));
        list.Add(new("rust",        ServiceRole.Runtime));
        // AI stack
        list.Add(new("ollama",      ServiceRole.AI));
        // Container / VCS / package tools
        list.Add(new("docker",      ServiceRole.Container));
        list.Add(new("git",         ServiceRole.Vcs));
        list.Add(new("composer",    ServiceRole.Tool));
        list.Add(new("mkcert",      ServiceRole.Tool));
        list.Add(new("cloudflared", ServiceRole.Tool));
        return list;
    }

    public static bool Exists(string key) => All.Any(s => s.Key == key);
    public static ServiceRole RoleOf(string key) => All.FirstOrDefault(s => s.Key == key)?.Role ?? ServiceRole.Other;

    /// <summary>A short version/label for a service row (best-effort, no process spawn).</summary>
    public static string ShortVersion(string key, Config cfg) => key switch
    {
        "nginx"       => Tools.NginxVersion() is { } nv ? $"nginx {nv}" : "nginx",
        "apache"      => "httpd 2.4",
        "mysql"       => Tools.DbVersionFor("mysql")   is { } mv ? $"MySQL {mv}"   : "MySQL",
        "mariadb"     => Tools.DbVersionFor("mariadb") is { } dv ? $"MariaDB {dv}" : "MariaDB",
        "postgresql"  => "PostgreSQL 16",
        "mongodb"     => "MongoDB",
        "sqlite"      => "SQLite",
        "redis"       => "Redis",
        "memcached"   => "Memcached",
        "valkey"      => "Valkey",
        "meilisearch" => "Meilisearch",
        "mailpit"     => "Mailpit",
        "mailhog"     => "MailHog",
        "mkcert"      => "mkcert",
        "fnm"         => "fnm (Node)",
        "pnpm"        => "pnpm",
        "yarn"        => "Yarn",
        "bun"         => "Bun",
        "deno"        => "Deno",
        "python"      => Tools.PythonVersion() is { } pv ? $"Python {pv}" : "Python",
        "java"        => "Java (Temurin)",
        "composer"    => "Composer",
        "go"          => "Go",
        "rust"        => "Rust",
        "ollama"      => "Ollama",
        "docker"      => "Docker",
        "git"         => "Git",
        "cloudflared" => "Cloudflared",
        _ when RoleOf(key) == ServiceRole.Php => "PHP " + PhpVersion(key, cfg),
        _ => "",
    };

    /// <summary>Normalize a --php value ("8.4" | "php@8.4" | "default" | "") to a registry key (mirrors bash php_key).</summary>
    public static string PhpKey(string? v, Config cfg)
    {
        v = (v ?? "").Trim();
        if (v is "" or "default") v = cfg.DefaultPhp;
        if (v == "php") return "php";
        return v.StartsWith("php@") ? v : $"php@{v}";
    }

    public static string PhpLabel(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key == "php" || key == "default") return "default";
        return key.StartsWith("php@") ? key[4..] : key;
    }

    public static string PhpVersion(string key, Config cfg)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key == "php" || key == "default") return cfg.DefaultPhp;
        return key.StartsWith("php@") ? key[4..] : key;
    }

    public static bool Installed(string key, Config cfg) => key switch
    {
        "nginx"       => Tools.NginxExe() is not null,
        "apache"      => Tools.HttpdExe() is not null,
        "mysql"       => Tools.MysqlInstalled,
        "mariadb"     => Tools.MariadbInstalled,
        "postgresql"  => Tools.PostgresExe() is not null,
        "mongodb"     => Tools.MongodExe() is not null,
        "sqlite"      => Tools.SqliteExe() is not null,
        "redis"       => Tools.RedisServerExe() is not null,
        "memcached"   => Tools.MemcachedExe() is not null,
        "valkey"      => Tools.ValkeyExe() is not null,
        "meilisearch" => Tools.MeilisearchExe() is not null,
        "mkcert"      => Tools.MkcertExe() is not null,
        "mailpit"     => Tools.MailpitExe() is not null,
        "mailhog"     => Tools.MailhogExe() is not null,
        "fnm"         => Tools.FnmExe() is not null,
        "pnpm"        => Tools.PnpmExe() is not null,
        "yarn"        => Tools.YarnCli() is not null,
        "bun"         => Tools.BunExe() is not null,
        "deno"        => Tools.DenoExe() is not null,
        "python"      => Tools.PythonInstalled,
        "java"        => Tools.JavaExe() is not null,
        "composer"    => Tools.ComposerPhar() is not null,
        "go"          => Tools.GoExe() is not null,
        "rust"        => Tools.CargoExe() is not null,
        "ollama"      => Tools.OllamaExe() is not null,
        "docker"      => Tools.DockerExe() is not null,
        "git"         => Tools.GitExe() is not null,
        "cloudflared" => Tools.CloudflaredExe() is not null,
        _ when RoleOf(key) == ServiceRole.Php => Tools.PhpCgiExe(PhpVersion(key, cfg)) is not null,
        _ => false,
    };

    private static string EnabledFile => Path.Combine(Paths.Config, "enabled");

    private static bool DefaultEnabled(string key, Config cfg) => key switch
    {
        "nginx" or "mysql" or "mariadb" => true,
        _ => key == PhpKey("default", cfg),
    };

    public static bool Enabled(string key, Config cfg)
    {
        if (File.Exists(EnabledFile))
            return File.ReadAllLines(EnabledFile).Any(l => l.Trim() == key);
        return DefaultEnabled(key, cfg);
    }

    private static void Materialize(Config cfg)
    {
        if (File.Exists(EnabledFile)) return;
        Directory.CreateDirectory(Paths.Config);
        File.WriteAllLines(EnabledFile, All.Select(s => s.Key).Where(k => DefaultEnabled(k, cfg)));
    }

    public static void Enable(string key, Config cfg)
    {
        Materialize(cfg);
        var lines = File.ReadAllLines(EnabledFile).ToList();
        if (!lines.Contains(key)) { lines.Add(key); File.WriteAllLines(EnabledFile, lines); }
    }

    public static void Disable(string key, Config cfg)
    {
        Materialize(cfg);
        var lines = File.ReadAllLines(EnabledFile).Where(l => l.Trim() != key).ToList();
        File.WriteAllLines(EnabledFile, lines);
    }
}
