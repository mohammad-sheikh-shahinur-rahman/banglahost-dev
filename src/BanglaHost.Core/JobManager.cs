using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private static readonly List<int> _trackedPids = new();

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
            lock (_gate)
            {
                EnsureInit();

                var added = _job?.TryAddProcess(process) ?? false;
                if (!added)
                {
                    try { if (!process.HasExited) _trackedPids.Add(process.Id); } catch { }
                }
                else
                {
                    // Track it as well — cheap, and lets Shutdown report what it owns.
                    try { _trackedPids.Add(process.Id); } catch { }
                }
            }
        }
        catch
        {
            // Bookkeeping must never break a service start.
        }
    }

    /// <summary>
    /// Kill everything we own. Called on GUI shutdown. Safe to call more than once, and safe to
    /// call when ownership is detached (in which case it does nothing — the CLI's whole point is
    /// that its children outlive it).
    /// </summary>
    public static void Shutdown()
    {
        lock (_gate)
        {
            if (!_killChildrenOnExit) return;

            // Disposing a KILL_ON_JOB_CLOSE job kills every member atomically. This is the
            // primary path and handles the case where a child spawned grandchildren.
            _job?.Dispose();
            _job = null;
            _initialised = false;

            // Belt and braces for anything that only made it into pid tracking.
            foreach (var pid in _trackedPids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (!p.HasExited) p.Kill(entireProcessTree: true);
                }
                catch { /* already gone, or not ours any more */ }
            }
            _trackedPids.Clear();
        }
    }

    /// <summary>Pids we believe we own, for diagnostics and the stray reaper.</summary>
    public static int[] TrackedPids { get { lock (_gate) return _trackedPids.ToArray(); } }

    private static void EnsureInit()
    {
        if (_initialised) return;
        _initialised = true;   // set first: a failure must not retry on every single spawn
        _job = JobObject.TryCreate(_killChildrenOnExit, out _unavailableReason);
        if (_job == null && string.IsNullOrEmpty(_unavailableReason))
            _unavailableReason = "unknown";
    }
}
