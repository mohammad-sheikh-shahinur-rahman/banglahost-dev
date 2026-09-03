using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record DbResult(string[] Columns, List<string[]> Rows, string Error = "");

/// <summary>
/// Runs one-off queries against the local database engines and returns them as a
/// simple columns+rows tuple the UI can render. Uses the SAME client resolution as
/// <see cref="Database"/>/<see cref="PgDatabase"/> so it works with BanglaHost's
/// versioned portable installs (e.g. bin\mariadb\mariadb-12.3.2-winx64\bin\mysql.exe).
///
/// All three query paths now build their command line with <see cref="ProcRunner"/>'s
/// ArgumentList and carry a timeout. Previously the MySQL path interpolated the whole command line —
/// including <c>-p{password}</c> and the user's SQL wrapped in manually escaped double quotes — into
/// one string. A query containing a backslash before a quote (routine in a LIKE pattern or a regex)
/// broke out of the quoting and became extra arguments to the client.
/// </summary>
public static class DbExplorer
{
    private const int QueryTimeoutMs = 120_000;

    private static async Task<DbResult> RunAsync(
        string exe, IEnumerable<string> args, string? workingDir, string? stdIn, CancellationToken ct)
    {
        try
        {
            var res = await ProcRunner.RunAsync(exe, args, workingDir, QueryTimeoutMs, stdIn, ct: ct)
                                      .ConfigureAwait(false);

            var err = string.Join('\n', res.StdErr.Split('\n').Where(l => !l.Contains("ssl-verify-server-cert"))).Trim();
            if (res.TimedOut)
                return new DbResult(Array.Empty<string>(), new(),
                    $"query timed out after {QueryTimeoutMs / 1000}s and was cancelled");
            if (res.ExitCode != 0 && string.IsNullOrWhiteSpace(res.StdOut))
                return new DbResult(Array.Empty<string>(), new(), string.IsNullOrWhiteSpace(err) ? "client failed" : err);

            return ParseTsv(res.StdOut, err);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new DbResult(Array.Empty<string>(), new(), ex.Message);
        }
    }

