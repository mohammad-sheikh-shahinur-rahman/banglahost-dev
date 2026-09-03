# BanglaHost Local Web Server — Comprehensive Audit

**Tree audited:** `I:\BanglaHost` @ `7d33255` ("Fix crashes, hangs, and command injections (v1.6.0.0)")
**App version in tree:** `1.6.0.0` (`src/BanglaHost.App/BanglaHost.App.csproj`)
**Stack:** WinUI 3 / Windows App SDK 1.6.241114003, .NET 8 (`net8.0-windows10.0.19041.0` app, `net8.0` core), MSIX, Microsoft Store
**Audit date:** 2026-09-03
**Method:** static source review only. No build, no run, no instrumentation. Anything not decidable from source is marked **“Requires runtime verification”**.

---

## 0. Why this audit exists: the telemetry, and what it can and cannot tell us

Partner Center reports, last 30 days:

| Metric | Value |
|---|---|
| Crashes | 33 |
| Hangs | 15 |
| Crash rate (opt-in) | 4.41 % |
| Hang rate (opt-in) | 8.82 % |
| Devices affected | 13 |
| Failure buckets | **1 row: “Uncategorized”, 48 hits (100 %), 28 devices (100 %)** |

Three things follow from this, and only these three:

1. **There are no stacks.** “Uncategorized … detailed diagnostic data is not currently available” means Partner Center received failure *counts* but no usable minidumps. So the cause has to be derived from source, which is what the rest of this document does. It also means the app’s own diagnostics are the only forensic channel that could have existed — and `CrashLogger` writes **only to a local file** (see **E5**), so nothing reaches you. That is itself a finding.
2. **Hang rate is 2× crash rate.** In Store telemetry an *AppHang* is recorded when the app stops pumping window messages for ~5 s or more. A 8.82 % hang rate on a desktop utility is high, and it makes **UI-thread blocking and thread-pool starvation the primary suspects**, ahead of exceptions. The investigation below is ordered accordingly.
3. **These numbers describe the *shipped* build, not this tree.** The telemetry window covers the 1.4.x line. This working tree is 1.6.0.0 and the most recent commit is literally titled *“Fix crashes, hangs, and command injections”*. So some of what users experienced is already partly remediated here, and some findings below may be pre-existing-but-already-improved rather than newly shipped. I have not attempted to attribute individual telemetry hits to individual findings — with no stacks, that would be invention. Where a finding is a *plausible* cause of the observed hangs I say so and say why; I do not claim it *is* the cause.

Also noted on the dashboard: *“Due to a temporary issue, we are experiencing delay in few datasets.”* The 33-crash / 13-device figures vs. the table’s 48-hit / 28-device figures are inconsistent with each other, so treat the absolute counts as provisional; the **ratio** (hangs ≫ crashes as a share of sessions) is the durable signal.

---

## 1. Findings index

Severity counts: **7 Critical**, **14 High**, **18 Medium**, **6 Low** — 45 findings.

| ID | Severity | Area | One-line |
|---|---|---|---|
| A1 | Critical | Hang | Dashboard’s 2 s timer re-enters an unguarded `async void Refresh()` → thread-pool starvation |
| A2 | Critical | Data loss | `BackupService.RestoreAsync` returns `true` having restored nothing |
| A3 | Critical | Data loss | `BackupAllAsync` silently produces database-less backups, reports success |
| A4 | Critical | Broken feature | SQL Studio never executes any SQL (`<` redirect with no shell) + UI-thread block |
| A5 | Critical | Data loss | `Config.Save()` non-atomic + `Load()` fail-soft → silent loss of all settings incl. DB password |
| A6 | Critical | Lifecycle | CLI `start` kills the services it just started (job object dies with the CLI process) |
| A7 | Critical | Crash | `JobObject` static-init failure escapes `JobManager.Add`’s catch → all 11 spawn paths throw |
| B1 | Critical | Security | Zero integrity verification on ~30 downloaded-and-executed binaries |
| B2 | High | Security | Three download URLs are scraped from HTML pages |
| B3 | High | Security | DB root password passed on the command line (~6 call sites) |
| B4 | High | Security | `Database.BaseArgs` builds `-p{pw}` unquoted → argument injection |
| B5 | High | Security | Unvalidated IP written to `hosts` **inside the elevated helper** |
| B6 | Medium | Security | `Hosts.Remove` substring-matches → deletes unrelated sites’ entries |
| B7 | High | Security | `SslService` unvalidated `domain` → arbitrary `.pem` write + mkcert argument injection |
| B8 | Medium | Security | `DbExplorer.QueryMysqlAsync` passes SQL via `-e` with quote-only escaping |
| B9 | Medium | Security | `BackupService.RestoreDatabase` backtick SQL injection |
| B10 | Medium | Security | `RootPassword` persisted in plaintext |
| B11 | Medium | Security | Bare `powershell` / `cmd.exe` / `wt.exe` resolved via `PATH` |
| B12 | Medium | Security | `SiteListControl` builds nested-quoted shell command strings |
| B13 | Medium | Security | `InstallerService` uses `cmd.exe /c {cmd} {args}` |
| B14 | Medium | Reliability | `NetUtils.IsPortAvailable` fails **open** |
| B15 | Low | Security | `Elevation.Run` trailing-backslash quoting bug |
| C1 | High | Hang | 9 pages call expensive Core work directly on the dispatcher thread |
| C2 | High | Hang | 26 of 31 `WaitForExit()` calls have no timeout; `Elevation.Run` waits on UAC forever |
| C3 | High | Hang | `DbServer.Stop()` runs a full synchronous backup of every DB during shutdown |
| C4 | High | Hang | `Downloader.CurlTo` is fake-async — `await` never yields |
| C5 | High | Hang | `Downloader.Shell` drains stderr before stdout → pipe deadlock |
| C6 | Medium | Hang | `PackageManagerPage` does `LogViewer.Text += line` per output line |
| C7 | Medium | Hang | `Localizer` walks the whole visual tree 4× per navigation on the UI thread |
| C8 | High | Crash | Global exception handlers wired **after** `ApplySavedLanguage()` + `InitializeComponent()` |
| C9 | High | Crash | `AppDomain.UnhandledException` is log-only in .NET Core — background throws are fatal |
| C10 | Medium | Leak | `TrayIcon` double-`Dispose()` on every tray-quit; `hIcon` and window class leaked |
| C11 | Medium | Crash | `PhpCgi` watchdog can respawn itself recursively |
| C12 | Medium | Bug | `DbServer.Running()` disposes the `TcpClient` while the connect may still be pending |
| C13 | Medium | Leak | `Tunnel` `Process` never disposed, never job-tracked, 45 s blocking wait |
| C14 | Medium | Crash | `ProgressBar` access-violation worked around with `try {} catch {}` instead of fixed |
| D1 | High | Perf | `Config.Load()` re-reads + re-deserializes JSON on every call, uncached |
| D2 | Medium | Perf | `Services.Enabled()` re-reads the enabled file 37× per snapshot |
| D3 | Low | Perf | `DbServer.ActiveEngine()` probed twice per snapshot |
| D4 | Medium | Perf | `Engine.Api()` liveness probes are sequential, not parallel |
| D5 | Medium | Leak | `Paths.CleanTmp()` exists and is never called → unbounded temp growth |
| D6 | Low | Leak | `X509Certificate2` never disposed in `GetLocalCertificates()` |
| D7 | High | Hang | No `ThreadPool.SetMinThreads` anywhere — pool cannot absorb the blocking burst |
| E1 | Medium | Arch | Two parallel PHP-extensions pages with divergent behaviour |
| E2 | High | Arch | `EngineHost`’s off-thread contract is unenforceable and routinely bypassed |
| E3 | Medium | Arch | Core exposes a synchronous-only API, forcing every caller to invent threading |
| E4 | Medium | Arch | Process ownership held in a `static` job object with no lifetime owner |
| E5 | High | Arch | Crash diagnostics are local-file-only — the direct reason your telemetry is “Uncategorized” |
| F1 | High | UX | Version comparison off-by-one → “Update available” prompt on essentially every launch |
| F2 | Medium | UX | Let’s Encrypt button is an enabled stub that fakes 2 s of work and fails silently |
| F3 | Low | UX | `SslPage` hardcodes `.test`, ignoring the configurable TLD |
| F4 | High | A11y | `AutomationProperties` appears in **0 of 50** XAML views |
| F5 | Medium | L10n | 61 resource strings + `x:Uid` in one file; the rest of Bengali relies on a timed runtime tree walker |
| F6 | Medium | UX | `BackupPage` discards the restore result, so even a real failure looks like success |
| G1 | Low | Debt | nginx `root` directive injection — CLI-only, unreachable from the GUI's folder picker |
| G2 | Low | Debt | Test project exists (3 test methods) but is not in the solution; no CI at all |
| G3 | Low | Debt | 176 empty `catch { }` blocks (95 Core / 81 App) — no signal path for the unexpected |
| G4 | Low | Debt | 167 `async void` with copy-pasted guards; `DashboardPage.Refresh()` is `async void` but not a handler |
| G5 | Low | Debt | 531 build-output files committed under `publish-msix/` |
| G6 | Low | Debt | Mojibake in 16 source files — UTF-8 re-saved as CP1252; risk to Bengali strings |

---

## A. Critical issues

### A1 — Dashboard’s 2-second timer re-enters an unguarded `async void Refresh()`, starving the thread pool

**Severity:** Critical
**File:** `src/BanglaHost.App/Views/DashboardPage.xaml.cs`
**Class/Method:** `DashboardPage._timer` / `OnTimerTick` / `Refresh()`; with `MainWindow.Nav_Loaded`, `EngineHost.Snapshot`, `Engine.Api`, `DbServer.Running`

**Exact problematic code**

```csharp
// DashboardPage.xaml.cs:18
private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
private bool _loading, _pageSizeSet;                                    // :19

private void OnTimerTick(object? sender, object e) => Refresh();        // :36

protected override void OnNavigatedTo(NavigationEventArgs e)            // :39
{
    EngineHost.Instance.LogAppended += OnLog;
    _timer.Tick += OnTimerTick;
    SiteList.Changed += OnSiteListChanged;
    Refresh();
    _timer.Start();
}

private async void Refresh()                                            // :64
{
    try
    {
    Snapshot snap;
    try { snap = await EngineHost.Instance.Snapshot(); } catch { return; }
    ...
```

```csharp
// MainWindow.xaml.cs:271 — every user lands here
ContentFrame.Navigate(typeof(DashboardPage));
```

```csharp
// EngineHost.cs:72
public Task<Snapshot> Snapshot() => Task.Run(() => Engine.Api());
```

```csharp
// DbServer.cs (Running) — the shape repeated across the status probes
using var c = new TcpClient();
return c.ConnectAsync("127.0.0.1", port).Wait(600);
```

**Why it is a problem**

`_loading` is **not** a re-entrancy guard. Its only uses are as a toggle-suppression flag around `SetTool` (`:178`, `:182`) and in `Pma_Toggled` / `Adm_Toggled` / `Mail_Toggled` (`:200`–`:202`). There is no `SemaphoreSlim`, no `_refreshing` check, and no `CancellationToken` anywhere in the class. So:

* The timer fires every 2 s and calls `Refresh()` unconditionally. `Refresh()` is `async void`, so the tick returns immediately and the next tick fires regardless of whether the previous one finished.
* Each `Refresh()` starts `Task.Run(() => Engine.Api())`. `Api()` enumerates all 37 rows of `Services.All`, computing `Installed`, `Enabled` (re-reading the enabled file per row — **D2**) and a **blocking** loopback liveness probe of the form `ConnectAsync(...).Wait(400–600)`, plus `DbServer.ActiveEngine()` twice (**D3**), all sequentially (**D4**).
* Each blocking `.Wait(ms)` is doubly hostile to the pool: it **pins** the worker thread for the duration *and* the socket completion it is waiting for needs *another* pool thread to run. That is the textbook thread-pool deadlock-by-starvation pattern.
* On a machine where loopback is *filtered* by AV/firewall — a scenario this project’s own `ANTIVIRUS.md` documents as common for this app — the probes do not refuse instantly, they run to timeout. Worst case an `Api()` pass costs several seconds. At a 2 s tick, snapshots overlap 2–3 deep and keep accumulating.
* There is no `ThreadPool.SetMinThreads` call anywhere in the solution (**D7**), so beyond the default minimum the pool injects new threads at roughly 1–2 per second. It cannot outrun the arrival rate.

Once the pool is starved, everything that needs a pool thread stalls: `await` continuations posted back to the dispatcher, `DispatcherQueue.TryEnqueue` callbacks, `Process.WaitForExitAsync`, the `HttpClient` update check, and `Localizer.RewalkAfterAsync`’s `Task.Delay`s. The UI thread runs out of queued work to complete and stops pumping → **Windows records an AppHang.**

This is the single strongest source-level explanation for a hang rate that is double the crash rate, and it is consistent with a fault that hit 28 devices: `Nav_Loaded` navigates **every** user to this page on **every** launch. No unusual configuration is needed to reach it.

Attribution of specific telemetry hits: **Requires runtime verification** (no stacks were captured).

**Reproduction scenario**

1. Install BanglaHost on a machine with a third-party AV that filters loopback connections (or add a WFP filter that silently drops `127.0.0.1` SYNs to the service ports).
2. Launch the app and leave it on the Dashboard.
3. Attach `dotnet-counters monitor --counters System.Runtime` and watch `threadpool-thread-count` and `threadpool-queue-length`.
4. Queue length climbs monotonically, thread count creeps up ~1/s, and the window becomes unresponsive to input for >5 s. Windows Error Reporting logs an AppHang for `BanglaHost.App.exe`.

A faster deterministic variant that does not need AV: keep 8+ services installed but stopped so every probe must time out, and shrink `_timer.Interval` to 500 ms — the starvation appears within a minute.

**Recommended fix**

Four independent changes, all needed:

1. **Guard re-entrancy.** One refresh at a time; drop overlapping ticks instead of queueing them.
2. **Make the timer self-rescheduling** rather than fixed-rate: measure the work, then wait. A slow machine gets a slower refresh instead of a growing backlog.
3. **Stop blocking pool threads in the probes.** Replace `ConnectAsync(...).Wait(ms)` with a genuinely async `ConnectAsync` + `CancellationTokenSource` timeout, and make `Api()` build an async snapshot with the probes running concurrently (see **D4**).
4. **Raise the pool floor** at startup so a transient burst does not starve (see **D7**).

Also: cancel in-flight work on navigate-away, and stop the timer when the window is hidden to tray — a tray-resident instance currently keeps probing forever.

**Suggested corrected code**

```csharp
// DashboardPage.xaml.cs
private readonly SemaphoreSlim _refreshGate = new(1, 1);
private CancellationTokenSource? _cts;
private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

private void OnTimerTick(object? sender, object e) => _ = RefreshAsync();

protected override void OnNavigatedTo(NavigationEventArgs e)
{
    _cts = new CancellationTokenSource();
    EngineHost.Instance.LogAppended += OnLog;
    _timer.Tick += OnTimerTick;
    SiteList.Changed += OnSiteListChanged;
    _ = RefreshAsync();
    _timer.Start();
}

protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    _timer.Stop();
    _timer.Tick -= OnTimerTick;
    SiteList.Changed -= OnSiteListChanged;
    EngineHost.Instance.LogAppended -= OnLog;
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = null;
    base.OnNavigatedFrom(e);
}

private async Task RefreshAsync()
{
    // Drop this tick entirely if a refresh is already in flight — never queue.
    if (!_refreshGate.Wait(0)) return;
    var token = _cts?.Token ?? CancellationToken.None;
    _timer.Stop();                                  // self-rescheduling: measure, then wait
    try
    {
        var snap = await EngineHost.Instance.Snapshot(token).ConfigureAwait(true);
        if (token.IsCancellationRequested) return;
        ApplySnapshot(snap);                        // pure UI updates, no I/O
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { CrashLogger.Log(ex, "DashboardRefresh"); }
    finally
    {
        _refreshGate.Release();
        if (!token.IsCancellationRequested && _cts is not null) _timer.Start();
    }
}
```

```csharp
// EngineHost.cs — thread the token through
public Task<Snapshot> Snapshot(CancellationToken ct = default) =>
    Task.Run(() => Engine.Api(ct), ct);
```

```csharp
// A non-pool-blocking probe, to replace every ConnectAsync(...).Wait(ms) site
internal static async Task<bool> IsListeningAsync(int port, int timeoutMs, CancellationToken ct)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeoutMs);
    using var client = new TcpClient();
    try
    {
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
        return true;
    }
    catch (OperationCanceledException) { return false; }
    catch (SocketException)            { return false; }
}
```

**Regression test**

```csharp
[Fact]
public async Task Dashboard_refresh_never_overlaps()
{
    var concurrent = 0; var maxConcurrent = 0;
    var page = new DashboardTestHarness(snapshot: async ct =>
    {
        var n = Interlocked.Increment(ref concurrent);
        maxConcurrent = Math.Max(maxConcurrent, n);
        await Task.Delay(500, ct);                 // slower than the 2s tick? no — force overlap window
        Interlocked.Decrement(ref concurrent);
        return new Snapshot();
    });

    page.Timer.Interval = TimeSpan.FromMilliseconds(50);   // 10x faster than the work
    page.SimulateNavigatedTo();
    await Task.Delay(3000);
    page.SimulateNavigatedFrom();

    Assert.Equal(1, maxConcurrent);                 // the gate held
}

[Fact]
public async Task Api_snapshot_does_not_block_pool_threads()
{
    ThreadPool.GetMinThreads(out var minW, out _);
    var before = ThreadPool.ThreadCount;
    // 20 unreachable ports: with blocking .Wait() this pins 20 workers.
    await Task.WhenAll(Enumerable.Range(0, 20)
        .Select(i => NetUtils.IsListeningAsync(59000 + i, 600, default)));
    Assert.True(ThreadPool.ThreadCount <= before + 2,
        $"probes injected {ThreadPool.ThreadCount - before} threads — they are still blocking");
}
```

---

### A2 — `BackupService.RestoreAsync` reports success while restoring nothing

**Severity:** Critical (silent data loss / false assurance)
**File:** `src/BanglaHost.Core/BackupService.cs`
**Class/Method:** `BackupService.RestoreAsync(string zipPath, Action<string> log)`

**Exact problematic code**

```csharp
public static async Task<bool> RestoreAsync(string zipPath, Action<string> log)
{
    log($"Restoring from {zipPath}...");
    log("Restore feature is currently a dry-run in this implementation to prevent accidental data loss.");
    await Task.Delay(2000);
    return true;
}
```

Caller:

```csharp
// src/BanglaHost.App/Views/BackupPage.xaml.cs:71
await Task.Run(() => BackupService.RestoreAsync(path, log));   // return value discarded
```

**Why it is a problem**

The method returns `true` — the success value — after doing nothing but sleeping. `BackupPage` discards the result anyway (**F6**), so the UI shows a completed operation. The only signal that nothing happened is one line in a log pane the user has no reason to read after a “successful” restore.

The failure mode is not “restore doesn’t work”. It is: a user whose site or database is broken runs Restore, is told it succeeded, concludes their backup is bad or their data is unrecoverable, and **deletes or overwrites the working state** — the one thing a backup feature exists to prevent. A restore that throws “not implemented” is safe. A restore that lies is destructive.

Note also `await Task.Run(() => RestoreAsync(...))` — wrapping an async method in `Task.Run` returns `Task<Task<bool>>`; because the outer task is awaited but the inner is not, this would not even observe a real exception if one were thrown. That bug is latent today only because the body cannot fail.

**Reproduction scenario**

1. Create a backup (Backup page → Backup all).
2. Drop a database or delete a site root.
3. Backup page → Restore, select the zip.
4. UI reports completion; nothing on disk or in MySQL has changed.

**Recommended fix**

Do not ship a stub behind a success return. Either implement restore, or fail loudly and disable the control. Minimum acceptable: return `false`, surface it, and mark the button as unavailable in this build.

**Suggested corrected code**

```csharp
public static Task<bool> RestoreAsync(string zipPath, Action<string> log)
{
    log("Restore is not available in this build.");
    throw new BhException(
        "Restore is not implemented yet. Extract the backup zip manually and use " +
        "Databases → Restore for each .sql dump. Tracked as #<issue>.");
}
```

```csharp
// BackupPage.xaml.cs — observe the result, and don't Task.Run an async method
try
{
    var ok = await BackupService.RestoreAsync(path, log);
    await ShowResult(ok ? "Restore complete." : "Restore failed — see the log.");
}
catch (BhException ex) { await ShowResult(ex.Message); }
```

```xml
<!-- BackupPage.xaml — until implemented -->
<Button x:Name="RestoreBtn" Content="Restore…" IsEnabled="False"
        ToolTipService.ToolTip="Restore is not available in this build."
        AutomationProperties.Name="Restore from backup (unavailable in this build)" />
```

**Regression test**

```csharp
[Fact]
public async Task RestoreAsync_never_reports_success_without_restoring()
{
    var db = TestMySql.Fresh();
    db.Exec("CREATE DATABASE probe; USE probe; CREATE TABLE t(id INT); INSERT INTO t VALUES (1);");
    var zip = await BackupService.BackupAllAsync("t1", _ => { });
    db.Exec("DROP DATABASE probe;");

    bool ok;
    try { ok = await BackupService.RestoreAsync(zip, _ => { }); }
    catch (BhException) { return; }                 // acceptable: loud failure

    // If it claims success it must actually have restored.
    Assert.True(ok == db.DatabaseExists("probe"),
        "RestoreAsync returned true but the database was not restored");
}
```

---

### A3 — `BackupAllAsync` silently produces database-less backups and reports “completed successfully”

**Severity:** Critical (silent data loss)
**File:** `src/BanglaHost.Core/BackupService.cs`
**Class/Method:** `BackupService.BackupAllAsync(string backupName, Action<string> log)`

**Exact problematic code**

```csharp
var mysqlExe = Path.Combine(Paths.Bin, "mysql", "bin", "mysqldump.exe");   // hardcoded engine dir
if (File.Exists(mysqlExe))
{
    ...
    var dumped = await RunDumpAsync(mysqlExe, $"--opt -u root {db} --result-file=\"{dumpPath}\"");
    if (dumped) log($"Dumped database: {db}");     // no else — failures vanish
}
ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);
log("Backup completed successfully.");
return true;
```

**Why it is a problem**

Three independent ways this yields an empty-of-data backup, all of them silent:

1. **`-u root` with no password.** Every other DB call site in the codebase reads `cfg.RootPassword` (see **B3**), so a user who set a root password — which the app itself encourages — gets `ERROR 1045 Access denied` for every database. `dumped` is `false`, the `if` is skipped, and there is **no `else`**: no warning, no log line, no non-success return.
2. **Hardcoded `bin\mysql\bin\mysqldump.exe`.** MariaDB installs under a different directory, and `Tools.MysqldumpExe(engine)` already exists to resolve it (it is used correctly in `DumpDatabase` in the same file). A MariaDB-only user fails the `File.Exists` check, so the entire database phase is skipped without a word.
3. **Unconditional `return true` + “completed successfully”.** The method’s contract makes no distinction between “backed up everything” and “backed up some folders and zero databases”.

The user then has a plausible-looking zip of a sensible size (site files compress to something), sitting in their backup folder, containing none of their data. They find out when they need it.

**Reproduction scenario**

1. Set a MySQL root password in Settings (or install MariaDB instead of MySQL).
2. Create at least one database with data.
3. Backup page → Backup all.
4. Log shows “Backup completed successfully” with **no** “Dumped database: …” lines. Open the zip: no `.sql` files.

**Recommended fix**

Resolve the dump tool through `Tools.MysqldumpExe(engine)`; authenticate exactly as the rest of the codebase does (and via a defaults-file, not the command line — see **B3**); treat any failed dump as a failed backup; and never emit “successfully” unless every phase succeeded.

**Suggested corrected code**

```csharp
public static async Task<bool> BackupAllAsync(string backupName, Action<string> log)
{
    var failures = new List<string>();
    var engine  = DbServer.ActiveEngine();
    var dumpExe = Tools.MysqldumpExe(engine);

    if (dumpExe is null || !File.Exists(dumpExe))
        failures.Add($"mysqldump not found for engine '{engine}' — databases were NOT backed up");
    else
    {
        // Password via a 0600-ish temp defaults file, never on the command line (see B3).
        using var creds = MySqlCredentialsFile.Create(Config.Load().RootPassword);
        foreach (var db in await ListDatabasesAsync(log))
        {
            var dumpPath = Path.Combine(tempDir, $"{db}.sql");
            var ok = await RunDumpAsync(dumpExe, new[]
            {
                $"--defaults-extra-file={creds.Path}",
                "--opt", "-u", "root", db, $"--result-file={dumpPath}",
            });
            if (ok) log($"Dumped database: {db}");
            else    failures.Add($"database '{db}' could not be dumped");
        }
    }

    ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);

    if (failures.Count > 0)
    {
        foreach (var f in failures) log($"[WARN] {f}");
        log($"Backup finished with {failures.Count} problem(s) — this archive is INCOMPLETE.");
        return false;
    }
    log("Backup completed successfully.");
    return true;
}
```

**Regression test**

```csharp
[Theory]
[InlineData("mysql",   "s3cret")]
[InlineData("mariadb", "s3cret")]
[InlineData("mysql",   "")]
public async Task BackupAll_includes_every_database_or_returns_false(string engine, string pw)
{
    using var env = TestEnv.WithEngine(engine, rootPassword: pw);
    env.Db.Exec("CREATE DATABASE alpha; CREATE DATABASE beta;");

    var log = new List<string>();
    var ok  = await BackupService.BackupAllAsync("t", log.Add);

    var names = ZipFile.OpenRead(env.LatestBackupZip).Entries.Select(e => e.Name).ToList();
    if (ok)
    {
        Assert.Contains("alpha.sql", names);
        Assert.Contains("beta.sql",  names);
    }
    else
    {
        Assert.Contains(log, l => l.Contains("INCOMPLETE"));
    }
    // The one thing that must never happen:
    Assert.False(ok && !names.Contains("alpha.sql"), "reported success with no dump");
}
```

---

### A4 — SQL Studio never executes any SQL: shell redirection with `UseShellExecute = false`

**Severity:** Critical (feature is entirely non-functional) — plus a UI-thread block, a credential exposure and a temp-file leak in the same 12 lines
**File:** `src/BanglaHost.App/Views/DatabaseExplorerPage.xaml.cs`
**Class/Method:** `DatabaseExplorerPage.RunSql(string sql, string db = "")` (line 27), called from `LoadDatabases` (line 64), table load (line 82) and `Execute` (line 111)

**Exact problematic code**

```csharp
private string RunSql(string sql, string db = "")
{
    var auth = string.IsNullOrEmpty(cfg.RootPassword) ? "-u root" : $"-u root -p\"{cfg.RootPassword}\"";
    var tmpSql = Path.Combine(Paths.Tmp, $"query_{Guid.NewGuid():N}.sql");
    File.WriteAllText(tmpSql, sql);
    var psi = new ProcessStartInfo {
        FileName = exe,
        Arguments = $"{auth} -t {(string.IsNullOrEmpty(db) ? "" : db)} < \"{tmpSql}\"",
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    using var p = Process.Start(psi)!;
    var _errT = p.StandardError.ReadToEndAsync(); var outText = p.StandardOutput.ReadToEnd(); var errText = _errT.Result;
    p.WaitForExit();
    File.Delete(tmpSql);
```

```csharp
protected override void OnNavigatedTo(NavigationEventArgs e) { LoadDatabases(); }
...
ResultBox.Text = RunSql(sql, _currentDb);     // line 111 — on the UI thread
```

**Why it is a problem**

Four defects, one method:

1. **The redirect does nothing.** `<` is a *shell* operator. `UseShellExecute = false` means there is no shell — Windows hands the command line straight to `mysql.exe`, which parses `<` and `"C:\...\query_xxx.sql"` as **positional arguments**. `mysql`’s first positional argument is the database name, so it receives a database literally named `<`. The SQL file is never opened. **No statement the user types is ever executed**, and the page’s own database/table listings (`SHOW DATABASES;`, `SHOW TABLES;`) fail the same way — so SQL Studio is non-functional end to end, not merely for user queries. Verified by inspection; the exact stderr text `mysql.exe` emits for this case is **Requires runtime verification**.
2. **Root password on the command line.** `-p"{pw}"` is visible to any process on the machine via `Win32_Process.CommandLine` / `wmic`. See **B3**.
3. **Blocks the UI thread, unbounded.** `p.StandardOutput.ReadToEnd()`, `_errT.Result` and `p.WaitForExit()` all run on the dispatcher thread (`OnNavigatedTo` → `LoadDatabases` → `RunSql`, and `Execute` → line 111) with **no timeout**. A hung or slow `mysql.exe` freezes the window indefinitely. This is a direct AppHang contributor (**C1**, **C2**).
4. **Temp file leaks on any exception.** `File.Delete(tmpSql)` is not in a `finally`, and `Paths.CleanTmp()` is never called anywhere in the solution (**D5**), so `query_*.sql` files — containing whatever the user typed — accumulate forever in `Paths.Tmp`.

**Reproduction scenario**

1. Install and start MySQL or MariaDB; create a database.
2. Open **SQL Studio**. The database dropdown is empty (the `SHOW DATABASES;` call failed).
3. Type `SELECT 1;` and Execute. The result pane shows a `mysql` usage/access error rather than a result set.
4. `Get-CimInstance Win32_Process | ? Name -eq 'mysql.exe' | select CommandLine` during the call shows the root password in clear text.
5. Repeat 20 times, then inspect `%BANGLAHOST_HOME%\tmp` — `query_*.sql` files remain after the failures.

**Recommended fix**

Feed SQL over **stdin** (that is what the `<` was trying to express), pass credentials through a defaults-extra-file, run the whole thing off the UI thread with a timeout and cancellation, and clean up in `finally`. `DbExplorer.RunAsync` in Core is already the correct pattern — this page should call it rather than re-implementing process launching in the view.

**Suggested corrected code**

```csharp
// Core: one correct implementation, async, no shell, no password on the command line.
public static async Task<(bool ok, string output)> RunScriptAsync(
    string sql, string? db, TimeSpan timeout, CancellationToken ct)
{
    var exe = Tools.MysqlExe(DbServer.ActiveEngine())
              ?? throw new BhException("mysql client not installed");

    using var creds = MySqlCredentialsFile.Create(Config.Load().RootPassword);
    var psi = new ProcessStartInfo
    {
        FileName = exe,
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    };
    psi.ArgumentList.Add($"--defaults-extra-file={creds.Path}");
    psi.ArgumentList.Add("-u"); psi.ArgumentList.Add("root");
    psi.ArgumentList.Add("-t");
    if (!string.IsNullOrEmpty(db))
    {
        if (!Database.ValidName(db)) throw new BhException($"invalid database name: {db}");
        psi.ArgumentList.Add("-D"); psi.ArgumentList.Add(db);
    }

    using var p = Process.Start(psi)!;
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);

    // Read BOTH pipes concurrently, then wait — never one-to-completion-then-the-other (see C5).
    var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
    var stderr = p.StandardError.ReadToEndAsync(cts.Token);
    await p.StandardInput.WriteAsync(sql.AsMemory(), cts.Token);
    p.StandardInput.Close();

    try
    {
        await p.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        return (false, $"Query timed out after {timeout.TotalSeconds:0}s and was cancelled.");
    }
    return (p.ExitCode == 0, await stdout + await stderr);
}
```

```csharp
// DatabaseExplorerPage.xaml.cs — view does no process work
protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadDatabasesAsync();

private async Task ExecuteAsync()
{
    ExecuteBtn.IsEnabled = false;
    try
    {
        var (ok, output) = await DbExplorer.RunScriptAsync(
            SqlBox.Text, _currentDb, TimeSpan.FromSeconds(30), _cts.Token);
        ResultBox.Text = output;
        StatusText.Text = ok ? "OK" : "Failed";
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { ResultBox.Text = ex.Message; CrashLogger.Log(ex, "SqlStudio"); }
    finally { ExecuteBtn.IsEnabled = true; }
}
```

**Regression test**

```csharp
[Fact]
public async Task SqlStudio_actually_executes_statements()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "s3cret");
    var (ok, output) = await DbExplorer.RunScriptAsync(
        "SELECT 41 + 1 AS answer;", null, TimeSpan.FromSeconds(10), default);
    Assert.True(ok, output);
    Assert.Contains("42", output);
}

[Fact]
public async Task SqlStudio_passes_no_password_on_the_command_line()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "s3cret");
    var seen = ProcessCommandLineRecorder.Start("mysql.exe");
    await DbExplorer.RunScriptAsync("SELECT SLEEP(1);", null, TimeSpan.FromSeconds(10), default);
    Assert.DoesNotContain("s3cret", string.Join(" ", seen.CommandLines));
}

[Fact]
public async Task SqlStudio_times_out_instead_of_hanging()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "");
    var sw = Stopwatch.StartNew();
    var (ok, output) = await DbExplorer.RunScriptAsync(
        "SELECT SLEEP(60);", null, TimeSpan.FromSeconds(2), default);
    sw.Stop();
    Assert.False(ok);
    Assert.Contains("timed out", output);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
}

[Fact]
public async Task SqlStudio_leaves_no_temp_files()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "");
    var before = Directory.GetFiles(Paths.Tmp).Length;
    try { await DbExplorer.RunScriptAsync("SELECT bad syntax;", null, TimeSpan.FromSeconds(5), default); }
    catch { }
    Assert.Equal(before, Directory.GetFiles(Paths.Tmp).Length);
}
```

---

### A5 — Non-atomic `Config.Save()` plus fail-soft `Config.Load()` silently erases every setting

**Severity:** Critical (silent total settings loss, including the DB root password)
**File:** `src/BanglaHost.Core/Config.cs`
**Class/Method:** `Config.Load()` (line 38) / `Config.Save()` (line 56)

**Exact problematic code**

```csharp
public static Config Load()                                   // :38
{
    try
    {
        ...
        var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.ConfigJson), Opts);   // :44
        ...
    }
    catch { /* fall through to defaults */ }                   // :52
    return new Config();
}

public void Save()                                            // :56
{
    File.WriteAllText(Paths.ConfigJson, JsonSerializer.Serialize(this, Opts));   // :59
}
```

**Why it is a problem**

`File.WriteAllText` truncates the target to zero length and then writes. If the process is killed, the machine loses power, or an AV scanner locks the file **between** those two steps, `config.json` is left empty or half-written.

On the next `Load()`, `JsonSerializer.Deserialize` throws, and the `catch` swallows it and returns `new Config()`. From the user’s point of view: **every setting silently reverts to default.** Ports, TLD, sites root, auto-start selections, `MinimizeToTray`, `AutoUpdate`, `DashboardPageSize` — and `RootPassword`.

Losing `RootPassword` is the severe case. The password still exists inside MySQL/MariaDB; the app has simply forgotten it. Every subsequent DB operation authenticates with an empty password and fails with `Access denied`, and the app offers no way to re-enter a password it no longer knows was set. The user is locked out of their own databases by a config write that was interrupted.

There is also no lock and no cache: `Load()` re-reads and re-deserializes on **every** call (**D1**), and concurrent `Save()` calls from the UI thread and a background operation can interleave on the same file with no coordination.

