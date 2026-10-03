using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;

namespace BanglaHost.App
{

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        // ── thread-pool floor ────────────────────────────────────────────────────────────
        // The engine performs many short blocking waits (loopback probes, WaitForExit) on pool
        // threads. The default pool starts at ProcessorCount and injects only ~1-2 threads/sec,
        // so a burst of blocking work starves `await` continuations — including the ones the UI
        // thread is waiting on, which Windows then reports as an AppHang. Raising the floor makes
        // the pool absorb the burst instead of queueing it. This is a safety net; the real fix is
        // that the probes below no longer block (NetUtils.IsListeningAsync).
        try
        {
            ThreadPool.GetMinThreads(out var minW, out var minIo);
            var want = Math.Max(Environment.ProcessorCount * 4, 16);
            ThreadPool.SetMinThreads(Math.Max(minW, want), Math.Max(minIo, want));
        }
        catch { /* SetMinThreads only fails on absurd values; the default floor still works */ }

        // ── process ownership ────────────────────────────────────────────────────────────
        // The GUI owns the service processes it spawns: if it exits or crashes, they must not be
        // left orphaned holding ports 80/3306. (The CLI declares the opposite policy — see
        // BanglaHost.Cli/Program.cs.) Declared here, before anything can spawn.
        try { BanglaHost.Core.JobManager.Configure(killChildrenOnExit: true); } catch { }

        // ── global exception handlers, wired FIRST ───────────────────────────────────────
        // These must be attached before ANY other work in the constructor. ApplySavedLanguage()
        // touches config + the globalization API and InitializeComponent() parses App.xaml; a
        // throw from either used to happen while no handler was attached, which is an instant
        // silent process death with no log entry — exactly the "Uncategorized" bucket.
        // The UI UnhandledException handler decides between RECOVER and CRASH. Both paths log;
        // only the recover path swallows.
        //
        // Why this is not "always swallow": `e.Handled = true` suppresses the WER fault, so
        // Windows never writes a minidump and the Store receives only "Uncategorized" hits with
        // no stack — which is exactly the 1 Crash / 3 Hangs bucket (100% uncategorized) this
        // build is fixing. A swallowed exception that keeps firing also leaves the app in a
        // wedged half-state that Windows eventually reports as an AppHang anyway.
        //
        // So we swallow ONLY the narrow set of exceptions that are known to be survivable —
        // they all mean "the UI object I wanted is already gone", and the correct response is to
        // drop that one callback, not to kill the process. Anything else is a genuine fault:
        // we log it and then let it terminate so the crash is diagnosable in the field.
        //
        // BANGLAHOST_DIAGNOSTIC=1 forces every exception down the terminating path, so a
        // developer can get a full fault for an exception that would normally be absorbed.
        this.UnhandledException += (s, e) =>
        {
            BanglaHost.App.Services.CrashLogger.Log(e.Exception, "UI UnhandledException");

            if (BanglaHost.App.Services.CrashLogger.DiagnosticMode)
            {
                e.Handled = false;   // let WER capture it
                return;
            }

            if (IsRecoverableUiException(e.Exception))
            {
                e.Handled = true;    // drop this one callback; the window stays usable
                return;
            }

            // Unhandled by design: fault the process so the crash carries a stack.
            try { BanglaHost.App.Services.CrashLogger.WriteCrashBreadcrumb(e.Exception); } catch { }
            BanglaHost.App.Services.CrashLogger.Flush();
            try { BanglaHost.Core.JobManager.Shutdown(); } catch { }   // don't orphan services on the way down
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            // NOTE: on .NET Core this handler CANNOT prevent termination — the runtime tears the
            // process down as soon as it returns. It exists only to leave a breadcrumb. Anything
            // that must survive has to be caught closer to the throw; background loops go through
            // BackgroundWork.RunGuarded for that reason.
            if (e.ExceptionObject is Exception ex)
                BanglaHost.App.Services.CrashLogger.Log(ex, e.IsTerminating ? "AppDomain UnhandledException (TERMINATING)" : "AppDomain UnhandledException");
            else
                BanglaHost.App.Services.CrashLogger.Log(new Exception("Unknown non-CLR failure object"), "AppDomain UnhandledException (TERMINATING)");
            try { BanglaHost.App.Services.CrashLogger.WriteCrashBreadcrumb(e.ExceptionObject as Exception); } catch { }
            BanglaHost.App.Services.CrashLogger.Flush();
            
            if (e.IsTerminating)
            {
                Environment.FailFast("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            // Non-actionable background exceptions — observe silently to prevent process termination.
            // COMException: WinUI layout races on disposed controls; ObjectDisposedException: timer
            // callbacks after page unload; OperationCanceledException: normal cancellation flow.
            var inner = e.Exception.InnerException;
            if (inner is System.Net.Sockets.SocketException or ArgumentException
                      or System.Runtime.InteropServices.COMException
                      or ObjectDisposedException or OperationCanceledException)
            {
                e.SetObserved();
                return;
            }
            BanglaHost.App.Services.CrashLogger.Log(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };

        // Now that failures are observable, do the work that can actually fail.

        // Re-apply the user's chosen UI language BEFORE any XAML loads. WinUI resolves x:Uid strings
        // at control-construction time against the primary-language override, and that override is
        // NOT reliably persisted for an unpackaged app across the Settings "restart". Without this,
        // picking Bangla in Settings silently reverts to English on the very next launch.
        ApplySavedLanguage();

        InitializeComponent();
    }

    /// <summary>
    /// True for the narrow set of exceptions raised by a WinUI callback against an object that
    /// has already been torn down (page navigated away, window closed, XAML tree disconnected).
    /// These are the classic source of the "async void on a dead page" storm; dropping the single
    /// callback is correct and the app stays usable.
    ///
    /// Everything else — NullReferenceException, InvalidOperationException from a real state
    /// bug, an I/O failure on startup — is NOT recoverable here and must fault so the crash
    /// carries a stack into WER/Partner Center instead of being absorbed into "Uncategorized".
    /// </summary>
    private static bool IsRecoverableUiException(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                // Normal cancellation flow — never a fault.
                case OperationCanceledException:
                // The target UI object is gone; there is nothing left to update.
                case ObjectDisposedException:
                    return true;

                // WinUI/COM marshalling failures are almost always a dispatcher-reentrancy or
                // torn-down-XamlRoot race. One callback is lost; the window is still fine.
                case System.Runtime.InteropServices.COMException com:
                    // RPC_E_DISCONNECTED (0x80010108) / RO_E_CLOSED (0x80000013) /
                    // E_ILLEGAL_METHOD_CALL (0x8000000E) — the WinRT object is disconnected.
                    const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
                    const int RO_E_CLOSED = unchecked((int)0x80000013);
                    const int E_ILLEGAL_METHOD_CALL = unchecked((int)0x8000000E);
                    if (com.HResult is RPC_E_DISCONNECTED or RO_E_CLOSED or E_ILLEGAL_METHOD_CALL)
                        return true;
                    break;

                // WinUI throws this for a control that is already in the tree or already loaded.
                // Layout-only; the page renders, so absorb it.
                case InvalidOperationException inv
                    when inv.Message.Contains("XamlRoot", StringComparison.OrdinalIgnoreCase)
                      || inv.Message.Contains("already", StringComparison.OrdinalIgnoreCase)
                      || inv.Message.Contains("dispatcher", StringComparison.OrdinalIgnoreCase):
                    return true;
            }
        }
        return false;
    }

