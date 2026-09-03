using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>
/// Manages BanglaHost's MySQL/MariaDB server on :3306. MySQL and MariaDB are separate engines
/// with SEPARATE data dirs (incompatible system tables), but only one runs on the port at a
/// time. The run-file records which engine is actually running, so status reflects reality
/// rather than which engine happens to be installed.
/// </summary>
public static class DbServer
{
    public const int Port = 3306;
    private static string RunFile => Path.Combine(Paths.Run, "mysqld.json");

    private static string DataDirFor(string engine) => Path.Combine(Paths.Home, engine == "mariadb" ? "data-mariadb" : "data");
    private static bool InitializedFor(string engine) => Directory.Exists(Path.Combine(DataDirFor(engine), "mysql"));
    private static string DefaultEngine() => Tools.MysqlInstalled ? "mysql" : Tools.MariadbInstalled ? "mariadb" : "mysql";

    public static bool Running()
    {
        // Was: ConnectAsync(...).Wait(600) with the TcpClient disposed while the connect could
        // still be pending — a race that also produced unobserved SocketExceptions later.
        return NetUtils.IsListening(Port, 600);
    }

    /// <summary>The engine ACTUALLY running on :3306 (from the run-file), or null if nothing is.</summary>
    public static string? ActiveEngine()
    {
        if (!Running()) return null;
        try
        {
            if (File.Exists(RunFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(RunFile));
                if (doc.RootElement.TryGetProperty("engine", out var e) && e.GetString() is { } s) return s;
            }
        }
        catch { }
        return "mysql";   // a legacy run-file (no engine field) was the MySQL default
    }

    public static bool ActiveIsMariadb => ActiveEngine() == "mariadb";
    public static bool Initialized => InitializedFor(DefaultEngine());

    /// <summary>
    /// Run a DB helper tool and collect its output.
    ///
    /// Was: a string <c>Arguments</c>, <c>StandardError.ReadToEnd()</c> to completion before stdout,
    /// and an unbounded <c>WaitForExit()</c>. Three separate hangs in eight lines — a full stdout
    /// pipe deadlocked the pair, and a tool that never exited (mysqladmin against a wedged server,
    /// mysql_install_db on a locked data dir) hung BanglaHost with no way out.
    /// </summary>
    private static (int code, string output) RunWait(string exe, IEnumerable<string> args, int timeoutMs = 120_000)
    {
        var res = ProcRunner.Run(exe, args, workingDir: Path.GetDirectoryName(exe), timeoutMs: timeoutMs);
        return (res.ExitCode, res.All);
    }

    public static (bool ok, string msg) EnsureInitialized(string engine)
    {
        if (InitializedFor(engine)) return (true, "data dir ready");
        var data = DataDirFor(engine);
        Directory.CreateDirectory(data);
        if (Directory.EnumerateFileSystemEntries(data).Any())
            return (false, $"data dir not empty and not initialized: {data}");

        (int code, string output) res;
        if (engine == "mariadb")
        {
            var installer = Tools.MariadbInstallDbExe();
            if (installer is null) return (false, "mariadb-install-db not found");
            // Windows mariadb-install-db.exe uses --datadir; no password arg = passwordless root.
            // (The --auth-root-authentication-method flag is Linux-only and errors out here.)
            res = RunWait(installer, new[] { $"--datadir={data}" }, 300_000);
        }
        else
        {
            var mysqld = Tools.MysqldExe("mysql");
            if (mysqld is null) return (false, "mysqld not found — install MySQL");
            res = RunWait(mysqld, new[] { "--initialize-insecure", $"--datadir={data}", "--console" }, 300_000);
        }
        if (!InitializedFor(engine))
        {
            try { Directory.Delete(data, true); } catch { }   // leave no half-init dir to block a retry
            var why = res.output.Trim();
            return (false, $"{engine} initialize failed" + (why.Length > 0 ? $": {why[^Math.Min(why.Length, 300)..]}" : ""));
        }
        return (true, $"initialized fresh {engine} data dir (root has no password)");
    }

