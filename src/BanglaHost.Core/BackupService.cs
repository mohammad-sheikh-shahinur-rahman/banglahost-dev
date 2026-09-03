using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public class BackupRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Date { get; set; }
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsEncrypted { get; set; }

    public BackupRecord() { }

    public BackupRecord(string Id, string Name, DateTime Date, string Path, long SizeBytes, bool IsEncrypted)
    {
        this.Id = Id; this.Name = Name; this.Date = Date; this.Path = Path; this.SizeBytes = SizeBytes; this.IsEncrypted = IsEncrypted;
    }
}

/// <summary>
/// Outcome of a backup or restore. The old code returned a bare <c>bool</c> and the UI ignored it,
/// so a backup that copied the sites but silently dumped zero databases — the normal case, because
/// the mysqldump path was hardcoded to a directory that only exists for MySQL, never MariaDB —
/// reported "Backup completed successfully". A backup you cannot trust is worse than none.
/// </summary>
public sealed record BackupResult(bool Ok, string Message, IReadOnlyList<string> Warnings, string? Path = null)
{
    public bool Partial => Ok && Warnings.Count > 0;

    public string Summary => Warnings.Count == 0
        ? Message
        : Message + "\n" + string.Join("\n", Warnings.Select(w => "• " + w));
}

/// <summary>What a restore is allowed to touch. Nothing is overwritten in place: an existing site
/// folder is renamed aside first, so a restore is always undoable.</summary>
public sealed class RestoreOptions
{
    public bool RestoreSites { get; set; } = true;
    public bool RestoreDatabases { get; set; } = true;
    /// <summary>Rename an existing site dir to <c>&lt;name&gt;.pre-restore-&lt;stamp&gt;</c> instead of
    /// merging into it. A rename is instant and keeps the old data recoverable.</summary>
    public bool PreserveExistingAside { get; set; } = true;
}

public static class BackupService
{
    private static string BackupsDir => Path.Combine(Paths.Home, "backups");

    /// <summary>Dumps and imports can legitimately run for minutes on a large database; they must
    /// still have a ceiling, or a mysqldump blocked on a locked table hangs the app forever.</summary>
    private const int DbOpTimeoutMs = 15 * 60 * 1000;

    private static readonly string[] SkipDirNames =
    {
        "node_modules", "vendor", ".git", ".svn", "bower_components",
        "__pycache__", ".venv", "venv", ".next", ".nuxt", "dist-cache",
    };

    private static readonly string[] SystemDatabases =
        { "information_schema", "performance_schema", "sys", "mysql" };

    public static void Init()
    {
        if (!Directory.Exists(BackupsDir))
            Directory.CreateDirectory(BackupsDir);
    }

