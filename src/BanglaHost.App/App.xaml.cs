using System;
using System.Linq;
using Microsoft.UI.Xaml;

namespace BanglaHost.App
{

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();

        this.UnhandledException += (s, e) =>
        {
            BanglaHost.App.Services.CrashLogger.Log(e.Exception, "UI UnhandledException");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                BanglaHost.App.Services.CrashLogger.Log(ex, "AppDomain UnhandledException");
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
    }

    // The inline LogCrash has been moved to Services.CrashLogger

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        // Launched with --tray (autostart at login) â†’ run in the TRAY ONLY: never show the window
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
        System.Threading.Tasks.Task.Run(() =>
        {
            try { BanglaHost.Core.SiteDbHostFix.Run(BanglaHost.Core.Config.Load().SitesRoot); } catch { }
        });

        // Optionally bring all services up on launch (Settings â†’ Start services when BanglaHost launches).
        if (BanglaHost.Core.Config.Load().StartServicesOnLaunch)
            System.Threading.Tasks.Task.Run(() =>
            {
                try { BanglaHost.App.Services.EngineHost.Instance.Engine.Start("all"); } catch { }
            });
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