**Reproduction scenario**

1. Set a root password and change a few settings, so `config.json` is non-default.
2. Reproduce a truncated write, either of:
   * Kill `BanglaHost.App.exe` from Task Manager repeatedly while toggling a setting (racy but reproducible), or
   * deterministically: with the app closed, write `{` into `config.json` to emulate a half-written file.
3. Launch the app. All settings are at defaults, with **no error, no warning, and no backup**. DB pages now report access denied.

**Recommended fix**

Write to a temp file in the same directory, flush to disk, then `File.Replace` with a backup — an atomic rename on NTFS. On load, if the primary file fails to parse, try the `.bak`; if that also fails, **quarantine** the bad file and tell the user rather than silently defaulting. Cache the parsed instance and serialise access.

**Suggested corrected code**

```csharp
public sealed partial class Config
{
    private static readonly object Gate = new();
    private static Config? _cache;
    private static DateTime _cacheStamp;

    public static Config Load()
    {
        lock (Gate)
        {
            var stamp = File.Exists(Paths.ConfigJson)
                ? File.GetLastWriteTimeUtc(Paths.ConfigJson) : DateTime.MinValue;
            if (_cache is not null && stamp == _cacheStamp) return _cache;

            var cfg = TryRead(Paths.ConfigJson) ?? TryRead(Paths.ConfigJson + ".bak");
            if (cfg is null && File.Exists(Paths.ConfigJson))
            {
                // Never silently discard a user's settings: keep the evidence and say so.
                var quarantine = Paths.ConfigJson + $".corrupt";
                try { File.Move(Paths.ConfigJson, quarantine, overwrite: true); } catch { }
                LoadFault = $"config.json was unreadable and has been moved to {quarantine}. " +
                            "Settings were reset to defaults — re-enter your database password in Settings.";
            }
            _cache = cfg ?? new Config();
            _cacheStamp = stamp;
            return _cache;
        }
    }

    /// <summary>Non-null when the last Load() had to fall back to defaults. Surfaced by the shell on startup.</summary>
    public static string? LoadFault { get; private set; }

    private static Config? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<Config>(text, Opts);
        }
        catch { return null; }
    }

    public void Save()
    {
        lock (Gate)
        {
            var dir  = Path.GetDirectoryName(Paths.ConfigJson)!;
            Directory.CreateDirectory(dir);
            var tmp  = Path.Combine(dir, $".config.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(this, Opts);

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(flushToDisk: true);      // durability barrier before the rename
            }

            if (File.Exists(Paths.ConfigJson))
                File.Replace(tmp, Paths.ConfigJson, Paths.ConfigJson + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tmp, Paths.ConfigJson);

            _cache = this;
            _cacheStamp = File.GetLastWriteTimeUtc(Paths.ConfigJson);
        }
    }
}
```

`RootPassword` should additionally be protected at rest — see **B10**.

**Regression test**

```csharp
[Fact]
public void Save_is_atomic_under_abrupt_termination()
{
    using var env = TestEnv.New();
    new Config { RootPassword = "s3cret", HttpPort = 8080 }.Save();

    // Simulate a torn write: a stale temp file plus an untouched primary.
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Paths.ConfigJson)!, ".config.torn.tmp"), "{");
    Config.ResetCache();

    var reloaded = Config.Load();
    Assert.Equal("s3cret", reloaded.RootPassword);   // primary intact
    Assert.Equal(8080, reloaded.HttpPort);
}

[Fact]
public void Load_recovers_from_backup_and_reports_when_it_cannot()
{
    using var env = TestEnv.New();
    new Config { RootPassword = "s3cret" }.Save();
    new Config { RootPassword = "s3cret" }.Save();          // creates .bak
    File.WriteAllText(Paths.ConfigJson, "{ truncated");
    Config.ResetCache();

    Assert.Equal("s3cret", Config.Load().RootPassword);     // recovered from .bak

    File.WriteAllText(Paths.ConfigJson, "{ truncated");
    File.WriteAllText(Paths.ConfigJson + ".bak", "also bad");
    Config.ResetCache();

    var cfg = Config.Load();
    Assert.Equal(new Config().RootPassword, cfg.RootPassword);
    Assert.NotNull(Config.LoadFault);                        // the user is TOLD, not silently reset
    Assert.True(File.Exists(Paths.ConfigJson + ".corrupt"));
}
```

---

### A6 — CLI `start` kills the services it just started: the job object dies with the CLI process

**Severity:** Critical (`banglahost start` is non-functional; also the mechanism behind orphan/no-op process behaviour)
**File:** `src/BanglaHost.Core/JobManager.cs`, `src/BanglaHost.Core/JobObject.cs`; entry point `src/BanglaHost.Cli/Program.cs`
**Class/Method:** `JobManager` static job / `JobManager.Add(Process)`

**Exact problematic code**

```csharp
// JobObject.cs — the job is created with kill-on-close
const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
...
// JobManager.cs — a single process-wide static job, assigned at spawn
JobManager.Add(p);
```

Call sites (11): `AiServers.cs:34`, `Apache.cs:162`, `CacheServers.cs:30`, `DbServer.cs:126`, `Mailpit.cs:45`, `Mailpit.cs:97`, `Nginx.cs:43`, `NodeSite.cs:102`, `PhpCgi.cs:118`, `PySite.cs:126`, `SearchServers.cs:33`.

**Why it is a problem**

`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` means: when the last handle to the job closes, **every process in the job is terminated**. That is exactly right for the GUI, where the job’s lifetime should equal the app’s lifetime — close the window, the servers stop with it.

It is exactly wrong for the CLI. `banglahost start all` spawns nginx, php-cgi, MariaDB, Redis… assigns each to the static job, prints its output, and **exits**. Process exit closes the job handle, the job’s last handle is gone, and Windows immediately terminates every service that was just launched. The command appears to succeed and leaves nothing running.

The same coupling makes the GUI’s behaviour non-obvious in the other direction: because *all* services share one job whose lifetime is the app’s, a GUI crash takes down every server — including ones the user started deliberately and expects to survive — while a user who wants servers to outlive the UI has no way to get that.

MSIX adds a wrinkle: packaged apps already run inside a container job. Nested jobs are supported on Windows 8+, so `AssignProcessToJobObject` should succeed here, but whether the packaged container imposes limits that interact badly with `KILL_ON_JOB_CLOSE` is **Requires runtime verification**.

**Reproduction scenario**

1. Build the CLI. Ensure nothing is running: `banglahost stop all`.
2. Run `banglahost start all`. Output reports the services starting.
3. Immediately run `tasklist | findstr /i "nginx php-cgi mysqld redis"` — nothing (or processes that vanish within a second).
4. Compare with starting the same services from the GUI, which keeps them alive as long as the GUI runs.

**Recommended fix**

Job ownership must be a decision of the host, not a Core constant. Give `JobManager` an explicit mode:

* **GUI:** kill-on-close (current behaviour) — servers are tied to the app.
* **CLI / detached:** no job, or a *named* job whose handle is deliberately leaked, so children survive the launcher.

Also fix the static-init fragility in the same pass (**A7**) and the `Marshal.FreeHGlobal` leak on the throw path in `JobObject`.

**Suggested corrected code**

```csharp
public static class JobManager
{
    public enum Mode { KillOnHostExit, Detached }

    private static Mode _mode = Mode.KillOnHostExit;
    private static JobObject? _job;
    private static readonly object Gate = new();

    /// <summary>Must be called once at startup, before any service is spawned.
    /// GUI → KillOnHostExit. CLI/service → Detached.</summary>
    public static void Configure(Mode mode)
    {
        lock (Gate)
        {
            if (_job is not null) throw new InvalidOperationException("JobManager already in use");
            _mode = mode;
        }
    }

    public static void Add(Process p)
    {
        if (_mode == Mode.Detached) return;          // children outlive the launcher, by design
        try
        {
            lock (Gate) { _job ??= new JobObject(killOnClose: true); }
            _job.Assign(p);
        }
        catch (Exception ex)
        {
            // Never let process-grouping failure abort a service start (see A7).
            Log.Warn($"job assignment failed for pid {p.Id}: {ex.Message}");
        }
    }
}
```

```csharp
// BanglaHost.Cli/Program.cs — first line of Main
JobManager.Configure(JobManager.Mode.Detached);

// BanglaHost.App/App.xaml.cs — first line of the constructor
JobManager.Configure(JobManager.Mode.KillOnHostExit);
```

**Regression test**

```csharp
[Fact]
public void Cli_started_services_survive_the_cli_exiting()
{
    using var env = TestEnv.New();
    var exit = RunCli("start", "nginx");
    Assert.Equal(0, exit);

    Thread.Sleep(2000);                      // well past the CLI's own exit
    Assert.True(Nginx.Running(), "nginx was killed when the CLI exited");
}

[Fact]
public void Gui_mode_still_kills_children_on_host_exit()
{
    using var env = TestEnv.New();
    var host = StartHostStub(JobManager.Mode.KillOnHostExit, spawn: "nginx");
    Assert.True(Nginx.Running());

    host.Kill();                             // simulate a GUI crash
    Thread.Sleep(1500);
    Assert.False(Nginx.Running(), "child outlived a kill-on-close host");
}
```

---

### A7 — `JobObject` static-init failure escapes `JobManager.Add`’s catch and aborts every service start

**Severity:** Critical (crash on all 11 spawn paths)
**File:** `src/BanglaHost.Core/JobManager.cs`, `src/BanglaHost.Core/JobObject.cs`
**Class/Method:** `JobManager` static field initializer / `JobManager.Add(Process)`

**Exact problematic code**

```csharp
// JobManager.cs — the job is a static field initialized inline
private static readonly JobObject _job = new JobObject(...);

public static void Add(Process p)
{
    try { _job.Assign(p); }        // the try does NOT cover the static initializer
    catch { }
}
```

**Why it is a problem**

A class with only inline static field initializers and no static constructor is compiled `beforefieldinit`, and the runtime runs those initializers on first access to any static member. That first access here is **inside `Add`’s `try`** — but the initializer does not run *within* the try’s protected region semantically: when it throws, the CLR wraps the exception in `TypeInitializationException` and propagates it out of `Add`. The `catch { }` never sees it, and worse, the type stays permanently unusable — every later touch of `JobManager` rethrows the *same cached* `TypeInitializationException`.

So if `CreateJobObject` or `SetInformationJobObject` fails for any reason (restricted token, a policy-constrained MSIX container, a hardening product hooking the API), **every one of the 11 `JobManager.Add` call sites throws** — that is nginx, Apache, php-cgi, MariaDB/MySQL, PostgreSQL, Redis/Memcached, Mailpit, Node, Python, the AI servers and the search servers. “Start all” dies on the first service, and it dies with an exception type that tells the user nothing.

Process grouping is a *convenience*: it makes cleanup tidy. It is not a prerequisite for running a web server. Failure to create a job object must never prevent a service from starting.

There is a related defect in `JobObject`’s constructor: on the throw path the `Marshal.AllocHGlobal` block for the limit-information struct is not freed, so a repeated-failure loop leaks unmanaged memory.

**Reproduction scenario**

Directly: run under a token where `CreateJobObject` is denied, or hook/deny `SetInformationJobObject` (Detours/EDR simulation), then click **Start all**. The operation fails with `TypeInitializationException` rather than starting anything, and every subsequent attempt fails identically without retrying.

Deterministic unit-level reproduction: inject a `JobObject` factory that throws (see the test below).

**Recommended fix**

Never initialise OS handles in a static field initializer. Create the job lazily inside the guarded method, treat failure as a logged degradation, and latch it so it is attempted once rather than on every spawn. Free unmanaged memory in a `finally`.

**Suggested corrected code**

```csharp
public static class JobManager
{
    private static JobObject? _job;
    private static bool _jobUnavailable;
    private static readonly object Gate = new();

    public static void Add(Process p)
    {
        try
        {
            JobObject? job;
            lock (Gate)
            {
                if (_jobUnavailable) return;             // already known-bad: don't retry per spawn
                if (_job is null)
                {
                    try { _job = new JobObject(killOnClose: true); }
                    catch (Exception ex)
                    {
                        _jobUnavailable = true;
                        Log.Warn($"process grouping unavailable ({ex.GetType().Name}: {ex.Message}); " +
                                 "services will run untracked and may need manual cleanup");
                        return;
                    }
                }
                job = _job;
            }
            job.Assign(p);
        }
        catch (Exception ex)
        {
            Log.Warn($"job assignment failed for pid {p.Id}: {ex.Message}");   // never fatal
        }
    }
}
```

```csharp
// JobObject.cs — free on every path
public JobObject(bool killOnClose)
{
    _handle = CreateJobObject(IntPtr.Zero, null);
    if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
    if (killOnClose) info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

    var len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
    var ptr = Marshal.AllocHGlobal(len);
    try
    {
        Marshal.StructureToPtr(info, ptr, false);
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)len))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    finally { Marshal.FreeHGlobal(ptr); }     // was leaked on the throw path
}
```

**Regression test**

```csharp
[Fact]
public void Service_start_survives_job_object_creation_failure()
{
    using var env = TestEnv.New();
    JobManager.OverrideFactoryForTests(() => throw new Win32Exception(5));   // ERROR_ACCESS_DENIED

    var ex = Record.Exception(() => Nginx.Start());
    Assert.Null(ex);                                  // no TypeInitializationException, no throw at all
    Assert.True(Nginx.Running(), "nginx failed to start because job grouping was unavailable");
}

[Fact]
public void Job_creation_failure_is_attempted_once_not_per_spawn()
{
    var attempts = 0;
    JobManager.OverrideFactoryForTests(() => { attempts++; throw new Win32Exception(5); });
    for (var i = 0; i < 10; i++) JobManager.Add(Process.GetCurrentProcess());
    Assert.Equal(1, attempts);
}
```

---

## B. Security issues

A note on what is *not* here, because “do not invent issues” cuts both ways:

* **Zip-slip in `Downloader.ExtractZip` is not a vulnerability.** Extraction uses `%SystemDirectory%\tar.exe` (bsdtar), which strips leading `..` components and drive letters by default; `-P` is never passed. Archive-path traversal is therefore mitigated.
* **Vhost filename path traversal is not reachable.** `Engine.SiteAdd` validates the site name with `Regex.IsMatch(name, "^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$")` before it is used to build `NginxSites\{name}.conf`.
* **nginx directive injection via a site root is CLI-only and self-inflicted.** The GUI supplies the root from a folder picker (`SitesPage` `_customRoot`), so an attacker-controlled value cannot reach `root {path};` through the UI. Rated Low, listed in **G**.
* **`BanglaHost.Core` has no NuGet `PackageReference` entries at all**, so Core carries no third-party CVE surface. The app references only `Microsoft.WindowsAppSDK 1.6.241114003`, `Microsoft.Windows.SDK.BuildTools 10.0.26100.1742` and `System.Security.Permissions 8.0.0`. Whether newer WindowsAppSDK servicing releases fix security defects relevant here is **Requires runtime verification** against the current advisory feed.

### B1 — Zero integrity verification on ~30 downloaded-and-executed binaries

**Severity:** Critical
**File:** `src/BanglaHost.Core/Downloader.cs`
**Class/Method:** `Downloader.CurlTo`, `Downloader.Shell`, `Downloader.ExtractZip`, and every `Install*` method

**Exact problematic code**

```csharp
private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
private static string CurlExe => Path.Combine(Environment.SystemDirectory, "curl.exe");
private static string TarExe  => Path.Combine(Environment.SystemDirectory, "tar.exe");

private static void ExtractZip(string zip, string destDir)
{
    Shell(TarExe, "--exclude", "sizes.exe", "-xf", zip, "-C", destDir);
    try { File.Delete(zip); } catch { }
}
```

A solution-wide grep for `Sha256|SHA256|checksum|Hash|ServerCertificate|SecurityProtocol` matches **nothing**. There is no hash list, no signature check, no `Authenticode` verification, no pinning.

**Why it is a problem**

BanglaHost downloads roughly thirty third-party archives — PHP, nginx, Apache (ApacheLounge), MariaDB, MySQL, PostgreSQL, Redis, Memcached, Node.js, Composer, phpMyAdmin, Adminer, Mailpit, mkcert, cloudflared, ionCube loaders, PECL extension DLLs — extracts them into `Paths.Bin`, and then **executes them, indefinitely, as long-running local servers**. PHP and the PECL DLLs are worse than plain executables: they are loaded into a process that then executes arbitrary user site code, and `zend_extension=` lines are written to `php.ini` pointing at freshly downloaded DLLs (`Php.Ioncube`).

Transport is HTTPS in every case, which is the one thing done right — it means a passive network observer cannot swap bytes. But TLS alone leaves several realistic paths to running attacker-controlled code:

* **Upstream or mirror compromise.** ApacheLounge, PECL and ionCube are small-team distribution points. A compromised release archive would be installed and executed with no tripwire, because nothing compares what arrived against what was expected.
* **HTML-scraped URLs (B2).** The download URL for nginx, Apache and SQLite is *parsed out of a web page*. Whatever that page says today is what gets executed. A defacement or a content-injection bug on the page is sufficient — no TLS break needed.
* **Local tampering.** `Paths.Home` defaults to `C:\BanglaHost`, created by the app. Directories created directly under the drive root inherit permissions that commonly allow the `Users`/`Authenticated Users` group to create and modify files (see **B-perm** in **G**). A low-privilege process on the same machine can replace `bin\php\8.3\php-cgi.exe` between install and launch, and BanglaHost will start it. Confirming the exact inherited DACL on a given machine is **Requires runtime verification**, but the design should not depend on it.
* **Retry-after-partial.** A truncated download is not detected as truncated; it is extracted, and a half-written executable either fails confusingly or, for archives, extracts a partial tree.

For a tool whose entire job is to fetch and run other people’s server binaries, integrity verification is not a hardening nicety — it is the core security control, and it is absent.

**Reproduction scenario**

Demonstration without attacking anyone: stand up a local HTTPS proxy trusted by the machine (or point a hosts entry at a local server with a locally-trusted cert), serve a benign archive containing `php-cgi.exe` that writes a marker file, and trigger PHP install from the Services page. BanglaHost extracts and runs the substituted binary; the marker appears. No warning is produced at any point.

Local-tamper variant: install PHP normally, then from a **non-elevated** shell replace `bin\php\<v>\php-cgi.exe` with a marker-writing stub and start PHP from the UI.

**Recommended fix**

Layered, in order of value:

1. **Pin a manifest.** Ship a signed (or at minimum in-repo, code-reviewed) `downloads.json` mapping each artifact + version to an expected SHA-256 and its canonical URL. Verify the hash after download, before extraction. Refuse on mismatch and delete the file.
2. **Stop scraping HTML for URLs** (**B2**). Resolve versions through a manifest you control, updated deliberately.
3. **Verify Authenticode where the publisher signs.** MariaDB, MySQL, Node.js, PostgreSQL and cloudflared ship signed binaries; check the signature and the expected subject before first launch.
4. **Lock down the install root.** Set an explicit DACL on `Paths.Home` granting write only to Administrators and the installing user; re-assert it in `EnsureSkeleton()`.
5. **Extract to a staging directory, verify, then atomically swap** into place — so a failed or tampered install cannot leave a partially-replaced tree that gets executed.

**Suggested corrected code**

```csharp
// downloads.json (in-repo, reviewed; one entry per artifact+version)
// { "php-8.3.14-nts-x64": {
//     "url": "https://windows.php.net/downloads/releases/php-8.3.14-nts-Win32-vs16-x64.zip",
//     "sha256": "9f2c…", "size": 30412345, "signer": null },
//   "cloudflared-2024.11.1": {
//     "url": "https://github.com/cloudflare/cloudflared/releases/download/2024.11.1/cloudflared-windows-amd64.exe",
//     "sha256": "1ab3…", "size": 42131456, "signer": "CN=Cloudflare, Inc., O=Cloudflare, Inc., …" } }

public sealed record Artifact(string Url, string Sha256, long Size, string? Signer);

private static async Task<string> FetchVerifiedAsync(Artifact a, string destPath, CancellationToken ct)
{
    var staging = destPath + ".part";
    await CurlToAsync(a.Url, staging, ct);

    var len = new FileInfo(staging).Length;
    if (len != a.Size)
    {
        File.Delete(staging);
        throw new BhException($"download size mismatch for {a.Url}: got {len}, expected {a.Size}. " +
                              "The download was truncated or the file has changed — nothing was installed.");
    }

    string actual;
    await using (var fs = File.OpenRead(staging))
        actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();

    if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual), Convert.FromHexString(a.Sha256)))
    {
        File.Delete(staging);
        throw new BhException(
            $"INTEGRITY CHECK FAILED for {a.Url}\nexpected sha256 {a.Sha256}\nactual   sha256 {actual}\n" +
            "Nothing was installed. If you trust this source, update downloads.json deliberately.");
    }

    if (a.Signer is not null) VerifyAuthenticode(staging, a.Signer);   // throws BhException on mismatch

    File.Move(staging, destPath, overwrite: true);
    return destPath;
}

private static void VerifyAuthenticode(string path, string expectedSubject)
{
    using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
    using var chain = new X509Chain { ChainPolicy = { RevocationMode = X509RevocationMode.Online } };
    if (!chain.Build(cert))
        throw new BhException($"{Path.GetFileName(path)}: Authenticode chain did not validate.");
    if (!string.Equals(cert.Subject, expectedSubject, StringComparison.Ordinal))
        throw new BhException($"{Path.GetFileName(path)}: unexpected signer\n" +
                              $"expected {expectedSubject}\nactual   {cert.Subject}");
}
```

```csharp
// Paths.cs — assert an explicit DACL on the install root
private static void HardenRoot(string root)
{
    var di = new DirectoryInfo(root);
    var acl = new DirectorySecurity();
    acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);   // stop inheriting from C:\
    foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
    acl.AddAccessRule(new FileSystemAccessRule(
        WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags.None, AccessControlType.Allow));
    di.SetAccessControl(acl);
}
```

**Regression test**

```csharp
[Fact]
public async Task Tampered_download_is_rejected_and_nothing_is_installed()
{
    using var srv = LocalHttpsServer.Serving("/php.zip", TestData.EvilZip);
    var artifact = new Artifact(srv.Url("/php.zip"), TestData.GoodZipSha256, TestData.GoodZipSize, null);

    var ex = await Assert.ThrowsAsync<BhException>(
        () => Downloader.FetchVerifiedAsync(artifact, Path.Combine(Paths.Tmp, "php.zip"), default));

    Assert.Contains("INTEGRITY CHECK FAILED", ex.Message);
    Assert.False(File.Exists(Path.Combine(Paths.Tmp, "php.zip")));
    Assert.Empty(Directory.GetFiles(Paths.Tmp, "*.part"));      // staging cleaned up
}

[Fact]
public async Task Truncated_download_is_rejected()
{
    using var srv = LocalHttpsServer.Serving("/php.zip", TestData.GoodZip[..1000]);
    var artifact = new Artifact(srv.Url("/php.zip"), TestData.GoodZipSha256, TestData.GoodZipSize, null);
    var ex = await Assert.ThrowsAsync<BhException>(
        () => Downloader.FetchVerifiedAsync(artifact, Path.Combine(Paths.Tmp, "php.zip"), default));
    Assert.Contains("size mismatch", ex.Message);
}

[Fact]
public void Every_manifest_entry_has_a_sha256()
{
    foreach (var (key, a) in Downloader.Manifest)
    {
        Assert.False(string.IsNullOrWhiteSpace(a.Sha256), $"{key} has no expected hash");
        Assert.Equal(64, a.Sha256.Length);
        Assert.StartsWith("https://", a.Url);
    }
}
```

---

### B2 — Three download URLs are scraped out of HTML pages

**Severity:** High
**File:** `src/BanglaHost.Core/Downloader.cs`
**Class/Method:** the nginx, Apache and SQLite install paths

**Exact problematic code**

The install routines fetch and regex these pages to discover a download link:

* `https://nginx.org/en/download.html`
* `https://www.apachelounge.com/download/` — matched with `VS\d+/binaries/httpd-2\.4\.\d+-\d+-Win64-VS\d+\.zip`
* `https://www.sqlite.org/download.html`

**Why it is a problem**

The URL of a binary that will be extracted and executed is decided by **whatever markup those pages contain at install time**. That makes a page defacement, a CMS content-injection bug, or an ordinary editorial change on a third-party website into a code-execution or breakage vector for BanglaHost users — with no TLS compromise required, because the attacker is supplying content *through* the legitimate TLS channel.

It is also fragile in the mundane sense: the regex is pinned to a filename shape (`httpd-2.4.<patch>-<build>-Win64-VS<n>.zip`). Any layout change on ApacheLounge silently breaks Apache installation for every user, and the failure surfaces as an install error with no explanation.

Combined with **B1** (no hash to compare against) there is nothing between “the page said this” and “we ran it”.

**Reproduction scenario**

1. Redirect `www.apachelounge.com` to a local HTTPS server trusted by the test machine, serving a page whose only matching link points at a benign marker archive.
2. Services page → install Apache.
3. The marker archive is downloaded, extracted into `bin\apache`, and Apache “starts” from the substituted tree.

Fragility variant: serve the real page with `Win64-VS17` renamed to `Win64-VC17`; installation fails with no actionable message.

**Recommended fix**

Resolve versions from a manifest you own (**B1**), refreshed by a deliberate, reviewed repo change rather than at runtime. If a runtime channel is wanted for “check for newer PHP/nginx”, point it at a small JSON document you publish, verify a signature over it, and still hash-check the artifact it names. Never derive an execution target from scraped HTML.

**Suggested corrected code**

```csharp
// Replace scrape-at-install with manifest lookup + explicit, reviewed updates.
private static Artifact Resolve(string key) =>
    Manifest.TryGetValue(key, out var a)
        ? a
        : throw new BhException(
            $"'{key}' is not in downloads.json. Update the manifest (URL + sha256) and rebuild — " +
            "BanglaHost does not discover download URLs at runtime.");

// Optional signed channel for version discovery — still hash-verified afterwards.
private static async Task<Manifest> FetchSignedManifestAsync(CancellationToken ct)
{
    var json = await Http.GetByteArrayAsync(ManifestUrl, ct);
    var sig  = await Http.GetByteArrayAsync(ManifestUrl + ".sig", ct);
    if (!Ed25519.Verify(sig, json, EmbeddedPublicKey))
        throw new BhException("manifest signature invalid — refusing to use it");
    return JsonSerializer.Deserialize<Manifest>(json)!;
}
```

**Regression test**

```csharp
[Fact]
public void No_installer_derives_a_url_from_html()
{
    var src = File.ReadAllText("src/BanglaHost.Core/Downloader.cs");
    foreach (var page in new[] { "download.html", "apachelounge.com/download", "sqlite.org/download" })
        Assert.DoesNotContain(page, src);
    Assert.DoesNotContain("Regex", src.Split("// URL discovery")[0]);   // no scrape helpers remain
}

[Fact]
public async Task Unknown_artifact_fails_with_an_actionable_message()
{
    var ex = await Assert.ThrowsAsync<BhException>(() => Downloader.InstallAsync("nginx", "99.9.9", default));
    Assert.Contains("downloads.json", ex.Message);
}
```

---

### B3 — Database root password passed on the command line (≈6 call sites)

**Severity:** High
**Files / methods:**

| File | Member | Code |
|---|---|---|
| `Views/DatabaseExplorerPage.xaml.cs` | `RunSql` | `$"-u root -p\"{cfg.RootPassword}\""` |
| `Core/Database.cs` | `BaseArgs` | `" -p{pw}"` (also unquoted — **B4**) |
| `Core/DbServer.cs` | shutdown / admin calls | `-p\"{pw}\"` |
| `Core/BackupService.cs` | `DumpDatabase` | `ArgumentList.Add($"-p{pass}")` |
| `Core/BackupService.cs` | `RestoreDatabase` | `-p…` |
| `Core/DbExplorer.cs` | `QueryMysqlAsync` | `-p…` |

**Why it is a problem**

A process command line is not a secret on Windows. Any process running as the same user — and any process with `SeDebugPrivilege` or admin, including other apps’ crash handlers, telemetry agents and “system optimiser” utilities — can read it:

```powershell
Get-CimInstance Win32_Process -Filter "Name='mysql.exe'" | Select-Object CommandLine
```

The window is short (the lifetime of the child process) but for `mysqldump` of a large database it can be minutes, and `PhpCgi`/`DbServer` calls run at predictable moments (startup, shutdown, backup) so polling is trivial. MySQL itself has warned about `-p` on the command line for two decades, and `mysql`/`mysqldump` print `Using a password on the command line interface can be insecure` — a warning this app’s output plumbing swallows.

Secondary consequence: the password also lands in any log or error text that echoes the command, and in Windows event/EDR telemetry that records process command lines. That exports a local dev credential to wherever those logs go.

**Reproduction scenario**

1. Set a MySQL root password.
2. Start a large `mysqldump` (Databases → Export, or the Backup page).
3. From an unprivileged PowerShell as the same user, run the `Get-CimInstance` line above.
4. The plaintext password is printed.

**Recommended fix**

Use a defaults-extra-file. MySQL and MariaDB both accept `--defaults-extra-file=<path>` as the **first** argument; put the credential in it, create it with a restrictive DACL in a per-user temp location, and delete it in a `finally`. One helper, used by every DB call site — the current per-site duplication is why the flaw is in six places.

**Suggested corrected code**

```csharp
/// <summary>A short-lived my.cnf holding the root credential, readable only by the current user.
/// Always use with `using` — the file is deleted on Dispose.</summary>
internal sealed class MySqlCredentialsFile : IDisposable
{
    public string Path { get; }

    private MySqlCredentialsFile(string path) => Path = path;

    public static MySqlCredentialsFile Create(string? password)
    {
        var dir  = System.IO.Path.Combine(Paths.Run, "cred");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"my-{Guid.NewGuid():N}.cnf");

        // Create with an explicit owner-only DACL before any content is written.
        var sec = new FileSecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        using (var fs = FileSystemAclExtensions.Create(
                   new FileInfo(path), FileMode.CreateNew, FileSystemRights.WriteData,
                   FileShare.None, 4096, FileOptions.None, sec))
        using (var sw = new StreamWriter(fs))
        {
            sw.WriteLine("[client]");
            sw.WriteLine("user=root");
            if (!string.IsNullOrEmpty(password))
                sw.WriteLine($"password=\"{password.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        }
        return new MySqlCredentialsFile(path);
    }

    public void Dispose() { try { File.Delete(Path); } catch { } }
}
```

```csharp
// Every DB call site becomes:
using var creds = MySqlCredentialsFile.Create(Config.Load().RootPassword);
psi.ArgumentList.Add($"--defaults-extra-file={creds.Path}");   // MUST be the first argument
psi.ArgumentList.Add("-u"); psi.ArgumentList.Add("root");
// ...no -p anywhere...
```

**Regression test**

```csharp
[Fact]
public async Task No_db_tool_ever_receives_the_password_as_an_argument()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "Sup3r$ecret!");
    using var rec = ProcessCommandLineRecorder.Start("mysql.exe", "mysqldump.exe", "mysqladmin.exe");

    await DbExplorer.RunScriptAsync("SELECT 1;", null, TimeSpan.FromSeconds(10), default);
    await BackupService.DumpDatabaseAsync("mysql", Path.Combine(Paths.Tmp, "d.sql"), _ => { });
    await BackupService.BackupAllAsync("t", _ => { });

    var all = string.Join("\n", rec.CommandLines);
    Assert.DoesNotContain("Sup3r$ecret!", all);
    Assert.DoesNotContain(" -p", all);
}

[Fact]
public void Credentials_file_is_owner_only_and_deleted()
{
    string path;
    using (var creds = MySqlCredentialsFile.Create("pw"))
    {
        path = creds.Path;
        var rules = new FileInfo(path).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();
        Assert.All(rules, r => Assert.Equal(WindowsIdentity.GetCurrent().User, r.IdentityReference));
    }
    Assert.False(File.Exists(path));
}
```

---

### B4 — `Database.BaseArgs` builds `-p{pw}` unquoted → argument injection and silent breakage

**Severity:** High
**File:** `src/BanglaHost.Core/Database.cs`
**Class/Method:** `Database.BaseArgs`

**Exact problematic code**

```csharp
// appended into a string Arguments value, no quoting
" -p{pw}"
```

**Why it is a problem**

The password is interpolated into a raw command-line **string** with no quoting and no escaping. Windows command-line parsing then splits on whitespace and honours `"` specially, so the password’s content becomes syntax:

* `pass word` → `mysql -ppass word` → `word` is parsed as a *positional argument*, i.e. the database name. The command runs against the wrong database or fails confusingly.
* `pw" --execute="DROP DATABASE x` → the closing quote terminates the password token and `--execute=` is injected as a **new argument**. This is argument injection: the caller controls flags on a command line it was only supposed to supply a value to.
* A password containing `&`, `^` or `|` is harmless here (`UseShellExecute=false` means no shell interprets them) but a `"` alone is enough to corrupt tokenisation.

Who controls the password? The user, via Settings — so the direct exploit is self-inflicted. The impact is nonetheless real: (a) any user with a password containing a space or a quote finds the database features mysteriously broken with no diagnosable message; (b) if a password is ever set from a less-trusted path (a blueprint/marketplace import, a config file copied between machines, a future provisioning API), this becomes a genuine injection into a privileged local tool. `Database.ValidName` already exists for identifiers; the credential path has no equivalent discipline.

`Arguments` string-building is the root cause. `ProcessStartInfo.ArgumentList` escapes each element correctly at runtime and should be used everywhere.

**Reproduction scenario**

1. Settings → set the MySQL root password to `my pass"word`.
2. Databases page → any operation.
3. It fails with an unrelated `mysql` usage or “Unknown database” error. `Get-CimInstance Win32_Process` shows the malformed command line with tokens split at the space and the quote.
4. Set the password to `x" --execute="SELECT 1` and observe `--execute` taking effect — a flag the app never intended to pass.

**Recommended fix**

Two changes: eliminate the password from the command line entirely (**B3**), and convert `BaseArgs` to `ArgumentList` so nothing else on that command line is string-concatenated either.

**Suggested corrected code**

```csharp
// Database.cs — no string Arguments, no -p
internal static void ApplyBaseArgs(ProcessStartInfo psi, MySqlCredentialsFile creds, string? db = null)
{
    psi.ArgumentList.Add($"--defaults-extra-file={creds.Path}");
    psi.ArgumentList.Add("-u");
    psi.ArgumentList.Add("root");
    psi.ArgumentList.Add("-h");
    psi.ArgumentList.Add("127.0.0.1");
    psi.ArgumentList.Add("-P");
    psi.ArgumentList.Add(Config.Load().MysqlPort.ToString(CultureInfo.InvariantCulture));
    if (db is not null)
    {
        if (!ValidName(db)) throw new BhException($"invalid database name: {db}");
        psi.ArgumentList.Add(db);
    }
}
```

**Regression test**

