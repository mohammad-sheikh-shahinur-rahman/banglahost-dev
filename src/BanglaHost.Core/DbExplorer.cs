using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record DbResult(string[] Columns, List<string[]> Rows, string Error = "");

/// <summary>
/// Runs one-off queries against the local database engines and returns them as a
/// simple columns+rows tuple the UI can render. Uses the SAME client resolution as
/// <see cref="Database"/>/<see cref="PgDatabase"/> so it works with BanglaHost's
/// versioned portable installs (e.g. bin\mariadb\mariadb-12.3.2-winx64\bin\mysql.exe).
/// </summary>
public static class DbExplorer
{
    private static async Task<DbResult> RunAsync(ProcessStartInfo psi, string stdIn)
    {
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new DbResult(Array.Empty<string>(), new(), "failed to start client");
            if (!string.IsNullOrEmpty(stdIn))
            {
                await p.StandardInput.WriteAsync(stdIn);
                p.StandardInput.Close();
            }
            var outT = p.StandardOutput.ReadToEndAsync();
            var errT = p.StandardError.ReadToEndAsync();
            await Task.WhenAll(outT, errT, p.WaitForExitAsync());
            var err = string.Join('\n', errT.Result.Split('\n').Where(l => !l.Contains("ssl-verify-server-cert"))).Trim();
            if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(outT.Result))
                return new DbResult(Array.Empty<string>(), new(), err);
            return ParseTsv(outT.Result, err);
        }
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

    public static async Task<DbResult> QueryMysqlAsync(string query, string db = "")
    {
        var engine = DbServer.ActiveEngine();
        var cli = (engine is not null ? Tools.MysqlClientFor(engine) : Tools.MysqlClientExe());
        if (cli is null) return new DbResult(Array.Empty<string>(), new(), "MySQL/MariaDB client not found — install MariaDB or MySQL");

        var bin = Path.GetDirectoryName(cli)!;
        var pdir = Path.GetFullPath(Path.Combine(bin, "..", "lib", "plugin"));
        var pdArg = Directory.Exists(pdir) ? $"--plugin-dir=\"{pdir}\" " : "";

        var pw = Config.Load().RootPassword;
        var args = new StringBuilder()
            .Append($"-u root -h 127.0.0.1 -P {DbServer.Port} --connect-timeout=5 ")
            .Append(pw.Length > 0 ? $"-p{pw} " : "")
            .Append(pdArg)
            .Append("--batch --raw ")
            .Append(string.IsNullOrEmpty(db) ? "" : $"-D {db} ")
            .Append("-e ")
            .Append('"').Append(query.Replace("\"", "\\\"")).Append('"')
            .ToString();

        return await RunAsync(new ProcessStartInfo
        {
            FileName = cli, Arguments = args,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = bin,
        }, stdIn: "");
    }

    public static async Task<DbResult> QueryPostgresAsync(string query, string db = "postgres")
    {
        var psql = Tools.PsqlExe();
        if (psql is null) return new DbResult(Array.Empty<string>(), new(), "psql not found — install PostgreSQL");

        var psi = new ProcessStartInfo
        {
            FileName = psql,
            Arguments = $"-h 127.0.0.1 -p {PgServer.Port} -U postgres -d {db} -A -F \"\t\" --pset=footer=off",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(psql)!,
        };
        psi.Environment["PGPASSWORD"] = "";
        return await RunAsync(psi, stdIn: query.EndsWith(';') ? query + "\n" : query + ";\n");
    }

    public static async Task<DbResult> QuerySqliteAsync(string dbPath, string query)
    {
        var exe = Path.Combine(Paths.Bin, "sqlite", "sqlite3.exe");
        if (!File.Exists(exe)) return new DbResult(Array.Empty<string>(), new(), "sqlite3.exe not found");
        if (!File.Exists(dbPath)) return new DbResult(Array.Empty<string>(), new(), "database file not found");

        var psi = new ProcessStartInfo
        {
            FileName = exe, Arguments = $"\"{dbPath}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        return await RunAsync(psi, stdIn: $".headers on\n.mode tabs\n{query}\n.quit\n");
    }

    public static async Task<List<string>> GetDatabases(string engine)
    {
        var list = new List<string>();
        if (engine == "mariadb" || engine == "mysql")
        {
            var r = await QueryMysqlAsync("SHOW DATABASES;", "");
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "information_schema", "performance_schema", "mysql", "sys" };
            foreach (var row in r.Rows) if (row.Length > 0 && !skip.Contains(row[0])) list.Add(row[0]);
        }
        else if (engine == "postgresql")
        {
            var r = await QueryPostgresAsync("SELECT datname FROM pg_database WHERE datistemplate = false;", "postgres");
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "postgres", "template0", "template1" };
            foreach (var row in r.Rows) if (row.Length > 0 && !skip.Contains(row[0])) list.Add(row[0]);
        }
        return list;
    }

    public static async Task<List<string>> GetTables(string engine, string db)
    {
        var list = new List<string>();
        if (engine == "mariadb" || engine == "mysql")
        {
            var r = await QueryMysqlAsync("SHOW TABLES;", db);
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        else if (engine == "postgresql")
        {
            var r = await QueryPostgresAsync("SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;", db);
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        else if (engine == "sqlite")
        {
            var r = await QuerySqliteAsync(db, "SELECT name FROM sqlite_master WHERE type='table';");
            foreach (var row in r.Rows) if (row.Length > 0) list.Add(row[0]);
        }
        return list;
    }
}