    /// <summary>Start a specific engine on :3306. Refuses if the OTHER engine already holds the port.</summary>
    public static (bool ok, string msg) Start(string engine)
    {
        engine = engine == "mariadb" ? "mariadb" : "mysql";
        if (Running())
        {
            var cur = ActiveEngine();
            return cur == engine
                ? (true, $"{engine} already running")
                : (false, $"{cur} is already running on :{Port} — stop it before starting {engine}");
        }
        var mysqld = Tools.MysqldExe(engine);
        if (mysqld is null) return (false, $"{engine} not installed");
        var init = EnsureInitialized(engine);
        if (!init.ok) return init;
        var data = DataDirFor(engine);

        var psi = new ProcessStartInfo
        {
            FileName = mysqld,
            // bind loopback only; big packets; dev-tuned InnoDB. --mysqlx=0 is MySQL-only.
            // --enable-named-pipe: many apps use DB_HOST='localhost', which on Windows means the
            // named pipe (not TCP) — without this their connection hangs. Matches Laragon.
            Arguments = $"--datadir=\"{data}\" --bind-address=127.0.0.1 --enable-named-pipe --max-allowed-packet=1024M " +
                        $"--innodb-buffer-pool-size=256M --innodb-flush-log-at-trx-commit=2 --port={Port}" +
                        $" --log-error=\"{Path.Combine(Paths.Logs, engine + ".log").Replace("\\", "/")}\"" +
                        (engine == "mariadb" ? "" : " --mysqlx=0"),
            UseShellExecute = false, CreateNoWindow = true,
            // NOT redirected on purpose. This is a long-lived daemon: a redirected pipe nobody
            // drains fills at 4 KB and blocks mysqld in write() forever — and we cannot drain it,
            // because the Process object is disposed as soon as Start() returns. mysqld's own
            // --log-error (set above) captures everything we would have read.
            WorkingDirectory = Path.GetDirectoryName(mysqld)!,
        };
        using var proc = Process.Start(psi);

        if (proc is null) return (false, "failed to spawn mysqld");

        JobManager.Add(proc);

        Directory.CreateDirectory(Paths.Run);
        File.WriteAllText(RunFile, JsonSerializer.Serialize(new { pid = proc.Id, port = Port, engine }));

        for (var i = 0; i < 30; i++)
        {
            if (Running()) return (true, $"{engine} started on :{Port} (root · no password)");
            System.Threading.Thread.Sleep(500);
        }
        return (false, $"{engine} started but port never opened (see {DataDirFor(engine)}\\*.err)");
    }

    /// <summary>Parameterless start (provisioning) — brings up the default installed engine.</summary>
    public static (bool ok, string msg) Start() => Running() ? (true, "database already running") : Start(DefaultEngine());

    /// <summary>Reconcile the data dir's system tables with the (just-installed) new engine binary —
    /// the standard post-upgrade step. MariaDB needs mariadb-upgrade; MySQL 8+/9 self-upgrades on
    /// start, so it's a no-op there. The data dir is never dumped/rebuilt — this only fixes the
    /// system schema. Returns a short status, or "" if nothing to do.</summary>
    public static string RunUpgrade(string engine)
    {
        if (engine != "mariadb" || !Running()) return "";
        var exe = Tools.MariadbUpgradeExe();
        if (exe is null) return "";
        using var auth = MySqlAuthFile.Create("root", Config.Load().RootPassword, Port);
        var (code, outp) = RunWait(exe, MySqlAuthFile.Apply(auth, "root", Port), 600_000);
        return code == 0 ? "system tables upgraded (mariadb-upgrade)"
                         : $"mariadb-upgrade reported: {outp.Trim()}";
    }

    /// <summary>
    /// Stop the database.
    ///
    /// The automatic dump of every database used to run here, synchronously, on every stop:
    /// "Stop All" then took as long as mysqldump needed for the user's largest database — minutes,
    /// on the UI-facing path, with no progress and no way to cancel, and it ran again on every
    /// restart. Daily backups are now scheduled separately (see
    /// <see cref="BackupService.AutoBackupAllDatabasesAsync"/>); pass <c>backupFirst: true</c> to
    /// opt back in for a specific stop.
    /// </summary>
    public static void Stop(bool backupFirst = false)
    {
        if (backupFirst && Running())
        {
            try { BackupService.AutoBackupAllDatabasesAsync().Wait(TimeSpan.FromSeconds(5)); } catch { }
        }

        var admin = Tools.MysqlClientExe() is { } cli ? Path.Combine(Path.GetDirectoryName(cli)!, "mysqladmin.exe") : null;
        if (admin is not null && File.Exists(admin) && Running())
        {
            using var auth = MySqlAuthFile.Create("root", Config.Load().RootPassword, Port);
            var args = MySqlAuthFile.Apply(auth, "root", Port);
            args.Add("shutdown");
            RunWait(admin, args, 30_000);
        }
        try
        {
            if (File.Exists(RunFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(RunFile));
                var pid = doc.RootElement.GetProperty("pid").GetInt32();
                System.Threading.Thread.Sleep(500);
                if (Running()) { try { BanglaHost.Core.ProcessUtils.KillSafe(pid); } catch { } }   // graceful shutdown didn't take â†’ force it
            }
        }
        catch { }
        try { File.Delete(RunFile); } catch { }
    }
}