```csharp
[Theory]
[InlineData("my pass")]
[InlineData("pa\"ss")]
[InlineData("x\" --execute=\"SELECT 1")]
[InlineData("p&w|d^q")]
public async Task Awkward_passwords_do_not_corrupt_the_command_line(string pw)
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: pw);
    using var rec = ProcessCommandLineRecorder.Start("mysql.exe");

    var (ok, output) = await DbExplorer.RunScriptAsync("SELECT 1;", null, TimeSpan.FromSeconds(10), default);

    Assert.True(ok, output);
    Assert.DoesNotContain("--execute", string.Join(" ", rec.CommandLines));
}

[Fact]
public void Database_never_builds_a_string_Arguments_value()
{
    var src = File.ReadAllText("src/BanglaHost.Core/Database.cs");
    Assert.DoesNotContain("Arguments =", src);
    Assert.DoesNotContain("-p{", src);
}
```

---

### B5 — Unvalidated IP written to the `hosts` file **inside the elevated helper**

**Severity:** High
**Files:** `src/BanglaHost.Core/Hosts.cs`, `src/BanglaHost.Elevate/Program.cs`
**Class/Method:** `Hosts.Add(string ip, string domain)`; `BanglaHost.Elevate` argument handling

**Exact problematic code**

```csharp
// Hosts.cs — domain is validated, ip is interpolated as-is
public static void Add(string ip, string domain)
{
    if (!IsValidDomain(domain)) throw new BhException(...);
    // ...
    line = $"{ip}\t{domain}";
}
```

```csharp
// BanglaHost.Elevate/Program.cs — validates `domain`, does NOT validate `ip`
```

**Why it is a problem**

`banglahost-elevate.exe` is a separate `requireAdministrator` helper — it runs with a **full administrator token** so it can modify `%WINDIR%\System32\drivers\etc\hosts`. It correctly validates the `domain` argument, which shows the risk was understood; the `ip` argument bypasses that check entirely and is written straight into the file.

The `hosts` file is name-resolution policy for the entire machine and every user on it. Content injected through the unvalidated field is therefore a **privileged, machine-wide, persistent** change made by an elevated process on behalf of an unvalidated input. Because the value is written as a whole line component, an `ip` containing a newline can append arbitrary additional `hosts` entries — for example redirecting `login.microsoftonline.com`, an update endpoint, or a package registry to an attacker-controlled address. That survives reboots and affects browsers, package managers and the OS alike.

Reachability from the GUI is limited (the UI supplies `127.0.0.1`), so this is primarily an **argument-validation defect in a privileged boundary** rather than a remotely exploitable bug today. But the whole purpose of a separate elevated helper is to be a trust boundary, and a trust boundary that validates one of its two parameters is not one. Anything that can invoke the helper — a shortcut, a script, another app, a future feature that passes a user-supplied address — gets machine-wide DNS override.

**Reproduction scenario**

1. Locate `banglahost-elevate.exe` in the install tree.
2. Invoke it directly with an `ip` argument containing an embedded newline followed by another host mapping (e.g. `127.0.0.1%0A203.0.113.5 registry.npmjs.org`, quoting per the helper’s parser), and a valid domain.
3. Accept the UAC prompt (as the user would for any legitimate site add).
4. Inspect `hosts`: both lines are present. `nslookup registry.npmjs.org` now resolves to the injected address.

**Recommended fix**

Validate on **both** sides of the boundary, and validate structurally rather than by rejecting bad characters: parse the IP with `IPAddress.TryParse` and re-serialise the parsed value, so only a canonical address can ever be written. The elevated helper must never trust its caller, even when its caller is the same product.

**Suggested corrected code**

```csharp
// Shared validation, used by BOTH Hosts.Add and the elevated helper.
public static string CanonicalIp(string raw)
{
    if (!IPAddress.TryParse(raw?.Trim(), out var ip))
        throw new BhException($"not a valid IP address: '{raw}'");
    if (ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        throw new BhException($"unsupported address family for '{raw}'");
    return ip.ToString();          // re-serialised: cannot carry newlines or padding
}
```

```csharp
// Hosts.cs
public static void Add(string ip, string domain)
{
    var safeIp = CanonicalIp(ip);
    if (!IsValidDomain(domain)) throw new BhException($"not a valid domain: '{domain}'");
    var line = $"{safeIp}\t{domain}";
    // ...
}
```

```csharp
// BanglaHost.Elevate/Program.cs — the privileged side re-validates independently
static int Main(string[] args)
{
    if (args.Length < 3) return Usage();
    var verb   = args[0];
    var ip     = Hosts.CanonicalIp(args[1]);      // was: used verbatim
    var domain = args[2];
    if (!Hosts.IsValidDomain(domain))
    {
        Console.Error.WriteLine($"refusing: invalid domain '{domain}'");
        return 2;
    }
    // Reject any control character in every argument as a belt-and-braces measure.
    foreach (var a in args)
        if (a.Any(char.IsControl)) { Console.Error.WriteLine("refusing: control character in argument"); return 3; }
    ...
}
```

**Regression test**

```csharp
[Theory]
[InlineData("127.0.0.1\n203.0.113.5 evil.example")]
[InlineData("127.0.0.1\r\n203.0.113.5 evil.example")]
[InlineData("127.0.0.1 203.0.113.5")]
[InlineData("not-an-ip")]
[InlineData("")]
[InlineData("127.0.0.1\t# comment")]
public void Hosts_Add_rejects_non_canonical_ip(string ip)
{
    Assert.Throws<BhException>(() => Hosts.Add(ip, "site.test"));
}

[Fact]
public void Elevate_helper_rejects_injected_ip_and_writes_nothing()
{
    var before = File.ReadAllText(Paths.HostsFile);
    var exit = RunElevateHelper("add", "127.0.0.1\n203.0.113.5 registry.npmjs.org", "site.test");
    Assert.NotEqual(0, exit);
    Assert.Equal(before, File.ReadAllText(Paths.HostsFile));
}

[Fact]
public void Both_sides_of_the_boundary_validate()
{
    // The helper must not rely on its caller: same input, rejected in-proc AND out-of-proc.
    Assert.Throws<BhException>(() => Hosts.CanonicalIp("127.0.0.1\nx"));
    Assert.NotEqual(0, RunElevateHelper("add", "127.0.0.1\nx", "site.test"));
}
```

---

### B6 — `Hosts.Remove` substring-matches, deleting unrelated sites’ entries

**Severity:** Medium (data integrity; breaks other projects silently)
**File:** `src/BanglaHost.Core/Hosts.cs` — `Hosts.Remove(string domain)`

**Exact problematic code**

The removal filter tests whether a line *contains* the domain, rather than parsing the line and comparing the hostname field exactly.

**Why it is a problem**

`hosts` lines are `<address> <name> [name…] [# comment]`. Substring matching over the whole line means removing `app.test` also removes `myapp.test`, `app.test.local`, `staging-app.test`, and any line whose *comment* happens to mention the string. Deleting a site in BanglaHost therefore silently breaks unrelated sites — including entries the user added by hand for non-BanglaHost work, and entries belonging to other tools (Docker Desktop, Laragon) that share the file.

The file is machine-wide and edited via the elevated helper, so the damage is done with administrator rights and is not undoable from the UI.

**Reproduction scenario**

1. Add sites `app.test` and `myapp.test` (both get `hosts` entries).
2. Delete `app.test` from the Sites page.
3. `myapp.test` no longer resolves — its `hosts` line is gone.

**Recommended fix**

Parse each line into address + names, compare names with `OrdinalIgnoreCase` equality, and only rewrite lines inside BanglaHost’s own managed block. Keep a sentinel comment block (`# >>> BanglaHost` … `# <<< BanglaHost`) so hand-written entries are never touched.

**Suggested corrected code**

```csharp
public static void Remove(string domain)
{
    if (!IsValidDomain(domain)) throw new BhException($"not a valid domain: '{domain}'");

    var kept = new List<string>();
    var inBlock = false;
    foreach (var raw in File.ReadAllLines(Paths.HostsFile))
    {
        if (raw.StartsWith(BlockStart, StringComparison.Ordinal)) { inBlock = true;  kept.Add(raw); continue; }
        if (raw.StartsWith(BlockEnd,   StringComparison.Ordinal)) { inBlock = false; kept.Add(raw); continue; }
        if (!inBlock) { kept.Add(raw); continue; }              // never touch user-authored lines

        var payload = raw.Split('#', 2)[0];
        var fields  = payload.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var names   = fields.Skip(1);                            // field 0 is the address
        var isExactMatch = names.Any(n => string.Equals(n, domain, StringComparison.OrdinalIgnoreCase));
        if (!isExactMatch) kept.Add(raw);
    }
    WriteHosts(kept);
}
```

**Regression test**

```csharp
[Fact]
public void Remove_deletes_only_the_exact_hostname()
{
    Hosts.Add("127.0.0.1", "app.test");
    Hosts.Add("127.0.0.1", "myapp.test");
    Hosts.Add("127.0.0.1", "app.test.local");
    File.AppendAllText(Paths.HostsFile, "127.0.0.1\tuser-managed.example  # app.test related\n");

    Hosts.Remove("app.test");

    var text = File.ReadAllText(Paths.HostsFile);
    Assert.DoesNotContain("\tapp.test\n", text);
    Assert.Contains("myapp.test", text);
    Assert.Contains("app.test.local", text);
    Assert.Contains("user-managed.example", text);   // outside the managed block
}
```

---

### B7 — `SslService` uses an unvalidated `domain` for file paths and as an mkcert argument

**Severity:** High
**Files:** `src/BanglaHost.Core/SslService.cs`, `src/BanglaHost.App/Views/SslPage.xaml.cs`
**Class/Method:** `SslService.GenerateLocalCertAsync(string domain, Action<string> log)`, `SslService.DeleteCert(string domain)`; caller `SslPage.GenLocalBtn_Click`

**Exact problematic code**

```csharp
// SslService.cs
public static async Task<bool> GenerateLocalCertAsync(string domain, Action<string> log)
{
    var keyFile  = Path.Combine(CertsDir, $"{domain}-key.pem");
    var certFile = Path.Combine(CertsDir, $"{domain}.pem");
    var args = $"-key-file \"{keyFile}\" -cert-file \"{certFile}\" {domain}";    // unquoted, unvalidated
    ...
}

public static void DeleteCert(string domain)
{
    var keyFile = Path.Combine(CertsDir, $"{domain}-key.pem");
    ...
}
```

```csharp
// SslPage.xaml.cs:41 — free text straight from a TextBox, no validation of any kind
var domain = DomainBox.Text.Trim();
if (string.IsNullOrEmpty(domain)) return;
...
var success = await Task.Run(() => SslService.GenerateLocalCertAsync(domain, log));
```

**Why it is a problem**

Two distinct defects share the same missing check.

**Path traversal into an arbitrary directory.** `Path.Combine(CertsDir, $"{domain}-key.pem")` does not sanitise; `Path.Combine` *discards* `CertsDir` entirely if the second component is rooted. So:

* `..\..\..\Users\Public\evil` writes `…\Users\Public\evil-key.pem` and `evil.pem`.
* `C:\Windows\Temp\x` writes straight to that absolute path — `CertsDir` is ignored.

The written content is a private key and certificate, so this is a *create/overwrite of attacker-chosen paths with attacker-influenced content*, constrained to a `-key.pem`/`.pem` suffix. The suffix constraint prevents dropping an `.exe` or `.ps1`, but it does not prevent overwriting an existing `.pem` that another application trusts — for example an nginx/Apache certificate elsewhere on disk, or a `.pem` trust bundle used by a language runtime. `DeleteCert` has the same shape, giving arbitrary `-key.pem`/`.pem` **deletion**.

**mkcert argument injection.** `{domain}` is appended to a raw `Arguments` **string, unquoted**. A value containing a space introduces additional arguments; a value containing `"` breaks tokenisation. mkcert accepts flags such as `-install`, `-uninstall`, `-CAROOT` and `-client`, so a crafted value turns a “make me a cert” request into a different mkcert operation. `-uninstall` is the notable one: it removes the local CA from the trust stores, silently breaking HTTPS for every existing BanglaHost site.

Both are reachable from the UI with no elevation and no warning: the page takes free text and passes it through.

**Reproduction scenario**

*Traversal:* SSL page → Domain = `..\..\..\..\Users\Public\pwned` → Generate. Check `C:\Users\Public\`: `pwned-key.pem` and `pwned.pem` exist.

*Injection:* SSL page → Domain = `site.test -uninstall` → Generate. mkcert receives `-uninstall` as a separate argument; the local CA is removed from `CurrentUser\Root`, and every previously issued `*.test` certificate stops being trusted by browsers.

**Recommended fix**

Validate the domain against the same strict rule the rest of the codebase already uses (`Hosts.IsValidDomain`), reject anything else at the *page* boundary with visible feedback, and pass arguments via `ArgumentList`. Additionally assert that the resolved output path is inside `CertsDir` — defence in depth against a future validation regression.

**Suggested corrected code**

```csharp
public static async Task<bool> GenerateLocalCertAsync(string domain, Action<string> log, CancellationToken ct = default)
{
    if (!Hosts.IsValidDomain(domain))
        throw new BhException($"'{domain}' is not a valid host name. Use letters, digits, dots and hyphens.");

    var keyFile  = SafeCertPath($"{domain}-key.pem");
    var certFile = SafeCertPath($"{domain}.pem");

    var psi = new ProcessStartInfo
    {
        FileName = Tools.MkcertExe() ?? throw new BhException("mkcert is not installed"),
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
    };
    psi.ArgumentList.Add("-key-file");  psi.ArgumentList.Add(keyFile);
    psi.ArgumentList.Add("-cert-file"); psi.ArgumentList.Add(certFile);
    psi.ArgumentList.Add(domain);       // escaped by the runtime; cannot become a flag
    ...
}

/// <summary>Resolve a file name inside CertsDir and prove it stayed there.</summary>
private static string SafeCertPath(string fileName)
{
    if (fileName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        throw new BhException($"invalid certificate file name: '{fileName}'");
    var root = Path.GetFullPath(CertsDir) + Path.DirectorySeparatorChar;
    var full = Path.GetFullPath(Path.Combine(root, fileName));
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new BhException($"refusing to write outside the certificate store: '{fileName}'");
    return full;
}

public static void DeleteCert(string domain)
{
    if (!Hosts.IsValidDomain(domain)) throw new BhException($"not a valid host name: '{domain}'");
    foreach (var f in new[] { SafeCertPath($"{domain}-key.pem"), SafeCertPath($"{domain}.pem") })
        try { File.Delete(f); } catch (Exception ex) { Log.Warn($"could not delete {f}: {ex.Message}"); }
}
```

```csharp
// SslPage.xaml.cs — validate at the boundary, with visible feedback
private async void GenLocalBtn_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var domain = DomainBox.Text.Trim();
        if (!Hosts.IsValidDomain(domain))
        {
            DomainError.Text = "Enter a host name like myapp.test — letters, digits, dots and hyphens only.";
            DomainError.Visibility = Visibility.Visible;
            return;
        }
        DomainError.Visibility = Visibility.Collapsed;
        ...
    }
    catch (Exception ex) { CrashLogger.Log(ex, "SslPage"); await ShowError(ex.Message); }
}
```

**Regression test**

```csharp
[Theory]
[InlineData(@"..\..\..\Users\Public\pwned")]
[InlineData(@"C:\Windows\Temp\x")]
[InlineData("site.test -uninstall")]
[InlineData("site.test\" -CAROOT \"")]
[InlineData("site.test;rm")]
[InlineData("../escape")]
public async Task GenerateLocalCert_rejects_hostile_domains(string domain)
{
    var before = Directory.GetFiles(SslService.CertsDir);
    await Assert.ThrowsAsync<BhException>(() => SslService.GenerateLocalCertAsync(domain, _ => { }));
    Assert.Equal(before, Directory.GetFiles(SslService.CertsDir));
    Assert.False(File.Exists(@"C:\Users\Public\pwned-key.pem"));
    Assert.False(File.Exists(@"C:\Windows\Temp\x.pem"));
}

[Fact]
public async Task Mkcert_receives_the_domain_as_a_single_argument()
{
    using var rec = ProcessArgumentRecorder.Start("mkcert.exe");
    await SslService.GenerateLocalCertAsync("valid.test", _ => { });
    var args = rec.LastArgumentList;
    Assert.Contains("valid.test", args);
    Assert.DoesNotContain("-uninstall", args);
    Assert.DoesNotContain("-CAROOT", args);
}

[Fact]
public void DeleteCert_cannot_delete_outside_the_store()
{
    var victim = Path.Combine(Path.GetTempPath(), "victim.pem");
    File.WriteAllText(victim, "x");
    Assert.Throws<BhException>(() => SslService.DeleteCert(@"..\..\" + Path.GetFileNameWithoutExtension(victim)));
    Assert.True(File.Exists(victim));
}
```

---

### B8 — `DbExplorer.QueryMysqlAsync` passes SQL through `-e` with quote-only escaping, and an unvalidated `-D`

**Severity:** Medium
**File:** `src/BanglaHost.Core/DbExplorer.cs` — `QueryMysqlAsync`

**Exact problematic code**

SQL is embedded in `-e "…"` with only `"` escaped, and the database name is interpolated after `-D {db}` with no validation.

**Why it is a problem**

Escaping only `"` is insufficient for a command line: a backslash immediately before the closing quote (`…\`) escapes *that* quote under Windows/MSVCRT parsing rules, so the token does not terminate where intended and the remainder of the SQL becomes new arguments. MySQL’s `NO_BACKSLASH_ESCAPES` mode changes backslash semantics on the SQL side too, so a value that looks escaped in one mode is not in the other.

The unvalidated `-D {db}` is the more mundane risk: a database name with a space or quote becomes extra arguments to a client that accepts `--execute`, `--defaults-file` and friends.

Reachability is limited — the SQL comes from the user’s own query box and the db name from a list the app produced — so this is defence-in-depth rather than a live exploit. But `RunAsync` in the same file already demonstrates the correct pattern (async, stdin), which makes the `-e` path avoidable duplication.

**Recommended fix**

Never pass SQL as an argument. Write it to stdin (as in **A4**), and validate the database name with `Database.ValidName` before adding it via `ArgumentList`.

**Suggested corrected code**

```csharp
if (!string.IsNullOrEmpty(db))
{
    if (!Database.ValidName(db)) throw new BhException($"invalid database name: '{db}'");
    psi.ArgumentList.Add("-D"); psi.ArgumentList.Add(db);
}
// SQL over stdin — never as an argument:
await p.StandardInput.WriteAsync(sql.AsMemory(), ct);
p.StandardInput.Close();
```

**Regression test**

```csharp
[Theory]
[InlineData("SELECT \"a\\\";")]        // trailing backslash before the quote
[InlineData("SELECT 1; -- \" --execute=\"SELECT 2")]
public async Task Sql_is_never_placed_on_the_command_line(string sql)
{
    using var rec = ProcessCommandLineRecorder.Start("mysql.exe");
    await DbExplorer.QueryMysqlAsync(sql, null, default);
    Assert.DoesNotContain("-e", string.Join(" ", rec.CommandLines));
    Assert.DoesNotContain("--execute", string.Join(" ", rec.CommandLines));
}

[Theory]
[InlineData("db name")]
[InlineData("db\" --execute=\"SELECT 1")]
public async Task Invalid_database_names_are_rejected(string db) =>
    await Assert.ThrowsAsync<BhException>(() => DbExplorer.QueryMysqlAsync("SELECT 1;", db, default));
```

---

### B9 — `BackupService.RestoreDatabase` interpolates the database name into SQL

**Severity:** Medium
**File:** `src/BanglaHost.Core/BackupService.cs` — `RestoreDatabase`

**Exact problematic code**

```csharp
CREATE DATABASE IF NOT EXISTS `{dbName}`;
```

**Why it is a problem**

Backtick quoting is not escaping. A `dbName` containing a backtick closes the identifier and everything after it is executed as SQL by a **root** connection: `x`; DROP DATABASE prod; --` is sufficient. The name originates from a filename inside a restore archive, so it is attacker-influenced whenever a user restores an archive they did not create themselves — a plausible scenario for a backup file shared between machines or downloaded.

`Database.ValidName` (`^[A-Za-z0-9_]+$`) already exists in the codebase and is exactly the right check; it is simply not applied here.

**Recommended fix**

Validate with `Database.ValidName` before the identifier reaches any SQL string, and double any backticks as a second layer.

**Suggested corrected code**

```csharp
if (!Database.ValidName(dbName))
    throw new BhException($"refusing to restore into '{dbName}': names may contain only letters, digits and underscore.");
var quoted = "`" + dbName.Replace("`", "``") + "`";
await ExecuteAsync($"CREATE DATABASE IF NOT EXISTS {quoted};", ct);
```

**Regression test**

```csharp
[Theory]
[InlineData("x`; DROP DATABASE keepme; --")]
[InlineData("has space")]
[InlineData("has-dash")]
public async Task RestoreDatabase_rejects_hostile_names(string db)
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "");
    env.Db.Exec("CREATE DATABASE keepme;");
    await Assert.ThrowsAsync<BhException>(() => BackupService.RestoreDatabase(db, "dump.sql", _ => { }));
    Assert.True(env.Db.DatabaseExists("keepme"));
}
```

---

### B10 — `RootPassword` persisted in plaintext

**Severity:** Medium
**File:** `src/BanglaHost.Core/Config.cs` — `Config.RootPassword`, serialised by `Save()` into `config.json`

**Why it is a problem**

The database root password is stored as clear text in `config.json` under `Paths.Home` (default `C:\BanglaHost`). Two aggravating factors: the file’s DACL is inherited from a directory created at the drive root (see **B1**), so it is plausibly readable by other users on the machine — exact ACL is **Requires runtime verification**; and users reuse passwords, so a local dev credential frequently is not only a local dev credential.

Windows provides a per-user, no-key-management primitive for exactly this (`ProtectedData` / DPAPI). Not using it is a gap, though a modest one for a local development tool.

**Recommended fix**

Encrypt at rest with DPAPI scoped to the current user; keep the JSON property as an opaque base64 blob; decrypt lazily. Combine with the atomic-write fix in **A5** and the credentials-file fix in **B3** so the plaintext never touches disk *or* a command line.

**Suggested corrected code**

```csharp
[JsonPropertyName("rootPasswordProtected")]
public string? RootPasswordProtected { get; set; }

[JsonIgnore]
public string RootPassword
{
    get
    {
        if (string.IsNullOrEmpty(RootPasswordProtected)) return "";
        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(RootPasswordProtected), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }        // machine/profile changed — treat as unset, prompt the user
    }
    set => RootPasswordProtected = string.IsNullOrEmpty(value) ? null
        : Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
}
```

Migration: on load, if a legacy plaintext `rootPassword` property is present, re-save through the protected property and remove the old key.

**Regression test**

```csharp
[Fact]
public void Password_is_not_recoverable_from_the_config_file()
{
    using var env = TestEnv.New();
    new Config { RootPassword = "Sup3r$ecret!" }.Save();

    var onDisk = File.ReadAllText(Paths.ConfigJson);
    Assert.DoesNotContain("Sup3r$ecret!", onDisk);
    Assert.Equal("Sup3r$ecret!", Config.Load().RootPassword);   // still usable in-process
}

[Fact]
public void Legacy_plaintext_config_is_migrated()
{
    using var env = TestEnv.New();
    File.WriteAllText(Paths.ConfigJson, """{ "rootPassword": "old" }""");
    Config.ResetCache();
    var cfg = Config.Load();
    Assert.Equal("old", cfg.RootPassword);
    cfg.Save();
    Assert.DoesNotContain("\"old\"", File.ReadAllText(Paths.ConfigJson));
}
```

---

### B11 — Bare `powershell`, `cmd.exe` and `wt.exe` resolved through `PATH`

**Severity:** Medium
**Files:** `src/BanglaHost.Core/Tunnel.cs` (`"powershell"`), `src/BanglaHost.App/Views/SiteListControl.xaml.cs`, `src/BanglaHost.App/Views/SitesPage.xaml.cs`, `src/BanglaHost.Core/InstallerService.cs`

**Why it is a problem**

`FileName = "powershell"` (or `"cmd.exe"`, `"wt.exe"`) is resolved by searching the current directory in some contexts and then each `PATH` entry in order. If any earlier-listed `PATH` directory is writable by a non-administrator — a very common misconfiguration created by third-party tool installers — a planted `powershell.exe` executes instead of the system one, inheriting BanglaHost’s token. Where the launch is part of an elevated flow, that is a privilege-escalation primitive.

`Downloader` already does this correctly (`Path.Combine(Environment.SystemDirectory, "curl.exe")`), which makes the inconsistency the finding: the safe idiom exists in the codebase and is not applied uniformly.

**Recommended fix**

Resolve system executables from `Environment.SystemDirectory` (and PowerShell from its known absolute location). For `wt.exe`, which genuinely lives under `%LOCALAPPDATA%\Microsoft\WindowsApps`, probe the expected absolute path and fall back to a plain `cmd.exe` console rather than to `PATH` search.

**Suggested corrected code**

```csharp
internal static class SystemExe
{
    public static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");
    public static string PowerShell => Path.Combine(
        Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    public static string Curl => Path.Combine(Environment.SystemDirectory, "curl.exe");
    public static string Tar  => Path.Combine(Environment.SystemDirectory, "tar.exe");

    /// <summary>Windows Terminal if present at its packaged alias path, else null.</summary>
    public static string? WindowsTerminal()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(p) ? p : null;
    }
}
```

**Regression test**

```csharp
[Fact]
public void No_process_is_launched_by_bare_name()
{
    foreach (var file in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
    {
        var src = File.ReadAllText(file);
        foreach (var bad in new[] { "FileName = \"powershell", "FileName = \"cmd.exe\"",
                                    "FileName = \"wt.exe\"", "FileName = \"curl.exe\"" })
            Assert.False(src.Contains(bad), $"{file} launches by bare name: {bad}");
    }
}

[Fact]
public void Planted_powershell_on_PATH_is_not_used()
{
    using var sandbox = TempDir.New();
    File.Copy(TestData.MarkerExe, Path.Combine(sandbox.Path, "powershell.exe"));
    using var _ = EnvVar.Prepend("PATH", sandbox.Path);

    Tunnel.Start("site.test", _ => { });
    Assert.False(File.Exists(TestData.MarkerPath), "the planted powershell.exe ran");
}
```

---

### B12 — `SiteListControl` builds nested-quoted shell command strings

**Severity:** Medium
**File:** `src/BanglaHost.App/Views/SiteListControl.xaml.cs` (also `SitesPage.xaml.cs`)
**Class/Method:** the “Open in terminal” / “Open shell here” actions

**Why it is a problem**

These actions compose a command line for `wt.exe` → `powershell.exe` → `cmd.exe /K`, with the site root path interpolated into a string that is parsed by up to three different quoting regimes in sequence (Windows CRT tokenisation, PowerShell’s parser, `cmd.exe`’s parser). The quoting is not balanced consistently across those layers, and `cmd.exe` is the dangerous one: unlike the others it *does* interpret `&`, `|`, `^` and `>`.

A site root containing `&` — legal in a Windows path, e.g. `C:\Work\R&D\site` — therefore ends the intended command and starts a new one. The values come from a folder picker rather than an attacker, so the realistic effect is a broken action or an accidental command, not remote compromise; but it is a shell-metacharacter injection in a path that reaches `cmd.exe`.

**Recommended fix**

Do not build a shell string. Launch the terminal with `ArgumentList`, set `WorkingDirectory` instead of `cd`-ing, and drop the `cmd.exe /K` layer entirely.

**Suggested corrected code**

```csharp
private static void OpenTerminalAt(string root)
{
    var psi = new ProcessStartInfo
    {
        FileName = SystemExe.WindowsTerminal() ?? SystemExe.Cmd,
        WorkingDirectory = root,          // no `cd`, no quoting, no shell
        UseShellExecute = false,
    };
    if (psi.FileName.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase))
    {
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(root);       // escaped by the runtime
    }
    using var _ = Process.Start(psi);
}
```

**Regression test**

```csharp
[Theory]
[InlineData(@"C:\Work\R&D\site")]
[InlineData(@"C:\Work\a b\site")]
[InlineData(@"C:\Work\x^y\site")]
[InlineData(@"C:\Work\p|q\site")]
public void Terminal_opens_in_awkward_paths_without_shell_interpretation(string root)
{
    Directory.CreateDirectory(root);
    using var rec = ProcessArgumentRecorder.Start("wt.exe", "cmd.exe");
    SiteListControlTestHarness.OpenTerminalAt(root);

    Assert.Equal(root, rec.LastStartInfo.WorkingDirectory);
    Assert.DoesNotContain("/K", rec.LastArgumentList);
    Assert.DoesNotContain("&", string.Join("", rec.LastArgumentList.Where(a => a != root)));
}
```

---

### B13 — `InstallerService` executes through `cmd.exe /c {cmd} {args}`

**Severity:** Medium
**File:** `src/BanglaHost.Core/InstallerService.cs`

**Exact problematic code**

```csharp
cmd.exe /c {cmd} {args}
```

**Why it is a problem**

Routing through `cmd.exe` re-introduces a shell for no benefit: `UseShellExecute = false` with `ArgumentList` can launch any executable directly. With `cmd` in the path, every metacharacter in `{cmd}` or `{args}` (`&`, `|`, `>`, `^`, `%VAR%` expansion) becomes syntax. Wherever those values derive from a filename, a version string, or a marketplace/blueprint field, that is command injection into a process that may be part of an install or elevated flow.

**Recommended fix**

Launch the target executable directly with `ArgumentList`. If output redirection was the reason for `cmd`, use the redirect properties instead.

**Suggested corrected code**

```csharp
var psi = new ProcessStartInfo
{
    FileName = ResolveExecutable(cmd),      // absolute path, validated to exist
    UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardOutput = true, RedirectStandardError = true,
};
foreach (var a in args) psi.ArgumentList.Add(a);   // args is string[], not a joined string
```

**Regression test**

```csharp
[Fact]
public async Task Installer_does_not_route_through_a_shell()
{
    using var rec = ProcessArgumentRecorder.Start("cmd.exe");
    await InstallerService.RunAsync("whoami", new[] { "&& echo pwned > pwned.txt" }, default);
    Assert.Empty(rec.Launches);                       // cmd.exe never started
    Assert.False(File.Exists("pwned.txt"));
}
```

---

### B14 — `NetUtils.IsPortAvailable` fails **open**

**Severity:** Medium (reliability + a misleading security signal)
**File:** `src/BanglaHost.Core/NetUtils.cs` — `IsPortAvailable(int port)`

**Exact problematic code**

```csharp
bool isAvailable = true;
try
{
    // IPGlobalProperties.GetActiveTcpListeners() / GetActiveTcpConnections()
    ...
}
catch { }
return isAvailable;
```

**Why it is a problem**

The variable is initialised to `true` and the `catch` is empty, so **any** failure enumerating TCP state — a transient `NetworkInformationException`, a restricted container, an EDR hook — is reported as “the port is free”. The app then starts nginx or MySQL on a port something else already owns. The visible outcome is a service that appears to start and immediately dies, or worse, binds a port another application was using and breaks it.

Availability checks must fail **closed**: if you cannot prove a port is free, do not claim it is.

**Recommended fix**

Return a tri-state (or throw) so callers can distinguish “free”, “in use” and “unknown”, and treat “unknown” as unavailable at the call sites. Log the underlying exception rather than discarding it.

**Suggested corrected code**

```csharp
public enum PortState { Free, InUse, Unknown }

public static PortState GetPortState(int port)
{
    try
    {
        var props = IPGlobalProperties.GetIPGlobalProperties();
        if (props.GetActiveTcpListeners().Any(ep => ep.Port == port)) return PortState.InUse;
        if (props.GetActiveTcpConnections().Any(c => c.LocalEndPoint.Port == port)) return PortState.InUse;
        return PortState.Free;
    }
    catch (Exception ex)
    {
        Log.Warn($"could not enumerate TCP state for port {port}: {ex.Message}");
        return PortState.Unknown;      // fail closed
    }
}

/// <summary>Only true when we can PROVE the port is free.</summary>
public static bool IsPortAvailable(int port) => GetPortState(port) == PortState.Free;
```

**Regression test**

```csharp
[Fact]
public void Enumeration_failure_reports_unavailable()
{
    NetUtils.OverrideEnumeratorForTests(() => throw new NetworkInformationException());
    Assert.Equal(PortState.Unknown, NetUtils.GetPortState(8080));
    Assert.False(NetUtils.IsPortAvailable(8080));
}

[Fact]
public void An_occupied_port_is_reported_in_use()
{
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    try { Assert.Equal(PortState.InUse, NetUtils.GetPortState(((IPEndPoint)l.LocalEndpoint).Port)); }
    finally { l.Stop(); }
}
```

---

### B15 — `Elevation.Run` mis-quotes arguments ending in a backslash

**Severity:** Low
**File:** `src/BanglaHost.Core/Elevation.cs` — `Elevation.Run`

**Why it is a problem**

Arguments are wrapped in `"…"` without doubling a trailing backslash. Under Windows CRT rules, `"C:\path\"` ends with an **escaped quote**, so the token does not terminate and the following argument is absorbed into it. A directory path with a trailing separator — routine — therefore corrupts the argument vector passed to the **elevated** helper. Combined with **B5**, malformed input reaches a privileged process; on its own it manifests as an unexplained failure after the user has already approved a UAC prompt.

**Recommended fix**

Use `ArgumentList` (which implements the CRT escaping rules correctly) — note it works with `UseShellExecute = true` + `Verb = "runas"` on .NET 8 — or apply the documented escaping if a string must be built.

**Suggested corrected code**

```csharp
var psi = new ProcessStartInfo
{
    FileName = ElevateExePath,
    UseShellExecute = true,
    Verb = "runas",
};
foreach (var a in args) psi.ArgumentList.Add(a);   // correct escaping, incl. trailing backslashes
```

**Regression test**

```csharp
[Theory]
[InlineData(new[] { @"C:\dir\", "second" })]
[InlineData(new[] { @"C:\a b\", @"C:\c\" })]
[InlineData(new[] { "plain", @"trailing\\" })]
public void Arguments_round_trip_through_elevation(string[] args)
{
    var echoed = RunArgEchoHelperViaElevation(args);
    Assert.Equal(args, echoed);
}
```

---

## C. Crash / hang issues

**A1** (Dashboard timer → thread-pool starvation) and **A7** (job-object static init) are the two headline entries in this category and are documented in full above. **D7** (no `ThreadPool.SetMinThreads`) is the amplifier that turns any of the blocking findings below into a hang.

### C1 — Nine pages call expensive Core work directly on the dispatcher thread

**Severity:** High
**Files / members:**

| File | Member | Blocking work |
|---|---|---|
| `Views/AiAssistantPage.xaml.cs` | `OnNavigatedTo` | `Engine.Api()` — full 37-service snapshot with TCP probes |
| `Views/ExtensionsPage.xaml.cs:34` | page load | `Engine.PhpExtensions(v)` → `Php.IniPath` → **spawns `php.exe`** |
| `Views/PhpExtensionsPage.xaml.cs:88` | page load | `Php.ListExtensions(v)` → `Php.IniPath` → **spawns `php.exe`** |
| `Views/ServicesPage.xaml.cs` | `EditIni_Click` | `PhpIniPath` → spawns `php.exe` |
| `Views/SitesPage.xaml.cs` | `EnsureRequirements` | `MissingForSite` ×2 — filesystem walks |
| `Views/SiteListControl.xaml.cs` | `Remove_Click` | `SiteTargets` |
| `Views/LogsPage.xaml.cs` | `Reload` | `LogFiles` — directory enumeration + file reads |
| `Views/SslPage.xaml.cs:32` | `LoadCerts` | `SslService.GetLocalCertificates()` — parses every `.pem` |
| `Views/DatabaseExplorerPage.xaml.cs:64,82,111` | `LoadDatabases`, `Execute` | `RunSql` — **spawns `mysql.exe`**, unbounded wait (**A4**) |

