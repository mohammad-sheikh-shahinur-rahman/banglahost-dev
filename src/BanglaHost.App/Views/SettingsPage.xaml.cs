using System;
using System.Diagnostics;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class SettingsPage : Page
{
    private bool _loading;
    private (string tld, int http, int https) _orig;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var v in BanglaHost.Core.Services.PhpVersions) PhpDefBox.Items.Add(new ComboBoxItem { Content = v });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Load();

    private void Load()
    {
        _loading = true;
        var cfg = Config.Load();
        TldBox.Text   = cfg.Tld;
        HttpBox.Value  = cfg.HttpPort;
        HttpsBox.Value = cfg.HttpsPort;
        PhpDefBox.SelectedIndex = Math.Max(0, Array.IndexOf(BanglaHost.Core.Services.PhpVersions, cfg.DefaultPhp));
        WebDefBox.SelectedIndex = cfg.DefaultWeb == "apache" ? 1 : 0;
        RootBox.Text  = cfg.SitesRoot;
        HomeText.Text = Paths.Home;

        AutostartToggle.IsOn  = Autostart.IsEnabled();
        StartSvcToggle.IsOn   = cfg.StartServicesOnLaunch;
        TrayToggle.IsOn       = cfg.MinimizeToTray;
        AutoUpdateToggle.IsOn = cfg.AutoUpdate;
        DashSizeBox.Value     = cfg.DashboardPageSize;
        SitesSizeBox.Value    = cfg.SitesPageSize;
        Version.Text = $"BanglaHost for Windows Â· {Updater.CurrentVersion}";
                try {
            LangBox.SelectedIndex = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride == "bn-BD" ? 1 : 0;
        } catch {
            LangBox.SelectedIndex = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride == "bn-BD" ? 1 : 0;
        }

        _orig = (cfg.Tld, cfg.HttpPort, cfg.HttpsPort);
        SaveStatus.Text = "";
        _loading = false;
    }

            private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedItem is not ComboBoxItem item) return;
        var lang = item.Tag?.ToString() ?? "en-US";
        try { Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang; } catch { }
        try { Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang; } catch { }
        
        var cfg = BanglaHost.Core.Config.Load();
        cfg.Language = lang;
        cfg.Save();
        
        Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tld   = TldBox.Text.Trim().TrimStart('.');
            var http  = double.IsNaN(HttpBox.Value) ? _orig.http : (int)HttpBox.Value;
            var https = double.IsNaN(HttpsBox.Value) ? _orig.https : (int)HttpsBox.Value;
            var dphp  = (PhpDefBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var dweb  = (WebDefBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "nginx";
            var root  = RootBox.Text.Trim();
            var topologyChanged = tld != _orig.tld || http != _orig.http || https != _orig.https;

            SaveBtn.IsEnabled = false; Busy.IsActive = true; SaveStatus.Text = "Saving…";
            var (ok, output) = await EngineHost.Instance.RunCaptured(() =>
            {
                var eng = EngineHost.Instance.Engine;
                eng.ConfigSet("tld", tld);
                eng.ConfigSet("http_port", http.ToString());
                eng.ConfigSet("https_port", https.ToString());
                eng.ConfigSet("default_php", dphp);
                eng.ConfigSet("default_web", dweb);
                eng.ConfigSet("sites_root", root);
                if (topologyChanged && Nginx.Running()) eng.Restart("nginx");
            });
            Busy.IsActive = false; SaveBtn.IsEnabled = true;
            SaveStatus.Text = ok ? "Saved." : "Some values were invalid — nothing partial was kept consistent; check and retry.";
            if (!ok && output.Length > 0)
            {
                if (this.XamlRoot != null)
                {
                    await new ContentDialog { Title = "Couldn't save", Content = output, CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                }
            }
            Load();
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e) => Load();

    private async void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await Picker.FolderAsync();
            if (!string.IsNullOrEmpty(path)) RootBox.Text = path;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private async void MoveData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await Picker.FolderAsync();
            if (string.IsNullOrEmpty(path) || path.Equals(Paths.Home, StringComparison.OrdinalIgnoreCase)) return;

            if (this.Content == null || this.XamlRoot == null) return;
            var dlg = new ContentDialog
            {
                Title = "Change Data Directory",
                Content = $"Change the data directory to {path}?\n\n" +
                          "This will only tell BanglaHost to look there upon next startup. " +
                          "You MUST manually close BanglaHost and move all files from your current data directory to this new location, otherwise BanglaHost will start fresh.",
                PrimaryButtonText = "Update Registry",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\BanglaHost");
                    key.SetValue("InstallPath", path);
                    HomeText.Text = path;
                    if (this.XamlRoot != null)
                        await new ContentDialog { Title = "Success", Content = "Registry updated. Please quit BanglaHost, manually move the files, and start it again.", CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                }
                catch (OperationCanceledException) { /* ignore */ }
                catch (Exception ex)
                {
                    if (this.XamlRoot != null)
                        await new ContentDialog { Title = "Error", Content = $"Failed to update registry: {ex.Message}", CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                }
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (AutostartToggle.IsOn) Autostart.Enable(); else Autostart.Disable();
    }

    private void Tray_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = Config.Load(); cfg.MinimizeToTray = TrayToggle.IsOn; cfg.Save();
    }

    private void Flag_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var cfg = Config.Load();
        cfg.StartServicesOnLaunch = StartSvcToggle.IsOn;
        cfg.AutoUpdate = AutoUpdateToggle.IsOn;
        cfg.Save();
    }

    private void ListSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loading) return;
        var cfg = Config.Load();
        if (!double.IsNaN(DashSizeBox.Value))  cfg.DashboardPageSize = Math.Clamp((int)DashSizeBox.Value, 1, 500);
        if (!double.IsNaN(SitesSizeBox.Value)) cfg.SitesPageSize = Math.Clamp((int)SitesSizeBox.Value, 1, 500);
        cfg.Save();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { using var p = Process.Start(new ProcessStartInfo { FileName = Paths.Home, UseShellExecute = true }); } catch { }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false; UpdateBusy.IsActive = true;
        UpdateStatus.Text = "Checking Microsoft Store…";
        try
        {
            var r = await Updater.Check();
            if (!string.IsNullOrEmpty(r.Error) && !r.UpdateAvailable)
            {
                UpdateStatus.Text = $"You have {Updater.CurrentVersion}. Couldn't reach the Store ({r.Error}).";
                return;
            }

            if (r.UpdateAvailable)
            {
                UpdateStatus.Text = $"Update available: {r.Latest} (you have {Updater.CurrentVersion}).";
                if (this.Content == null || this.XamlRoot == null) return;
                var dlg = new ContentDialog
                {
                    Title = $"Update available — BanglaHost {r.Latest}",
                    Content = $"A new version is on the Microsoft Store.\n\nYou have {Updater.CurrentVersion} Â· latest is {r.Latest}.\n\nOpen the Store to install the update.",
                    PrimaryButtonText = "Open Microsoft Store", CloseButtonText = "Later",
                    DefaultButton = ContentDialogButton.Primary, XamlRoot = this.XamlRoot,
                };
                if (await dlg.ShowAsync() == ContentDialogResult.Primary) Updater.OpenStore();
            }
            else
            {
                UpdateStatus.Text = $"You're up to date — BanglaHost {Updater.CurrentVersion}.";
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { UpdateStatus.Text = "Failed: " + ex.Message; }
        finally { UpdateBtn.IsEnabled = true; UpdateBusy.IsActive = false; }
    }
}

}


