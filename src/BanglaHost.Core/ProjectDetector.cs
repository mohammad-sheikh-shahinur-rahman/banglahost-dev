using System;
using System.IO;
using System.Linq;

namespace BanglaHost.Core;

public record ProjectInfo(
    string Framework,
    string Language,
    string PackageManager,
    string Database,
    string NodeVersion,
    string PhpVersion,
    string[] MissingDependencies,
    int HealthScore,
    string HealthNotes
);

public static class ProjectDetector
{
    public static ProjectInfo Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new ArgumentException("Invalid project path.");

        var framework = "Unknown";
        var lang = "Unknown";
        var pkg = "Unknown";
        var db = "Unknown";
        var nodeVer = "Not detected";
        var phpVer = "Not detected";
        var missing = new System.Collections.Generic.List<string>();
        int score = 100;
        var notes = new System.Collections.Generic.List<string>();

        var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                             .Select(Path.GetFileName)
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        var dirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
                            .Select(Path.GetFileName)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Detect Language & Framework
        if (files.Contains("artisan") && files.Contains("composer.json"))
        {
            framework = "Laravel";
            lang = "PHP";
            pkg = "Composer";
            var composer = File.ReadAllText(Path.Combine(path, "composer.json"));
            if (composer.Contains("php")) phpVer = "From composer.json"; // Simplification
            if (files.Contains(".env"))
            {
                var env = File.ReadAllText(Path.Combine(path, ".env"));
                if (env.Contains("DB_CONNECTION=mysql")) db = "MySQL";
                else if (env.Contains("DB_CONNECTION=pgsql")) db = "PostgreSQL";
                else if (env.Contains("DB_CONNECTION=sqlite")) db = "SQLite";
            }
        }
        else if (files.Contains("wp-config.php") || (dirs.Contains("wp-admin") && dirs.Contains("wp-includes")))
        {
            framework = "WordPress";
            lang = "PHP";
            pkg = "None";
            db = "MySQL";
        }
        else if (files.Contains("symfony.lock"))
        {
            framework = "Symfony";
            lang = "PHP";
            pkg = "Composer";
        }
        else if (files.Contains("requirements.txt") && files.Contains("manage.py"))
        {
            framework = "Django";
            lang = "Python";
            pkg = "pip";
        }
        else if (files.Contains("package.json"))
        {
            lang = "JavaScript/TypeScript";
            var pkgJson = File.ReadAllText(Path.Combine(path, "package.json"));
            
            if (files.Contains("yarn.lock")) pkg = "Yarn";
            else if (files.Contains("pnpm-lock.yaml")) pkg = "pnpm";
            else if (files.Contains("bun.lockb")) pkg = "Bun";
            else pkg = "npm";

            if (pkgJson.Contains("\"next\"")) framework = "Next.js";
            else if (pkgJson.Contains("\"nuxt\"")) framework = "Nuxt";
            else if (pkgJson.Contains("\"vue\"")) framework = "Vue";
            else if (pkgJson.Contains("\"react\"")) framework = "React";
            else if (pkgJson.Contains("\"express\"")) framework = "Express";
            else framework = "Node.js (Generic)";
            
            if (files.Contains(".nvmrc")) nodeVer = File.ReadAllText(Path.Combine(path, ".nvmrc")).Trim();
            
            if (!dirs.Contains("node_modules"))
            {
                score -= 30;
                missing.Add("node_modules");
                notes.Add("Run install command to fetch dependencies.");
            }
        }
        else if (files.Contains("go.mod"))
        {
            framework = "Go Module";
            lang = "Go";
            pkg = "Go Modules";
        }
        else if (files.Contains("Cargo.toml"))
        {
            framework = "Rust Project";
            lang = "Rust";
            pkg = "Cargo";
        }
        else if (files.Any(f => f?.EndsWith(".csproj") == true))
        {
            framework = "ASP.NET Core";
            lang = "C#";
            pkg = "NuGet";
        }
        else if (files.Contains("index.php"))
        {
            framework = "Custom PHP";
            lang = "PHP";
        }
        else if (files.Contains("index.html"))
        {
            framework = "Static HTML";
            lang = "HTML/JS/CSS";
        }

        // Generic checks
        if (lang == "PHP" && !dirs.Contains("vendor") && files.Contains("composer.json"))
        {
            score -= 30;
            missing.Add("vendor");
            notes.Add("Run 'composer install' to fetch dependencies.");
        }
        if (lang == "PHP" && files.Contains(".env.example") && !files.Contains(".env"))
        {
            score -= 10;
            missing.Add(".env file");
            notes.Add("Copy .env.example to .env and configure it.");
        }

        return new ProjectInfo(framework, lang, pkg, db, nodeVer, phpVer, missing.ToArray(), Math.Max(0, score), string.Join(" ", notes));
    }
}
