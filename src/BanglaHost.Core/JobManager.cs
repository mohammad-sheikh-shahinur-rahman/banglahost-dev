using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace BanglaHost.Core;

/// <summary>
/// Owns the lifetime of every service process BanglaHost spawns.
///
/// Two problems this class exists to solve:
///
/// 1. <b>Ownership.</b> The GUI wants children killed when it exits, so a crash never leaves
///    nginx/MariaDB orphaned. The CLI wants the exact opposite: `banglahost start all` must leave
///    the stack running after the CLI process returns. Previously both used the same
///    KILL_ON_JOB_CLOSE job, so the CLI killed everything it had just started the instant it
///    exited. Ownership is now an explicit, per-host decision — see <see cref="Configure"/>.
///
/// 2. <b>Failure isolation.</b> The job object used to be built in a static field initializer, so
///    a failure surfaced as a TypeInitializationException thrown from the static constructor. The
///    CLR caches that permanently and rethrows it on every subsequent access, and because it is
///    thrown by the field access rather than inside the try block, the caller's catch never saw
///    it — one transient failure disabled all 11 process-spawn paths for the rest of the session.
///    Creation is now lazy, non-throwing, and degrades to pid tracking.
/// </summary>
public static class JobManager
{
    private static readonly object _gate = new();
    private static JobObject? _job;
    private static bool _initialised;
    private static bool _killChildrenOnExit = true;   // GUI default: safest
    private static string _unavailableReason = "";

    // Fallback bookkeeping when the OS won't give us a job object. Not as strong as a job
    // (a hard-killed host can't run cleanup) but it lets `Shutdown` and the next launch's
    // reaper find strays instead of leaving them forever.
    //
    // We record the image NAME alongside the pid, not just the pid. Windows recycles pids
    // aggressively: by the time the user closes the window, a pid recorded an hour ago can
    // belong to an unrelated process, and `Kill(entireProcessTree: true)` would then take out
    // something that was never ours. The name is captured by the spawner (which knows what it
    // started) so the kill stays verifiable — same contract as ProcessUtils.KillSafeChecked.
    private readonly record struct TrackedProcess(int Pid, string Name);

    private static readonly List<TrackedProcess> _tracked = new();

    // Hard ceiling on the fallback list. It is fed by the php-cgi watchdog and the service
    // start paths, so a machine that churns services all day would otherwise grow it without
    // bound and make shutdown slower every hour. Oldest entries fall off first.
    private const int MaxTracked = 512;

    /// <summary>True when the OS gave us a real job object. False means we're on pid tracking.</summary>
    public static bool JobObjectAvailable { get { lock (_gate) { EnsureInit(); return _job != null; } } }

    /// <summary>Why the job object is unavailable, for logging. Empty when it is available.</summary>
    public static string UnavailableReason { get { lock (_gate) { EnsureInit(); return _unavailableReason; } } }

    /// <summary>
    /// Declare who owns spawned processes. Call once, before any process is spawned.
    /// <paramref name="killChildrenOnExit"/>: true for the GUI (children die with the app), false
    /// for the CLI and any other short-lived host that starts long-lived services.
    /// </summary>
    public static void Configure(bool killChildrenOnExit)
    {
        lock (_gate)
        {
            if (_initialised && _killChildrenOnExit != killChildrenOnExit)
            {
                // Ownership changed after the job already exists — rebuild it so the new policy
                // actually applies. Disposing a non-kill job does not kill its members.
                if (_killChildrenOnExit)
                    throw new InvalidOperationException(
                        "JobManager.Configure(false) called after processes may already be in a kill-on-exit job. " +
                        "Configure ownership at host startup, before spawning anything.");
                _job?.Dispose();
                _job = null;
                _initialised = false;
            }
            _killChildrenOnExit = killChildrenOnExit;
        }
    }

