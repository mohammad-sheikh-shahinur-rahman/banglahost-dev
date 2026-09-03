using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.Core;

/// <summary>
/// MySQL/MariaDB database operations via the bundled <c>mysql.exe</c> client over TCP as root
/// (the <c>cmd_db</c> analog). System schemas are hidden.
///
/// Every invocation now goes through <see cref="ProcRunner"/> with a real timeout and through
/// <see cref="MySqlAuthFile"/> for credentials. The previous implementation:
/// <list type="bullet">
/// <item>put <c>-p{password}</c> on the command line, where any process on the machine could read
/// it out of <c>Win32_Process.CommandLine</c> for the lifetime of the child;</item>
/// <item>built the whole command line as one interpolated string, so a password containing a space
/// or a quote produced a malformed (and injectable) command line;</item>
/// <item>read stderr to completion before stdout — a client that filled the stdout pipe deadlocked
/// against a parent blocked on stderr;</item>
/// <item>called <c>WaitForExit()</c> with no timeout, so a wedged server hung the app forever.</item>
/// </list>
/// </summary>
public static class Database
{
    private static readonly string[] SystemSchemas = { "information_schema", "performance_schema", "mysql", "sys" };

    /// <summary>Client calls are local and should answer in milliseconds; a minute is already an
    /// eternity, and the point is that there IS a ceiling.</summary>
    private const int ClientTimeoutMs = 60_000;

    public static bool HasRootPassword => Config.Load().RootPassword.Length > 0;

    /// <summary>Set (or clear, with "") the root@localhost / root@127.0.0.1 password and persist it
    /// so every later connection uses it. Local-dev only.</summary>
    public static void SetRootPassword(string newPw)
    {
        if (!DbServer.Running()) throw new BhException("database server not running — banglahost start mariadb");
        var p = Esc(newPw);
        var sql =
            $"ALTER USER 'root'@'localhost' IDENTIFIED BY '{p}';" +
            $"ALTER USER 'root'@'127.0.0.1' IDENTIFIED BY '{p}';" +
            "FLUSH PRIVILEGES;";
        var (code, outp) = Mysql("-e", sql);
        if (code != 0) throw new BhException("set root password failed:\n" + outp);

        var cfg = Config.Load(); cfg.RootPassword = newPw; cfg.Save();
    }

    /// <summary>Run the matching client with the given trailing arguments. Each argument is passed
    /// as a separate argv element, so SQL text never has to be quoted or escaped for the shell.</summary>
    private static (int code, string output) Mysql(params string[] trailingArgs)
        => MysqlAsync(default, trailingArgs).GetAwaiter().GetResult();

    private static async Task<(int code, string output)> MysqlAsync(CancellationToken ct, params string[] trailingArgs)
    {
        // Use the client that MATCHES the running engine (a MariaDB client can't load MySQL's
        // caching_sha2_password plugin, and vice-versa) and tell it where its auth-plugin DLLs live —
        // the client can't find lib\plugin by default, so loading caching_sha2_password /
        // mysql_native_password fails with ERROR 1156/2059. --plugin-dir fixes it.
        var engine = DbServer.ActiveEngine();
        var cli = (engine is not null ? Tools.MysqlClientFor(engine) : Tools.MysqlClientExe())
                  ?? throw new BhException("mysql client not found");
        var bin = Path.GetDirectoryName(cli)!;
        var pdir = Path.GetFullPath(Path.Combine(bin, "..", "lib", "plugin"));

        using var auth = MySqlAuthFile.Create("root", Config.Load().RootPassword, DbServer.Port);
        var args = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        if (Directory.Exists(pdir)) args.Add($"--plugin-dir={pdir}");
        args.AddRange(trailingArgs);

        var res = await ProcRunner.RunAsync(cli, args, workingDir: bin, timeoutMs: ClientTimeoutMs, ct: ct)
                                  .ConfigureAwait(false);

        // MariaDB's client prints a harmless "--ssl-verify-server-cert is disabled … passwordless
        // login" warning to stderr on local logins. Drop it so it can't be mistaken for a database
        // name in List() or clutter error messages.
        var err = string.Join('\n', res.StdErr.Split('\n').Where(l => !l.Contains("ssl-verify-server-cert")));
        if (res.TimedOut) err += $"\nclient timed out after {ClientTimeoutMs / 1000}s";

        return (res.ExitCode, res.StdOut + err);
    }

