using System;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class VsCodePage : Page
{
    public VsCodePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
        await RefreshAsync();
        var snap = await EngineHost.Instance.Snapshot();
        SiteList.ItemsSource = snap.Sites.Select(s => s.Root).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        await RefreshAsync();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async Task RefreshAsync()
    {
        var installed = await VsCodeService.IsInstalledAsync();
        StatusLabel.Text = installed ? "VS Code is installed and ready." : "VS Code was not detected on this system.";
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            var success = VsCodeService.OpenProject(path);
            if (!success) ExtLog.Text += $"Failed to open {path}\n";
        }
    }

    private async void InstallExt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (sender is Button btn && btn.Tag is string extId)
        {
            btn.IsEnabled = false;
            ExtLog.Text += $"Installing {extId}...\n";
            
            Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => ExtLog.Text += msg + "\n");
            await Task.Run(() => VsCodeService.InstallExtensionAsync(extId, log));
            
            btn.IsEnabled = true;
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }
}