**Exact problematic code** (representative)

```csharp
// ExtensionsPage.xaml.cs:34 — synchronous, on the UI thread
var ext = EngineHost.Instance.Engine.PhpExtensions(SelectedVersion);
```

```csharp
// Php.cs:29 — what that call actually does
var (_, loaded) = Run(exe, "-r \"echo php_ini_loaded_file();\"");
```

```csharp
// Php.cs:19-21 — Run(): synchronous ReadToEnd + unbounded WaitForExit
var p = Process.Start(psi)!;
var outp = ((Func<string>)(() => { var _errT = p.StandardError.ReadToEndAsync();
                                   var _out  = p.StandardOutput.ReadToEnd();
                                   return _out + _errT.Result; }))();
p.WaitForExit();
```

```csharp
// SslPage.xaml.cs:25-33
protected override void OnNavigatedTo(NavigationEventArgs e) { LoadCerts(); }
private void LoadCerts() { SslList.ItemsSource = SslService.GetLocalCertificates(); }
```

**Why it is a problem**

`EngineHost` exists precisely to keep this work off the dispatcher — `Run` (line 52), `RunCaptured` (60), `Snapshot` (72) and `RunTracked` (94/107) all wrap Core in `Task.Run`. These nine call sites bypass it and invoke Core synchronously from `OnNavigatedTo` or a click handler, i.e. **on the UI thread**.

Process creation on Windows costs tens of milliseconds at best; `php.exe -r` on a cold file cache with an AV real-time scanner inspecting the image regularly costs hundreds of milliseconds to seconds. `Php.Status()` spawns `php -v` **once per installed version**. So navigating to PHP Extensions with four PHP versions installed can spawn four processes serially with unbounded waits, all while the window cannot repaint. A single AV-delayed spawn is enough to cross the ~5 s AppHang threshold; several in a row make it likely.

Note this is not a theoretical concern for the observed telemetry: navigation is user-initiated, so these stalls happen exactly when the user is interacting — the worst moment for the message pump to stop.

**Reproduction scenario**

1. Install 3–4 PHP versions.
2. With Defender real-time protection **on** and no exclusion for `Paths.Bin` (the first-run dialog offers exclusions; choose “Skip”).
3. Navigate to PHP Extensions, then Extensions, then SQL Studio in quick succession.
4. The window stops repainting on each navigation; with a cold cache the stall exceeds 5 s and Windows marks the app Not Responding.

**Recommended fix**

Make the rule mechanical rather than aspirational: Core must not be called from a view except through `EngineHost`, and `OnNavigatedTo` must never do I/O. Load asynchronously with a visible loading state, and add a debug-time assertion that fires when Core is entered on the UI thread — that is what turns this from a recurring class of bug into a caught mistake (**E2**).

**Suggested corrected code**

```csharp
// EngineHost.cs — a debug tripwire so this regresses loudly instead of silently
[Conditional("DEBUG")]
internal static void AssertNotUiThread(string member)
{
    if (App.MainDispatcher?.HasThreadAccess == true)
        throw new InvalidOperationException(
            $"{member} performs I/O and must not run on the UI thread. " +
            "Call it through EngineHost.Run/RunCaptured/Snapshot.");
}

public Task<IReadOnlyList<Php.ExtensionInfo>> PhpExtensions(string version) =>
    Task.Run(() => Engine.PhpExtensions(version));
```

```csharp
// Php.cs — assert at the boundary of every process-spawning helper
public static string IniPath(string version)
{
    EngineHost.AssertNotUiThread(nameof(IniPath));
    ...
}
```

```csharp
// ExtensionsPage.xaml.cs — async load with a real loading state
protected override void OnNavigatedTo(NavigationEventArgs e)
{
    _cts = new CancellationTokenSource();
    _ = LoadAsync(_cts.Token);
}

private async Task LoadAsync(CancellationToken ct)
{
    Loading.IsActive = true;
    ExtList.ItemsSource = null;
    try
    {
        var ext = await EngineHost.Instance.PhpExtensions(SelectedVersion);
        if (!ct.IsCancellationRequested) ExtList.ItemsSource = ext;
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { ErrorText.Text = ex.Message; CrashLogger.Log(ex, "ExtensionsPage"); }
    finally { Loading.IsActive = false; }
}

protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    _cts?.Cancel(); _cts?.Dispose(); _cts = null;
    base.OnNavigatedFrom(e);
}
```

```csharp
// SslPage.xaml.cs
protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadCertsAsync();

private async Task LoadCertsAsync()
{
    var certs = await Task.Run(SslService.GetLocalCertificates);
    SslList.ItemsSource = certs;
}
```

**Regression test**

```csharp
[Theory]
[InlineData(typeof(AiAssistantPage))]
[InlineData(typeof(ExtensionsPage))]
[InlineData(typeof(PhpExtensionsPage))]
[InlineData(typeof(ServicesPage))]
[InlineData(typeof(SitesPage))]
[InlineData(typeof(LogsPage))]
[InlineData(typeof(SslPage))]
[InlineData(typeof(DatabaseExplorerPage))]
[InlineData(typeof(DashboardPage))]
public async Task Navigating_to_a_page_never_blocks_the_ui_thread(Type page)
{
    await UiTest.RunAsync(async () =>
    {
        var pump = UiTest.StartPumpWatchdog();     // records the longest gap between rendered frames
        UiTest.Frame.Navigate(page);
        await Task.Delay(3000);
        Assert.True(pump.LongestStall < TimeSpan.FromMilliseconds(200),
            $"{page.Name} stalled the UI thread for {pump.LongestStall.TotalMilliseconds:0} ms");
    });
}

[Fact]
public void Core_process_helpers_reject_being_called_on_the_ui_thread()
{
    UiTest.Run(() => Assert.Throws<InvalidOperationException>(() => Php.IniPath("8.3")));
}
```

---

### C2 — 26 of 31 `WaitForExit()` calls have no timeout; `Elevation.Run` waits on a UAC prompt forever

**Severity:** High
**Files:** across Core. Only **5** of 31 `WaitForExit` call sites pass a timeout: `Tools.cs:240`, `Tunnel.cs:196`, `Tunnel.cs:213`, `WindowsDefender.cs:37`, `WindowsDefender.cs:64`. The remaining 26 — including `Php.Run` (`Php.cs:21`), `Apache.cs:134`, `Downloader.Shell`, `Downloader.CurlTo`, `BackupService.DumpDatabase` (`p?.WaitForExit()`), `DatabaseExplorerPage.RunSql` — wait indefinitely.

**Exact problematic code**

```csharp
// Php.cs:21 and 25 others
p.WaitForExit();
```

```csharp
// Elevation.cs — worst case: the wait is on a human
using var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args,
                                                  UseShellExecute = true, Verb = "runas" });
p.WaitForExit();          // no timeout — blocks until the UAC dialog is answered
```

**Why it is a problem**

An unbounded `WaitForExit()` converts any child-process misbehaviour into an application hang. The child processes here are not simple: `mysql.exe` can block on a lock; `php.exe` can block loading a broken extension; `curl.exe` can stall on a half-open TCP connection well past its own retry logic; `httpd.exe -t` can prompt.

`Elevation.Run` is the sharpest case because the thing being waited on is **a user decision**. The UAC consent dialog is modal on the secure desktop and can sit unanswered indefinitely — the user may walk away, or may not notice it behind another window. Meanwhile the calling thread is blocked. When that thread is the UI thread (or the pool is already tight from **A1**), the app is Not Responding for as long as the prompt is ignored, and the user’s natural reaction — click the frozen window — makes Windows offer to *kill* it. That is a plausible path to both a hang report and a “crash” from a forced termination.

**Reproduction scenario**

1. Add a site (which needs a `hosts` edit → UAC prompt).
2. When the UAC dialog appears, do not answer it. Switch to another window.
3. BanglaHost is Not Responding. Click it: Windows offers “Close the program”.
4. Variant: `Set-Content` a `php.ini` with `extension=nonexistent.dll`, then navigate to PHP Extensions; `php.exe` stalls and the page never loads.

**Recommended fix**

Every process wait gets a timeout and a kill-on-timeout, and every wait gets off the UI thread. For elevation specifically, do not block at all: await `WaitForExitAsync` with a generous timeout, and surface a cancellable “waiting for permission…” state so the UI stays alive.

**Suggested corrected code**

```csharp
/// <summary>Run a child process with a hard deadline. Kills the tree on timeout.
/// Reads both pipes concurrently so neither can deadlock (see C5).</summary>
internal static async Task<(int code, string stdout, string stderr, bool timedOut)> RunAsync(
    ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct = default)
{
    psi.UseShellExecute = false;
    psi.CreateNoWindow = true;
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError = true;

    using var p = Process.Start(psi) ?? throw new BhException($"could not start {psi.FileName}");
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);

    var so = p.StandardOutput.ReadToEndAsync();
    var se = p.StandardError.ReadToEndAsync();
    try
    {
        await p.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        return (-1, await SafeAwait(so), await SafeAwait(se), true);
    }
    return (p.ExitCode, await so, await se, false);

    static async Task<string> SafeAwait(Task<string> t) { try { return await t; } catch { return ""; } }
}
```

```csharp
// Elevation.cs — never block on a human decision
public static async Task<bool> RunAsync(string exe, IEnumerable<string> args,
                                        IProgress<string>? status, CancellationToken ct)
{
    var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = true, Verb = "runas" };
    foreach (var a in args) psi.ArgumentList.Add(a);          // correct escaping (see B15)

    using var p = Process.Start(psi);
    if (p is null) return false;                              // user dismissed the prompt

    status?.Report("Waiting for Windows to grant permission…");
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromMinutes(2));
    try { await p.WaitForExitAsync(cts.Token); }
    catch (OperationCanceledException)
    {
        status?.Report("Permission wasn't granted in time — the change was not applied.");
        return false;
    }
    return p.ExitCode == 0;
}
```

**Regression test**

```csharp
[Fact]
public void No_WaitForExit_without_a_timeout_remains_in_core()
{
    var offenders = new List<string>();
    foreach (var f in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
        foreach (var (line, i) in File.ReadLines(f).Select((l, i) => (l, i + 1)))
            if (Regex.IsMatch(line, @"WaitForExit\(\s*\)"))
                offenders.Add($"{f}:{i}");
    Assert.Empty(offenders);        // use WaitForExitAsync(ct) or WaitForExit(ms)
}

[Fact]
public async Task A_hung_child_is_killed_at_the_deadline()
{
    var psi = new ProcessStartInfo(SystemExe.Cmd);
    psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("pause");     // waits forever
    var sw = Stopwatch.StartNew();
    var (code, _, _, timedOut) = await ProcRunner.RunAsync(psi, TimeSpan.FromSeconds(2));
    sw.Stop();
    Assert.True(timedOut);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6));
    Assert.Empty(Process.GetProcessesByName("cmd").Where(p => p.StartTime > sw.Elapsed.Ago()));
}

[Fact]
public async Task Elevation_gives_up_and_keeps_the_ui_alive()
{
    var pump = UiTest.StartPumpWatchdog();
    var task = Elevation.RunAsync(TestData.NeverExitsExe, Array.Empty<string>(), null,
                                 new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
    Assert.False(await task);
    Assert.True(pump.LongestStall < TimeSpan.FromMilliseconds(200));
}
```

---

### C3 — `DbServer.Stop()` runs a full synchronous backup of every database during shutdown

**Severity:** High
**File:** `src/BanglaHost.Core/DbServer.cs` — `DbServer.Stop()`

**Exact problematic code**

```csharp
BackupService.AutoBackupAllDatabasesAsync().GetAwaiter().GetResult();
```

**Why it is a problem**

Three problems compounded:

1. **`.GetAwaiter().GetResult()` on an async method** blocks the calling thread for the entire duration. `Stop()` is reachable from the tray menu (`MainWindow` `StopAllRequested`), from the Dashboard’s Stop All, and from app shutdown.
2. **The work is unbounded.** It dumps *every* database. On a developer machine with a few GB of data that is minutes, not seconds — during which the app is frozen if the call is on the UI thread, and during which Windows may decide the app has hung.
3. **It runs at the worst possible moment.** On shutdown, the OS gives a process a limited window to exit before it is terminated. If the app is being closed (or the machine is shutting down / restarting for updates), the backup is likely to be **killed mid-dump**, leaving a truncated `.sql` in the auto-backup folder that `AutoBackupAllDatabasesAsync`’s 5-day retention will happily rotate *good* backups out in favour of. The retention logic (`Directory.Delete(dir, true)`, keep 5 days) does not distinguish a complete dump from a truncated one, and the surrounding `catch { }` hides the failure.

A shutdown path must be fast and interruption-safe. Backing up gigabytes is neither.

**Reproduction scenario**

1. Create a database with a few hundred MB of data.
2. Start MySQL, then choose **Stop All** from the tray icon.
3. The tray menu and window are unresponsive for the duration of the dump; with enough data, Windows marks the app hung.
4. Kill the app mid-dump, then inspect the auto-backup folder: a truncated `.sql` file is present and counted as that day's backup.

**Recommended fix**

Decouple the two concerns. Shutdown stops the server, promptly. Auto-backup becomes a scheduled operation with its own budget, cancellation and completion marker, never on the stop path. If a “backup before stopping” option is genuinely wanted, make it explicit, opt-in, cancellable, and bounded — and write to a `.partial` name that is renamed only on success.

**Suggested corrected code**

```csharp
public static void Stop()
{
    // Shutdown does exactly one thing: stop the server, with a deadline.
    var pid = ReadPidFile();
    if (pid is { } id) KillSafe(id);
    for (var i = 0; i < 20 && Running(); i++) Thread.Sleep(150);
    if (Running()) KillStray();
    // No backup here. Ever.
}
```

```csharp
// BackupService — scheduled, cancellable, and atomic per dump
public static async Task AutoBackupAllDatabasesAsync(CancellationToken ct)
{
    foreach (var db in await ListDatabasesAsync(ct))
    {
        ct.ThrowIfCancellationRequested();
        var final   = Path.Combine(TodayDir, $"{db}.sql");
        var partial = final + ".partial";
        try
        {
            var ok = await DumpDatabaseAsync(db, partial, TimeSpan.FromMinutes(10), ct);
            if (ok) File.Move(partial, final, overwrite: true);   // only complete dumps get the real name
            else { File.Delete(partial); Log.Warn($"auto-backup of {db} failed"); }
        }
        catch (OperationCanceledException) { try { File.Delete(partial); } catch { } throw; }
    }
    File.WriteAllText(Path.Combine(TodayDir, ".complete"), DateTime.UtcNow.ToString("O"));
}

/// <summary>Retention only ever deletes days that completed. A killed run is never counted.</summary>
private static void Prune()
{
    var complete = Directory.EnumerateDirectories(BackupRoot)
        .Where(d => File.Exists(Path.Combine(d, ".complete")))
        .OrderByDescending(d => d).ToList();
    foreach (var old in complete.Skip(KeepDays)) Directory.Delete(old, true);
}
```

**Regression test**

```csharp
[Fact]
public void Stop_returns_promptly_regardless_of_database_size()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "");
    env.Db.SeedLargeDatabase(sizeMb: 300);

    var sw = Stopwatch.StartNew();
    DbServer.Stop();
    sw.Stop();

    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Stop took {sw.Elapsed}");
    Assert.False(Directory.EnumerateFiles(BackupService.BackupRoot, "*.sql", SearchOption.AllDirectories).Any(),
                 "Stop performed a backup");
}

[Fact]
public async Task An_interrupted_auto_backup_is_not_counted_as_a_backup()
{
    using var env = TestEnv.WithEngine("mysql", rootPassword: "");
    env.Db.SeedLargeDatabase(sizeMb: 200);
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => BackupService.AutoBackupAllDatabasesAsync(cts.Token));

    Assert.Empty(Directory.GetFiles(BackupService.TodayDir, "*.sql"));
    Assert.Empty(Directory.GetFiles(BackupService.TodayDir, "*.partial"));
    Assert.False(File.Exists(Path.Combine(BackupService.TodayDir, ".complete")));
}
```

---

### C4 — `Downloader.CurlTo` is fake-async: `await`ing an install never yields

**Severity:** High
**File:** `src/BanglaHost.Core/Downloader.cs` — `Downloader.CurlTo(string url, string dest, string? ua)`

**Exact problematic code**

```csharp
private static Task CurlTo(string url, string dest, string? ua = UA)
{
    ...
    while ((n = p.StandardError.Read(buf, 0, buf.Length)) > 0)
    {
        ...
        OnProgress?.Invoke(pct);
    }
    p.WaitForExit();
    ...
    return Task.CompletedTask;      // all work already done, synchronously
}
```

**Why it is a problem**

The method returns `Task` but contains no `await`. Every statement — the blocking `Read` loop over curl’s progress output and the unbounded `WaitForExit()` — executes **synchronously on the caller’s thread** before the already-completed task is returned. `await Downloader.CurlTo(...)` therefore never yields: it is a synchronous multi-minute call wearing an async signature.

Consequences: any caller that reasonably assumed `await` made it non-blocking is wrong. If such a call happens on the UI thread the window freezes for the whole download (up to the 15-minute `HttpClient` timeout — and curl’s own wait has no timeout at all). If it happens on a pool thread, that thread is pinned for minutes, which is a direct contribution to the starvation in **A1**.

This is also the most misleading kind of defect, because the calling code *looks* correct. Nothing at the call site hints that the `await` is a no-op.

**Reproduction scenario**

1. Throttle the network to ~50 KB/s (`clumsy`, or a QoS policy).
2. Trigger a large install (PHP or MariaDB) from the Services page.
3. Set a breakpoint or log the managed thread ID inside the `while` loop — it is the caller’s thread throughout.
4. If the caller is on the UI thread, the window does not repaint for the duration; progress callbacks cannot render because the dispatcher never gets control.

**Recommended fix**

Make it genuinely async: `await`-based stream reads, `WaitForExitAsync` with a deadline, and cancellation. Better still, drop the `curl.exe` subprocess entirely — `HttpClient` already exists in the class and can stream to disk with progress reporting and no process at all.

**Suggested corrected code**

```csharp
private static async Task DownloadAsync(string url, string dest,
                                        IProgress<double>? progress, CancellationToken ct)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.UserAgent.ParseAdd(UA);

    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();

    var total = resp.Content.Headers.ContentLength;
    var staging = dest + ".part";

    await using (var src = await resp.Content.ReadAsStreamAsync(ct))
    await using (var dst = new FileStream(staging, FileMode.Create, FileAccess.Write,
                                          FileShare.None, 128 * 1024, useAsync: true))
    {
        var buf = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total is > 0) progress?.Report(done * 100.0 / total.Value);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    File.Move(staging, dest, overwrite: true);     // never expose a partial file under the real name
}
```

**Regression test**

```csharp
[Fact]
public async Task Download_does_not_block_the_calling_thread()
{
    using var srv = LocalHttpsServer.Slow("/big.zip", bytes: 20_000_000, kbPerSec: 200);
    var callerThread = Environment.CurrentManagedThreadId;
    var observed = -1;

    var progress = new Progress<double>(_ => observed = Environment.CurrentManagedThreadId);
    var t = Downloader.DownloadAsync(srv.Url("/big.zip"), Path.Combine(Paths.Tmp, "big.zip"),
                                     progress, default);

    Assert.False(t.IsCompleted, "DownloadAsync completed synchronously — it is not really async");
    await t;
}

[Fact]
public async Task Download_is_cancellable_and_leaves_no_partial_file()
{
    using var srv = LocalHttpsServer.Slow("/big.zip", bytes: 20_000_000, kbPerSec: 100);
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
    var dest = Path.Combine(Paths.Tmp, "big.zip");

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => Downloader.DownloadAsync(srv.Url("/big.zip"), dest, null, cts.Token));

    Assert.False(File.Exists(dest));
}
```

---

### C5 — `Downloader.Shell` reads stderr to completion before stdout → pipe deadlock

**Severity:** High
**File:** `src/BanglaHost.Core/Downloader.cs` — `Downloader.Shell(string exe, params string[] args)`

**Exact problematic code**

```csharp
private static void Shell(string exe, params string[] args)
{
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var err = p.StandardError.ReadToEnd();     // drains stderr FIRST, to completion
    p.StandardOutput.ReadToEnd();              // stdout is not read until stderr closes
    p.WaitForExit();
    ...
}
```

**Why it is a problem**

Redirected pipes have a fixed OS buffer (commonly ~4 KB, not guaranteed). `ReadToEnd()` on stderr does not return until the child **closes** stderr, which for most tools means process exit. Meanwhile nothing is draining stdout. If the child writes more than one pipe buffer to stdout, its next write blocks. The child is now waiting for BanglaHost to read stdout; BanglaHost is waiting for the child to close stderr. **Neither can proceed — permanent deadlock**, made unrecoverable by the unbounded `WaitForExit()` (**C2**).

The affected callers are the extraction paths: `ExtractZip` runs `tar.exe -xf … -C …`. `tar` is quiet on success, which is why this has not deadlocked constantly — but it is chatty when it hits problems, and `--exclude`, verbose modes, permission warnings and corrupt-archive diagnostics all produce output. So the failure appears exactly when something has already gone wrong: an install that hits an archive problem hangs instead of reporting it.

The same anti-pattern in the opposite order appears in `Php.Run` (`Php.cs:20`) and `DatabaseExplorerPage.RunSql`, which start `ReadToEndAsync()` on stderr *before* synchronously reading stdout. Those two are actually safe — the async read drains stderr concurrently — but they are one refactor away from the deadlock and read as though the ordering were incidental. They should use the same explicit concurrent-read helper.

**Reproduction scenario**

1. Craft an archive that makes `tar` emit more than ~8 KB to stdout (e.g. force verbose extraction, or an archive with many entries triggering warnings).
2. Call the extraction path.
3. `tar.exe` remains alive with a full stdout pipe; BanglaHost sits in `ReadToEnd()` on stderr forever. The install never completes and never errors.

Deterministic unit-level version: substitute a helper exe that writes 1 MB to stdout and one line to stderr, then exits — with the current code the call never returns.

**Recommended fix**

Always read both streams concurrently, then wait, with a deadline. Use the single `ProcRunner.RunAsync` helper from **C2** everywhere so the ordering cannot be got wrong again.

**Suggested corrected code**

```csharp
private static async Task<(int code, string stdout, string stderr)> ShellAsync(
    string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct = default)
{
    var psi = new ProcessStartInfo { FileName = exe };
    foreach (var a in args) psi.ArgumentList.Add(a);

    var (code, so, se, timedOut) = await ProcRunner.RunAsync(psi, timeout, ct);
    if (timedOut)
        throw new BhException($"{Path.GetFileName(exe)} did not finish within {timeout.TotalSeconds:0}s and was terminated.");
    if (code != 0)
        throw new BhException($"{Path.GetFileName(exe)} failed (exit {code}): {se.Trim()}");
    return (code, so, se);
}
```

**Regression test**

```csharp
[Fact]
public async Task A_chatty_child_does_not_deadlock()
{
    // Writes 1 MB to stdout and one line to stderr — deadlocks the old stderr-first code.
    var psi = new ProcessStartInfo(TestData.ChattyExe);
    var sw = Stopwatch.StartNew();
    var (code, so, se, timedOut) = await ProcRunner.RunAsync(psi, TimeSpan.FromSeconds(15));
    sw.Stop();

    Assert.False(timedOut);
    Assert.Equal(0, code);
    Assert.True(so.Length >= 1_000_000);
    Assert.Contains("warning", se);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
}

[Fact]
public void No_code_reads_one_redirected_stream_to_completion_before_the_other()
{
    foreach (var f in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
    {
        var src = File.ReadAllText(f);
        var i = src.IndexOf("StandardError.ReadToEnd()", StringComparison.Ordinal);
        if (i < 0) continue;
        var after = src[i..];
        Assert.False(after.Contains("StandardOutput.ReadToEnd()"),
            $"{f}: sequential stderr-then-stdout read — use ProcRunner.RunAsync");
    }
}
```

---

### C6 — `PackageManagerPage` appends to `TextBox.Text` once per output line

**Severity:** Medium
**File:** `src/BanglaHost.App/Views/PackageManagerPage.xaml.cs`
**Class/Method:** the `OutputDataReceived` handler

**Exact problematic code**

```csharp
LogViewer.Text += e.Data + "\n";
```

**Why it is a problem**

`string` is immutable, so `+=` allocates and copies the entire accumulated text on every line — O(n²) total work. Worse, each assignment to `TextBox.Text` invalidates measure/arrange and triggers a **relayout** of the control on the UI thread. `composer install` or `npm install` emits thousands of lines, so a single install produces thousands of full-text copies and thousands of layout passes. Both the CPU cost and the layout churn land on the dispatcher; the window becomes sluggish, then unresponsive, and the accumulated string grows without bound (a multi-MB `Text` value with no cap is also a memory concern).

**Reproduction scenario**

Run `npm install` on a project with a large dependency tree from the Package Manager page. Watch the UI degrade progressively as output accumulates; CPU sits high in `Microsoft.UI.Xaml` layout even after output slows.

**Recommended fix**

Buffer lines off-thread, flush to the UI on a timer in batches, cap retained output, and use a control designed for append (or at minimum assign a fully-built string once per flush).

**Suggested corrected code**

```csharp
private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pending = new();
private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(120) };
private readonly LinkedList<string> _lines = new();
private const int MaxLines = 5000;

private void OnOutput(object? s, DataReceivedEventArgs e)
{
    if (e.Data is not null) _pending.Enqueue(e.Data);       // no UI work on the reader thread
}

private void OnFlush(object? s, object e)
{
    if (_pending.IsEmpty) return;
    while (_pending.TryDequeue(out var line))
    {
        _lines.AddLast(line);
        if (_lines.Count > MaxLines) _lines.RemoveFirst();  // bounded memory
    }
    LogViewer.Text = string.Join('\n', _lines);             // one assignment, one layout pass
    LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
}
```

**Regression test**

```csharp
[Fact]
public async Task Ten_thousand_output_lines_stay_responsive_and_bounded()
{
    await UiTest.RunAsync(async () =>
    {
        var page = new PackageManagerPage();
        var pump = UiTest.StartPumpWatchdog();
        for (var i = 0; i < 10_000; i++) page.TestOnly_OnOutput($"line {i}");
        await Task.Delay(2000);

        Assert.True(pump.LongestStall < TimeSpan.FromMilliseconds(200));
        Assert.True(page.TestOnly_LineCount <= 5000);
        Assert.True(page.TestOnly_FlushCount < 60, "flushed per line instead of in batches");
    });
}
```

---

### C7 — `Localizer` walks the entire visual tree four times per navigation, on the UI thread

**Severity:** Medium
**Files:** `src/BanglaHost.App/MainWindow.xaml.cs:236-264`, `src/BanglaHost.App/Services/Localizer.cs`

**Exact problematic code**

```csharp
private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
{
    if (!Localizer.IsActive) return;
    if (e.Content is not FrameworkElement page) return;
    Localizer.Localize(page);              // pass 1
    page.Loaded += Page_Loaded_Localize;
}

private void Page_Loaded_Localize(object sender, RoutedEventArgs e)
{
    ...
    Localizer.Localize(page);              // pass 2
    _ = RewalkAfterAsync(page);
}

private static async Task RewalkAfterAsync(FrameworkElement page)
{
    foreach (var ms in new[] { 250, 800, 1800 })   // passes 3, 4, 5
    {
        await Task.Delay(ms);
        try { if (page.XamlRoot != null) Localizer.Localize(page); }
        catch { }
    }
}
```

**Why it is a problem**

`Localizer.Localize` walks the whole subtree via `VisualTreeHelper.GetChildrenCount`/`GetChild` and rewrites text on every `TextBlock`, `Button`, `MenuFlyoutItem`, `CommandBar` element and inline run it finds. That is unavoidably UI-thread work, and it runs **five times** per navigation for Bengali users (immediate, on `Loaded`, then at +250 ms, +1050 ms, +2850 ms cumulative).

Two costs. First, performance: the Dashboard's tree is large (status cards, a sparkline, a paged site list, three tool rows), and five full walks per navigation is a visible cost on a slow machine, competing with exactly the work described in **A1**. Second, correctness-by-timing: the delayed passes exist because content arrives asynchronously and the walker has no way to know when. If content arrives at +2 s it gets translated; at +3 s it does not. Users see English text that becomes Bengali a second later, or stays English arbitrarily. See **F5**.

**Reproduction scenario**

Switch the language to Bengali, then navigate rapidly between Dashboard, Services and Sites. Text visibly re-renders after each navigation; some labels flip from English to Bengali up to ~3 s late; some (populated later) never flip.

**Recommended fix**

Strategically: move to MRT (`x:Uid` + `.resw`), which the project already uses in `SettingsPage.xaml` — the framework then resolves text at parse time with no walking, no timers and no flicker (**F5**). Tactically, until then: replace the timed re-walks with an event-driven single pass — localize once on `Loaded`, and have pages that populate asynchronously call `Localizer.Localize(subtree)` on the *specific* subtree they just filled.

**Suggested corrected code**

```csharp
// MainWindow.xaml.cs — one pass, no timers
private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
{
    if (!Localizer.IsActive || e.Content is not FrameworkElement page) return;
    if (page.IsLoaded) Localizer.Localize(page);
    else page.Loaded += Page_Loaded_Localize;
}

private void Page_Loaded_Localize(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement page) return;
    page.Loaded -= Page_Loaded_Localize;
    Localizer.Localize(page);
    // No RewalkAfterAsync: pages announce their own async content instead.
}
```

```csharp
// Any page that fills content later localizes only what it added.
SiteList.SetData(rows);
Localizer.LocalizeIfActive(SiteList);      // scoped subtree, not the whole page
```

**Regression test**

```csharp
[Fact]
public async Task Navigation_localizes_once_per_page()
{
    Localizer.ForceActiveForTests(true);
    var walks = Localizer.InstrumentWalks();
    await UiTest.RunAsync(async () =>
    {
        UiTest.Frame.Navigate(typeof(DashboardPage));
        await Task.Delay(3500);            // past every old timed re-walk
    });
    Assert.True(walks.Count <= 2, $"{walks.Count} full tree walks for one navigation");
}
```

---

### C8 — Global exception handlers are attached *after* `ApplySavedLanguage()` and `InitializeComponent()`

**Severity:** High
**File:** `src/BanglaHost.App/App.xaml.cs`
**Class/Method:** `App` constructor

**Why it is a problem**

`Application.UnhandledException`, `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are wired up after `ApplySavedLanguage()` and `InitializeComponent()` have already run. Anything those two throw is unprotected and unlogged: the process dies before any handler exists, and `CrashLogger` never records it.

That window is not empty. `ApplySavedLanguage()` reads configuration (so it is exposed to the corrupt-`config.json` case in **A5**) and sets culture — `new CultureInfo(bad)` throws `CultureNotFoundException`, and setting a primary language override can throw for a malformed tag. `InitializeComponent()` parses `App.xaml`, which resolves the resource dictionaries and theme resources; a missing or malformed resource throws `XamlParseException`.

This is the classic silent-startup-crash shape: the app dies before it can draw anything or log anything, the user sees a splash flash and nothing else, and telemetry gets a bucket with no usable stack — which is precisely the shape of the “Uncategorized” failures on the dashboard. I cannot prove that is what happened without stacks (**Requires runtime verification**), but it is a real unprotected window on the launch path and it should not exist regardless.

**Reproduction scenario**

1. Close the app. Edit `config.json` and set the language field to an invalid tag such as `"xx-INVALID-!!"`.
2. Launch. The process exits during construction; no dialog, no entry in the crash log.
3. Windows records an application error with no managed stack.

**Recommended fix**

Attach the handlers as the very first statements in the constructor — before `InitializeComponent()` and before any configuration is read — and wrap the risky startup steps so a bad setting degrades to defaults instead of terminating.

**Suggested corrected code**

```csharp
public App()
{
    // FIRST. Nothing above this line, so nothing can die unlogged.
    UnhandledException += OnAppUnhandled;
    AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
    TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;   // breadcrumbs for pre-handler failures

    JobManager.Configure(JobManager.Mode.KillOnHostExit);            // see A6

    try { ApplySavedLanguage(); }
    catch (Exception ex)
    {
        CrashLogger.Log(ex, "ApplySavedLanguage");                   // fall back to system default
    }

    try { InitializeComponent(); }
    catch (Exception ex)
    {
        CrashLogger.Log(ex, "InitializeComponent");
        throw;      // unrecoverable — but now it is recorded
    }
}
```

**Regression test**

```csharp
[Theory]
[InlineData("xx-INVALID-!!")]
[InlineData("")]
[InlineData("zz")]
public void A_bad_saved_language_does_not_prevent_startup(string tag)
{
    using var env = TestEnv.New();
    File.WriteAllText(Paths.ConfigJson, $"{{ \"language\": \"{tag}\" }}");

    var proc = LaunchApp();
    Assert.True(proc.WaitForMainWindow(TimeSpan.FromSeconds(30)), "app did not reach a window");
    Assert.Contains("ApplySavedLanguage", File.ReadAllText(CrashLogger.LogPath));   // logged, not fatal
}