    public static List<BackupRecord> GetBackups()
    {
        Init();
        // *.zip only — an interrupted backup is left as *.zip.partial and must never be listed as
        // restorable.
        var files = Directory.GetFiles(BackupsDir, "*.zip");
        var list = new List<BackupRecord>();
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var name = Path.GetFileNameWithoutExtension(file);
                list.Add(new BackupRecord(name, name, info.CreationTime, file, info.Length, false));
            }
            catch { }
        }
        return list.OrderByDescending(b => b.Date).ToList();
    }

    /// <summary>
    /// A backup name becomes a file name under <see cref="BackupsDir"/>. Unvalidated, a name of
    /// <c>..\..\Windows\System32\x</c> wrote the archive outside the backups directory.
    /// </summary>
    public static string ValidBackupName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new BhException("Backup name is required.");
        if (name.Length > 120) throw new BhException("Backup name is too long.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9._-]+$"))
            throw new BhException("Backup name may contain only letters, digits, dot, dash and underscore.");
        if (name is "." or "..") throw new BhException("Invalid backup name.");
        return name;
    }

    // ── backup ───────────────────────────────────────────────────────────────────────────────

    public static async Task<BackupResult> BackupAllAsync(
        string backupName, Action<string> log, CancellationToken ct = default)
    {
        log ??= _ => { };
        Init();

        string name;
        try { name = ValidBackupName(backupName); }
        catch (Exception ex) { log(ex.Message); return new BackupResult(false, ex.Message, Array.Empty<string>()); }

        var zipPath = Path.Combine(BackupsDir, $"{name}.zip");
        // Build to .partial and only rename on success, so a crash mid-zip can never leave a
        // truncated archive that GetBackups() offers as a valid restore point.
        var partialPath = zipPath + ".partial";
        var tempDir = Path.Combine(Paths.Tmp, $"backup_{Guid.NewGuid():N}");
        var warnings = new List<string>();

        try
        {
            Directory.CreateDirectory(tempDir);

            // 1. Sites
            var cfg = Config.Load();
            var sitesRoot = cfg.SitesRoot;
            var siteFiles = 0L;
            if (Directory.Exists(sitesRoot))
            {
                log("Copying sites…");
                siteFiles = CopyDirectory(sitesRoot, Path.Combine(tempDir, "sites"), log, warnings, ct);
                log($"Copied {siteFiles} site file(s).");
            }
            else
            {
                warnings.Add($"Sites root '{sitesRoot}' does not exist — no site files were backed up.");
            }

            // 2. MySQL / MariaDB dumps
            var dbCount = 0;
            var engine = DbServer.ActiveEngine();
            if (engine is null)
            {
                warnings.Add("Database server is not running — no databases were backed up. "
                           + "Start MySQL/MariaDB and back up again if you need your data.");
            }
            else
            {
                // Resolve the dump tool for the engine that is ACTUALLY running. The old code looked
                // only in bin\mysql\bin\mysqldump.exe, which does not exist on a MariaDB install (or
                // on any MySQL layout with a version-stamped folder), and skipped the whole database
                // section without a word when it was missing.
                var dumpExe = Tools.MysqldumpExe(engine);
                if (dumpExe is null || !File.Exists(dumpExe))
                {
                    warnings.Add($"mysqldump/mariadb-dump not found for '{engine}' — databases were NOT backed up.");
                }
                else
                {
                    log($"Dumping {engine} databases…");
                    var dbDir = Path.Combine(tempDir, "databases", engine == "mariadb" ? "mariadb" : "mysql");
                    Directory.CreateDirectory(dbDir);

                    var listing = await DbExplorer.QueryMysqlAsync("SHOW DATABASES;").ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(listing.Error))
                    {
                        warnings.Add($"Could not list databases: {listing.Error}");
                    }

                    using var auth = MySqlAuthFile.Create("root", cfg.RootPassword, DbServer.Port);

                    foreach (var row in listing.Rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (row.Length == 0) continue;
                        var db = row[0];
                        if (SystemDatabases.Contains(db, StringComparer.OrdinalIgnoreCase)) continue;
                        if (!IsSafeDbName(db))
                        {
                            warnings.Add($"Skipped database '{db}': name contains characters BanglaHost will not pass to the shell.");
                            continue;
                        }

                        var dumpPath = Path.Combine(dbDir, $"{db}.sql");
                        var res = await RunDumpAsync(dumpExe, auth, db, dumpPath, ct).ConfigureAwait(false);
                        if (res.Ok && new FileInfo(dumpPath).Length > 0)
                        {
                            dbCount++;
                            log($"Dumped database: {db}");
                        }
                        else
                        {
                            // Never keep a zero-byte or partial .sql in a backup: on restore it would
                            // "succeed" and leave an empty database.
                            try { if (File.Exists(dumpPath)) File.Delete(dumpPath); } catch { }
                            var why = res.TimedOut ? "timed out" : FirstLine(res.StdErr);
                            warnings.Add($"Database '{db}' was NOT backed up ({why}).");
                        }
                    }
                }
            }

            // 3. Manifest — lets a restore know what it is looking at, and lets the user see what
            //    the archive actually contains without unzipping it.
            var manifest = new
            {
                schema = 1,
                created_utc = DateTime.UtcNow.ToString("O"),
                app_version = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
                sites_root = sitesRoot,
                site_files = siteFiles,
                db_engine = engine,
                databases = dbCount,
                warnings = warnings.ToArray(),
            };
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }
            catch { }

            // 4. Compress
            ct.ThrowIfCancellationRequested();
            log($"Compressing backup to {zipPath}…");
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            ZipFile.CreateFromDirectory(tempDir, partialPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(partialPath, zipPath);

            var msg = dbCount > 0 || siteFiles > 0
                ? $"Backup completed: {siteFiles} file(s), {dbCount} database(s)."
                : "Backup completed, but it is EMPTY — nothing was found to back up.";
            if (siteFiles == 0 && dbCount == 0) warnings.Add("The archive contains no site files and no databases.");

            log(msg);
            foreach (var w in warnings) log("Warning: " + w);
            return new BackupResult(true, msg, warnings, zipPath);
        }
        catch (OperationCanceledException)
        {
            log("Backup cancelled.");
            return new BackupResult(false, "Backup cancelled.", warnings);
        }
        catch (Exception ex)
        {
            log($"Backup failed: {ex.Message}");
            return new BackupResult(false, $"Backup failed: {ex.Message}", warnings);
        }
        finally
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            SafeDeleteTree(tempDir);
        }
    }

    /// <summary>
    /// Recursive copy that is safe to point at a user's web root.
    ///
    /// Fixes over the previous version:
    /// <list type="bullet">
    /// <item><c>CopyTo(target)</c> with no <c>overwrite</c> threw <see cref="IOException"/> on the
    /// first pre-existing file, aborting the whole backup.</item>
    /// <item>No reparse-point check: a junction or symlink inside <c>www</c> — which Laravel's
    /// <c>storage:link</c> and every <c>npm link</c> create — was followed, so the copy could recurse
    /// forever or silently pull in gigabytes from elsewhere on the disk.</item>
    /// <item>No exclusions: <c>node_modules</c>/<c>vendor</c> are reinstallable and routinely dwarf
    /// the actual project by 10×.</item>
    /// <item>A single unreadable file (locked by an editor, or denied by ACL) killed the backup
    /// instead of being reported.</item>
    /// </list>
    /// Returns the number of files copied.
    /// </summary>
    private static long CopyDirectory(
        string sourceDir, string destinationDir, Action<string> log,
        List<string> warnings, CancellationToken ct, int depth = 0)
    {
        const int MaxDepth = 64;
        if (depth > MaxDepth)
        {
            warnings.Add($"Stopped at {MaxDepth} levels deep under '{sourceDir}'.");
            return 0;
        }

        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destinationDir);
        long copied = 0;

        FileInfo[] files;
        DirectoryInfo[] dirs;
        try { files = dir.GetFiles(); dirs = dir.GetDirectories(); }
        catch (Exception ex)
        {
            warnings.Add($"Could not read '{dir.FullName}': {ex.Message}");
            return 0;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(file.Attributes)) continue;   // don't copy through a file symlink
            try
            {
                file.CopyTo(Path.Combine(destinationDir, file.Name), overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped '{file.FullName}': {ex.Message}");
            }
        }

        foreach (var subDir in dirs)
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(subDir.Attributes))
            {
                warnings.Add($"Skipped link '{subDir.FullName}' (junction/symlink not followed).");
                continue;
            }
            if (SkipDirNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            copied += CopyDirectory(subDir.FullName, Path.Combine(destinationDir, subDir.Name),
                                    log, warnings, ct, depth + 1);
        }

        return copied;
    }

    private static bool IsReparsePoint(FileAttributes attrs) =>
        (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    private static async Task<ProcResult> RunDumpAsync(
        string exe, MySqlAuthFile? auth, string db, string dumpPath, CancellationToken ct)
    {
        var args = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        args.Add("--opt");
        args.Add("--single-transaction");        // consistent dump without locking the whole server
        args.Add("--routines");
        args.Add("--events");
        args.Add("--triggers");
        args.Add(db);
        args.Add($"--result-file={dumpPath}");

        try
        {
            return await ProcRunner.RunAsync(exe, args, timeoutMs: DbOpTimeoutMs, ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProcResult(-1, "", ex.Message, false);
        }
    }

    // ── restore ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restore sites and databases from an archive produced by <see cref="BackupAllAsync"/>.
    ///
    /// This used to be a stub: it logged "Restore feature is currently a dry-run…", slept 2 seconds
    /// and returned <c>true</c>. The UI reported success. Users who had been told they had backups
    /// discovered on the day they needed one that the button had never restored anything.
    /// </summary>
    public static async Task<BackupResult> RestoreAsync(
        string zipPath, Action<string> log, RestoreOptions? options = null, CancellationToken ct = default)
    {
        log ??= _ => { };
        var opts = options ?? new RestoreOptions();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return new BackupResult(false, $"Backup archive not found: {zipPath}", warnings);

        var tempDir = Path.Combine(Paths.Tmp, $"restore_{Guid.NewGuid():N}");
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        try
        {
            log($"Reading {Path.GetFileName(zipPath)}…");
            Directory.CreateDirectory(tempDir);
            ExtractSafely(zipPath, tempDir, ct);

            var restoredSites = 0;
            var restoredDbs = 0;

            // 1. Sites
            var srcSites = Path.Combine(tempDir, "sites");
            if (opts.RestoreSites && Directory.Exists(srcSites))
            {
                var sitesRoot = Config.Load().SitesRoot;
                Directory.CreateDirectory(sitesRoot);
                log("Restoring sites…");

                foreach (var srcSite in Directory.GetDirectories(srcSites))
                {
                    ct.ThrowIfCancellationRequested();
                    var siteName = Path.GetFileName(srcSite);
                    var target = Path.Combine(sitesRoot, siteName);

                    if (Directory.Exists(target) && opts.PreserveExistingAside)
                    {
                        var aside = $"{target}.pre-restore-{stamp}";
                        try
                        {
                            Directory.Move(target, aside);
                            log($"Existing '{siteName}' moved aside to {Path.GetFileName(aside)}");
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Could not move existing '{siteName}' aside ({ex.Message}) — merged instead.");
                        }
                    }

                    CopyDirectory(srcSite, target, log, warnings, ct);
                    restoredSites++;
                }

                // Loose files that sat directly in the web root.
                foreach (var f in Directory.GetFiles(srcSites))
                {
                    try { File.Copy(f, Path.Combine(sitesRoot, Path.GetFileName(f)), overwrite: true); }
                    catch (Exception ex) { warnings.Add($"Skipped '{Path.GetFileName(f)}': {ex.Message}"); }
                }
            }
            else if (opts.RestoreSites)
            {
                warnings.Add("Archive contains no sites folder.");
            }

            // 2. Databases
            if (opts.RestoreDatabases)
            {
                var dbRoot = Path.Combine(tempDir, "databases");
                var sqlFiles = Directory.Exists(dbRoot)
                    ? Directory.GetFiles(dbRoot, "*.sql", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                if (sqlFiles.Length == 0)
                {
                    warnings.Add("Archive contains no database dumps.");
                }
                else if (DbServer.ActiveEngine() is null)
                {
                    warnings.Add($"Database server is not running — {sqlFiles.Length} dump(s) were NOT restored.");
                }
                else
                {
                    var cfg = Config.Load();
                    foreach (var sql in sqlFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        var dbName = Path.GetFileNameWithoutExtension(sql);
                        try
                        {
                            log($"Importing database '{dbName}'…");
                            await RestoreDatabaseAsync(dbName, sql, cfg, ct).ConfigureAwait(false);
                            restoredDbs++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Database '{dbName}' was NOT restored: {ex.Message}");
                        }
                    }
                }
            }

            var msg = $"Restore finished: {restoredSites} site folder(s), {restoredDbs} database(s).";
            log(msg);
            foreach (var w in warnings) log("Warning: " + w);
            return new BackupResult(true, msg, warnings, zipPath);
        }
        catch (OperationCanceledException)
        {
            log("Restore cancelled.");
            return new BackupResult(false, "Restore cancelled.", warnings);
        }
        catch (Exception ex)
        {
            log($"Restore failed: {ex.Message}");
            return new BackupResult(false, $"Restore failed: {ex.Message}", warnings);
        }
        finally
        {
            SafeDeleteTree(tempDir);
        }
    }

    /// <summary>
    /// Extract with an explicit containment check on every entry ("zip slip"). A crafted archive
    /// with an entry named <c>..\..\Windows\System32\x.dll</c> must not escape the destination.
    /// </summary>
    private static void ExtractSafely(string zipPath, string destDir, CancellationToken ct)
    {
        var root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new BhException($"Refusing to extract '{entry.FullName}': it points outside the restore folder.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || entry.Length == 0 && entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public static void DeleteBackup(string id)
    {
        // id comes from a UI Tag; treat it as untrusted so it cannot reach outside BackupsDir.
        var name = ValidBackupName(id);
        var target = Path.Combine(BackupsDir, $"{name}.zip");
        if (File.Exists(target)) File.Delete(target);
    }

    // ── single-database dump / import ────────────────────────────────────────────────────────

    public static void DumpDatabase(string dbName, string outputPath, Config cfg)
        => DumpDatabaseAsync(dbName, outputPath, cfg).GetAwaiter().GetResult();

    public static async Task DumpDatabaseAsync(
        string dbName, string outputPath, Config cfg, CancellationToken ct = default)
    {
        MySqlAuthFile.ValidIdentifier(dbName, "database name");

        var engine = DbServer.ActiveEngine() ?? "mysql";
        var dumpExe = Tools.MysqldumpExe(engine);
        if (dumpExe == null || !File.Exists(dumpExe))
            throw new BhException($"mysqldump not found for '{engine}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var auth = MySqlAuthFile.Create("root", cfg.RootPassword, DbServer.Port);
        var res = await RunDumpAsync(dumpExe, auth, dbName, outputPath, ct).ConfigureAwait(false);

        if (!res.Ok)
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            var why = res.TimedOut ? $"timed out after {DbOpTimeoutMs / 60000} minutes" : FirstLine(res.StdErr);
            throw new BhException($"Failed to dump database '{dbName}': {why}");
        }
    }

    public static void RestoreDatabase(string dbName, string sqlPath, Config cfg)
        => RestoreDatabaseAsync(dbName, sqlPath, cfg).GetAwaiter().GetResult();

    public static async Task RestoreDatabaseAsync(
        string dbName, string sqlPath, Config cfg, CancellationToken ct = default)
    {
        // The CREATE DATABASE below interpolates the name into SQL. Backtick-quoting alone is not a
        // defence — a name containing a backtick closes the quote and the rest is executed. Validate.
        MySqlAuthFile.ValidIdentifier(dbName, "database name");

        if (!File.Exists(sqlPath)) throw new BhException($"SQL file not found: {sqlPath}");

        var engine = DbServer.ActiveEngine() ?? "mysql";
        var mysqlExe = Tools.MysqlClientFor(engine);
        if (mysqlExe == null || !File.Exists(mysqlExe))
            throw new BhException($"mysql client not found for '{engine}'.");

        using var auth = MySqlAuthFile.Create("root", cfg.RootPassword, DbServer.Port);

        // 1. Ensure the database exists.
        var createArgs = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        createArgs.Add("-e");
        createArgs.Add($"CREATE DATABASE IF NOT EXISTS `{dbName}`;");
        var create = await ProcRunner.RunAsync(mysqlExe, createArgs, timeoutMs: 60_000, ct: ct).ConfigureAwait(false);
        if (!create.Ok)
            throw new BhException($"Could not create database '{dbName}': {FirstLine(create.StdErr)}");

        // 2. Stream the dump into the client's stdin. Streaming (rather than reading the whole file
        //    into a string) matters: dumps are routinely hundreds of megabytes.
        var importArgs = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        importArgs.Add(dbName);
        await ImportSqlFileAsync(mysqlExe, importArgs, sqlPath, dbName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Feed a .sql file to the client over stdin, draining stdout AND stderr concurrently and with a
    /// hard timeout. Draining both matters: the client writes warnings to stderr, and if that pipe
    /// fills while we are busy copying the file into stdin, the child blocks and nothing ever
    /// completes.
    /// </summary>
    private static async Task ImportSqlFileAsync(
        string exe, IEnumerable<string> args, string sqlPath, string dbName, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && stderr.Length < 64 * 1024) stderr.Append(e.Data).Append('\n');
        };

        if (!proc.Start()) throw new BhException($"Could not start {Path.GetFileName(exe)}.");
        JobManager.Add(proc);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DbOpTimeoutMs);

        try
        {
            using (var fs = new FileStream(sqlPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                           bufferSize: 81920, useAsync: true))
            {
                await fs.CopyToAsync(proc.StandardInput.BaseStream, 81920, cts.Token).ConfigureAwait(false);
                await proc.StandardInput.BaseStream.FlushAsync(cts.Token).ConfigureAwait(false);
            }
            proc.StandardInput.Close();

            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ProcRunner.KillTree(proc);
            if (ct.IsCancellationRequested) throw;
            throw new BhException($"Import of '{dbName}' timed out after {DbOpTimeoutMs / 60000} minutes.");
        }

        var exit = -1;
        try { exit = proc.ExitCode; } catch { }
        if (exit != 0)
            throw new BhException($"Failed to import database '{dbName}': {FirstLine(stderr.ToString())}");
    }

    // ── scheduled database backup ────────────────────────────────────────────────────────────

    /// <summary>
    /// Dump every user database to <c>db_backups\yyyy-MM-dd\</c> and prune folders older than
    /// 5 days. Returns a result so callers can surface a failure — the previous version swallowed
    /// every error, including "the DB server isn't running", so a user could go weeks believing the
    /// automatic backup was working.
    /// </summary>
    public static async Task<BackupResult> AutoBackupAllDatabasesAsync(CancellationToken ct = default)
    {
        var warnings = new List<string>();
        try
        {
            var cfg = Config.Load();
            var backupDir = Path.Combine(Paths.Home, "db_backups");
            Directory.CreateDirectory(backupDir);

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var todayDir = Path.Combine(backupDir, today);
            Directory.CreateDirectory(todayDir);

            if (DbServer.ActiveEngine() is null)
                return new BackupResult(false, "Automatic database backup skipped: database server is not running.",
                                        warnings);

            var res = await DbExplorer.QueryMysqlAsync("SHOW DATABASES;").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(res.Error))
                return new BackupResult(false, $"Automatic database backup failed: {res.Error}", warnings);

            var dbs = res.Rows
                .Where(r => r.Length > 0)
                .Select(r => r[0])
                .Where(db => !SystemDatabases.Contains(db, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var done = 0;
            foreach (var db in dbs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DumpDatabaseAsync(db, Path.Combine(todayDir, $"{db}.sql"), cfg, ct).ConfigureAwait(false);
                    done++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { warnings.Add($"{db}: {ex.Message}"); }
            }

            // Mark the day's folder complete, so retention never deletes the only usable copy in
            // favour of a half-written newer one.
            try { File.WriteAllText(Path.Combine(todayDir, ".complete"), $"{done}/{dbs.Count}"); } catch { }

            PruneOldDbBackups(backupDir, warnings);

            var msg = $"Automatic database backup: {done}/{dbs.Count} database(s) dumped to {todayDir}.";
            return new BackupResult(done == dbs.Count, msg, warnings, todayDir);
        }
        catch (OperationCanceledException)
        {
            return new BackupResult(false, "Automatic database backup cancelled.", warnings);
        }
        catch (Exception ex)
        {
            return new BackupResult(false, $"Automatic database backup failed: {ex.Message}", warnings);
        }
    }

    private static void PruneOldDbBackups(string backupDir, List<string> warnings)
    {
        var limit = DateTime.Now.Date.AddDays(-5);
        string[] dirs;
        try { dirs = Directory.GetDirectories(backupDir); }
        catch (Exception ex) { warnings.Add($"Retention scan failed: {ex.Message}"); return; }

        // Keep at least one complete folder no matter how old, so a machine that has been off for a
        // month does not come back to zero backups.
        var dated = dirs
            .Select(d => (dir: d, ok: DateTime.TryParseExact(Path.GetFileName(d), "yyyy-MM-dd",
                              null, System.Globalization.DateTimeStyles.None, out var dt), date: default(DateTime)))
            .Select(t => (t.dir, t.ok, date: t.ok
                ? DateTime.ParseExact(Path.GetFileName(t.dir), "yyyy-MM-dd", null)
                : default))
            .Where(t => t.ok)
            .OrderByDescending(t => t.date)
            .ToList();

        foreach (var t in dated.Skip(1))
        {
            if (t.date >= limit) continue;
            try { Directory.Delete(t.dir, recursive: true); }
            catch (Exception ex) { warnings.Add($"Could not prune '{Path.GetFileName(t.dir)}': {ex.Message}"); }
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static bool IsSafeDbName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name ?? "", @"^[A-Za-z0-9_$]+$") && name!.Length <= 64;

    private static string FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "no error output";
        var line = s.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return string.IsNullOrEmpty(line) ? "no error output" : line;
    }

    private static void SafeDeleteTree(string dir)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch { Thread.Sleep(100 * (i + 1)); }
        }
    }
}