    /// <summary>
    /// Adds a process to the global job object so it gets killed when the app closes or crashes.
    /// Never throws. When no job object is available the pid is tracked instead.
    /// </summary>
    public static void Add(Process process)
    {
        if (process is null) return;
        try
        {
            // Capture the image name BEFORE taking the lock — ProcessName is a handle query and
            // the old code held _gate across it on every spawn.
            string name;
            try { name = process.ProcessName; } catch { name = ""; }

            lock (_gate)
            {
                EnsureInit();

                var added = _job?.TryAddProcess(process) ?? false;
                if (!added)
                {
                    try { if (!process.HasExited) Track(process.Id, name); } catch { }
                }
                else
                {
                    // Track it as well — cheap, and lets Shutdown report what it owns.
                    try { Track(process.Id, name); } catch { }
                }
            }
        }
        catch
        {
            // Bookkeeping must never break a service start.
        }
    }

    /// <summary>Record a pid + image name, pruning entries that have already exited so the list
    /// stays proportional to what is actually running rather than to session length.</summary>
    private static void Track(int pid, string name)
    {
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].Pid == pid) return;              // already known
            if (!IsAlive(_tracked[i].Pid)) _tracked.RemoveAt(i);
        }
        _tracked.Add(new TrackedProcess(pid, name));
        while (_tracked.Count > MaxTracked) _tracked.RemoveAt(0);
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>
    /// Kill everything we own. Called on GUI shutdown. Safe to call more than once, and safe to
    /// call when ownership is detached (in which case it does nothing — the CLI's whole point is
    /// that its children outlive it).
    ///
    /// The job-object dispose is synchronous and fast (the kernel kills the members). The pid
    /// fallback is NOT: it opens a handle per pid and can block for seconds on a long-lived
    /// session — and it used to run inside <c>lock (_gate)</c> on the UI thread, which is one of
    /// the paths that produced the field AppHangs. The list is snapshotted under the lock, the
    /// lock is released, and the sweep continues underneath so the caller returns immediately.
    /// </summary>
    public static void Shutdown()
    {
        TrackedProcess[] sweep;
        lock (_gate)
        {
            if (!_killChildrenOnExit) return;

            // Disposing a KILL_ON_JOB_CLOSE job kills every member atomically. This is the
            // primary path and handles the case where a child spawned grandchildren.
            _job?.Dispose();
            _job = null;
            _initialised = false;

            sweep = _tracked.ToArray();
            _tracked.Clear();
        }

        if (sweep.Length == 0) return;

        // Belt and braces for anything that only made it into pid tracking. Off the caller's
        // thread so process teardown never pins the UI. Name-verified: a recycled pid whose
        // current owner isn't the process we recorded is left alone.
        System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var t in sweep)
            {
                try
                {
                    if (string.IsNullOrEmpty(t.Name))
                    {
                        // No name captured (the spawner couldn't read ProcessName). Fall back to
                        // killing only if the image path is under our own install root.
                        KillIfOurs(t.Pid);
                    }
                    else
                    {
                        ProcessUtils.KillSafeChecked(t.Pid, t.Name);
                    }
                }
                catch { /* already gone, or not ours any more */ }
            }
        });
    }

    /// <summary>Last-resort kill for an entry with no recorded image name: only fires when the
    /// process image lives under BanglaHost's own directories, so a recycled pid pointing at an
    /// unrelated application is never touched.</summary>
    private static void KillIfOurs(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return;
            string path;
            try { path = p.MainModule?.FileName ?? ""; } catch { return; }   // deny-by-default
            if (path.Length == 0) return;

            var home = Paths.Home;
            var app = AppContext.BaseDirectory;
            var ours =
                (home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase)) ||
                (app.Length > 0 && path.StartsWith(app, StringComparison.OrdinalIgnoreCase));
            if (ours) p.Kill(entireProcessTree: true);
        }
        catch { }
    }

    /// <summary>Pids we believe we own, for diagnostics and the stray reaper.</summary>
    public static int[] TrackedPids
    {
        get { lock (_gate) return _tracked.Select(t => t.Pid).ToArray(); }
    }

    private static void EnsureInit()
    {
        if (_initialised) return;
        _initialised = true;   // set first: a failure must not retry on every single spawn
        _job = JobObject.TryCreate(_killChildrenOnExit, out _unavailableReason);
        if (_job == null && string.IsNullOrEmpty(_unavailableReason))
            _unavailableReason = "unknown";
    }
}