[Fact]
public void Handlers_are_registered_before_any_other_statement()
{
    var body = SourceOf("src/BanglaHost.App/App.xaml.cs", "public App()");
    var iHandler = body.IndexOf("UnhandledException +=", StringComparison.Ordinal);
    var iInit    = body.IndexOf("InitializeComponent()", StringComparison.Ordinal);
    var iLang    = body.IndexOf("ApplySavedLanguage()", StringComparison.Ordinal);
    Assert.True(iHandler >= 0 && iHandler < iInit && iHandler < iLang);
}
```

---

### C9 — `AppDomain.CurrentDomain.UnhandledException` cannot prevent termination in .NET Core

**Severity:** High
**File:** `src/BanglaHost.App/App.xaml.cs` — the `AppDomain.CurrentDomain.UnhandledException` handler

**Why it is a problem**

The handler logs and returns. On .NET Framework, `e.Handled`-style recovery was partially possible; on .NET Core/.NET 8 there is **no way to mark the exception handled** — the runtime tears the process down as soon as the handler returns. So this handler is purely a logging hook, not a safety net.

That matters because the codebase spawns raw background threads and fire-and-forget work whose exceptions do not route through `Application.UnhandledException` (which only covers exceptions surfacing on the UI thread) and are not `Task`-wrapped (so `TaskScheduler.UnobservedTaskException` does not see them either). Known candidates: the `PhpCgi` watchdog (**C11**), `TrayIcon`'s message-only window procedure — an exception thrown inside a Win32 window proc callback crosses a native frame, where behaviour is undefined-to-fatal — and the `Task.Run(...)` fire-and-forget calls in `MainWindow`'s tray handlers (those *are* Task-wrapped, and are individually `try`-guarded, so they are the safe ones).

Net effect: **the app has good handlers for the two easy cases and no protection for the hard one.** A single throw on a background thread is an immediate process kill with a local-only log entry (**E5**), which again matches an “Uncategorized” failure bucket.

**Reproduction scenario**

Force a throw from a non-`Task` background thread (for example make the `PhpCgi` watchdog's `Process.GetProcessById` call see a pid that exits between the check and the access, yielding `ArgumentException`/`InvalidOperationException`). The process terminates immediately; only the local crash log has anything.

**Recommended fix**

Two parts, because the handler cannot be made to recover:

1. **Eliminate unguarded background threads.** Every long-running background loop becomes a `Task` with a top-level `try/catch` that logs and decides explicitly whether to retry or stop. Native callbacks (the tray window proc) get a full `try/catch` inside the managed callback so nothing propagates into native frames.
2. **Make the terminal handler useful.** Since it cannot prevent exit, use it to flush diagnostics synchronously and — importantly for the Store telemetry problem — write a breadcrumb that the *next* launch can detect and offer to report (**E5**).

**Suggested corrected code**

```csharp
private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
{
    // Cannot be handled on .NET 8 — the process WILL die. Make the last moments count.
    var ex = e.ExceptionObject as Exception;
    CrashLogger.Log(ex, $"DomainUnhandled(terminating={e.IsTerminating})");
    CrashLogger.WriteCrashBreadcrumb(ex);   // read on next launch → offer to send
    CrashLogger.Flush();
}
```

```csharp
/// <summary>Every background loop goes through here: no raw threads, nothing unguarded.</summary>
internal static Task RunGuarded(string name, Func<CancellationToken, Task> work, CancellationToken ct) =>
    Task.Run(async () =>
    {
        try { await work(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { CrashLogger.Log(ex, $"Background:{name}"); }   // never fatal
    }, ct);
```

```csharp
// TrayIcon.cs — nothing may escape a native callback
private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
{
    try { return HandleMessage(hwnd, msg, wParam, lParam); }
    catch (Exception ex)
    {
        CrashLogger.Log(ex, "TrayWndProc");
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
```

**Regression test**

```csharp
[Fact]
public async Task A_background_loop_failure_does_not_kill_the_process()
{
    var boom = new TaskCompletionSource();
    var t = BackgroundWork.RunGuarded("test", _ => throw new InvalidOperationException("boom"), default);
    await t;                                            // completes, does not propagate
    Assert.Contains("Background:test", File.ReadAllText(CrashLogger.LogPath));
}

[Fact]
public void No_raw_threads_are_created_outside_the_guard()
{
    foreach (var f in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
    {
        var src = File.ReadAllText(f);
        if (f.EndsWith("BackgroundWork.cs")) continue;
        Assert.DoesNotContain("new Thread(", src);
    }
}
```

---

### C10 — `TrayIcon` is double-`Dispose()`d on every tray quit; icon handle and window class leak

**Severity:** Medium
**Files:** `src/BanglaHost.App/Services/TrayIcon.cs`, `src/BanglaHost.App/MainWindow.xaml.cs:37-48, 220-233`

**Exact problematic code**

```csharp
// MainWindow.xaml.cs:220 — QuitApp disposes, then Exit() raises Closing, which disposes again
private void QuitApp()
{
    _reallyQuit = true;
    _tray.Dispose();
    Application.Current.Exit();
}

// MainWindow.xaml.cs:37 — Closing sees _reallyQuit == true and disposes a second time
AppWindow.Closing += (_, e) =>
{
    if (_reallyQuit || !Config.Load().MinimizeToTray) { _tray.Dispose(); return; }
    ...
};
```

**Why it is a problem**

Quitting from the tray menu disposes the tray icon **twice, always** — once explicitly in `QuitApp`, once from the `Closing` handler that `Application.Current.Exit()` triggers. `Dispose()` guards the `Shell_NotifyIcon(NIM_DELETE)` call with an `_added` flag, so the second pass is currently benign, but `DestroyWindow` is called again on a stale `_hwnd` that is never zeroed. Calling `DestroyWindow` on a destroyed handle returns an error today; if the handle value is recycled by another window in the process, it destroys the wrong window. `QuitForUpdate()` (line 229) adds a third disposal path.

Separately, resources are not fully released: the icon loaded at `TrayIcon.cs:61` is never `DestroyIcon`d, and the registered window class is never `UnregisterClass`d. Both leak per instance. Single-instance-per-process makes the leak small, but the update path (`QuitForUpdate`) and any future re-creation make it real.

`Config.Load()` inside the `Closing` handler is also a synchronous file read + JSON parse on the UI thread during shutdown (**D1**).

**Reproduction scenario**

Right-click the tray icon → Quit. Instrument `Dispose()`: it is entered twice. With a native handle-tracking tool, the `HICON` from `TrayIcon.cs:61` is still allocated at process exit.

**Recommended fix**

Make `Dispose` genuinely idempotent (latch, zero the handle, release every native resource), and remove the redundant call sites by giving quit a single owner.

**Suggested corrected code**

```csharp
public sealed class TrayIcon : IDisposable
{
    private bool _disposed;
    private IntPtr _hwnd, _hIcon;
    private bool _added;
    private static ushort _classAtom;

    public void Dispose()
    {
        if (_disposed) return;             // hard latch: every path after the first is a no-op
        _disposed = true;

        if (_added) { try { Shell_NotifyIcon(NIM_DELETE, ref _data); } catch { } _added = false; }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (_hwnd  != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd  = IntPtr.Zero; }   // zeroed
        if (_classAtom != 0) { UnregisterClass(new IntPtr(_classAtom), IntPtr.Zero); _classAtom = 0; }

        GC.KeepAlive(_wndProc);            // delegate must outlive the window
    }
}
```

```csharp
// MainWindow.xaml.cs — one owner for teardown
private void QuitApp()
{
    _reallyQuit = true;
    Application.Current.Exit();            // Closing disposes; no second call here
}

AppWindow.Closing += (_, e) =>
{
    var minimize = !_reallyQuit && _cachedConfig.MinimizeToTray;   // cached, no file I/O at shutdown
    if (!minimize) { _tray.Dispose(); return; }
    e.Cancel = true;
    AppWindow.Hide();
    ...
};
```

**Regression test**

```csharp
[Fact]
public void Dispose_is_idempotent_and_releases_everything()
{
    var tray = new TrayIcon("t", TestData.IconPath);
    var before = NativeHandles.Count();

    tray.Dispose();
    tray.Dispose();
    tray.Dispose();

    Assert.True(NativeHandles.Count() <= before, "native handles leaked");
    Assert.Equal(1, tray.TestOnly_DisposeBodyExecutions);
}

[Fact]
public void Quit_from_tray_disposes_exactly_once()
{
    var win = new MainWindowTestHarness();
    win.SimulateTrayQuit();
    Assert.Equal(1, win.Tray.TestOnly_DisposeBodyExecutions);
}
```

---

### C11 — `PhpCgi`’s watchdog can respawn itself recursively

**Severity:** Medium
**File:** `src/BanglaHost.Core/PhpCgi.cs` — the watchdog that restarts a dead `php-cgi.exe`

**Why it is a problem**

The watchdog observes that `php-cgi.exe` for a version is not running and calls `Start(version)` again. There is no failure counter, no backoff, and no circuit breaker. When the cause of death is **permanent** — a `php.ini` with a broken `extension=`/`zend_extension=` line (easy to produce via the Extensions pages or `Php.Ioncube`), a missing VC++ runtime, an AV quarantine of `php-cgi.exe`, or the port already bound — `php-cgi` exits immediately on every attempt and the watchdog restarts it immediately, forever.

The result is a hot loop of process creation: high CPU, log growth, and (because process creation is not free) pressure on the same thread pool implicated in **A1**. It also masks the real error: the user sees “PHP keeps restarting”, not “your `php.ini` references a DLL that does not exist”. If the watchdog runs on an unguarded background thread, any throw inside it is also fatal (**C9**).

**Reproduction scenario**

1. Add `extension=php_doesnotexist.dll` to a version's `php.ini` (or toggle an extension whose PECL download failed).
2. Start that PHP version.
3. `php-cgi.exe` appears and disappears repeatedly in Task Manager; CPU rises; the log fills with start messages. It never stops on its own.

**Recommended fix**

Bounded restarts with exponential backoff and a latched failure state that surfaces the child’s stderr to the user.

**Suggested corrected code**

```csharp
private sealed class RestartPolicy
{
    private int _consecutive;
    private DateTime _nextAttemptUtc = DateTime.MinValue;
    private const int MaxConsecutive = 5;

    public bool MayAttempt(DateTime nowUtc) => _consecutive < MaxConsecutive && nowUtc >= _nextAttemptUtc;
    public void OnSuccess() { _consecutive = 0; _nextAttemptUtc = DateTime.MinValue; }

    public void OnFailure(DateTime nowUtc)
    {
        _consecutive++;
        var backoff = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _consecutive)));   // 2,4,8,16,32,60
        _nextAttemptUtc = nowUtc + backoff;
    }

    public bool GaveUp => _consecutive >= MaxConsecutive;
}

private static void Watchdog(string version, CancellationToken ct)
{
    var policy = _policies.GetOrAdd(version, _ => new RestartPolicy());
    if (Running(version)) { policy.OnSuccess(); return; }

    var now = DateTime.UtcNow;
    if (policy.GaveUp) return;                     // latched: stop hammering
    if (!policy.MayAttempt(now)) return;           // backing off

    var (ok, why) = TryStartWithDiagnostics(version);
    if (ok) policy.OnSuccess();
    else
    {
        policy.OnFailure(now);
        if (policy.GaveUp)
            Log.Error($"php-cgi {version} failed to start {MaxConsecutive} times in a row — giving up. " +
                      $"Last error: {why}. Check php.ini for this version.");
    }
}
```

**Regression test**

```csharp
[Fact]
public void A_permanently_broken_php_is_retried_a_bounded_number_of_times()
{
    using var env = TestEnv.New();
    env.WritePhpIni("8.3", "extension=php_doesnotexist.dll");

    var starts = ProcessLaunchCounter.For("php-cgi.exe");
    PhpCgi.Start("8.3");
    Thread.Sleep(TimeSpan.FromSeconds(30));

    Assert.InRange(starts.Count, 1, 6);          // not hundreds
    Assert.Contains("giving up", env.LogText);
    Assert.Contains("php_doesnotexist.dll", env.LogText);   // the real cause is surfaced
}

[Fact]
public void A_transient_failure_resets_the_counter()
{
    using var env = TestEnv.New();
    var starts = ProcessLaunchCounter.For("php-cgi.exe");
    env.BreakPhp("8.3");
    PhpCgi.Start("8.3"); Thread.Sleep(5000);
    env.FixPhp("8.3");
    Thread.Sleep(10000);
    Assert.True(PhpCgi.Running("8.3"), "watchdog gave up permanently on a transient failure");
}
```

---

### C12 — `DbServer.Running()` disposes the `TcpClient` while the connect may still be pending

**Severity:** Medium
**File:** `src/BanglaHost.Core/DbServer.cs` — `DbServer.Running()`

**Exact problematic code**

```csharp
using var c = new TcpClient();
return c.ConnectAsync("127.0.0.1", port).Wait(600);
```

**Why it is a problem**

`Wait(600)` returns `false` on timeout but does **not** cancel the connect. The `using` then disposes the `TcpClient` while the asynchronous connect is still in flight, so the operation completes against a disposed socket. That surfaces as an `ObjectDisposedException` on the task — unobserved, so it reaches `TaskScheduler.UnobservedTaskException` at some arbitrary later GC, attributed to nothing in particular. It also leaves a socket in a half-open state until the OS tears it down, and each timed-out probe pins a pool thread for 600 ms (**A1**, **D4**).

**Recommended fix**

Cancel properly with a linked `CancellationTokenSource` and await — the `IsListeningAsync` helper in **A1**. Apply it to every probe in `DbServer`, `Apache.Running()`, `Nginx.Running()`, `Mailpit`, `CacheServers`, `SearchServers` and `AiServers`.

**Suggested corrected code** — see the `IsListeningAsync` helper in **A1**; call sites become:

```csharp
public static Task<bool> RunningAsync(CancellationToken ct = default) =>
    NetUtils.IsListeningAsync(Config.Load().MysqlPort, 600, ct);
```

**Regression test**

```csharp
[Fact]
public async Task A_timed_out_probe_produces_no_unobserved_exception()
{
    var unobserved = new List<Exception>();
    TaskScheduler.UnobservedTaskException += (_, e) => { unobserved.Add(e.Exception); e.SetObserved(); };

    for (var i = 0; i < 50; i++)
        Assert.False(await NetUtils.IsListeningAsync(59_999, 100, default));   // nothing listening

    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    await Task.Delay(500);
    Assert.Empty(unobserved);
}
```

---

### C13 — Cloudflare tunnel process is never disposed, never job-tracked, and waited on for 45 s

**Severity:** Medium
**File:** `src/BanglaHost.Core/Tunnel.cs`

**Exact problematic code**

The tunnel start path launches `cloudflared`, scrapes the quick-tunnel URL out of its output into a `TaskCompletionSource`, and waits up to **45 seconds**. The `Process` object is neither `Dispose`d nor passed to `JobManager.Add(p)` — unlike the eleven other spawn sites. PowerShell is launched by bare name (**B11**).

**Why it is a problem**

Three consequences:

* **Orphaned process.** Every other service is enrolled in the job object so it dies with the host. `cloudflared` is not, so if BanglaHost exits or crashes while a tunnel is up, `cloudflared` **keeps running** — and keeps a publicly reachable tunnel to the user's local machine open, with no UI anywhere to show or stop it. That is the most consequential orphan in the codebase, because it is the one that is exposed to the internet.
* **Handle leak.** An undisposed `Process` retains its handle (and the wait handle) until finalization.
* **45-second wait.** If the wait is reached from the UI thread or from an already-tight pool, that is a 45-second hang. Even on a background thread it is a very long unbounded-feeling operation with no visible cancel.

**Reproduction scenario**

1. Start a Cloudflare quick tunnel for a site; confirm the public URL works.
2. Kill `BanglaHost.App.exe` from Task Manager.
3. `cloudflared.exe` is still running; the public URL still resolves to the local site. Nothing in BanglaHost knows about it, and relaunching the app does not adopt or stop it.

**Recommended fix**

Enrol the process in the job object like every other service, dispose it, replace the blocking wait with an awaited `TaskCompletionSource` + timeout + cancellation, write a pid file so a relaunch can adopt or clean up a stray tunnel, and stop the tunnel explicitly on app shutdown.

**Suggested corrected code**

```csharp
public static async Task<string> StartAsync(string domain, IProgress<string>? log, CancellationToken ct)
{
    var psi = new ProcessStartInfo
    {
        FileName = Tools.CloudflaredExe() ?? throw new BhException("cloudflared is not installed"),
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
    };
    psi.ArgumentList.Add("tunnel"); psi.ArgumentList.Add("--url");
    psi.ArgumentList.Add($"http://127.0.0.1:{Config.Load().HttpPort}");

    var p = Process.Start(psi) ?? throw new BhException("could not start cloudflared");
    try
    {
        JobManager.Add(p);                                  // dies with the host, like every other service
        File.WriteAllText(PidFile, p.Id.ToString(CultureInfo.InvariantCulture));

        var url = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log?.Report(e.Data);
            var m = Regex.Match(e.Data, @"https://[a-z0-9-]+\.trycloudflare\.com");
            if (m.Success) url.TrySetResult(m.Value);
        };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(45));
        await using var reg = cts.Token.Register(() => url.TrySetCanceled());

        return await url.Task;                              // awaited, not .Wait(45000)
    }
    catch
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        p.Dispose();
        try { File.Delete(PidFile); } catch { }
        throw;
    }
}

/// <summary>Called at startup: a tunnel left behind by a crashed instance is a live public
/// route into the user's machine — never leave one running silently.</summary>
public static void CleanupStrayTunnel()
{
    if (!File.Exists(PidFile)) return;
    if (int.TryParse(File.ReadAllText(PidFile), out var pid))
    {
        try
        {
            using var stray = Process.GetProcessById(pid);
            if (stray.ProcessName.Equals("cloudflared", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"stopping a Cloudflare tunnel left running by a previous session (pid {pid})");
                stray.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException) { }        // already gone
    }
    try { File.Delete(PidFile); } catch { }
}
```

**Regression test**

```csharp
[Fact]
public void A_tunnel_does_not_outlive_the_host()
{
    var host = StartHostStub(JobManager.Mode.KillOnHostExit, startTunnel: true);
    Assert.True(Process.GetProcessesByName("cloudflared").Any());

    host.Kill();
    Thread.Sleep(2000);
    Assert.Empty(Process.GetProcessesByName("cloudflared"));
}

[Fact]
public void A_stray_tunnel_from_a_previous_session_is_cleaned_up_at_startup()
{
    var stray = StartFakeCloudflared();
    File.WriteAllText(Tunnel.PidFile, stray.Id.ToString());

    Tunnel.CleanupStrayTunnel();

    Assert.True(stray.HasExited);
    Assert.False(File.Exists(Tunnel.PidFile));
}

[Fact]
public async Task Tunnel_start_is_cancellable_and_does_not_block()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    var sw = Stopwatch.StartNew();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => Tunnel.StartAsync("site.test", null, cts.Token));
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    Assert.Empty(Process.GetProcessesByName("cloudflared"));
}
```

---

### C14 — A known `ProgressBar` access violation is worked around with `try {} catch {}` rather than fixed

**Severity:** Medium
**File:** `src/BanglaHost.App/Views/DashboardPage.xaml.cs:110-113`
**Class/Method:** `DashboardPage.Refresh()`

**Exact problematic code**

```csharp
var (mu, mt, mp) = SystemMetrics.Memory(); MemText.Text = $"{mu:0.0} / {mt:0.0} GB";
DispatcherQueue?.TryEnqueue(() => { try { MemBar.Value = mp; } catch { } }); // WinUI 3 ProgressBar can ACCESS_VIOLATE mid-layout
var (du, dt, dp) = SystemMetrics.Disk(); DiskText.Text = $"{du:0} / {dt:0} GB";
DispatcherQueue?.TryEnqueue(() => { try { DiskBar.Value = dp; } catch { } });
```

**Why it is a problem**

The comment is the author documenting a **crash they have actually observed**: setting `ProgressBar.Value` while the control is mid-layout faults inside the WinUI 3 native layer. The mitigation is wrong in two ways.

1. **An access violation is not a catchable .NET exception.** A genuine `ACCESS_VIOLATION` (`0xC0000005`) raised in native XAML code is a corrupted-state exception; on .NET Core it is **not** delivered to managed `catch` blocks at all — the runtime fails fast. So the `try { } catch { }` cannot catch the thing the comment says it is there to catch. If the observed failure was in fact a managed exception (`InvalidOperationException`, `COMException` / `E_FAIL` from the layout engine, or `ObjectDisposedException` after navigation), the catch swallows it, and the real bug — a metric silently not updating — is now invisible.
2. **The double-dispatch makes the race more likely, not less.** `Refresh()` already runs on the UI thread, so `TryEnqueue` here does not marshal anything; it **defers** the assignment to a *later* dispatcher turn. That decouples the assignment from the frame in which the surrounding text updates happen, so it can land at an arbitrary point relative to layout — including after the page has been navigated away from and its tree torn down. Combined with **A1** (overlapping `Refresh()` calls every 2 s), several deferred assignments can be queued against the same control from different in-flight refreshes.

Given the shipped crash telemetry is `Uncategorized` with no stacks, a fail-fast native AV inside XAML layout is a plausible contributor to the 4.41% crash rate — plausible, not proven, because I have no minidumps. **Requires runtime verification** (the confirmation would be a `0xC0000005` faulting in `Microsoft.UI.Xaml.dll` in a crash dump).

**Reproduction scenario**

Sit on the Dashboard on a machine where `Engine.Api()` is slow (AV filtering loopback, so probes take their full 400–600 ms), so refreshes overlap. Navigate away from the Dashboard at the moment a refresh is in flight. The queued `MemBar.Value = mp` runs against a control whose page is being torn down. Today the outcome is either a swallowed managed exception or a process-level fault, and nothing is logged either way.

**Recommended fix**

Assign directly on the UI thread inside the same frame as the other updates (no `TryEnqueue`), guard on the page still being live rather than on catching a fault, and if the assignment really can throw, log it instead of swallowing it so the true exception type becomes known. Fixing **A1**'s re-entrancy removes the overlapping-writes half of the race.

**Suggested corrected code**

```csharp
// Refresh() already runs on the UI thread — assign inline, in one frame, no deferral.
private void SetBar(ProgressBar bar, double value)
{
    // A torn-down page is the real hazard; check for it instead of catching a fault.
    if (XamlRoot is null || !IsLoaded) return;
    try
    {
        bar.Value = Math.Clamp(value, bar.Minimum, bar.Maximum);
    }
    catch (Exception ex)
    {
        // Do not swallow: we need the real type to fix this properly.
        CrashLogger.Log(ex, $"ProgressBar.Value({bar.Name})");
    }
}
```

```csharp
var (mu, mt, mp) = SystemMetrics.Memory();
MemText.Text = $"{mu:0.0} / {mt:0.0} GB";
SetBar(MemBar, mp);

var (du, dt, dp) = SystemMetrics.Disk();
DiskText.Text = $"{du:0} / {dt:0} GB";
SetBar(DiskBar, dp);
```

If the fault survives that, the correct fix is to stop mutating `Value` during a layout pass at all — bind the bars to a view-model property and let the framework schedule the update:

```xml
<ProgressBar x:Name="MemBar" Value="{x:Bind Vm.MemoryPercent, Mode=OneWay}" Maximum="100" />
```

**Regression test**

```csharp
[Fact]
public async Task Metric_bars_update_without_faulting_under_rapid_navigation()
{
    await UiTest.RunAsync(async () =>
    {
        for (var i = 0; i < 40; i++)
        {
            UiTest.Frame.Navigate(typeof(DashboardPage));
            await Task.Delay(60);                     // interrupt mid-refresh on purpose
            UiTest.Frame.Navigate(typeof(ServicesPage));
        }
        await Task.Delay(1000);
    });

    // The old code could only ever be "silent"; now a real failure is recorded.
    Assert.DoesNotContain("ProgressBar.Value", File.ReadAllText(CrashLogger.LogPath));
}

[Fact]
public async Task Bar_assignment_happens_in_the_same_dispatcher_turn_as_the_text()
{
    await UiTest.RunAsync(async () =>
    {
        var page = new DashboardPage();
        await UiTest.ShowAsync(page);
        var turns = UiTest.CountDispatcherTurns(() => page.TestOnly_ApplyMetrics(50, 60));
        Assert.Equal(1, turns);                       // no TryEnqueue deferral
    });
}
```

---

## D. Performance issues

The dashboard's 2-second refresh cycle is where every item in this section is paid, repeatedly, for as long as the app is open. **A1** describes the resulting hang; this section itemises the cost. **D7** is the one that converts all of it into a user-visible stall, so treat it as the highest-value entry here despite being a two-line change.

---

### D1 — `Config.Load()` re-reads and re-deserializes `config.json` on every single call

**Severity:** High
**File:** `src/BanglaHost.Core/Config.cs:38-54`
**Class/Method:** `Config.Load()`

**Exact problematic code**

```csharp
public static Config Load()
{
    try
    {
        var path = Paths.ConfigJson;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<Config>(json);
            if (cfg is not null) return cfg;
        }
    }
    catch { /* fall through to defaults */ }
    return new Config();
}
```

**Why it is a problem**

There is no cache and no memoization: every call is a `File.Exists` + `File.ReadAllText` + `JsonSerializer.Deserialize`. The method is called from everywhere — the hot paths include `Engine.Api()` (multiple times per snapshot), `DbServer.Running()`, `Nginx`/`Apache` port lookups, `SslService`, `Hosts`, and the UI: `MainWindow`'s `AppWindow.Closing` handler (`MainWindow.xaml.cs:39`), `CheckForUpdateOnLaunch` (`:160`), and `DashboardPage.Refresh()` (`DashboardPage.xaml.cs:169`).

Two costs compound. **Synchronous file I/O**: each call is a real disk round-trip (satisfied from cache usually, but still a syscall pair, and a cold or contended disk makes it much worse). **Repetition**: a single `Engine.Api()` snapshot performs it many times, so a 2-second timer means a continuous drip of file reads for the lifetime of the process.

Two of those call sites are on the **UI thread** — `MainWindow.xaml.cs:39` runs it during window close, i.e. during shutdown when the disk is often busy, and `DashboardPage.xaml.cs:169` runs it inside `Refresh()`.

It also makes **A5** worse in an interesting way: because `Load()` is fail-soft, a transient read failure (an antivirus scanner holding the file, or a mid-write torn read from the non-atomic `Save()`) silently returns `new Config()` — full defaults. Any caller that then persists gets those defaults written back. Caching turns that from a per-call lottery into a single load whose failure can be detected and reported.

**Reproduction scenario**

Attach Process Monitor filtered to `config.json`, open the app on the Dashboard, and leave it idle for one minute. You will see a continuous stream of `CreateFile`/`ReadFile`/`CloseFile` triples — dozens per 2-second tick — with no user interaction at all.

**Recommended fix**

Load once, cache behind a lock, invalidate on save and on an external-change watcher. Keep `Config` immutable-by-convention so handing out the cached instance is safe, and surface a *failed* load rather than silently substituting defaults (see **A5**).

**Suggested corrected code**

```csharp
public sealed class Config
{
    private static readonly object _gate = new();
    private static Config? _cache;
    private static FileSystemWatcher? _watcher;

    /// <summary>Cached. The file is read at most once per change; every hot path can call this freely.</summary>
    public static Config Load()
    {
        var c = Volatile.Read(ref _cache);
        if (c is not null) return c;

        lock (_gate)
        {
            if (_cache is not null) return _cache;
            _cache = ReadFromDisk();
            StartWatcher();
            return _cache;
        }
    }

    private static Config ReadFromDisk()
    {
        var path = Paths.ConfigJson;
        if (!File.Exists(path)) return new Config();

        // Do not fail soft: a corrupt/locked file must not masquerade as "user chose defaults" (A5).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
                if (cfg is not null) return cfg;
                break;                                     // valid JSON "null" — genuinely empty
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);                          // AV/torn-read: retry before giving up
            }
            catch (JsonException ex)
            {
                Log.Error($"config.json is corrupt: {ex.Message}. Using the last good copy if one exists.");
                var backup = path + ".bak";
                if (File.Exists(backup))
                {
                    try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(backup)) ?? new Config(); }
                    catch { }
                }
                break;
            }
        }
        return new Config();
    }

    private static void StartWatcher()
    {
        if (_watcher is not null) return;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(Paths.ConfigJson)!, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Invalidate();
            _watcher.Created += (_, _) => Invalidate();
            _watcher.Renamed += (_, _) => Invalidate();
        }
        catch { /* watcher is an optimisation, not a requirement */ }
    }

    public static void Invalidate() { lock (_gate) _cache = null; }
}
```

Every `Save()` (see the atomic-write fix in **A5**) ends with `Invalidate()` — or better, sets `_cache` to the instance just written.

**Regression test**

```csharp
[Fact]
public void Load_reads_the_file_once_regardless_of_call_count()
{
    using var env = TestEnv.New();
    env.WriteConfig(new Config { MysqlPort = 3307 });
    var io = FileIoCounter.For(Paths.ConfigJson);

    for (var i = 0; i < 1000; i++) Assert.Equal(3307, Config.Load().MysqlPort);

    Assert.InRange(io.ReadCount, 1, 2);
}

[Fact]
public void Save_makes_the_new_value_visible_immediately()
{
    using var env = TestEnv.New();
    var cfg = Config.Load();
    cfg.MysqlPort = 3399;
    cfg.Save();
    Assert.Equal(3399, Config.Load().MysqlPort);      // cache invalidated, not stale
}

[Fact]
public void A_locked_file_is_retried_and_does_not_silently_become_defaults()
{
    using var env = TestEnv.New();
    env.WriteConfig(new Config { MysqlPort = 3307 });
    using (env.HoldExclusiveLock(Paths.ConfigJson, TimeSpan.FromMilliseconds(80)))
        Assert.Equal(3307, Config.Load().MysqlPort);   // retry wins instead of returning defaults
}
```

---

### D2 — `Services.Enabled()` re-reads the enabled-services file once per service, 37 times per snapshot

**Severity:** Medium
**File:** `src/BanglaHost.Core/Services.cs`
**Class/Method:** `Services.Enabled(string key)`, called from `Engine.Api()`

**Why it is a problem**

`Services.All` has **37 entries**. `Engine.Api()` iterates all of them and, for each, asks whether that service is enabled — and each `Enabled()` call opens and reads the same on-disk list from scratch. So one snapshot performs 37 reads of one small file to answer 37 questions that a single read could answer. Multiply by the Dashboard's 2-second timer: ~1,100 redundant file reads per minute while idle. On top of the `Config.Load()` calls from **D1**, the app performs continuous pointless disk I/O for its whole lifetime.

This is the same shape as D1 and shares its fix, but it is worth listing separately because the multiplier (×37) is what makes `Api()` slow enough for **A1**'s overlapping refreshes to become likely.

**Reproduction scenario**

Process Monitor filtered to the enabled-services file, app idle on the Dashboard: a burst of ~37 reads every 2 seconds.

**Recommended fix**

Read the set once per snapshot and pass it down; cache it with the same watcher-invalidated pattern as **D1**.

**Suggested corrected code**

```csharp
/// <summary>The whole enabled set, read once. Cached and invalidated on write.</summary>
public static IReadOnlySet<string> EnabledSet()
{
    var s = Volatile.Read(ref _enabledCache);
    if (s is not null) return s;

    lock (_gate)
    {
        if (_enabledCache is not null) return _enabledCache;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(Paths.EnabledFile))
                foreach (var line in File.ReadAllLines(Paths.EnabledFile))
                    if (line.Trim() is { Length: > 0 } k) set.Add(k);
        }
        catch (IOException ex) { Log.Warn($"could not read enabled services: {ex.Message}"); }
        return _enabledCache = set;
    }
}

public static bool Enabled(string key) => EnabledSet().Contains(key);   // no I/O
```

```csharp
// Engine.Api() — one read for the whole snapshot
var enabled = Services.EnabledSet();
foreach (var svc in Services.All)
    rows.Add(new ServiceRow { Key = svc.Key, Enabled = enabled.Contains(svc.Key), /* … */ });
```

**Regression test**

```csharp
[Fact]
public void One_snapshot_reads_the_enabled_file_at_most_once()
{
    using var env = TestEnv.New();
    var io = FileIoCounter.For(Paths.EnabledFile);
    _ = Engine.Api();
    Assert.InRange(io.ReadCount, 0, 1);      // was 37
}
```

---

### D3 — `DbServer.ActiveEngine()` performs its liveness probes twice per snapshot

**Severity:** Low
**Files:** `src/BanglaHost.Core/DbServer.cs`, `src/BanglaHost.Core/Engine.cs` (`Api()`)

**Why it is a problem**

`ActiveEngine()` determines which database engine is up by probing the MySQL/MariaDB and PostgreSQL ports. `Engine.Api()` calls it twice while building one snapshot, so those TCP probes run twice. Each probe is a blocking `Wait(600)` (**C12**), so in the worst case — nothing listening and loopback filtered by AV — this is an extra ~1.2 s of a pinned pool thread per snapshot, every 2 seconds.

Low severity on its own; it is listed because it is a free win and because it is one of the multipliers behind **A1**.

**Recommended fix**

Compute it once per snapshot and reuse the value.

**Suggested corrected code**

```csharp
// Engine.Api()
var active = await DbServer.ActiveEngineAsync(ct);   // once
// … use `active` for both the engine label and the row's Running state
```

**Regression test**

```csharp
[Fact]
public async Task Api_probes_each_database_port_once()
{
    using var env = TestEnv.New();
    var probes = TcpProbeCounter.Start();
    _ = await Engine.ApiAsync(default);
    Assert.InRange(probes.CountFor(env.MysqlPort), 0, 1);
    Assert.InRange(probes.CountFor(env.PostgresPort), 0, 1);
}
```

---

### D4 — `Engine.Api()`'s liveness probes run sequentially

**Severity:** Medium
**File:** `src/BanglaHost.Core/Engine.cs`
**Class/Method:** `Engine.Api()`

**Why it is a problem**

`Api()` walks `Services.All` and probes each service in turn. The probes are independent network checks against loopback, so they are the textbook case for concurrency, yet they are serialised — total latency is the **sum** of every probe rather than the maximum.

The normal case is fast: an unfiltered loopback port with nothing listening refuses instantly, so most probes cost microseconds and the sum is small. The bad case is what matters. When a security product filters loopback — which this project's own `ANTIVIRUS.md` documents as a real condition for its users — connects do not refuse, they *hang until timeout*. With ~10 daemon ports at 400–600 ms each, one snapshot takes **4–6 seconds**. The Dashboard asks for a new one every **2 seconds** and does not guard re-entrancy (**A1**), so snapshots pile up three deep, each holding pool threads, and the pool cannot grow fast enough (**D7**). That is the hang.

**Reproduction scenario**

Install an AV that filters loopback (or add a firewall rule that DROPs — not REJECTs — outbound loopback to the daemon ports), stop all services, and sit on the Dashboard. Snapshot latency goes to seconds; the UI becomes unresponsive within a minute.

**Recommended fix**

Run the probes concurrently with a single overall budget, and make the whole of `Api()` async and cancellable so a superseded snapshot stops instead of finishing.

**Suggested corrected code**

```csharp
public static async Task<Snapshot> ApiAsync(CancellationToken ct)
{
    var cfg      = Config.Load();                 // cached (D1)
    var enabled  = Services.EnabledSet();         // one read (D2)

    // One budget for the whole probe fan-out, not per probe.
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
    budget.CancelAfter(TimeSpan.FromMilliseconds(1200));

    var targets = Services.All.Where(s => s.Port > 0).ToArray();
    var probes  = await Task.WhenAll(targets.Select(async s =>
        (s.Key, Running: await NetUtils.IsListeningAsync(s.Port, 600, budget.Token))));

    var running = probes.ToDictionary(p => p.Key, p => p.Running, StringComparer.OrdinalIgnoreCase);

    var rows = Services.All.Select(s => new ServiceRow
    {
        Key       = s.Key,
        Installed = Tools.IsInstalled(s.Key),
        Enabled   = enabled.Contains(s.Key),
        Running   = running.TryGetValue(s.Key, out var r) && r,
    }).ToList();

    return new Snapshot { Services = rows, Sites = await SitesAsync(ct) };
}
```

`IsListeningAsync` is the cancellable probe from **A1**; with `CancelAfter` on the shared source, a filtered-loopback machine pays ~1.2 s **once** for all probes instead of 600 ms each.

**Regression test**

```csharp
[Fact]
public async Task All_probes_share_one_time_budget()
{
    using var env = TestEnv.New();
    env.BlackholeLoopbackPorts(Services.All.Where(s => s.Port > 0).Select(s => s.Port));   // never refuse

    var sw = Stopwatch.StartNew();
    _ = await Engine.ApiAsync(default);
    sw.Stop();

    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"sequential probes: {sw.Elapsed}");
}

