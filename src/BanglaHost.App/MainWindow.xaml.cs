using BanglaHost.App.Services;
using BanglaHost.App.Views;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App
{

public sealed partial class MainWindow : Window
{
    private readonly TrayIcon _tray;
    private bool _reallyQuit;
    private bool _trayHintShown;
    private Updater.Result? _pendingUpdate;   // an available update waiting to be offered (shown when the window is visible)
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };   // daily re-check for long-running (tray) instances

    public MainWindow()
    {
        InitializeComponent();

        // Bangla mode: translate every page's static text as it loads (the sidebar itself is
        // handled separately by MRT via x:Uid). No-op in English mode.
        ContentFrame.Navigated += ContentFrame_Navigated;

        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(icon)) { try { AppWindow.SetIcon(icon); } catch { } }

        _tray = new TrayIcon($"BanglaHost {Updater.CurrentVersion} — local web stack", icon);
        _tray.OpenRequested += () => DispatcherQueue?.TryEnqueue(ShowFromTray);
        _tray.QuitRequested += () => DispatcherQueue?.TryEnqueue(QuitApp);
        _tray.StartAllRequested   += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Start("all"); } catch { } });
        _tray.StopAllRequested    += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Stop("all"); } catch { } });
        _tray.RestartAllRequested += () => System.Threading.Tasks.Task.Run(() => { try { EngineHost.Instance.Engine.Restart("all"); } catch { } });

        // Close â†’ hide to tray when "keep running" is on (Settings); otherwise really quit.
        AppWindow.Closing += (_, e) =>
        {
            if (_reallyQuit || !Config.Load().MinimizeToTray) { _tray.Dispose(); return; }
            e.Cancel = true;
            AppWindow.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloon("BanglaHost is still running",
                    "Your sites stay up in the background. Click this icon to reopen — use the ^ to show hidden icons if you don't see it. Turn this off in Settings.");
            }
        };

        _ = FirstRunThenUpdateCheck();
        // Re-check once a day so an instance that stays open (tray/autostart) still notices updates
        // without needing a restart. Same gating + prompt as the launch check.
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateOnLaunch();
        _updateTimer.Start();
    }

    /// <summary>On launch: if it's a fresh install with no core stack, offer one-click setup; otherwise
    /// run the normal update check. (A fresh install is on the latest version, so the two never collide.)</summary>
    private async System.Threading.Tasks.Task FirstRunThenUpdateCheck()
    {
        // Wait for the XAML tree to be ready (XamlRoot set) so dialogs can show.
        for (int i = 0; i < 50 && (Content as FrameworkElement)?.XamlRoot is null; i++)
            await System.Threading.Tasks.Task.Delay(100);
        if (await OfferFirstRunSetup()) return;
        await CheckForUpdateOnLaunch();
    }

    /// <summary>First-run welcome: if the core stack (nginx + PHP + database + mkcert) isn't installed,
    /// offer to install + start it in one click — so users who skip the readme are ready to add sites.
    /// Returns true if this was a fresh install we handled (so we skip the update check).</summary>
    private async System.Threading.Tasks.Task<bool> OfferFirstRunSetup()
    {
        try
        {
            if (!AppWindow.IsVisible || (Content as FrameworkElement)?.XamlRoot is not { } xamlRoot) return false;
            var missing = EngineHost.Instance.Engine.MissingCore();
            if (missing.Count == 0) return false;

            var list = string.Join("\n", missing.Select(m => "        •  " + m.label));
            var ask = new ContentDialog
            {
                Title = "Welcome to BanglaHost — quick setup",
                Content = $"Before you can create sites, BanglaHost needs to install:\n\n{list}\n\nInstall them now? (one-time download, about a minute)",
                PrimaryButtonText = "Install now", CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot,
            };
            if (await ask.ShowAsync() != ContentDialogResult.Primary) return true;   // chose Later — still a handled first run

            // Offer to add Defender exclusions BEFORE anything downloads, so AV can't quarantine the
            // server binaries BanglaHost fetches. Defender-only (other AVs have no API â†’ manual, see README).
            var avDlg = new ContentDialog
            {
                Title = "Protect BanglaHost from antivirus (recommended)",
                Content = "BanglaHost downloads server programs (PHP, nginx, MariaDB, Redis…) that some antivirus engines wrongly flag and delete.\n\n" +
                          "Add BanglaHost's two folders to Windows Defender's exclusions now? Windows will ask for your permission.\n\n" +
                          "Using a different antivirus (ESET, Avast, Bitdefender…)? Add them manually — see the README's antivirus section.",
                PrimaryButtonText = "Add exclusions", CloseButtonText = "Skip",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot,
            };
            if (await avDlg.ShowAsync() == ContentDialogResult.Primary)
            {
                var (exOk, exMsg) = await System.Threading.Tasks.Task.Run(
                    () => BanglaHost.Core.WindowsDefender.AddExclusions(AppContext.BaseDirectory, BanglaHost.Core.Paths.Home));
                if (!exOk)
                    await new ContentDialog
                    {
                        Title = "Couldn't add the exclusions automatically",
                        Content = $"BanglaHost couldn't add the Windows Defender exclusions ({exMsg}).\n\n" +
                                  "Setup will continue. You can add them by hand anytime — see the README's antivirus section.",
                        CloseButtonText = "OK", XamlRoot = xamlRoot,
                    }.ShowAsync();
            }

            var progress = new ContentDialog
            {
                Title = "Setting up BanglaHost…",
                Content = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new ProgressRing { IsActive = true, Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "Installing nginx, PHP and the database. This takes about a minute…", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    },
                },
                XamlRoot = xamlRoot,
            };
            _ = progress.ShowAsync();
            await EngineHost.Instance.RunCaptured(() =>
            {
                EngineHost.Instance.Engine.Install("all");
                EngineHost.Instance.Engine.Start("all");
            });
            progress.Hide();

            var still = EngineHost.Instance.Engine.MissingCore();
            await new ContentDialog
            {
                Title = still.Count == 0 ? "BanglaHost is ready \U0001F389" : "Setup didn't fully finish",
                Content = still.Count == 0
                    ? "All set! Head to the Sites tab and add your first site."
                    : "These couldn't be installed:\n\n" + string.Join("\n", still.Select(m => "        •  " + m.label)) +
                      "\n\nYou can retry from the Services tab (check your antivirus if a download was blocked).",
                CloseButtonText = "OK", XamlRoot = xamlRoot,
            }.ShowAsync();
            return true;
        }
        catch { return false; }
    }

    /// <summary>On launch (auto-update on), ask the Microsoft Store what version is
    /// currently published for BanglaHost. If it's newer than what's running, stash it
    /// as _pendingUpdate and — if the window is already visible — offer the Store link
    /// right away. If BanglaHost started hidden (autostart), a tray balloon nudges the
    /// user; the dialog appears the first time they reopen the window.</summary>
    private async System.Threading.Tasks.Task CheckForUpdateOnLaunch()
    {
        try
        {
            if (!Config.Load().AutoUpdate) return;
            if (!Updater.AutomaticCheckDue()) return;
            Updater.StampAutomaticCheck();

            var r = await Updater.Check();
            if (!r.UpdateAvailable) return;

            _pendingUpdate = r;
            if (AppWindow.IsVisible) await ShowUpdatePromptIfPending();
            else
            {
                try
                {
                    _tray.ShowBalloon("BanglaHost update available",
                        $"Version {r.Latest} is on the Microsoft Store. Open BanglaHost to update.");
                }
                catch { }
            }
        }
        catch { /* silent — a transient network error shouldn't nag the user */ }
    }

    /// <summary>If an update is waiting, show the "open Store / later" prompt. Cleared after one show so
    /// it doesn't re-nag within a session (it re-checks next launch). Safe to call when nothing's pending.</summary>
    private async System.Threading.Tasks.Task ShowUpdatePromptIfPending()
    {
        if (_pendingUpdate is not { } r) return;
        if ((Content as FrameworkElement)?.XamlRoot is not { } xamlRoot) return;   // window not ready yet — retry on next show
        _pendingUpdate = null;

        var dlg = new ContentDialog
        {
            Title = $"Update available — BanglaHost {r.Latest}",
            Content = $"A new version of BanglaHost is on the Microsoft Store.\n\n" +
                      $"You have {Updater.CurrentVersion} · latest is {r.Latest}.\n\n" +
                      "Open the Store to install the update.",
            PrimaryButtonText = "Open Microsoft Store", CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot,
        };
        try
        {
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                Updater.OpenStore();
        }
        catch { /* another dialog already open — re-offered on the next check */ }
    }

    /// <summary>Autostart-at-login entry point: keep BanglaHost running in the tray ONLY. The window is
    /// never Activate()'d (so it never appears on screen and never gets a taskbar button), and we also
    /// Hide() it as a belt-and-suspenders. The tray icon is the only entry point until the user opens it.</summary>
    public void StartHiddenInTray() => AppWindow.Hide();

    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.Restore();
        Activate();
        _ = ShowUpdatePromptIfPending();   // if an update was found while hidden, offer it now that we're visible
    }

    private void QuitApp()
    {
        _reallyQuit = true;
        _tray.Dispose();
        Application.Current.Exit();
    }

    /// <summary>Self-updater path: mark a real quit (so the close handler doesn't hide to tray) and
    /// remove the tray icon. App.ForceQuit() then exits the process, unlocking the files for the installer.</summary>
    public void QuitForUpdate()
    {
        _reallyQuit = true;
        try { _tray.Dispose(); } catch { }
    }

    // ── Bangla localization: translate each content page as it appears ──────
    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (!Localizer.IsActive) return;
        if (e.Content is not FrameworkElement page) return;

        // Translate now (covers a page whose tree is already realized) and again on Loaded
        // (first realization), then a few delayed passes for content that services populate
        // asynchronously (e.g. "No sites yet.", status labels).
        Localizer.Localize(page);
        page.Loaded += Page_Loaded_Localize;
    }

    private void Page_Loaded_Localize(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement page) return;
        page.Loaded -= Page_Loaded_Localize;
        Localizer.Localize(page);
        _ = RewalkAfterAsync(page);
    }

    private static async System.Threading.Tasks.Task RewalkAfterAsync(FrameworkElement page)
    {
        foreach (var ms in new[] { 250, 800, 1800 })
        {
            await System.Threading.Tasks.Task.Delay(ms);
            try { if (page.XamlRoot != null) Localizer.Localize(page); }
            catch { }
        }
    }

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        // First real menu entry (skip the "Overview" header)
        foreach (var m in Nav.MenuItems)
            if (m is NavigationViewItem nvi) { Nav.SelectedItem = nvi; break; }
        ContentFrame.Navigate(typeof(DashboardPage));
        try { VersionLabel.Text = $"v{Updater.CurrentVersion}"; } catch { }
    }

    // ── sidebar search: filter items live by their text label ──────────────
    private void NavSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        var hits = new System.Collections.Generic.List<string>();
        foreach (var m in Nav.MenuItems)
            if (m is NavigationViewItem { Content: string label } &&
                (q.Length == 0 || label.Contains(q, System.StringComparison.OrdinalIgnoreCase)))
                hits.Add(label);
        sender.ItemsSource = hits;
    }

    private void NavSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not string label) return;
        foreach (var m in Nav.MenuItems)
            if (m is NavigationViewItem nvi && nvi.Content?.ToString() == label)
            {
                Nav.SelectedItem = nvi;
                break;
            }
        sender.Text = "";
    }

    // ── pane-footer quick actions ───────────────────────────────────────────
    private async void StartAll_Click(object sender, RoutedEventArgs e)
    {
        try { await EngineHost.Instance.RunCaptured(() => EngineHost.Instance.Engine.Start("all")); } catch { }
    }

    private async void StopAll_Click(object sender, RoutedEventArgs e)
    {
        try { await EngineHost.Instance.RunCaptured(() => EngineHost.Instance.Engine.Stop("all")); } catch { }
    }

    private async void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        try
        {
        if (args.IsSettingsSelected) { ContentFrame.Navigate(typeof(SettingsPage)); return; }
        if (args.SelectedItemContainer is NavigationViewItem { Tag: string tag })
        {
            try
            {
                ContentFrame.Navigate(tag switch
                {
                    "support_donate"  => typeof(DonatePage),
                    "support_docs"    => typeof(DocumentationPage),
                    "support_email"   => typeof(ContactSupportPage),
                    "support_bug"     => typeof(ContactSupportPage),
                    "support_feature" => typeof(ContactSupportPage),
                    "about"      => typeof(AboutPage),
                "dashboard"  => typeof(DashboardPage),
                "services"   => typeof(ServicesPage),
                "sites"      => typeof(SitesPage),
                "db_explorer"=> typeof(DatabaseExplorerPage),
                "databases"  => typeof(DatabasesPage),
                "terminal"   => typeof(TerminalPage),
                "files"      => typeof(FilesPage),
                "git"        => typeof(GitPage),
                "explorer"   => typeof(ExplorerPage),
                "node"       => typeof(NodePage),
                "python"     => typeof(PythonPage),
                "php_manager"=> typeof(PhpManagerPage),
                "php_ext"    => typeof(PhpExtensionsPage),
                "php_ini"    => typeof(PhpIniPage),
                "ssl"        => typeof(SslPage),
                "domains"    => typeof(DomainsPage),
                "deploy"     => typeof(DeployPage),
                "backup"     => typeof(BackupPage),
                "security"   => typeof(SecurityPage),
                "network"    => typeof(NetworkPage),
                "cache"      => typeof(CachePage),
                "queue"      => typeof(QueuePage),
                "email"      => typeof(EmailPage),
                "docker"     => typeof(DockerPage),
                "vscode"     => typeof(VsCodePage),
                "github_actions" => typeof(GithubActionsPage),
                "cf_tunnel"  => typeof(CloudflareTunnelPage),
                "site_health" => typeof(SiteHealthPage),
                "profiler"   => typeof(ProfilerPage),
                "file_watcher" => typeof(FileWatcherPage),
                "reverse_proxy" => typeof(ReverseProxyPage),
                "env_manager" => typeof(EnvManagerPage),
                "package_manager" => typeof(PackageManagerPage),
                "scheduler"  => typeof(SchedulerPage),
                "cron_manager" => typeof(CronManagerPage),
                "npm_runner" => typeof(NpmRunnerPage),
                "email_inbox" => typeof(EmailInboxPage),
                "ai"         => typeof(AiAssistantPage),
                "marketplace" => typeof(MarketplacePage),
                "blueprint"  => typeof(BlueprintPage),
                "extensions" => typeof(ExtensionsPage),
                "logs"       => typeof(LogsPage),
                    "project_analyzer" => typeof(ProjectDetectionPage),
                    "api_tester" => typeof(ApiTesterPage),
                    _            => typeof(DashboardPage),
                });
            }
            catch (Exception ex)
            {
                // Never let a page constructor / OnNavigatedTo take the whole app down.
                if (this.Content == null || this.Content.XamlRoot == null) return;
                var dlg = new ContentDialog
                {
                    Title = "Couldn't open that page",
                    Content = $"{ex.GetType().Name}: {ex.Message}\n\nThe app stays open — try another page.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                };
                try { await dlg.ShowAsync(); } catch { }
            }
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }
}
}