    private static DbResult ParseTsv(string output, string error)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return new DbResult(Array.Empty<string>(), new(), error);
        var cols = lines[0].Split('\t');
        var rows = new List<string[]>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++) rows.Add(lines[i].Split('\t'));
        return new DbResult(cols, rows, error);
    }

    public static async Task<DbResult> QueryMysqlAsync(string query, string db = "", CancellationToken ct = default)
    {
        var engine = DbServer.ActiveEngine();
        var cli = (engine is not null ? Tools.MysqlClientFor(engine) : Tools.MysqlClientExe());
        if (cli is null) return new DbResult(Array.Empty<string>(), new(), "MySQL/MariaDB client not found — install MariaDB or MySQL");

        // -D takes an identifier; it is not quotable, so it must be validated rather than escaped.
        if (!string.IsNullOrEmpty(db))
        {
            try { MySqlAuthFile.ValidIdentifier(db, "database name"); }
            catch (Exception ex) { return new DbResult(Array.Empty<string>(), new(), ex.Message); }
        }

        var bin = Path.GetDirectoryName(cli)!;
        var pdir = Path.GetFullPath(Path.Combine(bin, "..", "lib", "plugin"));

        using var auth = MySqlAuthFile.Create("root", Config.Load().RootPassword, DbServer.Port);
        var args = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        if (Directory.Exists(pdir)) args.Add($"--plugin-dir={pdir}");
        args.Add("--batch");
        args.Add("--raw");
        if (!string.IsNullOrEmpty(db)) { args.Add("-D"); args.Add(db); }
        args.Add("-e");
        args.Add(query);          // its own argv element: no escaping, nothing to break out of

        return await RunAsync(cli, args, bin, stdIn: null, ct).ConfigureAwait(false);
    }

    public static async Task<DbResult> QueryPostgresAsync(
        string query, string db = "postgres", CancellationToken ct = default)
    {
        var psql = Tools.PsqlExe();
        if (psql is null) return new DbResult(Array.Empty<string>(), new(), "psql not found — install PostgreSQL");

        if (!System.Text.RegularExpressions.Regex.IsMatch(db ?? "", @"^[A-Za-z0-9_$-]{1,63}$"))
            return new DbResult(Array.Empty<string>(), new(), $"invalid database name '{db}'");

        var args = new List<string>
        {
            "-h", "127.0.0.1",
            "-p", PgServer.Port.ToString(),
            "-U", "postgres",
            "-d", db!,
            "-A",
            "-F", "\t",
            "--pset=footer=off",
        };

        var env = new Dictionary<string, string> { ["PGPASSWORD"] = "" };
        var sql = query.EndsWith(';') ? query + "\n" : query + ";\n";

        try
        {
            var res = await ProcRunner.RunAsync(psql, args, Path.GetDirectoryName(psql),
                                                QueryTimeoutMs, sql, env, ct: ct).ConfigureAwait(false);
            if (res.TimedOut)
                return new DbResult(Array.Empty<string>(), new(), $"query timed out after {QueryTimeoutMs / 1000}s");
            var err = res.StdErr.Trim();
            if (res.ExitCode != 0 && string.IsNullOrWhiteSpace(res.StdOut))
                return new DbResult(Array.Empty<string>(), new(), string.IsNullOrWhiteSpace(err) ? "psql failed" : err);
            return ParseTsv(res.StdOut, err);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new DbResult(Array.Empty<string>(), new(), ex.Message); }
    }

    public static async Task<DbResult> QuerySqliteAsync(string dbPath, string query, CancellationToken ct = default)
    {
        // Was hardcoded to bin\sqlite\sqlite3.exe, which misses both the bundled install root and
        // any versioned extract dir — so SQLite browsing silently reported "not found".
        var exe = Tools.SqliteExe();
        if (exe is null || !File.Exists(exe)) return new DbResult(Array.Empty<string>(), new(), "sqlite3.exe not found");
        if (!File.Exists(dbPath)) return new DbResult(Array.Empty<string>(), new(), "database file not found");

        var stdin = $".headers on\n.mode tabs\n{query}\n.quit\n";
        return await RunAsync(exe, new[] { dbPath }, Path.GetDirectoryName(exe), stdin, ct).ConfigureAwait(false);
    }

    public static async Task<List<string>> GetDatabases(string engine, CancellationToken ct = default)
    {
        var list = new List<string>();
        if (engine == "mariadb" || engine == "mysql")
        {
            var r = await QueryMysqlAsync("SHOW DATABASES;", "", ct).ConfigureAwait(false);
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "information_schema", "performance_schema", "mysql", "sys" };
            foreach (var row in r.Rows) if (row.Length > 0 && !skip.Contains(row[0])) list.Add(row[0]);
        }
        else if (engine == "postgresql")
        {
            var r = await QueryPostgresAsync("SELECT datname FROM pg_database WHERE datistemplate = false;", "postgres", ct).ConfigureAwait(false);
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "postgres", "template0", "template1" };
            foreach (var row in r.Rows) if (row.Length > 0 && !skip.Contains(row[0])) list.Add(row[0]);
        }
        return list;
    }

    public static async Task<List<string>> GetTables(string engine, string db, CancellationToken ct = default)
    {
        var list = new List<string>();
        if (engine == "mariadb" || engine == "mysql")
        {
            var r = await QueryMysqlAsync("SHOW TABLES;", db, ct).ConfigureAwait(false);
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        else if (engine == "postgresql")
        {
            var r = await QueryPostgresAsync("SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;", db, ct).ConfigureAwait(false);
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        else if (engine == "sqlite")
        {
            var r = await QuerySqliteAsync(db, "SELECT name FROM sqlite_master WHERE type='table';", ct).ConfigureAwait(false);
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        return list;
    }
}