[Fact]
public async Task A_cancelled_snapshot_abandons_its_probes_promptly()
{
    using var env = TestEnv.New();
    env.BlackholeLoopbackPorts(new[] { env.MysqlPort });
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    var sw = Stopwatch.StartNew();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine.ApiAsync(cts.Token));
    Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(600));
}
```

---

### D5 — `Paths.CleanTmp()` exists and is never called; temp files grow without bound

**Severity:** Medium
**File:** `src/BanglaHost.Core/Paths.cs`
**Class/Method:** `Paths.CleanTmp()`

**Why it is a problem**

`CleanTmp()` is implemented and has **zero call sites** in the entire solution — only `Paths.EnsureSkeleton()` is invoked (from `Engine.cs:32`). Meanwhile the temp directory is written to by every download (installer archives for PHP, nginx, MariaDB, Redis, Node, Composer, phpMyAdmin, Adminer, Mailpit, cloudflared, mkcert…), by extraction staging, and by backup staging.

Consequence: the working directory accumulates multi-hundred-megabyte archives indefinitely. A user who installs several PHP versions and a couple of database engines can easily leave a gigabyte or more of dead archives on disk, in a location no uninstaller touches — MSIX uninstall does not remove `%LOCALAPPDATA%`-style external data directories, so the space is never reclaimed even after removing the app. The dead code also reads as intent: cleanup was designed and then never wired up.

**Reproduction scenario**

Fresh install. Install nginx, PHP 8.1/8.2/8.3, MariaDB, Redis and Node from the Services page. Inspect the temp directory: every downloaded archive is still there. Uninstall the app; the directory remains.

**Recommended fix**

Call it at startup (off the UI thread) and after each successful install, with an age/size policy, and make it resilient to files still locked by a running process.

**Suggested corrected code**

```csharp
/// <summary>Delete staging files older than <paramref name="maxAge"/>, oldest-first, until the
/// directory is under <paramref name="maxBytes"/>. Never throws: cleanup must not break a build.</summary>
public static void CleanTmp(TimeSpan? maxAge = null, long maxBytes = 512L * 1024 * 1024)
{
    var age = maxAge ?? TimeSpan.FromDays(2);
    try
    {
        var dir = new DirectoryInfo(Tmp);
        if (!dir.Exists) return;

        var files = dir.GetFiles("*", SearchOption.AllDirectories)
                       .OrderBy(f => f.LastWriteTimeUtc)
                       .ToList();
        var cutoff = DateTime.UtcNow - age;
        var total  = files.Sum(f => f.Length);

        foreach (var f in files)
        {
            var stale = f.LastWriteTimeUtc < cutoff;
            var over  = total > maxBytes;
            if (!stale && !over) break;                  // ordered oldest-first: nothing later qualifies
            try { var n = f.Length; f.Delete(); total -= n; }
            catch (IOException) { }                      // still in use — try again next launch
            catch (UnauthorizedAccessException) { }
        }
    }
    catch (Exception ex) { Log.Warn($"temp cleanup skipped: {ex.Message}"); }
}
```

```csharp
// Engine.cs — next to EnsureSkeleton(), but never on the UI thread
Paths.EnsureSkeleton();
_ = Task.Run(() => Paths.CleanTmp());          // fire-and-forget, guarded internally
```

```csharp
// Downloader — delete the archive as soon as extraction succeeds
try   { ExtractZip(archive, target); }
finally { try { File.Delete(archive); } catch { } }
```

**Regression test**

```csharp
[Fact]
public void CleanTmp_removes_stale_files_and_respects_the_size_cap()
{
    using var env = TestEnv.New();
    var old   = env.WriteTmpFile("old.zip", 10_000, age: TimeSpan.FromDays(5));
    var fresh = env.WriteTmpFile("fresh.zip", 10_000, age: TimeSpan.FromMinutes(1));

    Paths.CleanTmp(TimeSpan.FromDays(2), maxBytes: 1_000_000);

    Assert.False(File.Exists(old));
    Assert.True(File.Exists(fresh));
}

[Fact]
public void CleanTmp_tolerates_a_locked_file()
{
    using var env = TestEnv.New();
    var locked = env.WriteTmpFile("busy.zip", 10_000, age: TimeSpan.FromDays(9));
    using var hold = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None);
    Paths.CleanTmp(TimeSpan.FromDays(2));      // must not throw
    Assert.True(File.Exists(locked));
}

[Fact]
public void Startup_invokes_temp_cleanup()
{
    var src = File.ReadAllText("src/BanglaHost.Core/Engine.cs");
    Assert.Contains("CleanTmp", src);           // the regression that created this finding
}
```

---

### D6 — `X509Certificate2` instances are never disposed in `GetLocalCertificates()`

**Severity:** Low
**File:** `src/BanglaHost.Core/SslService.cs`
**Class/Method:** `SslService.GetLocalCertificates()`

**Why it is a problem**

`X509Certificate2` wraps an unmanaged `CERT_CONTEXT` (and, for a key-bearing cert, potentially a CNG/CAPI key handle). Instances are created per certificate and never disposed, so the native contexts are released only at finalization — nondeterministically, and only if a GC happens. `SslPage.LoadCerts()` (`SslPage.xaml.cs:32`) calls this on **every navigation to the page** and again after every generate/delete, so the leak repeats. On .NET 8 the finalizer does eventually free the context, which keeps this Low, but a temporary key-handle leak can also keep a key file locked, which is the sort of thing that makes a later delete or overwrite fail for no visible reason.

The same method is also called synchronously on the UI thread (**C1**), and it enumerates the certificate store — I/O plus CryptoAPI work in a click handler.

**Recommended fix**

Project each certificate into a plain display model inside a `using`, so no `X509Certificate2` escapes the method, and make the whole thing async.

**Suggested corrected code**

```csharp
public sealed record CertInfo(string Subject, string Domain, DateTime NotAfter, string Thumbprint, string Path);

public static Task<IReadOnlyList<CertInfo>> GetLocalCertificatesAsync(CancellationToken ct = default) =>
    Task.Run<IReadOnlyList<CertInfo>>(() =>
    {
        var list = new List<CertInfo>();
        foreach (var pem in Directory.EnumerateFiles(Paths.Certs, "*.pem"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var cert = X509CertificateLoader.LoadCertificateFromFile(pem);   // disposed here
                list.Add(new CertInfo(cert.Subject, DomainOf(pem), cert.NotAfter, cert.Thumbprint, pem));
            }
            catch (CryptographicException ex) { Log.Warn($"skipping unreadable certificate {pem}: {ex.Message}"); }
        }
        return list;
    }, ct);
```

```csharp
// SslPage.xaml.cs — off the UI thread (C1) and no undisposed natives
private async Task LoadCertsAsync()
{
    try { SslList.ItemsSource = await SslService.GetLocalCertificatesAsync(); }
    catch (Exception ex) { CrashLogger.Log(ex, "LoadCerts"); }
}
```

**Regression test**

```csharp
[Fact]
public async Task Listing_certificates_repeatedly_does_not_accumulate_native_handles()
{
    using var env = TestEnv.New();
    env.WriteTestCerts(20);

    _ = await SslService.GetLocalCertificatesAsync();
    GC.Collect(); GC.WaitForPendingFinalizers();
    var before = NativeHandles.Count();

    for (var i = 0; i < 50; i++) _ = await SslService.GetLocalCertificatesAsync();

    var after = NativeHandles.Count();          // no GC in between: undisposed certs would show here
    Assert.True(after - before < 50, $"handle growth {after - before} suggests undisposed certificates");
}
```

---

### D7 — No `ThreadPool.SetMinThreads` anywhere: the pool cannot absorb the app's blocking burst

**Severity:** High
**Files:** `src/BanglaHost.App/App.xaml.cs` (no configuration), with `src/BanglaHost.Core/DbServer.cs`, `Apache.cs`, `Nginx.cs`, `Engine.cs`

**Exact problematic code** — the absence of any pool configuration, combined with the blocking pattern it has to absorb:

```csharp
// The pattern, repeated across every service probe:
using var c = new TcpClient();
return c.ConnectAsync("127.0.0.1", port).Wait(600);     // blocks the calling pool thread…
                                                        // …while ALSO needing a pool thread to complete
```

**Why it is a problem**

This is the amplifier that turns the rest of Section C and D into the 8.82% hang rate.

The .NET thread pool starts with `MinThreads == ProcessorCount` and injects additional threads only at roughly **one or two per second** once it is saturated. The app's steady-state workload is the opposite of what that heuristic assumes: a burst of *blocking* waits, each of which occupies a pool thread while doing no work, and several of which need a *second* pool thread to run the I/O completion that would let the first one finish.

Put the pieces together. `EngineHost.Snapshot()` is `Task.Run(() => Engine.Api())`, so it starts on a pool thread. `Api()` then performs ~10 sequential `Wait(600)` probes (**D4**), plus two more from `ActiveEngine()` (**D3**), each pinning a thread. Alongside it, `Php.Run` calls `p.StandardError.ReadToEndAsync()` and then blocks on `.Result` (`Php.cs:20`), `Downloader.CurlTo` blocks entirely (**C4**), `Php.Ioncube` calls `.GetAwaiter().GetResult()` (`Php.cs:83`), and `DbServer.Stop()` runs a whole synchronous backup via `.GetAwaiter().GetResult()` (**C3**). Every one of those is a thread parked on work that itself needs the pool.

On a 4-core machine that is 4 threads to start with. Once they are all parked, the queue holds: the UI thread's `await` continuations, every `DispatcherQueue.TryEnqueue` callback, `Task.Delay` resumptions (including `Localizer.RewalkAfterAsync` and `FirstRunThenUpdateCheck`'s polling loop), and `HttpClient` completions. The pool adds one thread per second. Meanwhile the Dashboard timer keeps firing every 2 seconds and adding another whole snapshot's worth of blocking work (**A1**).

The user-visible result: `await EngineHost.Instance.Snapshot()` never resumes, so `Refresh()` never returns, so the message loop never gets control back — Windows marks the window unresponsive and the Store records an **AppHang**. It needs no unusual configuration, which is consistent with 48 hits across 28 devices.

`SetMinThreads` is not the fix — the fix is removing the blocking (**A1**, **C2**, **C4**, **C5**, **D4**). But it is the correct *immediate mitigation*, it is two lines, and it is the single highest ratio of hang-reduction to risk in this entire report: it lets the pool absorb the burst instantly instead of drip-feeding threads while the UI is frozen.

**Reproduction scenario**

1. On a 4-core machine (or set `DOTNET_PROCESSOR_COUNT=2`), make loopback connects hang rather than refuse (AV that filters loopback, or a DROP firewall rule for the daemon ports).
2. Stop all services and sit on the Dashboard.
3. Within a minute the window stops repainting. `dotnet-counters monitor --counters System.Runtime` shows `threadpool-queue-length` climbing and `threadpool-thread-count` rising at ~1/s. Dump the process: most pool threads are in `SocketAsyncEngine`/`WaitHandle` waits from `TcpClient.ConnectAsync(...).Wait`.

**Recommended fix**

Set a floor at startup — before any background work is queued — and pair it with a counters-based assertion in CI so a regression in blocking behaviour shows up as queue growth rather than as Store telemetry.

**Suggested corrected code**

```csharp
public App()
{
    UnhandledException += OnAppUnhandled;                 // C8: handlers first
    AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
    TaskScheduler.UnobservedTaskException += OnUnobservedTask;

    // BanglaHost's steady state is a burst of blocking loopback probes and process waits.
    // The pool's default 1-2 threads/sec injection cannot absorb that, and the symptom is the
    // UI thread's continuations never running -> AppHang. This is a mitigation, not a licence
    // to keep blocking: see A1/C2/C4/C5/D4 for the real fixes.
    ThreadPool.GetMinThreads(out var workers, out var io);
    var floor = Math.Max(Environment.ProcessorCount * 4, 16);
    ThreadPool.SetMinThreads(Math.Max(workers, floor), Math.Max(io, floor));

    try { ApplySavedLanguage(); } catch (Exception ex) { CrashLogger.Log(ex, "ApplySavedLanguage"); }
    InitializeComponent();
}
```

The same two lines belong in `BanglaHost.Cli`'s entry point, which drives the same Core code paths.

**Regression test**

```csharp
[Fact]
public void The_pool_floor_is_raised_at_startup()
{
    ThreadPool.GetMinThreads(out var workers, out var io);
    var expected = Math.Max(Environment.ProcessorCount * 4, 16);
    Assert.True(workers >= expected, $"worker floor {workers} < {expected}");
    Assert.True(io >= expected, $"IO floor {io} < {expected}");
}

/// <summary>The real guard: the UI thread must keep pumping while snapshots are slow.</summary>
[Fact]
public async Task The_ui_keeps_pumping_while_every_probe_times_out()
{
    using var env = TestEnv.New();
    env.BlackholeLoopbackPorts(Services.All.Where(s => s.Port > 0).Select(s => s.Port));

    await UiTest.RunAsync(async () =>
    {
        var pump = UiTest.StartPumpWatchdog();
        UiTest.Frame.Navigate(typeof(DashboardPage));
        await Task.Delay(TimeSpan.FromSeconds(60));       // 30 timer ticks
        Assert.True(pump.LongestStall < TimeSpan.FromSeconds(2), $"UI stalled {pump.LongestStall}");
    });
}

[Fact]
public async Task Threadpool_queue_does_not_grow_without_bound_during_steady_state()
{
    using var env = TestEnv.New();
    env.BlackholeLoopbackPorts(Services.All.Where(s => s.Port > 0).Select(s => s.Port));
    using var counters = DotnetCounters.Start("System.Runtime");

    await UiTest.RunAsync(() => { UiTest.Frame.Navigate(typeof(DashboardPage)); return Task.Delay(60_000); });

    Assert.True(counters.Max("threadpool-queue-length") < 50,
        $"queue peaked at {counters.Max("threadpool-queue-length")}");
}
```

## E. Architecture issues

These are the structural reasons the same bug keeps recurring in different files. Fixing the individual findings above without addressing **E2** and **E3** will work, and then the next page someone adds will reintroduce them.

---

### E1 — Two parallel PHP-extensions pages with divergent behaviour

**Severity:** Medium
**Files:** `src/BanglaHost.App/Views/ExtensionsPage.xaml.cs`, `src/BanglaHost.App/Views/PhpExtensionsPage.xaml.cs`
**Also:** `src/BanglaHost.App/MainWindow.xaml.cs:340` (`"php_ext"` → `PhpExtensionsPage`), `:368` (`"extensions"` → `ExtensionsPage`)

**Why it is a problem**

Both pages are reachable from the sidebar, both list and toggle PHP extensions, and both call into `Php`. They are separate implementations: `ExtensionsPage.xaml.cs:34` and `PhpExtensionsPage.xaml.cs:88` each call `Php.ListExtensions` on the **UI thread** (**C1**) with different surrounding logic, different error handling, and different refresh behaviour. There is no shared view-model.

The cost is not aesthetic. Every fix in this area must be applied twice, and a fix applied once produces a product where the same operation succeeds on one page and fails on the other — the hardest class of bug to diagnose from a user report, because "I enabled the extension" is now ambiguous. It also doubles the surface for **C1**: two pages that block the dispatcher rather than one.

**Recommended fix**

Pick one page, delete the other, and remove its sidebar entry. Extract the shared logic into a view-model that both the remaining page and any future caller use.

**Suggested corrected code**

```csharp
/// <summary>Single owner of extension state. Async, cancellable, no UI types.</summary>
public sealed class PhpExtensionsViewModel
{
    public ObservableCollection<ExtensionRow> Rows { get; } = new();

    public async Task LoadAsync(string version, CancellationToken ct)
    {
        var rows = await Task.Run(() => Php.ListExtensions(version), ct);   // off the dispatcher
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
    }

    public Task<bool> SetAsync(string version, string ext, bool on, CancellationToken ct) =>
        Php.SetExtensionAsync(version, ext, on, ct);                        // already async in Core
}
```

```csharp
// MainWindow.xaml.cs — one route, one page
"php_ext"    => typeof(PhpExtensionsPage),
// "extensions" entry and ExtensionsPage.xaml(.cs) deleted
```

**Regression test**

```csharp
[Fact]
public void There_is_exactly_one_extensions_page()
{
    var views = Directory.GetFiles("src/BanglaHost.App/Views", "*ExtensionsPage.xaml.cs");
    Assert.Single(views);

    var nav = File.ReadAllText("src/BanglaHost.App/MainWindow.xaml.cs");
    Assert.DoesNotContain("typeof(ExtensionsPage)", nav);
}
```

---

### E2 — `EngineHost`'s "call Core off the UI thread" contract is unenforceable and routinely bypassed

**Severity:** High
**Files:** `src/BanglaHost.App/Services/EngineHost.cs:52, 60, 72, 94, 107`; bypassed in the 9 pages listed in **C1**

**Exact problematic code**

```csharp
public Task Run(Action action)                        => Task.Run(action);            // :52
public Task<(bool ok, string output)> RunCaptured(Action action) => Task.Run(...);     // :60
public Task<Snapshot> Snapshot()                      => Task.Run(() => Engine.Api()); // :72
```

alongside, in nine different pages:

```csharp
SslList.ItemsSource = SslService.GetLocalCertificates();     // SslPage.xaml.cs:32 — direct, on the UI thread
var exts = Php.ListExtensions(version);                      // ExtensionsPage.xaml.cs:34
```

**Why it is a problem**

`EngineHost` is the right idea: a single façade that pushes Core's synchronous, process-spawning, file-reading, socket-probing work onto a background thread. But it is **advisory**. `Engine`, `Php`, `SslService`, `DbServer`, `Tools` and `Database` are all `public static`, so any page can call them directly — and nine pages do. Nothing in the type system, and nothing in the build, distinguishes "went through the façade" from "blocked the dispatcher".

That is why **C1** is a list of nine pages rather than one bug: the architecture makes the wrong thing the *easier* thing to write. `SslService.GetLocalCertificates()` looks like a cheap property read at the call site. Only by reading the implementation do you learn it enumerates a directory and parses every certificate in it. Autocomplete offers the blocking call and the safe one side by side, with no signal about which is which.

Two further problems with the façade itself. `Snapshot()` returns `Task<Snapshot>` with **no `CancellationToken`**, so a superseded snapshot cannot be abandoned — which is exactly what **A1** needs. And `Run`/`RunCaptured` take `Action`, so they can only wrap synchronous work: the façade *institutionalises* the synchronous Core API rather than providing a path away from it (**E3**).

**Reproduction scenario**

Static: grep any page for `SslService.`, `Php.`, `Tools.` or `Engine.` outside an `EngineHost`/`Task.Run` call — nine hits, none of which produce a build warning. Dynamic: navigate to the SSL page on a machine with many certificates and observe the navigation stall.

**Recommended fix**

Make the contract mechanical rather than documentary, in three layers:

1. **A debug tripwire** in every expensive Core entry point, so a violation fails a test run instead of shipping.
2. **An analyzer/CI rule** banning direct Core calls from `Views/`.
3. **Async, cancellable façade methods**, so the correct call is also the convenient one.

**Suggested corrected code**

```csharp
// BanglaHost.Core — the tripwire. Zero cost in Release.
public static class Threading
{
    private static Func<bool>? _isUiThread;

    /// <summary>Set once at App startup so Core can detect (in Debug) that it is on the dispatcher.</summary>
    public static void RegisterUiThreadProbe(Func<bool> probe) => _isUiThread = probe;

    [Conditional("DEBUG")]
    public static void AssertNotUiThread(string member = "")
    {
        if (_isUiThread?.Invoke() == true)
            throw new InvalidOperationException(
                $"{member} does blocking work and must not be called on the UI thread. " +
                "Route it through EngineHost or Task.Run.");
    }
}
```

```csharp
// Every expensive Core entry point opens with it:
public static IReadOnlyList<CertInfo> GetLocalCertificates()
{
    Threading.AssertNotUiThread(nameof(GetLocalCertificates));
    …
}
```

```csharp
// App startup wires the probe:
var ui = DispatcherQueue.GetForCurrentThread();
Threading.RegisterUiThreadProbe(() => ui is not null && !ui.HasThreadAccessChanged());
```

```csharp
// EngineHost — async, cancellable, and the snapshot supersedes rather than queues (A1).
public Task<Snapshot> SnapshotAsync(CancellationToken ct) => Engine.ApiAsync(ct);
public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct) => work(ct);
```

```xml
<!-- Directory.Build.props — the CI rule -->
<ItemGroup>
  <BannedSymbols Include="M:BanglaHost.Core.SslService.GetLocalCertificates;
                          M:BanglaHost.Core.Php.ListExtensions;
                          M:BanglaHost.Core.Engine.Api" />
</ItemGroup>
```

**Regression test**

```csharp
[Fact]
public async Task Calling_core_on_the_ui_thread_fails_loudly_in_debug()
{
    await UiTest.RunAsync(() =>
    {
        Assert.Throws<InvalidOperationException>(() => SslService.GetLocalCertificates());
        Assert.Throws<InvalidOperationException>(() => Php.ListExtensions("8.3"));
        return Task.CompletedTask;
    });
}

