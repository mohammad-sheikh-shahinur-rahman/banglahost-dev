using System;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace BanglaHost.App.Views
{

public sealed partial class SecurityPage : Page
{
    public SecurityPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            ScanDirBox.Text = Config.Load().SitesRoot;
            var wafEnabled = await SecurityService.IsWafEnabledAsync();
            WafToggle.IsOn = wafEnabled;
            WafStatus.Text = wafEnabled ? "WAF is active." : "WAF is disabled.";
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private async void BrowseScanDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPath = await BanglaHost.App.Services.Picker.FolderAsync();
            if (folderPath != null)
            {
                ScanDirBox.Text = folderPath;
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = ScanDirBox.Text.Trim();
            if (string.IsNullOrEmpty(dir)) return;

            ScanBtn.IsEnabled = false;
            EmptyScanText.Text = "Scanning... This may take a moment.";
            ScanResultsList.Visibility = Visibility.Collapsed;
            EmptyScanText.Visibility = Visibility.Visible;

            Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => 
            {
                EmptyScanText.Text = msg;
            });

            var results = await Task.Run(() => SecurityService.ScanDirectoryAsync(dir, log));

            DispatcherQueue?.TryEnqueue(() => 
            {
                ScanBtn.IsEnabled = true;
                if (results.Count > 0)
                {
                    ScanResultsList.ItemsSource = results;
                    ScanResultsList.Visibility = Visibility.Visible;
                    EmptyScanText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    EmptyScanText.Text = "Scan complete. No issues found.";
                }
            });
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private async void WafToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsLoaded) return;
            WafToggle.IsEnabled = false;
            
            Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => WafStatus.Text = msg);
            await Task.Run(() => SecurityService.ToggleWafAsync(WafToggle.IsOn, log));
            
            WafToggle.IsEnabled = true;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }
}

}