    // The inline LogCrash has been moved to Services.CrashLogger

    /// <summary>Read the saved UI language from config and set it as the primary-language override
    /// (both the WinAppSDK and the classic WinRT API — one of the two applies depending on packaging).
    /// Values are BCP-47 tags matching the Strings\&lt;lang&gt; resw folders ("en-US", "bn-BD"). Best-effort:
    /// any failure just leaves the system default in place.</summary>
    private static void ApplySavedLanguage()
    {
        try
        {
            var lang = BanglaHost.Core.Config.Load().Language;
            if (string.IsNullOrWhiteSpace(lang)) return;
            try { Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang; } catch { }
            try { Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang; } catch { }
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        // Launched with --tray (autostart at login) → run in the TRAY ONLY: never show the window
        // and keep it out of the taskbar/Alt-Tab. The old code Activate()'d then Minimize()'d, which
        // flashed the window on screen and left a taskbar button. The tray icon is the only UI until
        // the user opens it.
        var startInTray = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (startInTray) Window.StartHiddenInTray();
        else             Window.Activate();

        // Auto-repair the Windows "localhost" DB stall on imported sites (idempotent, best-effort) so
        // users don't have to touch any config — pages that felt like they loaded from a remote server
        // become instant. New BanglaHost sites already use 127.0.0.1.
        BanglaHost.App.Services.BackgroundWork.RunGuarded(
            () => BanglaHost.Core.SiteDbHostFix.Run(BanglaHost.Core.Config.Load().SitesRoot),
            "SiteDbHostFix");

        // A crash leaves cloudflared tunnels (live public ingress) behind — reap them on launch
        // instead of leaving a silent route into the machine.
        BanglaHost.App.Services.BackgroundWork.RunGuarded(
            () => BanglaHost.Core.Tunnel.CleanupStrayTunnels(),
            "TunnelCleanup");

        // Optionally bring all services up on launch (Settings → Start services when BanglaHost launches).
        if (BanglaHost.Core.Config.Load().StartServicesOnLaunch)
            BanglaHost.App.Services.BackgroundWork.RunGuarded(
                () => BanglaHost.App.Services.EngineHost.Instance.Engine.Start("all"),
                "StartServicesOnLaunch");
    }

    /// <summary>Fully exit the app — including the tray — bypassing the "hide to tray on close"
    /// behavior. Used by the self-updater so the running BanglaHost.App.exe / Core.dll unlock and the
    /// installer can replace them (otherwise the close request just hides the window to the tray and
    /// the installer reports it couldn't close the app).</summary>
    public static void ForceQuit()
    {
        Window?.QuitForUpdate();
        Application.Current.Exit();
    }
}

}