    private static void ValidName(string name)
    {
        if (!Regex.IsMatch(name ?? "", "^[A-Za-z0-9_]+$"))
            throw new BhException($"invalid db name '{name}' (letters, digits, underscore)");
        if (name!.Length > 64)
            throw new BhException($"db name '{name}' is too long (MySQL allows 64 characters)");
    }

    public static IReadOnlyList<string> List()
    {
        if (!DbServer.Running()) return Array.Empty<string>();
        var (code, outp) = Mysql("-N", "-e", "SHOW DATABASES;");
        if (code != 0) return Array.Empty<string>();
        return outp.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(d => !d.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) && !d.Contains(' '))
                   .Where(d => !SystemSchemas.Contains(d))
                   .ToList();
    }

    public static string Create(string name)
    {
        ValidName(name);
        if (!DbServer.Running()) throw new BhException("database server not running — banglahost start mariadb");
        var (code, outp) = Mysql("-e",
            $"CREATE DATABASE IF NOT EXISTS `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        if (code != 0) throw new BhException("create failed:\n" + outp);
        return name;
    }

    public static void Drop(string name)
    {
        ValidName(name);
        if (!DbServer.Running()) throw new BhException("database server not running — banglahost start mariadb");
        var (code, outp) = Mysql("-e", $"DROP DATABASE IF EXISTS `{name}`;");
        if (code != 0) throw new BhException("drop failed:\n" + outp);
    }

    /// <summary>True if a dedicated user named after the database exists.</summary>
    public static bool HasUser(string name)
    {
        if (!DbServer.Running()) return false;
        var (code, outp) = Mysql("-N", "-e", $"SELECT COUNT(*) FROM mysql.user WHERE user='{Esc(name)}';");
        return code == 0 && int.TryParse(outp.Trim(), out var n) && n > 0;
    }

    /// <summary>Create the database; if <paramref name="password"/> is non-empty, also create a
    /// dedicated user named after the DB (@localhost + @127.0.0.1) with all privileges on it.</summary>
    public static string Create(string name, string password)
    {
        Create(name);   // the no-user create (validates + creates the schema)
        if (string.IsNullOrEmpty(password)) return name;
        SetPassword(name, password);
        return name;
    }

    /// <summary>Create-or-update the dedicated user for a database and grant it full access.</summary>
    public static void SetPassword(string name, string password)
    {
        ValidName(name);
        if (!DbServer.Running()) throw new BhException("database server not running — banglahost start mariadb");
        var p = Esc(password);
        var sql =
            $"CREATE USER IF NOT EXISTS '{name}'@'localhost' IDENTIFIED BY '{p}';" +
            $"CREATE USER IF NOT EXISTS '{name}'@'127.0.0.1' IDENTIFIED BY '{p}';" +
            $"ALTER USER '{name}'@'localhost' IDENTIFIED BY '{p}';" +
            $"ALTER USER '{name}'@'127.0.0.1' IDENTIFIED BY '{p}';" +
            $"GRANT ALL PRIVILEGES ON `{name}`.* TO '{name}'@'localhost';" +
            $"GRANT ALL PRIVILEGES ON `{name}`.* TO '{name}'@'127.0.0.1';" +
            "FLUSH PRIVILEGES;";
        var (code, outp) = Mysql("-e", sql);
        if (code != 0) throw new BhException("set password failed:\n" + outp);
    }

    /// <summary>Escape a value destined for a single-quoted SQL string literal. Correct for the
    /// default sql_mode (backslash escapes enabled), which is what BanglaHost's servers run with.
    /// Identifiers must go through <see cref="ValidName"/> instead — escaping is not a substitute
    /// for validation there, because a backtick inside an identifier closes the quote.</summary>
    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");
}