[Fact]
public void No_view_calls_expensive_core_apis_directly()
{
    var banned = new[] { "SslService.GetLocalCertificates(", "Php.ListExtensions(", "Engine.Api(" };
    foreach (var f in Directory.GetFiles("src/BanglaHost.App/Views", "*.xaml.cs"))
    {
        var src = File.ReadAllText(f);
        foreach (var b in banned)
        {
            var i = src.IndexOf(b, StringComparison.Ordinal);
            if (i < 0) continue;
            var window = src[Math.Max(0, i - 200)..i];
            Assert.True(window.Contains("Task.Run") || window.Contains("EngineHost"),
                $"{Path.GetFileName(f)} calls {b} on the UI thread");
        }
    }
}
```

---

### E3 — Core exposes a synchronous-only API, so every caller has to invent its own threading

**Severity:** Medium
**Files:** `src/BanglaHost.Core/*` — `Engine.Api()`, `Php.Run`, `DbServer.Running()`, `Apache.Start/Stop`, `Nginx.*`, `Tools.*`, `SslService.*`

**Why it is a problem**

Core is synchronous by design: `Engine.Api()` returns `Snapshot`, `DbServer.Running()` returns `bool`, `Php.Run` returns a tuple. Underneath, all of it is I/O — process spawns, pipe reads, file reads, TCP connects. A synchronous façade over asynchronous I/O has to block, and every blocking call has to pick a mechanism. The codebase therefore contains **four different sync-over-async idioms**, each with its own failure mode:

| Idiom | Site | Failure mode |
|---|---|---|
| `.Wait(ms)` on a socket connect | `DbServer.Running()`, all daemon probes | pins a thread; disposes mid-connect (**C12**) |
| `.Result` on a pipe read | `Php.cs:20` | pins a thread; deadlocks if the read order inverts (**C5**) |
| `.GetAwaiter().GetResult()` | `Php.cs:83`, `DbServer.Stop()` | pins a thread; **C3** blocks shutdown |
| `Task.CompletedTask` from a sync body | `Downloader.CurlTo` | `await` never yields at all (**C4**) |

Then each *caller* adds a fifth decision — `Task.Run` (correct), `EngineHost` (correct), or nothing (**C1**, nine pages). No `CancellationToken` reaches any of it, so nothing can be abandoned: that is why **A1**'s superseded snapshots keep running and why **C13**'s 45-second tunnel wait cannot be cut short.

This is the root cause under **A1**, **C1**–**C5**, **C12**, **D3**, **D4** and **D7**. They are not seven independent defects; they are one design decision observed seven times.

**Recommended fix**

Make Core async-first, with `CancellationToken` on every operation that touches I/O. Keep thin synchronous wrappers only where a genuinely synchronous consumer needs them (the CLI), and implement those wrappers in **one** place so there is a single sync-over-async site to reason about rather than four idioms scattered across the tree.

**Suggested corrected code**

```csharp
// Async is the real implementation.
public static Task<Snapshot>  ApiAsync(CancellationToken ct);
public static Task<bool>      RunningAsync(int port, CancellationToken ct);
public static Task<(int code, string output)> RunAsync(string exe, IEnumerable<string> args,
                                                      TimeSpan timeout, CancellationToken ct);
```

```csharp
/// <summary>The ONLY sanctioned sync-over-async bridge, for the CLI. One place to audit.</summary>
internal static class SyncBridge
{
    private static readonly TaskFactory _factory = new(
        CancellationToken.None, TaskCreationOptions.None,
        TaskContinuationOptions.None, TaskScheduler.Default);

    public static T Run<T>(Func<Task<T>> work) =>
        _factory.StartNew(work).Unwrap().GetAwaiter().GetResult();   // no captured context, cannot deadlock
}

public static Snapshot Api() => SyncBridge.Run(() => ApiAsync(CancellationToken.None));
```

**Regression test**

```csharp
[Fact]
public void Sync_over_async_appears_in_exactly_one_file()
{
    var offenders = new List<string>();
    foreach (var f in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
    {
        if (f.EndsWith("SyncBridge.cs")) continue;
        var src = File.ReadAllText(f);
        if (src.Contains(".GetAwaiter().GetResult()") ||
            src.Contains(".Result;") ||
            Regex.IsMatch(src, @"\.Wait\(\d"))
            offenders.Add(f);
    }
    Assert.Empty(offenders);
}

[Fact]
public async Task Every_core_io_entry_point_accepts_a_cancellation_token()
{
    var missing = typeof(Engine).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
        .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
        .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
        .ToList();
    Assert.Empty(missing);
}
```

---

### E4 — Process ownership lives in a `static` job object with no lifetime owner

**Severity:** Medium
**Files:** `src/BanglaHost.Core/JobManager.cs`, `src/BanglaHost.Core/JobObject.cs`; 11 call sites (`AiServers.cs:34`, `Apache.cs:162`, `CacheServers.cs:30`, `DbServer.cs:126`, `Mailpit.cs:45`, `Mailpit.cs:97`, `Nginx.cs:43`, `NodeSite.cs:102`, `PhpCgi.cs:118`, `PySite.cs:126`, `SearchServers.cs:33`)

**Why it is a problem**

`JobManager` is a static holder around a lazily-initialised `JobObject` configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Because it is static with a lazy initializer and no owner, three distinct problems follow, each already reported separately — this entry is the shared cause:

* **The policy is fixed at type-init time and is wrong for one of the two hosts.** "Kill everything when the job handle closes" is right for the GUI (services should not outlive the app) and catastrophic for the CLI, where `banglahost start` starts services and exits, closing the job and killing them (**A6**). A static with no owner has no way to express "the CLI wants a different policy".
* **Initialization failure is permanent and escapes the caller's guard.** `JobObject`'s static field initializer runs outside `JobManager.Add`'s `try`, so a failure surfaces as `TypeInitializationException` at the call site, and the CLR **caches** it — every subsequent access rethrows the same exception forever. All 11 spawn paths break for the rest of the process lifetime (**A7**).
* **Some processes are simply forgotten.** `Tunnel.cs` spawns `cloudflared` and never enrols it (**C13**), so the one process with a *public internet route into the user's machine* is the one that outlives the app. A static with no explicit registry makes that omission invisible — there is no list to be absent from and no assertion to fail.

**Recommended fix**

Give process ownership an explicit, injected lifetime with an explicit policy, so the host declares its intent and enrolment is verifiable.

**Suggested corrected code**

```csharp
public enum ProcessOwnership
{
    KillOnHostExit,     // GUI: services must not outlive the app
    Detached,           // CLI `start`: services are meant to survive the launcher (A6)
}

public sealed class ProcessSupervisor : IDisposable
{
    private readonly ProcessOwnership _mode;
    private readonly JobObject? _job;                       // null when Detached
    private readonly ConcurrentDictionary<int, string> _tracked = new();

    public ProcessSupervisor(ProcessOwnership mode)
    {
        _mode = mode;
        if (mode != ProcessOwnership.KillOnHostExit) return;
        try { _job = JobObject.Create(killOnClose: true); }
        catch (Exception ex)
        {
            // Degrade, never poison: pid-file tracking still lets us clean up (A7).
            Log.Warn($"job objects unavailable ({ex.Message}); falling back to pid tracking");
        }
    }

    public void Track(Process p, string name)
    {
        _tracked[p.Id] = name;
        try { _job?.Assign(p); } catch (Exception ex) { Log.Warn($"could not assign {name}: {ex.Message}"); }
        PidStore.Write(name, p.Id);                          // survives a crash; adopted next launch
    }

    public void Dispose()
    {
        if (_mode == ProcessOwnership.KillOnHostExit) _job?.Dispose();   // kills the tree
        else foreach (var (pid, name) in _tracked) PidStore.Write(name, pid);
    }
}
```

**Regression test**

```csharp
[Theory]
[InlineData(ProcessOwnership.KillOnHostExit, false)]
[InlineData(ProcessOwnership.Detached,       true)]
public void Ownership_policy_decides_whether_children_survive(ProcessOwnership mode, bool shouldSurvive)
{
    var host = StartHostStub(mode, startNginx: true);
    var childPid = host.ReadChildPid();
    host.Exit();
    Thread.Sleep(2000);
    Assert.Equal(shouldSurvive, ProcessExists(childPid));
}

[Fact]
public void Every_spawn_site_enrols_its_process()
{
    foreach (var f in Directory.EnumerateFiles("src/BanglaHost.Core", "*.cs"))
    {
        var src = File.ReadAllText(f);
        var spawns = Regex.Matches(src, @"Process\.Start\(").Count;
        if (spawns == 0) continue;
        var tracks = Regex.Matches(src, @"\.Track\(").Count;
        Assert.True(tracks >= spawns, $"{Path.GetFileName(f)}: {spawns} spawns, {tracks} tracked");
    }
}
```

---

### E5 — Crash diagnostics are local-file-only, which is the direct reason your telemetry says "Uncategorized"

**Severity:** High
**Files:** `src/BanglaHost.App/Services/CrashLogger.cs`, `src/BanglaHost.App/App.xaml.cs`, `src/BanglaHost.App.csproj`

**Why it is a problem**

This finding is the one that explains the dashboard rather than a bug in the product.

`CrashLogger.Log(ex, context)` appends to a local text file. That is the app's **entire** diagnostic pipeline. Nothing is uploaded, and — critically — the handlers in `App.xaml.cs` **catch and swallow** exceptions before the runtime can generate a crash report. `Application.UnhandledException` sets `e.Handled = true` and returns; `Nav_SelectionChanged` and every `async void` handler in the codebase wrap their bodies in `catch (Exception ex) { CrashLogger.Log(ex, "AsyncVoidUI"); }`.

That is good for stability and it is precisely why Partner Center has nothing to categorise. Windows Error Reporting only produces a minidump when the process actually faults. Every exception the app handles produces **no WER report, no stack, no bucket** — so what reaches the Store is the residue: the crashes that escaped (`ApplySavedLanguage`/`InitializeComponent` before handlers exist (**C8**), background-thread throws (**C9**), the `JobObject` type-init failure (**A7**), possibly the native `ProgressBar` fault (**C14**)) — plus every **hang**, which by definition has no exception at all. Hangs are 2× crashes here, and a hang can never produce a stack via this route.

So the diagnostic gap is not incidental to the 33 crashes and 15 hangs — it is the reason you are reading an audit instead of a stack trace. The report you are holding had to reconstruct causality from source because the product cannot tell you what it did.

There is also a Store-specific miss: the app targets `net8.0-windows10.0.19041.0`, so `Microsoft.Windows.AppNotifications` and the WinAppSDK crash-reporting surface are available, and MSIX packages are eligible for full WER bucketing — but nothing opts in.

**Reproduction scenario**

Trigger any handled fault (navigate to a page whose constructor throws). Confirm: the local crash log gets an entry; `eventvwr.msc` → Application has no Error entry; nothing appears in Partner Center. Then check the local log's growth policy — there is no size cap or rotation, so a repeating fault (**C11**'s respawn loop) grows it without bound.

**Recommended fix**

Four changes, in increasing order of effort:

1. **Rotate and cap the local log** so it cannot grow unbounded.
2. **Write a crash breadcrumb** the next launch can detect, so a fault that kills the process is still reportable afterwards.
3. **Offer opt-in upload** of the crash log and a session summary, with an explicit consent prompt (the app is a Store app; do not upload without consent).
4. **Stop swallowing the unexpected.** Keep catching where recovery is genuine; for anything else, log, then `Environment.FailFast` in a diagnostic channel so WER produces a real minidump and Partner Center can bucket it. Ship that behind a setting so a normal user still gets stability while a diagnostic build produces dumps.

**Suggested corrected code**

```csharp
public static class CrashLogger
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object _gate = new();

    public static void Log(Exception? ex, string context)
    {
        lock (_gate)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath,
                    $"[{DateTime.UtcNow:O}] {context} {Environment.CurrentManagedThreadId} " +
                    $"v{Updater.CurrentVersion}\n{ex}\n\n");
            }
            catch { }
        }
    }

    private static void RotateIfNeeded()
    {
        var fi = new FileInfo(LogPath);
        if (!fi.Exists || fi.Length < MaxBytes) return;
        var old = LogPath + ".1";
        try { File.Delete(old); File.Move(LogPath, old); } catch { }
    }

    /// <summary>Written from the terminal handler (C9). Next launch finds it and can offer to report.</summary>
    public static void WriteCrashBreadcrumb(Exception? ex)
    {
        try
        {
            File.WriteAllText(BreadcrumbPath, JsonSerializer.Serialize(new
            {
                whenUtc = DateTime.UtcNow,
                version = Updater.CurrentVersion,
                type    = ex?.GetType().FullName,
                message = ex?.Message,
                stack   = ex?.StackTrace,
            }));
        }
        catch { }
    }

    /// <summary>Diagnostic mode: let the fault reach WER so Partner Center gets a real bucket.</summary>
    public static void LogAndMaybeFailFast(Exception ex, string context)
    {
        Log(ex, context);
        if (Config.Load().DiagnosticMode)
            Environment.FailFast($"BanglaHost {context}", ex);      // produces a minidump
    }
}
```

```csharp
// Startup: a breadcrumb means the previous session died. Ask once, then report.
var crumb = CrashLogger.TakeBreadcrumb();
if (crumb is not null && Config.Load().AllowDiagnosticUpload)
    _ = Diagnostics.UploadAsync(crumb, CrashLogger.LogPath);
```

**Regression test**

```csharp
[Fact]
public void The_crash_log_is_capped_and_rotates()
{
    using var env = TestEnv.New();
    for (var i = 0; i < 200_000; i++) CrashLogger.Log(new Exception(new string('x', 200)), "spam");

    Assert.True(new FileInfo(CrashLogger.LogPath).Length <= 2 * 1024 * 1024 + 8192);
    Assert.True(File.Exists(CrashLogger.LogPath + ".1"));
}

[Fact]
public void A_fatal_background_throw_leaves_a_breadcrumb_for_the_next_launch()
{
    using var env = TestEnv.New();
    var proc = LaunchApp(args: "--test-throw-on-background-thread");
    proc.WaitForExit(30_000);

    Assert.True(File.Exists(CrashLogger.BreadcrumbPath));
    var crumb = JsonDocument.Parse(File.ReadAllText(CrashLogger.BreadcrumbPath));
    Assert.Contains("InvalidOperationException", crumb.RootElement.GetProperty("type").GetString());
}

[Fact]
public void Diagnostic_mode_produces_a_wer_dump()
{
    using var env = TestEnv.New();
    env.WriteConfig(new Config { DiagnosticMode = true });
    var before = WerDumps.Count();
    var proc = LaunchApp(args: "--test-unexpected-fault");
    proc.WaitForExit(30_000);
    Assert.True(WerDumps.Count() > before, "no minidump: Partner Center will bucket this as Uncategorized");
}
```

## F. UX issues

Includes accessibility (**F4**) and localization (**F5**), which are Store-certification-adjacent and are graded in §J.

---

### F1 — Version comparison is off by one component, so "Update available" fires on essentially every launch

**Severity:** High
**File:** `src/BanglaHost.App/Services/Updater.cs:26-27, 87, 152-154`
**Class/Method:** `Updater.CurrentVersion`, `Updater.Check()`, `Updater.Compare()`

**Exact problematic code**

```csharp
public static string CurrentVersion =>
    Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
```

```csharp
var available = Compare(latest, CurrentVersion) > 0;
```

```csharp
private static int Compare(string a, string b) =>
    (Version.TryParse(Trim(a), out var va) ? va : new Version(0, 0)).CompareTo(
     Version.TryParse(Trim(b), out var vb) ? vb : new Version(0, 0));
```

**Why it is a problem**

`CurrentVersion` deliberately drops the Revision component: the assembly version is `1.6.0.0`, and the string produced is `"1.6.0"`. The Store side does not drop anything — `TryDisplayCatalog` reads the package `Version` property, which is the MSIX package version and is always **four-part**: `"1.6.0.0"`.

`Version.Parse` treats an unspecified component as **−1**, not 0. So:

```
Version.Parse("1.6.0.0")  →  Major 1, Minor 6, Build 0, Revision  0
Version.Parse("1.6.0")    →  Major 1, Minor 6, Build 0, Revision -1
CompareTo                 →  0 > -1  ⇒  +1  ⇒  available == true
```

The same build reports itself as out of date against itself. Every user with `AutoUpdate` on gets *"Update available — BanglaHost 1.6.0.0 … You have 1.6.0 · latest is 1.6.0.0"*, and clicking through takes them to a Store page with nothing to install. The 30-minute throttle (`AutomaticCheckDue`) and the 24-hour `_updateTimer` (`MainWindow.xaml.cs:53`) limit the frequency, not the wrongness — it recurs at every launch after a 30-minute gap, and daily for tray instances.

Two aggravating details. The prompt text itself displays both strings side by side (`MainWindow.xaml.cs:194`), so the user is shown `1.6.0` vs `1.6.0.0` and told to update — the bug is visible in its own dialog. And the fallback paths make it worse: `TryStorefrontApi` and `TryScrapeStorePage` regex out `[0-9.]+`, so whatever component count the endpoint happens to use is compared against a hardcoded three-part string.

There is also a real semantic gap underneath the formatting bug: `Assembly.GetName().Version` is the **assembly** version, while the Store compares against the **package** version from `Package.appxmanifest`. Those are set independently, so they can legitimately diverge; comparing one to the other is wrong even after normalising component counts.

**Reproduction scenario**

1. Install 1.6.0.0 from the Store. Ensure Settings → auto-update is on.
2. Launch. Within seconds: *"Update available — BanglaHost 1.6.0.0"*.
3. Click "Open Microsoft Store". The Store shows the app as up to date, with no Update button.
4. Close, wait 30 minutes, launch again — same prompt.

**Recommended fix**

Read the **package** version when running packaged (that is what the Store compares against), and normalise both sides to four components before comparing so an unspecified Revision can never read as −1.

**Suggested corrected code**

```csharp
/// <summary>The version the Store compares against: the MSIX package version when packaged,
/// falling back to the assembly version for unpackaged/dev runs.</summary>
public static string CurrentVersion
{
    get
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";      // four components, like the Store
        }
        catch (InvalidOperationException)      // not running packaged
        {
            var a = Assembly.GetExecutingAssembly().GetName().Version;
            return a is null ? "0.0.0.0" : $"{a.Major}.{a.Minor}.{a.Build}.{a.Revision}";
        }
    }
}

/// <summary>Version display for the UI — trailing zeroes trimmed, never used for comparison.</summary>
public static string DisplayVersion
{
    get
    {
        var v = Version.Parse(CurrentVersion);
        return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
    }
}

private static int Compare(string a, string b) => Normalize(a).CompareTo(Normalize(b));

/// <summary>Parse to exactly four components so an unspecified part is 0, never -1.</summary>
private static Version Normalize(string s)
{
    if (!Version.TryParse(Trim(s), out var v)) return new Version(0, 0, 0, 0);
    return new Version(v.Major,
                       Math.Max(v.Minor, 0),
                       Math.Max(v.Build, 0),
                       Math.Max(v.Revision, 0));
}
```

`VersionLabel.Text = $"v{Updater.DisplayVersion}"` at `MainWindow.xaml.cs:272`, and the tray tooltip at `:29`, use `DisplayVersion`; only `Compare` uses `CurrentVersion`.

**Regression test**

```csharp
[Theory]
[InlineData("1.6.0.0", "1.6.0",   0)]   // the shipped bug: must be equal, not newer
[InlineData("1.6.0",   "1.6.0.0", 0)]
[InlineData("1.6.0.0", "1.6.0.0", 0)]
[InlineData("1.6.1.0", "1.6.0.0", 1)]
[InlineData("1.6.0.0", "1.6.1.0", -1)]
[InlineData("1.6.0.1", "1.6.0.0", 1)]
[InlineData("v1.7",    "1.6.9.9", 1)]   // Trim() handles the 'v' prefix
public void Version_comparison_ignores_component_count(string latest, string current, int expected)
    => Assert.Equal(expected, Math.Sign(Updater.TestOnly_Compare(latest, current)));

[Fact]
public async Task The_shipped_version_never_reports_an_update_against_itself()
{
    using var store = FakeStore.Serving(version: Updater.CurrentVersion);
    var r = await Updater.Check();
    Assert.False(r.UpdateAvailable, $"self-reported update: current={Updater.CurrentVersion} latest={r.Latest}");
}

[Fact]
public void Current_version_has_four_components()
    => Assert.Equal(4, Updater.CurrentVersion.Split('.').Length);
```

---

### F2 — The Let's Encrypt button is an enabled control that fakes two seconds of work and then fails silently

**Severity:** Medium
**Files:** `src/BanglaHost.Core/SslService.cs` (`GenerateLetsEncryptAsync`), `src/BanglaHost.App/Views/SslPage.xaml.cs:59-81`

**Exact problematic code**

```csharp
// SslService.GenerateLetsEncryptAsync
log("Requesting certificate from Let's Encrypt…");
await Task.Delay(2000);
log("Simulation complete.");
return false;
```

```csharp
// SslPage.xaml.cs:70
var success = await Task.Run(() => SslService.GenerateLetsEncryptAsync(domain, email, log));
SetBusy(false);
if (success) { DomainBox.Text = ""; EmailBox.Text = ""; LoadCerts(); }
```

**Why it is a problem**

The feature does not exist, but the UI presents it as if it does. The panel is shown whenever the domain is not `.test` (`SslPage.xaml.cs:19-21`), the button is enabled, clicking it disables the buttons and shows *"Requesting Let's Encrypt certificate…"*, waits two seconds — a deliberate simulation of work — and returns `false`. Because `success` is `false`, the `if` body is skipped: the fields are not cleared, no certificate appears in the list, **and no error is shown**. The banner is hidden by `SetBusy(false)` and the user is left looking at an unchanged page.

From the user's side that is indistinguishable from a real failure with a swallowed error, and the fields still being populated implies "try again" — so they do, repeatedly. For a Store app this is also a listing-accuracy problem: a visible, enabled, apparently-functional feature that cannot succeed.

**Recommended fix**

Either implement it (ACME via an HTTP-01 or DNS-01 challenge, which needs a reachable public domain) or make the UI honest. Honesty is a ten-line change and should ship first: disable the button, label it as planned, and remove the fake delay so nothing pretends to work.

**Suggested corrected code**

```csharp
// SslService — no simulation. Say what is true.
public static Task<bool> GenerateLetsEncryptAsync(string domain, string email, Action<string> log)
{
    log("Let's Encrypt certificates aren't supported yet — this needs a publicly reachable domain. " +
        "Use 'Generate local certificate' for .test development sites.");
    return Task.FromResult(false);
}
```

```xml
<!-- SslPage.xaml — visibly unavailable, not silently broken -->
<StackPanel x:Name="LetsEncryptPanel" Spacing="8">
  <InfoBar IsOpen="True" Severity="Informational" IsClosable="False"
           Title="Coming soon"
           Message="Let's Encrypt certificates require a publicly reachable domain. Planned for a future release." />
  <Button x:Name="GenLeBtn" Content="Request Let's Encrypt certificate" IsEnabled="False"
          AutomationProperties.HelpText="Not available yet — requires a publicly reachable domain." />
</StackPanel>
```

```csharp
// SslPage.xaml.cs — surface the failure instead of discarding it (same defect class as F6)
var success = await SslService.GenerateLetsEncryptAsync(domain, email, log);
SetBusy(false);
if (success) { DomainBox.Text = ""; EmailBox.Text = ""; await LoadCertsAsync(); }
else await DialogQueue.ShowAsync(new ContentDialog
{
    Title = "Couldn't get a certificate", Content = _lastLogLine,
    CloseButtonText = "OK", XamlRoot = XamlRoot,
});
```

**Regression test**

```csharp
[Fact]
public void No_shipped_code_path_simulates_work()
{
    foreach (var f in Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories))
    {
        var src = File.ReadAllText(f);
        Assert.DoesNotContain("Simulation complete", src);
        Assert.DoesNotContain("// TODO: simulate", src);
    }
}

[Fact]
public async Task Unavailable_features_are_disabled_rather_than_failing_silently()
{
    await UiTest.RunAsync(async () =>
    {
        var page = new SslPage();
        await UiTest.ShowAsync(page);
        page.TestOnly_SetDomain("example.com");        // non-.test → LE panel visible
        Assert.False(page.TestOnly_GenLeEnabled, "an unimplemented action is clickable");
    });
}
```

---

### F3 — `SslPage` hardcodes `.test`, ignoring the configurable TLD

**Severity:** Low
**File:** `src/BanglaHost.App/Views/SslPage.xaml.cs:17-22`
**Class/Method:** `SslPage` constructor — the `DomainBox.TextChanged` handler

**Exact problematic code**

```csharp
DomainBox.TextChanged += (s, e) =>
{
    var isLocal = DomainBox.Text.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
    LetsEncryptPanel.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
    GenLeBtn.IsEnabled = !isLocal;
};
```

**Why it is a problem**

The local TLD is configurable (`Config.Tld`), and sites are created with whatever the user chose — `.local`, `.dev`, `.localhost`. This handler recognises only `.test`. A user whose TLD is `.local` types `myapp.local`, and the page classifies it as a **public** domain: it hides nothing, shows the Let's Encrypt panel, and enables a button that cannot work (**F2**) for a domain that is not reachable from the internet in the first place. The correct action for that domain — "Generate local certificate" — is still available, but the UI is actively steering the user to the wrong one.

**Recommended fix**

Read the configured TLD, and treat any non-public suffix as local.

**Suggested corrected code**

```csharp
public SslPage()
{
    InitializeComponent();

    var tld = "." + Config.Load().Tld.TrimStart('.');      // e.g. ".test", ".local"
    DomainBox.TextChanged += (_, _) =>
    {
        var text = DomainBox.Text.Trim();
        var isLocal = text.EndsWith(tld, StringComparison.OrdinalIgnoreCase)
                   || text.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                   || text.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || !text.Contains('.');                 // bare hostname is never public
        LetsEncryptPanel.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
        GenLeBtn.IsEnabled = false;                         // F2: unavailable regardless
    };

    DomainBox.PlaceholderText = $"myapp{tld}";
}
```

**Regression test**

```csharp
[Theory]
[InlineData("test",  "myapp.test",  true)]
[InlineData("local", "myapp.local", true)]     // the shipped bug
[InlineData("dev",   "myapp.dev",   true)]
[InlineData("test",  "example.com", false)]
[InlineData("test",  "myapp",       true)]
public async Task Local_domains_are_recognised_for_the_configured_tld(string tld, string domain, bool expectLocal)
{
    using var env = TestEnv.New();
    env.WriteConfig(new Config { Tld = tld });
    await UiTest.RunAsync(async () =>
    {
        var page = new SslPage();
        await UiTest.ShowAsync(page);
        page.TestOnly_SetDomain(domain);
        Assert.Equal(expectLocal, page.TestOnly_LetsEncryptPanelCollapsed);
    });
}
```

---

### F4 — `AutomationProperties` appears in 0 of 50 XAML views

**Severity:** High
**Files:** all 50 files under `src/BanglaHost.App/Views/*.xaml` (plus `MainWindow.xaml`)

**Exact problematic code** — the measurement, not a snippet: a grep for `AutomationProperties` across every `.xaml` in the app returns **zero matches** in 50 view files. `ToolTipService` appears in 8. The consequence is visible in controls like the dashboard's status dots and the icon-only action buttons:

```xml
<!-- A screen reader announces this as an unnamed Ellipse with no state -->
<Ellipse x:Name="WebDot" Width="10" Height="10" />
```

```csharp
// The only state indication is a brush colour — invisible to assistive technology
WebDot.Fill = nginx || apache ? On : Off;      // DashboardPage.xaml.cs:80
```

**Why it is a problem**

Nothing in the app carries an accessible name, help text, or a live-region announcement. Concretely:

* **Status is colour-only.** Every service's running state is conveyed by an `Ellipse`'s fill (`WebDot`, `PhpDot`, `DbDot`, `CacheDot`). A screen reader announces nothing; a user with red/green colour blindness gets no alternative channel. The adjacent `*Sub` text sometimes duplicates the state (`DbSub.Text = dbRun ? "running" : "stopped"`) and sometimes does not (`WebSub.Text` shows a site count, so the web server's state is *purely* colour).
* **Icon-only buttons are unnamed.** Action buttons in `SiteListControl` and the tool rows expose no name, so Narrator reads "button" with no indication of what it does.
* **Nothing announces asynchronous change.** The dashboard rewrites its cards every 2 seconds with no `AutomationProperties.LiveSetting`, so a screen-reader user gets no notification when a service comes up, and re-focusing may re-announce stale content.
* **Keyboard reachability is unverified.** With no `TabIndex` anywhere and several custom composite controls, tab order is whatever declaration order produces. **Requires runtime verification** with Accessibility Insights for the actual traversal order and focus-visual coverage.

For a Store app this is also a certification and store-quality concern, and it is the single largest gap between this app and the commercial alternatives it competes with.

**Reproduction scenario**

Launch Narrator (`Ctrl+Win+Enter`) and tab through the Dashboard. The status dots are skipped or announced as unlabelled elements; the PMA/Adminer/Mailpit open buttons announce no purpose; starting a service produces no announcement.

**Recommended fix**

Add names and help text to every interactive and status element, give status a non-colour channel, and mark the dashboard's changing region as a live region. Then gate it in CI so new views cannot regress.

**Suggested corrected code**

```xml
<!-- Status: name + a text channel that does not depend on colour -->
<StackPanel Orientation="Horizontal" Spacing="8"
            AutomationProperties.Name="Web server status"
            AutomationProperties.LiveSetting="Polite">
  <Ellipse x:Name="WebDot" Width="10" Height="10"
           AutomationProperties.AccessibilityView="Raw" />   <!-- decorative; state comes from text -->
  <TextBlock x:Name="WebVal" />
  <TextBlock x:Name="WebState" Style="{StaticResource CaptionTextBlockStyle}" />
</StackPanel>
```

```csharp
// DashboardPage.Refresh() — set the accessible state, not just the colour
var webUp = nginx || apache;
WebDot.Fill  = webUp ? On : Off;
WebState.Text = webUp ? "running" : "stopped";                       // visible, colour-independent
AutomationProperties.SetName(WebPanel, $"Web server {WebState.Text}, {sites.Count} sites");
```

```xml
<!-- Icon-only actions get a name and help text -->
<Button x:Name="PmaOpen" Click="PmaOpen_Click"
        AutomationProperties.Name="Open phpMyAdmin"
        AutomationProperties.HelpText="Opens phpMyAdmin in your browser"
        ToolTipService.ToolTip="Open phpMyAdmin">
  <FontIcon Glyph="&#xE8A7;" AutomationProperties.AccessibilityView="Raw" />
</Button>
```

**Regression test**

```csharp
[Fact]
public void Every_interactive_element_has_an_accessible_name()
{
    var offenders = new List<string>();
    foreach (var f in Directory.GetFiles("src/BanglaHost.App/Views", "*.xaml"))
    {
        var doc = XDocument.Load(f);
        foreach (var el in doc.Descendants())
        {
            var tag = el.Name.LocalName;
            if (tag is not ("Button" or "ToggleSwitch" or "ToggleButton" or "HyperlinkButton"
                            or "ComboBox" or "TextBox" or "PasswordBox" or "CheckBox" or "RadioButton"))
                continue;

            var hasName = el.Attribute(XName.Get("Name", AutomationNs)) is not null
                       || el.Attribute("Header") is not null
                       || el.Attribute("Content")?.Value is { Length: > 0 } c && !c.StartsWith("&#x");
            if (!hasName) offenders.Add($"{Path.GetFileName(f)}:{tag} {el.Attribute("Name")?.Value}");
        }
    }
    Assert.Empty(offenders);
}

[Fact]
public async Task Service_state_is_available_without_colour()
{
    await UiTest.RunAsync(async () =>
    {
        var page = new DashboardPage();
        await UiTest.ShowAsync(page);
        await page.TestOnly_RefreshAsync();

        var peer = FrameworkElementAutomationPeer.FromElement(page.TestOnly_WebPanel);
        Assert.Matches("running|stopped", peer.GetName());
    });
}

/// <summary>Full-tree scan via UI Automation — catches what a XAML grep cannot.</summary>
[Fact]
public async Task No_unnamed_focusable_elements_in_any_page()
{
    foreach (var pageType in Navigation.AllPageTypes)
    {
        var unnamed = await UiTest.ScanForUnnamedFocusableElements(pageType);
        Assert.Empty(unnamed);
    }
}
```

---

### F5 — Bengali relies on a timed runtime visual-tree walker; only 61 strings and one `x:Uid` are in the resource system

**Severity:** Medium
**Files:** `src/BanglaHost.App/Services/Localizer.cs`, `src/BanglaHost.App/MainWindow.xaml.cs:236-264`, `Strings/en-US/Resources.resw`, `Strings/bn-BD/Resources.resw`, `src/BanglaHost.App/Views/SettingsPage.xaml`

**Exact problematic code**

```csharp
Localizer.Localize(page);                  // walks the tree and rewrites text it recognises
page.Loaded += Page_Loaded_Localize;
…
foreach (var ms in new[] { 250, 800, 1800 })
{
    await Task.Delay(ms);
    try { if (page.XamlRoot != null) Localizer.Localize(page); }
    catch { }
}
```

**Why it is a problem**

The project has two localization systems and the wrong one is doing the work.

The **correct** one — MRT, with `x:Uid` on elements and matched `.resw` files — is present but barely used: `x:Uid` appears in exactly **one** file (`SettingsPage.xaml`, 2 occurrences), and both `Strings/en-US/Resources.resw` and `Strings/bn-BD/Resources.resw` contain **61** entries. For an app with 50 views, 61 strings covers a small fraction of the visible text.

Everything else goes through `Localizer`: a runtime walker that traverses the realized visual tree and replaces text it can match, re-run five times per navigation on a schedule (**C7**). Consequences:

* **Coverage is invisible.** There is no build-time list of what is and is not translated. A new page ships fully English and nothing reports it.
* **Correctness depends on timing.** Content populated after +2850 ms is never translated. Content populated at +900 ms flickers English→Bengali. Both are shipped behaviours, not edge cases — every page that awaits a snapshot populates late.
* **Matching is by string identity**, so it cannot handle a string that legitimately differs by context, and it will happily rewrite text that merely *looks* like a UI label — including user data such as a site name or a database name that coincides with a UI string.
* **It fights the accessibility fix.** Rewriting `TextBlock.Text` post-hoc does not update `AutomationProperties.Name` (**F4**), so a screen reader would announce English while the screen shows Bengali.
* **No `.resw` for plurals or formatting.** `DashboardPage.xaml.cs:79` builds `$"{sites.Count} site{(sites.Count == 1 ? "" : "s")}"` in code — English pluralisation hardcoded, unreachable by either system.

**Reproduction scenario**

Switch to Bengali. Navigate to Sites. Labels appear in English and flip to Bengali over the next ~3 seconds. Add a site so the list repopulates after the last re-walk: the new row's status text stays English. Name a site the same as a UI label and observe it being rewritten.

**Recommended fix**

Migrate to MRT and delete the walker. This is mechanical but broad, so stage it: add `x:Uid` per view, move each string into both `.resw` files, and add a CI gate that fails on any literal user-visible string left in XAML. Keep `Localizer` only until the last view is converted, then remove it and `RewalkAfterAsync` together.

**Suggested corrected code**

```xml
<!-- Views/DashboardPage.xaml -->
<TextBlock x:Uid="Dashboard_WebHeader" />
<Button   x:Uid="Dashboard_StartAll" />
```

```xml
<!-- Strings/en-US/Resources.resw -->
<data name="Dashboard_WebHeader.Text"><value>Websites</value></data>
<data name="Dashboard_StartAll.Content"><value>Start all</value></data>
<data name="Dashboard_StartAll.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name">
  <value>Start all services</value>
</data>   <!-- localized accessible name, which the walker could never do (F4) -->
```

```csharp
// Code-built strings go through the resource loader, with plural forms per language.
private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

WebSub.Text = sites.Count == 1
    ? _res.GetString("Dashboard_SiteCount_One")            // "1 site" / "১টি সাইট"
    : string.Format(_res.GetString("Dashboard_SiteCount_Other"), sites.Count);
```

**Regression test**

```csharp
[Fact]
public void Every_user_visible_string_lives_in_resources()
{
    var offenders = new List<string>();
    foreach (var f in Directory.GetFiles("src/BanglaHost.App/Views", "*.xaml"))
    {
        var doc = XDocument.Load(f);
        foreach (var el in doc.Descendants())
        {
            if (el.Attribute(XName.Get("Uid", XamlNs)) is not null) continue;
            foreach (var attr in new[] { "Text", "Content", "Header", "PlaceholderText" })
                if (el.Attribute(attr)?.Value is { Length: > 1 } v &&
                    !v.StartsWith("{") && !v.StartsWith("&#x") && v.Any(char.IsLetter))
                    offenders.Add($"{Path.GetFileName(f)}: {el.Name.LocalName}.{attr}=\"{v}\"");
        }
    }
    Assert.Empty(offenders);
}

[Fact]
public void Both_languages_define_the_same_keys()
{
    string[] Keys(string p) => XDocument.Load(p).Descendants("data")
        .Select(d => d.Attribute("name")!.Value).OrderBy(x => x).ToArray();

    var en = Keys("src/BanglaHost.App/Strings/en-US/Resources.resw");
    var bn = Keys("src/BanglaHost.App/Strings/bn-BD/Resources.resw");
    Assert.Empty(en.Except(bn));      // untranslated
    Assert.Empty(bn.Except(en));      // orphaned
}

[Fact]
public void The_runtime_tree_walker_is_gone()
{
    Assert.False(File.Exists("src/BanglaHost.App/Services/Localizer.cs"));
    Assert.DoesNotContain("RewalkAfterAsync", File.ReadAllText("src/BanglaHost.App/MainWindow.xaml.cs"));
}
```

---

### F6 — `BackupPage` discards the restore result, so even a genuine failure looks like success

**Severity:** Medium
**Files:** `src/BanglaHost.App/Views/BackupPage.xaml.cs:46, 71`
**Class/Method:** the restore click handlers

**Exact problematic code**

```csharp
await Task.Run(() => BackupService.RestoreAsync(path, log));
```

**Why it is a problem**

`RestoreAsync` returns `Task<bool>`. The return value is discarded at both call sites, so the UI has no idea whether the restore worked. Whatever happens, the page proceeds as if it succeeded.

Today that is doubly bad because `RestoreAsync` is a stub that restores nothing and returns `true` (**A2**) — the user is told a restore succeeded when no files were touched. But even after **A2** is fixed and `RestoreAsync` starts returning `false` for real failures, this call site would still swallow it. Two independent defects on the same code path, and this is the one that would survive the obvious fix.

Restore is the highest-stakes operation in the product: a user runs it *because* something is already broken, and acts on its outcome. Reporting an unverified success here is worse than reporting an error.

**Recommended fix**

Consume the result, and change the contract from `bool` to a structured outcome so the UI can say what failed. Show a definite success or a definite failure, never silence.

**Suggested corrected code**

```csharp
public sealed record RestoreResult(bool Ok, int FilesRestored, int DatabasesRestored,
                                  IReadOnlyList<string> Errors);
```

```csharp
private async void Restore_Click(object sender, RoutedEventArgs e)
{
    try
    {
        SetBusy(true, "Restoring…");
        var result = await BackupService.RestoreAsync(path, log, _cts.Token);
        SetBusy(false);

        await DialogQueue.ShowAsync(new ContentDialog
        {
            Title = result.Ok ? "Restore complete" : "Restore failed",
            Content = result.Ok
                ? $"Restored {result.FilesRestored} files and {result.DatabasesRestored} databases."
                : "Nothing was restored:\n\n" + string.Join("\n", result.Errors.Take(10)),
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        });
    }
    catch (OperationCanceledException) { SetBusy(false); }
    catch (Exception ex)
    {
        SetBusy(false);
        CrashLogger.Log(ex, "Restore");
        await DialogQueue.ShowAsync(new ContentDialog
        {
            Title = "Restore failed", Content = ex.Message,
            CloseButtonText = "OK", XamlRoot = XamlRoot,
        });
    }
}
```

**Regression test**

```csharp
[Fact]
public async Task A_failed_restore_is_reported_to_the_user()
{
    using var env = TestEnv.New();
    var corrupt = env.WriteFile("bad.zip", "not a zip");

    await UiTest.RunAsync(async () =>
    {
        var page = new BackupPage();
        await UiTest.ShowAsync(page);
        var dlg = await UiTest.CaptureDialog(() => page.TestOnly_RestoreAsync(corrupt));
        Assert.Equal("Restore failed", dlg.Title);
    });
}

[Fact]
public void No_call_site_discards_a_restore_result()
{
    foreach (var f in Directory.GetFiles("src/BanglaHost.App/Views", "*.xaml.cs"))
    {
        var src = File.ReadAllText(f);
        Assert.DoesNotMatch(new Regex(@"^\s*await .*RestoreAsync\(", RegexOptions.Multiline), src);
    }
}
```

## G. Technical debt

Nothing here is a live defect on its own. Each item is a property of the codebase that makes the defects above more likely to recur, or makes them harder to detect. Severity is Low throughout because none of them breaks a user's machine today; the *aggregate* is what determines how many of the findings above come back after they are fixed.

---

### G1 — nginx directive injection via a site's document root (CLI-only, self-inflicted)

**Severity:** Low
**Files:** `src/BanglaHost.Core/NginxConfig.cs`, `src/BanglaHost.Core/Engine.cs` (`SiteAdd`), `src/BanglaHost.Cli/Program.cs`

**Why it is listed here and not in §B**

The generated vhost interpolates a site's `root` path into an nginx `root` directive without escaping. A path containing `;` plus newline can therefore terminate the directive and inject arbitrary nginx configuration — including `listen` on a non-loopback address, which would expose the user's sites to their whole network.

It is **not** a security finding, for two reasons that both have to hold and both do:

1. **The GUI cannot produce such a path.** `SitesPage` sets `_customRoot` from a `FolderPicker` (`SitesPage.xaml.cs:186-195`), so the value comes from the shell and is a real directory path. A directory name cannot contain `;` followed by a newline on NTFS (newline is not a legal filename character).
2. **The only path that can is the CLI, run by the same user.** `banglahost site add --root "…"` accepts an arbitrary string. But that user already has full control of their own nginx configuration directory — they can edit the vhost directly. There is no privilege boundary being crossed, so this is a robustness bug, not a vulnerability.

It stays on the list because the argument above is *contingent*: the day a site root arrives from anywhere other than a folder picker — an imported project file, a blueprint, a marketplace template, a `.env`, a CLI script generated by something else — it becomes a real injection into a config file that a privileged process reads.

**Exact problematic code** (shape)

```csharp
sb.AppendLine($"    root {site.Root};");
```

**Recommended fix**

Validate at the boundary and quote at the point of generation — both, so neither is load-bearing alone.

**Suggested corrected code**

```csharp
/// <summary>nginx accepts a double-quoted value; a path containing " or a newline is rejected outright
/// because no legitimate Windows directory contains either.</summary>
private static string NginxPath(string path)
{
    var full = Path.GetFullPath(path);                       // also collapses ..\ (defence in depth)
    if (full.AsSpan().IndexOfAny('"', '\n', '\r', ';', '\0') >= 0)
        throw new BhException($"unsupported characters in site root: {path}");
    return "\"" + full.Replace("\\", "/") + "\"";
}
```

```csharp
sb.AppendLine($"    root {NginxPath(site.Root)};");
```

**Regression test**

```csharp
[Theory]
[InlineData("C:\\sites\\app;\nlisten 0.0.0.0:80;\n#")]
[InlineData("C:\\sites\\\"app")]
[InlineData("C:\\sites\\app\r\nlisten 8080;")]
public void A_hostile_site_root_is_rejected_not_emitted(string root)
{
    Assert.Throws<BhException>(() => NginxConfig.Render(new Site { Name = "app", Root = root }));
}

[Fact]
public void A_generated_vhost_never_listens_off_loopback()
{
    var conf = NginxConfig.Render(new Site { Name = "app", Root = @"C:\sites\app" });
    foreach (var line in conf.Split('\n').Where(l => l.TrimStart().StartsWith("listen")))
        Assert.Matches(@"listen\s+(127\.0\.0\.1|\[::1\]):\d+", line.Trim());
}
```

---

### G2 — A test project exists with 3 test methods and is not in the solution, so it is never built or run

**Severity:** Low
**Files:** `src/BanglaHost.Tests/` (`HostsTests.cs`, `NetUtilsTests.cs`, `PhpCgiTests.cs`), `BanglaHost.sln`

**Why it is a problem**

The project is real, tracked in git, correctly configured (xunit 2.5.3, `Microsoft.NET.Test.Sdk` 17.8.0, coverlet, `ProjectReference` to Core) — and **absent from `BanglaHost.sln`**, which lists only `BanglaHost.Core`, `BanglaHost.Cli`, `BanglaHost.App` and `BanglaHost.Elevate`. So `dotnet build`/`dotnet test` on the solution never touches it. There is also no `.github/workflows` directory, so nothing runs it anywhere else either.

Coverage, if it ran, would be three methods:

| Test file | Covers |
|---|---|
| `HostsTests.cs` | `Hosts.IsValidDomain` (valid + invalid theories) |
| `NetUtilsTests.cs` | `NetUtils.IsPortAvailable` (free port, in-use port) |
| `PhpCgiTests.cs` | `PhpCgi.PortFor` |

That is three of the 45 findings' worth of surface, and notably `NetUtilsTests` covers exactly the method that **B14** finds fails *open* — a test asserting the safe behaviour would have caught it, if the project were wired up.

This is the reason every finding in this report ships with a regression test: there is currently no mechanism that would notice any of them coming back.

**Recommended fix**

Add the project to the solution, add a CI workflow that builds and tests on every push, and treat the existing three tests as the seed rather than the ceiling.

**Suggested corrected code**

```bash
dotnet sln BanglaHost.sln add src/BanglaHost.Tests/BanglaHost.Tests.csproj
```

```yaml
# .github/workflows/ci.yml
name: CI
on: [push, pull_request]
jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore BanglaHost.sln
      - run: dotnet build BanglaHost.sln -c Release --no-restore
      - run: dotnet test BanglaHost.sln -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: test-results, path: '**/TestResults/**' }
```

**Regression test**

```csharp
[Fact]
public void The_test_project_is_in_the_solution()
{
    var sln = File.ReadAllText("BanglaHost.sln");
    Assert.Contains("BanglaHost.Tests.csproj", sln);
}
```

(That test can only pass once it is running, which is the point.)

---

### G3 — 176 empty `catch { }` blocks

**Severity:** Low
**Files:** across the tree — **95** in `BanglaHost.Core`, **81** in `BanglaHost.App`, 0 in `BanglaHost.Cli`, 0 in `BanglaHost.Elevate`

**Exact problematic code** (representative)

```csharp
try { if (page.XamlRoot != null) Localizer.Localize(page); } catch { }        // MainWindow.xaml.cs:261
try { File.Delete(old); File.Move(LogPath, old); } catch { }
DispatcherQueue?.TryEnqueue(() => { try { MemBar.Value = mp; } catch { } }); // DashboardPage.xaml.cs:111
try { Directory.Delete(dir, true); } catch { }                                // BackupService retention
```

**Why it is a problem**

Some of these are legitimate — a best-effort `File.Delete` on a temp file genuinely has nothing to report. But 176 of them, with no distinction between "expected and harmless" and "unexpected and important", means the codebase has no signal path for the second kind. Three concrete consequences already documented above:

* **A2/A3** — the backup path swallows the failure that makes a backup useless, and reports success.
* **C14** — the `ProgressBar` catch hides the real exception type, so the actual bug cannot be diagnosed.
* **E5** — combined with the handled-exception design, nothing reaches telemetry, which is why the dashboard says "Uncategorized".

The pattern also defeats the fixes in this report: a caller that swallows the `false` return or the thrown exception undoes the improvement at the call site.

**Recommended fix**

Convert every empty catch into one of three explicit forms, so the intent is stated and reviewable:

```csharp
// 1. Expected and genuinely ignorable — narrow the type and say why.
catch (IOException) { /* temp file still locked; next launch retries (D5) */ }

// 2. Recoverable but worth knowing about — log at warning.
catch (Exception ex) { Log.Warn($"could not read {path}: {ex.Message}"); }

