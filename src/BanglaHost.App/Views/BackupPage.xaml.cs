using System;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class BackupPage : Page
{
    public BackupPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        BackupNameBox.Text = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        LoadBackups();
    }

    private void LoadBackups()
    {
        BackupList.ItemsSource = BackupService.GetBackups();
    }

    private async void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = BackupNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            BackupAllBtn.IsEnabled = false;
            OpBanner.Visibility = Visibility.Visible;
            OpMsg.Text = "Starting backup...";

            Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => 
            {
                OpMsg.Text = msg;
            });

            await Task.Run(() => BackupService.BackupAllAsync(name, log));

            DispatcherQueue?.TryEnqueue(() => 
            {
                OpBanner.Visibility = Visibility.Collapsed;
                BackupAllBtn.IsEnabled = true;
                BackupNameBox.Text = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                LoadBackups();
            });
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private async void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                btn.IsEnabled = false;
                Action<string> log = msg => EngineHost.Instance.Append($"[Restore] {msg}");
                await Task.Run(() => BackupService.RestoreAsync(path, log));
                btn.IsEnabled = true;
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] {ex.Message}");
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            BackupService.DeleteBackup(id);
            LoadBackups();
        }
    }
}

}