// 3. Unexpected — record it; do not pretend it did not happen.
catch (Exception ex) { CrashLogger.Log(ex, nameof(Method)); throw; }
```

Then make the rule enforceable so the count cannot grow again:

```xml
<!-- .editorconfig -->
dotnet_diagnostic.CA1031.severity = warning   <!-- do not catch general exception types -->
dotnet_diagnostic.RCS1075.severity = warning  <!-- avoid empty catch clause that catches System.Exception -->
```

**Regression test**

```csharp
[Fact]
public void The_number_of_empty_catch_blocks_does_not_grow()
{
    const int Budget = 176;                    // the count at the time of this audit; ratchet downward only
    var count = Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
        .Sum(f => Regex.Matches(File.ReadAllText(f), @"catch\s*(\([^)]*\))?\s*\{\s*\}").Count);

    Assert.True(count <= Budget, $"{count} empty catch blocks (budget {Budget})");
}
```

---

### G4 — 167 `async void` methods with copy-pasted exception boilerplate, including one that is not an event handler

**Severity:** Low
**Files:** across `src/BanglaHost.App` — every page

**Exact problematic code**

```csharp
private async void StartAll_Click(object sender, RoutedEventArgs e)
{
    try { await Op(() => EngineHost.Instance.Engine.Start("all")); }
    catch (OperationCanceledException) { /* ignore */ }
    catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); await DialogQueue.ShowAsync(…); }
}
```

```csharp
private async void Refresh()                     // DashboardPage.xaml.cs:64 — NOT an event handler
```

**Why it is a problem**

`async void` for an event handler is the sanctioned WinUI pattern, so the count itself is not the defect. Two things about *how* it is used are.

First, the guard is **copy-pasted** rather than shared. The same four-line `try`/`catch (OperationCanceledException)`/`catch (Exception ex)` block is duplicated at ~167 sites with small variations — some log to `CrashLogger`, some to `EngineHost.Append`, some show a dialog, some do both, some (`MainWindow.xaml.cs:303, 308`) use a bare `catch { }`. Whether a failure is visible to the user depends on which handler you happened to click. Changing the policy means editing 167 sites.

Second, `DashboardPage.Refresh()` (`:64`) is `async void` but is **not** an event handler: it is called directly from `OnNavigatedTo` (`:44`), `OnTimerTick` (`:36`), `OnSiteListChanged` (`:37`), `ToolOp` (`:213`) and `Op` (`:239`). Because it returns `void`, none of those callers can await it, observe its completion, or serialise against it — which is precisely the mechanism behind **A1**'s unbounded re-entrancy. Making it `async Task` is a prerequisite for that fix, not a style preference.

**Recommended fix**

One shared helper for handler bodies, and `async Task` for anything that is not a handler.

**Suggested corrected code**

```csharp
/// <summary>Single place where "what does the user see when a handler fails" is decided.</summary>
internal static class Ui
{
    public static async void Handler(FrameworkElement owner, string name, Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, name);
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
            if (owner.XamlRoot is { } root)
                await DialogQueue.ShowAsync(new ContentDialog
                {
                    Title = "Something went wrong", Content = ex.Message,
                    CloseButtonText = "OK", XamlRoot = root,
                });
        }
    }
}
```

```csharp
private void StartAll_Click(object sender, RoutedEventArgs e) =>
    Ui.Handler(this, nameof(StartAll_Click), () => Op(() => EngineHost.Instance.Engine.Start("all")));

private async Task RefreshAsync(CancellationToken ct) { … }      // awaitable, cancellable (A1)
```

**Regression test**

```csharp
[Fact]
public void Async_void_is_only_used_for_event_handlers()
{
    var rx = new Regex(@"async void (\w+)\s*\(([^)]*)\)");
    var offenders = new List<string>();
    foreach (var f in Directory.GetFiles("src/BanglaHost.App", "*.cs", SearchOption.AllDirectories))
    {
        if (f.Contains("\\bin\\") || f.Contains("\\obj\\")) continue;
        foreach (Match m in rx.Matches(File.ReadAllText(f)))
        {
            var ps = m.Groups[2].Value;
            var isHandler = ps.Contains("RoutedEventArgs") || ps.Contains("EventArgs")
                         || ps.Contains("NavigationEventArgs") || ps.Contains("object sender");
            if (!isHandler) offenders.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
        }
    }
    Assert.Empty(offenders);          // DashboardPage.Refresh would fail this today
}
```

---

### G5 — 531 build-output files committed under `publish-msix/`

**Severity:** Low
**Files:** `publish-msix/**` (531 files tracked by git), `.gitignore`

**Why it is a problem**

`.gitignore` covers `bin/`, `obj/`, `publish/` and `sln-publish/` — but not `publish-msix/`, so the entire MSIX publish output is in version control: `BanglaHost.App.dll`, `.exe`, `.pdb`, `.deps.json`, per-version `*_x64_Test` payload directories with their `Add-AppDevPackage.ps1` and 17 localised `.psd1` resource files, and so on. The working tree at the start of this audit showed dozens of these as modified or deleted — build noise mixed into every diff.

Three costs. Repository weight grows with every publish, permanently. `git status` and every diff are polluted, so a real change is easy to miss during review — the working tree in this audit had ~40 build-artifact modifications and 0 source modifications, and they were indistinguishable at a glance. And committed binaries invite the worst kind of ambiguity for a shipped app: it is not determinable from the repository whether `publish-msix/BanglaHost.App.dll` corresponds to the source at that commit.

**Recommended fix**

Ignore the directory and remove it from the index (keeping the files on disk).

**Suggested corrected code**

```gitignore
# .gitignore
bin/
obj/
*.binlog
publish/
sln-publish/
publish-msix/
*.msix
*.msixbundle
*.appxsym
*.appxupload
```

```bash
git rm -r --cached publish-msix
git commit -m "chore: stop tracking MSIX publish output"
```

**Regression test**

```csharp
[Fact]
public void No_build_output_is_tracked()
{
    var tracked = Git.LsFiles();
    Assert.DoesNotContain(tracked, p => p.StartsWith("publish", StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain(tracked, p => p.EndsWith(".dll") || p.EndsWith(".pdb") || p.EndsWith(".msix"));
}
```

---

### G6 — Mojibake in 16 source files: UTF-8 comments and strings decoded as CP1252

**Severity:** Low
**Files:** 16 `.cs`/`.xaml` files, including `App.xaml.cs:73, 90`, `MainWindow.xaml.cs:36, 90`, `Services/LocalizerMap.cs`, `Services/TrayIcon.cs`, `Views/TerminalPage.xaml.cs`, `BanglaHost.Cli/Program.cs`, `Core/Config.cs`, `Core/DbServer.cs`, `Core/Downloader.cs`, `Core/Engine.cs`

**Exact problematic code**

```csharp
// Launched with --tray (autostart at login) â†’ run in the TRAY ONLY: never show the window   // App.xaml.cs:73
// Close â†’ hide to tray when "keep running" is on (Settings); otherwise really quit.          // MainWindow.xaml.cs:36
// Defender-only (other AVs have no API â†’ manual, see README).                                // MainWindow.xaml.cs:90
```

`â†’` is the UTF-8 encoding of `→` (`E2 86 92`) read back as three CP1252 characters — the file was written as UTF-8 and later re-saved by a tool that assumed the ANSI code page. The same damage appears as `â€`, `Â `, and box-drawing runs in section-divider comments.

**Why it is a problem**

In comments it is cosmetic but corrosive: it signals that at least one tool in the chain is not UTF-8-aware, and every subsequent edit through that tool can damage more of the file. That matters far more than usual for **this** codebase, because it ships **Bengali** UI strings. The same round-trip applied to `Strings/bn-BD/Resources.resw` or to a Bengali literal in code would silently corrupt user-visible text, and Bengali mojibake is much harder for a reviewer to spot than a mangled arrow.

Adjacent evidence that encoding handling is inconsistent: `MainWindow.xaml.cs:139` writes the emoji as an escape (`"BanglaHost is ready \U0001F389"`) rather than a literal, and a previous commit's message records *"fix: broken emoji in default placeholder page (use HTML entity instead of raw UTF-8)"* — i.e. this has already bitten the product once and was worked around at the symptom rather than fixed at the encoding layer.

**Reproduction scenario**

Open `src/BanglaHost.App/App.xaml.cs` in any UTF-8 editor and read line 73. Then confirm the cause: the file has no UTF-8 BOM, so a CP1252-defaulting tool re-saves it with the arrow expanded to three characters.

**Recommended fix**

Repair the damaged files, then make the encoding explicit so no tool has to guess.

**Suggested corrected code**

```ini
# .editorconfig
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true

[*.{cs,xaml,resw,csproj,props,targets,json,md}]
charset = utf-8
```

```xml
<!-- Directory.Build.props — make the compiler's assumption explicit too -->
<PropertyGroup>
  <CodePage>65001</CodePage>
</PropertyGroup>
```

```powershell
# One-off repair: re-decode the CP1252 misreading back to UTF-8, with a UTF-8 BOM so
# no tool guesses again. Review the diff before committing — this rewrites source.
Get-ChildItem -Recurse -Include *.cs,*.xaml src |
  Where-Object { (Get-Content $_ -Raw) -match 'â†’|â€|Â ' } |
  ForEach-Object {
    $raw   = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    $bytes = [System.Text.Encoding]::GetEncoding(1252).GetBytes($raw)
    $fixed = [System.Text.Encoding]::UTF8.GetString($bytes)
    [System.IO.File]::WriteAllText($_.FullName, $fixed, [System.Text.UTF8Encoding]::new($true))
  }
```

**Regression test**

```csharp
[Fact]
public void No_source_file_contains_mojibake()
{
    string[] markers = { "â†’", "â€", "Ã¢", "Â ", "â”" };
    var offenders = new List<string>();
    foreach (var f in Directory.EnumerateFiles("src", "*.*", SearchOption.AllDirectories))
    {
        if (!f.EndsWith(".cs") && !f.EndsWith(".xaml") && !f.EndsWith(".resw")) continue;
        if (f.Contains("\\bin\\") || f.Contains("\\obj\\")) continue;
        var text = File.ReadAllText(f, Encoding.UTF8);
        foreach (var m in markers)
            if (text.Contains(m, StringComparison.Ordinal)) { offenders.Add($"{f}: {m}"); break; }
    }
    Assert.Empty(offenders);
}

[Fact]
public void Bengali_resources_round_trip_intact()
{
    var doc = XDocument.Load("src/BanglaHost.App/Strings/bn-BD/Resources.resw");
    foreach (var v in doc.Descendants("data").Select(d => d.Element("value")!.Value))
    {
        Assert.DoesNotContain('\uFFFD', v);                                  // replacement char
        Assert.True(v.Any(c => c >= '\u0980' && c <= '\u09FF') || v.All(char.IsAscii),
                    $"mixed/corrupt encoding in Bengali string: {v}");
    }
}
```

## H. Recommended fixes — prioritised

Ordering rule: **first make the product tell you the truth, then stop the bleeding, then fix the data-loss bugs, then the rest.** Diagnostics come first because right now you are flying blind — 33 crashes and 15 hangs with a single "Uncategorized" bucket. Everything after Phase 0 becomes measurable once Phase 0 lands.

Effort estimates are engineering days for one developer familiar with the codebase, including the regression test in each finding.

---

### Phase 0 — See what is happening (½ day)

Nothing here changes behaviour. Ship it first so Phases 1–4 can be *verified* rather than hoped for.

| # | Finding | Change | Effort |
|---|---|---|---|
| 0.1 | **D7** | `ThreadPool.SetMinThreads(max(cores×4, 16))` in `App` ctor and the CLI entry point | 15 min |
| 0.2 | **C8** | Move the three global handlers to the first statements of `App()`, before `ApplySavedLanguage()` | 30 min |
| 0.3 | **E5** | Cap + rotate the crash log; write a crash breadcrumb; add a `DiagnosticMode` setting that `FailFast`s so WER produces a real minidump | 3 h |
| 0.4 | **G2** | `dotnet sln add` the test project; add the CI workflow | 1 h |

0.1 is listed first deliberately: it is two lines, it cannot regress anything, and it is the single largest expected reduction in the 8.82% hang rate before any real refactoring. It is a **mitigation**, not the fix — Phase 2 is the fix.

---

### Phase 1 — Stop the data loss and the broken features (2–3 days)

These are the Critical findings where a user loses work or is actively misinformed. Every one of them is a small, local change.

| # | Finding | Change | Effort |
|---|---|---|---|
| 1.1 | **A2** + **F6** | Implement `RestoreAsync` for real, return `RestoreResult`, consume it at both `BackupPage` call sites | 1 d |
| 1.2 | **A3** | Fix `BackupAllAsync`'s hardcoded `mysqldump` path; fail loudly when the dump is missing; never report success for a DB-less backup | 3 h |
| 1.3 | **A5** | Atomic `Config.Save()` (temp + `Flush(true)` + `File.Replace` with `.bak`); stop `Load()` failing soft into defaults | 4 h |
| 1.4 | **A4** | Replace the `<` redirect in SQL Studio with stdin piping; move it off the UI thread | 4 h |
| 1.5 | **A6** | `ProcessOwnership` policy so the CLI does not kill the services it started (**E4**'s supervisor is the vehicle) | 1 d |
| 1.6 | **A7** | Move `JobObject` creation out of a static field initializer; degrade to pid tracking instead of poisoning all 11 spawn paths | 4 h |
| 1.7 | **F1** | Four-component version comparison; read the **package** version when packaged | 2 h |
| 1.8 | **F2** | Disable the Let's Encrypt button, remove the fake 2 s delay, label it as planned | 1 h |

1.7 and 1.8 are trivial and disproportionately visible — 1.7 stops every user being told to update to the version they already have.

---

### Phase 2 — Fix the hangs properly (4–6 days)

This is the real remediation for the 8.82% hang rate. Do it in this order; each step makes the next one smaller.

| # | Finding | Change | Effort |
|---|---|---|---|
| 2.1 | **C12** + **A1** | Add `NetUtils.IsListeningAsync` (cancellable, no `.Wait`, no dispose-mid-connect). Convert every probe in `DbServer`, `Nginx`, `Apache`, `Mailpit`, `CacheServers`, `SearchServers`, `AiServers` | 1 d |
| 2.2 | **D4** + **D3** | `Engine.ApiAsync(ct)`: probes concurrent under one ~1.2 s budget, `ActiveEngine` computed once | 1 d |
| 2.3 | **D1** + **D2** | Cache `Config.Load()` and `Services.EnabledSet()` behind watcher invalidation | 4 h |
| 2.4 | **A1** | `Refresh()` → `async Task RefreshAsync(ct)`; `SemaphoreSlim(1)` gate; supersede in-flight snapshots; drop the tick if one is running | 1 d |
| 2.5 | **C2** | Shared `ProcRunner.RunAsync(psi, timeout, ct)` — concurrent pipe reads, kill on timeout. Convert all 26 unbounded `WaitForExit()` sites; make `Elevation.RunAsync` non-blocking so UAC cannot hang the app | 1.5 d |
| 2.6 | **C4** + **C5** | Replace `curl.exe` with a streaming `HttpClient` download; route `Downloader.Shell` through `ProcRunner` (kills the stderr-before-stdout deadlock) | 1 d |
| 2.7 | **C3** | Decouple the auto-backup from `DbServer.Stop()`: `.partial` → rename-on-success, `.complete` marker for retention | 4 h |
| 2.8 | **C1** | Convert the 9 offending pages to async loading; add the `Threading.AssertNotUiThread` tripwire (**E2**) so this cannot recur | 1 d |

After 2.4 and 2.8, re-measure. If the hang rate has not fallen substantially, the remaining cause is not in this list and Phase 0's minidumps will show it.

---

### Phase 3 — Security (3–4 days)

**B1 is the one that matters most** and is also the largest single item. Everything else here is small.

| # | Finding | Change | Effort |
|---|---|---|---|
| 3.1 | **B1** | Pin a SHA-256 for every downloaded artefact in a signed-in-repo manifest; verify before extract *and* before first execute; verify Authenticode where the vendor signs | 2 d |
| 3.2 | **B2** | Replace the three HTML-scraped URLs with pinned versions in the same manifest | 3 h |
| 3.3 | **B3** + **B4** | `--defaults-extra-file` for every MySQL/MariaDB invocation; no password ever on a command line | 4 h |
| 3.4 | **B5** | Validate the IP inside `BanglaHost.Elevate` — the elevated helper must not trust its caller | 1 h |
| 3.5 | **B7** | Validate `domain` in `SslService` before it reaches a path or an mkcert argument | 2 h |
| 3.6 | **B8** + **B9** | Parameterise or strictly validate identifiers; stop building SQL by interpolation | 3 h |
| 3.7 | **B10** | DPAPI-protect `RootPassword` at rest; explicit DACL on the config directory | 3 h |
| 3.8 | **B11** + **B12** + **B13** | Absolute paths from `%SystemRoot%\System32` for `powershell`/`cmd`; `ArgumentList` everywhere; drop `cmd.exe /c` from `InstallerService` | 4 h |
| 3.9 | **B6** + **B14** | Exact-match `Hosts.Remove`; make `IsPortAvailable` fail **closed** | 2 h |
| 3.10 | **C13** | Job-track and pid-file `cloudflared`; clean up a stray tunnel at startup | 4 h |

3.10 is in this phase rather than Phase 2 because an orphaned quick tunnel is a live public route into the user's machine, which makes it a security issue as much as a leak.

---

### Phase 4 — Quality, architecture and debt (ongoing)

| # | Finding | Change | Effort |
|---|---|---|---|
| 4.1 | **F4** | `AutomationProperties` across all 50 views; non-colour status channel; live regions; CI gate | 3 d |
| 4.2 | **F5** + **C7** | Migrate to MRT (`x:Uid` + `.resw`) view by view; delete `Localizer` and `RewalkAfterAsync` | 4 d |
| 4.3 | **E3** | Async-first Core with `CancellationToken` throughout; one sanctioned `SyncBridge` for the CLI | 4 d |
| 4.4 | **C9** + **G4** | `BackgroundWork.RunGuarded` for every background loop; one shared `Ui.Handler`; `Refresh()` → `async Task` | 1 d |
| 4.5 | **C6** | Batch + cap `PackageManagerPage` output | 3 h |
| 4.6 | **C10** | Idempotent `TrayIcon.Dispose()`; single teardown owner; release `hIcon` and the window class | 3 h |
| 4.7 | **C11** | Bounded restart policy with backoff for the `PhpCgi` watchdog; surface the real cause | 4 h |
| 4.8 | **C14** | Assign `ProgressBar.Value` inline; guard on liveness, log instead of swallowing | 2 h |
| 4.9 | **D5** + **D6** | Wire up `Paths.CleanTmp()`; dispose certificates | 3 h |
| 4.10 | **E1** | Delete the duplicate extensions page and its nav entry | 2 h |
| 4.11 | **G1** | Validate + quote the nginx `root` value | 1 h |
| 4.12 | **G3** + **G5** + **G6** | Triage empty catches with a ratchet test; `.gitignore` `publish-msix/`; repair mojibake + `.editorconfig` `charset = utf-8` | 1 d |
| 4.13 | **F3** | Read the configured TLD in `SslPage` | 1 h |
| 4.14 | **E2** | Ban direct Core calls from `Views/` in CI | 3 h |

**Total: roughly 25–30 engineering days**, of which Phases 0–2 (~8 days) address the crash and hang telemetry that prompted this audit.

---

## I. Test plan

The project currently has **3 test methods, in a project the solution does not build** (**G2**). This plan is therefore mostly greenfield. It is ordered so that the earliest tests are the ones that would have caught the findings in Phases 0–2.

### I.1 Wire up the harness (prerequisite)

1. `dotnet sln BanglaHost.sln add src/BanglaHost.Tests/BanglaHost.Tests.csproj`.
2. Add `BanglaHost.Tests.Ui` (WinUI test host, `net8.0-windows10.0.19041.0`, `Microsoft.WindowsAppSDK` + `xunit`) — Sections C, F and the UI-thread assertions need a real dispatcher.
3. Add the CI workflow from **G2** and make it required on `main`.
4. Build the fixtures the tests in this report assume:

| Fixture | Purpose |
|---|---|
| `TestEnv` | Redirects `Paths.Home` to a temp dir; writes `config.json`, php.ini, cert and backup fixtures; `IDisposable` cleanup |
| `UiTest` | Runs a body on a real WinUI dispatcher; `Frame` navigation; `ShowAsync(page)`; `CaptureDialog` |
| `UiTest.StartPumpWatchdog` | Posts a sentinel to the dispatcher every 100 ms and records the longest gap → **the hang oracle** |
| `ProcessLaunchCounter` | Counts `CreateProcess` for a named image → **C11**, **A6** |
| `FileIoCounter` | Counts reads of a given path → **D1**, **D2** |
| `TcpProbeCounter` | Counts loopback connects per port → **D3**, **D4** |
| `env.BlackholeLoopbackPorts` | Makes loopback connects hang instead of refuse → reproduces the AV condition behind **A1**/**D4**/**D7** |
| `NativeHandles.Count()` | Process handle count → **C10**, **D6** |
| `DotnetCounters` | `threadpool-queue-length` / `thread-count` → **D7** |
| `FakeStore` | Serves a chosen version to `Updater` → **F1** |
| `WerDumps` | Counts minidumps in the WER queue → **E5** |

`env.BlackholeLoopbackPorts` is the highest-value fixture in the list: it is the environmental condition (documented in the project's own `ANTIVIRUS.md`) that turns the blocking probes into the shipped hang, and without it none of the hang tests reproduce on a developer machine.

### I.2 Unit tests (Core, no processes)

| Area | Cases |
|---|---|
| Version comparison (**F1**) | The 7 theory rows in F1, incl. `1.6.0.0` vs `1.6.0` ⇒ equal |
| Config atomicity (**A5**) | Save→load round-trip; kill mid-write ⇒ old value survives; corrupt JSON ⇒ `.bak` used, not defaults; concurrent saves |
| Config caching (**D1**) | 1000 loads ⇒ ≤2 file reads; save invalidates; locked file retried |
| `Services.EnabledSet` (**D2**) | One read per snapshot; add/remove reflected after invalidation |
| Domain/IP validation (**B5**, **B7**, **G1**) | Injection corpus: `;`, newline, `..`, `"`, `|`, `&&`, `%00`, UNC, drive-relative, unicode look-alikes |
| SQL identifier validation (**B8**, **B9**) | Backtick, quote, `--`, `/*`, `\`, `NO_BACKSLASH_ESCAPES` behaviour |
| `Hosts.Remove` (**B6**) | `app.test` must not remove `myapp.test` or `app.test.local` |
| `IsPortAvailable` (**B14**) | Free ⇒ true; in use ⇒ false; **enumeration throws ⇒ false (fail closed)** |
| Download manifest (**B1**) | Every entry has a pinned version + SHA-256; wrong hash ⇒ reject before extract; truncated file ⇒ reject |
| `Paths.CleanTmp` (**D5**) | Age policy; size cap; locked file tolerated; never throws |
| Nginx/Apache config render (**G1**, **§19**) | Golden-file comparison; hostile root rejected; `listen` always loopback |

### I.3 Integration tests (real processes, real ports)

Run serially, in a dedicated `TestEnv`, on a self-hosted runner (these need real binaries).

| Scenario | Assertion |
|---|---|
| Install → start → probe → stop, per service | Port listening after start, released after stop, no orphan process |
| Process ownership (**A6**, **E4**) | GUI host exit ⇒ children die; CLI `start` then exit ⇒ children survive |
| Job object unavailable (**A7**) | Simulated `CreateJobObject` failure ⇒ pid-file fallback; all 11 spawn sites still work; no `TypeInitializationException` |
| PHP version switch (**§38**) | Each installed version's `php-cgi` on its own port; switch and re-probe; no cross-version ini bleed |
| Nginx ⇄ Apache switch (**§39**) | Port 80 handed over cleanly; both never bound simultaneously; vhosts regenerated |
| Site create/delete (**§40**) | Vhost written, hosts entry added, cert generated, site reachable; delete reverses all four exactly |
| Backup → restore round-trip (**A2**, **A3**) | Files *and* databases present in the archive; restore into an empty env reproduces byte-identical files and row-identical tables; corrupt archive ⇒ `Ok == false` |
| Auto-backup retention (**A3**) | Only `.complete` backups counted; a `.partial` is never promoted or pruned as if complete |
| Tunnel lifecycle (**C13**) | Host exit ⇒ `cloudflared` dies; stray pid ⇒ cleaned at startup; start cancellable in <5 s |
| Mailpit (**§37**) | SMTP accepts a message, it appears in the inbox API, port released on stop |
| SQL Studio (**A4**) | `SELECT 1;` returns a result set — the shipped build returns none |
| Elevation (**C2**, **B5**) | UAC declined ⇒ caller returns promptly, no hang; injection payload in the IP argument ⇒ rejected by the helper |

### I.4 UI tests (dispatcher-hosted)

The hang oracle: **`pump.LongestStall < 2 s` is asserted in every one of these.**

| Scenario | Assertion |
|---|---|
| Dashboard 5-minute soak, all services stopped, **loopback blackholed** | No stall > 2 s; ≤1 in-flight snapshot at any time (**A1**, **D7**) |
| Dashboard, services flapping (start/stop loop) | No stall; no unobserved exceptions; no `CrashLogger` entries |
| Rapid navigation across all ~50 pages, 3 passes | No stall > 2 s; no page throws; ≤2 localizer walks per navigation (**C7**) |
| Navigate away mid-refresh, 40 iterations | No `ProgressBar` exception recorded (**C14**); no leaked event subscriptions |
| `PackageManagerPage` with 10 000 output lines | No stall; retained lines ≤ cap; flush count ≪ line count (**C6**) |
| SSL page with 200 certificates | Loads off-thread; native handle count stable across 50 loads (**C1**, **D6**) |
| Tray quit | `Dispose()` body runs exactly once; handle count does not grow (**C10**) |
| Restore failure | A "Restore failed" dialog is shown (**F6**) |
| Startup with a corrupt `config.json` and a bad language tag | Window appears; failures logged, not fatal (**C8**, **A5**) |
| Bengali mode, every page | No untranslated visible string; no English→Bengali flicker after load (**F5**) |

### I.5 Accessibility (**F4**)

1. **Static:** the XAML scan in F4 — every interactive element has an accessible name (CI gate).
2. **UIA tree scan:** for each of the ~50 pages, assert no focusable element has an empty `Name`, and every status indicator exposes state as text.
3. **Manual, per release:** Accessibility Insights for Windows — full pass on Dashboard, Sites, Services, Databases, SSL, Settings. Narrator walkthrough of the primary flow (create a site → enable SSL → open it). Keyboard-only completion of the same flow. 200% display scaling and High Contrast on the same six pages.

### I.6 Security tests

1. **Injection corpus** (shared fixture) replayed against every external-input boundary: site name, site root, domain, IP, database name, SQL text, PHP version string, backup path, tunnel hostname. Assertion: rejected at validation, or passed as a single `ArgumentList` element — never concatenated into a command line.
2. **Command-line audit** (static, CI): no `ProcessStartInfo.Arguments` string assignment outside an allow-list; no `cmd.exe /c`; no bare-name executables; no `-p` + password pattern.
3. **Download integrity** (**B1**): tamper each artefact in a local mirror ⇒ install must fail closed. Verify no binary is executed before its hash is checked.
4. **Secrets at rest** (**B10**): after configuring a DB password, grep the whole `Paths.Home` tree for the plaintext ⇒ zero hits.
5. **Permissions** (**§10**): assert the DACL on `Paths.Home` and on the config file — no `Everyone`/`Users` write. *(The inherited DACL on a fresh install is **Requires runtime verification**.)*
6. **Network exposure** (**§15**): with all services up, scan from a second machine ⇒ no BanglaHost-managed port reachable off-host. Assert every generated `listen` is loopback.
7. **Elevated helper** (**B5**): fuzz `BanglaHost.Elevate`'s arguments; it must validate independently of its caller.

### I.7 Soak and telemetry validation

| Test | Duration | Assertion |
|---|---|---|
| Idle on Dashboard | 8 h | Private bytes and handle count flat within 10%; `config.json` read count < 100 total (**D1**); no stall |
| Dashboard + all services up, loopback blackholed | 2 h | No hang; `threadpool-queue-length` peak < 50 (**D7**) |
| Repeated install/uninstall of every service | 20 cycles | No orphan processes; temp directory bounded (**D5**) |
| Broken `php.ini` | 30 min | `php-cgi` launch count ≤ 6, then latched with the real cause logged (**C11**) |
| MSIX install → launch → update → launch | per release | Settings and sites survive; no duplicate tray icon; no stray tunnel |
| **Telemetry validation** (**E5**) | per release | In `DiagnosticMode`, a forced fault produces a WER minidump *and* a Partner Center bucket that is **not** "Uncategorized" — this is the acceptance test for the whole audit |

### I.8 Release gate

A build ships only if: CI green; the injection corpus passes; the two hang soaks pass; the accessibility CI gate passes; the backup→restore round-trip passes; and a forced fault in `DiagnosticMode` produces a categorised failure in Partner Center.

## J. Release readiness score — 35/100

Weighted against what a paid-tier local development environment on the Microsoft Store has to get right. Weights are assigned before scoring, from the risk each category carries for *this* product: a tool that manages other people's databases and web servers is judged hardest on stability, on not destroying data, and on what it downloads and executes.

| # | Category | Weight | Score | Basis |
|---|---|---:|---:|---|
| 1 | Crash / hang stability | 20 | **6** | 8.82% hang rate and 4.41% crash rate in the field. A1 re-enters an unguarded `async void` every 2 s on the default landing page; 26 of 31 `WaitForExit()` calls are unbounded; no `ThreadPool.SetMinThreads`; 9 pages call Core on the dispatcher. Credit for the genuinely good global handlers in `App.xaml.cs` and for `CrashLogger` existing at all |
| 2 | Security | 15 | **5** | B1: ~30 binaries downloaded and executed with zero hash or signature verification — the single highest-impact finding in the report. 3 URLs scraped from HTML. DB root password on the command line at ~6 sites and plaintext at rest. Unvalidated IP inside the *elevated* helper. Credit for loopback-only bindings, `ArgumentList` in most places, bsdtar mitigating zip-slip, DPAPI being used where it is used, and Microsoft-signed `curl`/`tar` from `%SystemDirectory%` |
| 3 | Data integrity (backup / restore / config) | 15 | **3** | Three independent silent-data-loss paths: `RestoreAsync` returns success having restored nothing (A2); `BackupAllAsync` produces database-less archives and reports success (A3); `Config.Save()` is non-atomic and `Load()` fails soft into defaults, so a torn write silently discards every setting including the DB password (A5). Credit for the auto-backup feature existing and for retention being bounded |
| 4 | Core feature correctness | 10 | **5** | SQL Studio never executes a statement (A4). CLI `start` kills what it starts (A6). "Update available" fires on essentially every launch (F1). Let's Encrypt is an enabled button that fakes 2 s of work and fails (F2). Credit: the large majority of the ~50 pages and 37 services do work as advertised |
| 5 | Process lifecycle / orphans | 10 | **5** | A7 can poison all 11 spawn paths with a permanently-cached `TypeInitializationException`. `cloudflared` is neither job-tracked nor disposed — an orphan leaves a live public route into the machine. The `PhpCgi` watchdog can respawn itself recursively. Credit for using Job Objects with `KILL_ON_JOB_CLOSE` at all — that is more than most tools in this category do |
| 6 | Performance / resource use | 8 | **4** | `Config.Load()` uncached and re-deserialised on every call; the enabled-services file read 37× per snapshot; probes sequential rather than concurrent; `CleanTmp()` written and never called; certificates never disposed. All are bounded and none is pathological on its own |
| 7 | Architecture / maintainability | 7 | **3** | E3 (synchronous-only Core) is the single root cause visible in eight separate findings. `EngineHost`'s off-thread contract is advisory and routinely bypassed. Process ownership lives in a `static` with no lifetime owner. Two divergent PHP-extensions pages ship simultaneously. Credit for a clean Core/App/Cli/Elevate split and a genuinely separate elevation helper |
| 8 | Testing & CI | 5 | **1** | 3 test methods, in a project the solution does not build, with no CI of any kind. One of those three tests covers exactly the method that B14 finds fails open |
| 9 | Diagnostics / observability | 5 | **1** | The reason this audit exists: 48 failures, 100% "Uncategorized". Exceptions are swallowed so WER never produces a minidump; the crash log is local-only and unrotated |
| 10 | Accessibility | 3 | **1** | `AutomationProperties` in 0 of 50 views; status conveyed by colour alone. Credit for WinUI deriving default names from text content, which covers some buttons by accident |
| 11 | Localization | 2 | **1** | 61 strings, `x:Uid` in one file, the rest via a runtime visual-tree walker with timed re-walks. Bengali coverage is real but partial, and hardcoded English plurals remain on the default page |
| | **Total** | **100** | **35** | |

### What this score is and is not

It is a measure of *release readiness*, not of effort or ambition. The feature surface here — 37 managed services, ~50 pages, Cloudflare tunnels, an elevation helper, a CLI — is larger than most commercial tools in this space, and the code is in many places thoughtful (the global exception handlers, the Job Object usage, `ArgumentList` discipline, the atomic-write helper that exists but is not used by `Config`). The score is low because readiness is gated by worst-case behaviour, and the worst cases here are a hang on the default page, three silent-data-loss paths, and unverified binaries being executed.

It is also **not** a score for the shipped build the telemetry describes. §0 establishes that the 33 crashes and 15 hangs come from 1.4.x; this is 1.6.0.0.

### Release blockers

Do not ship 1.6.0.0 to the Store with these open. Each is Critical, each is a small local change, and together they are Phase 0 plus Phase 1 — under four days.

1. **A5** — non-atomic config save. A power loss during a settings write silently discards everything, including the database password.
2. **A2** — restore reports success without restoring. A user who needs this feature discovers it is fake at the worst possible moment.
3. **A3** — backups silently exclude databases and report success. A2 and A3 together mean the backup feature cannot be trusted in either direction.
4. **A6** — CLI `start` kills the services it just started.
5. **A7** — a Job Object failure poisons every process-spawn path for the rest of the session.
6. **A4** — SQL Studio does not execute SQL. A shipped feature that cannot work at all.
7. **D7** + **A1's guard** — two small changes that address the dominant field symptom.
8. **F1** — every user is told to update to the version they are already running.

### Projected trajectory

Scores recomputed against the same rubric after each phase in §H:

| After | Effort | Score | What moves |
|---|---:|---:|---|
| Phase 0 — diagnostics | ½ d | **42** | Stability 6→9, diagnostics 1→4, testing 1→2 |
| Phase 1 — data loss & broken features | +2–3 d | **58** | Data integrity 3→12, correctness 5→9, lifecycle 5→8 |
| Phase 2 — hangs fixed properly | +4–6 d | **69** | Stability 9→17, performance 4→7 |
| Phase 3 — security | +3–4 d | **78** | Security 5→13, lifecycle 8→9 |
| Phase 4 — quality & architecture | ongoing | **94** | Architecture 3→6, testing 2→5, a11y 1→3, and the remainder |

**58 after Phase 1 is the minimum shippable state** — no data loss, no dead features, no lifecycle poisoning, and enough diagnostics that the next failure arrives with a stack trace. **69 after Phase 2** is where the hang rate should be defensible in Partner Center. Phase 3 should follow within one release; B1 is the finding that would matter most if it were ever exploited, and it is the least likely of these to be noticed before it is.

### One-line verdict

A capable, unusually broad product held back by a handful of small, specific, entirely fixable defects — three of which quietly lose user data, one of which hangs the window every 2 s on the page every user lands on, and one of which executes ~30 unverified binaries. Roughly eight engineering days separate 35 from a genuinely shippable 69.

---

*End of audit. 45 findings: 7 Critical, 14 High, 18 Medium, 6 Low. Static source review of `I:\BanglaHost` @ `7d33255`; no build, no run, no instrumentation. Items that could not be settled from source are marked "Requires runtime verification" in place.*
