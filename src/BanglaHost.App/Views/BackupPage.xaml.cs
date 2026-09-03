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
        _ = LoadBackupsAsync();
    }

    /// <summary>Enumerating the backups folder is disk IO; on a slow or network-mapped
    /// %LOCALAPPDATA% it stalled the UI thread during page navigation.</summary>
    private async Task LoadBackupsAsync()
    {
        try
        {
            var list = await Task.Run(() => BackupService.GetBackups());
            BackupList.ItemsSource = list;
        }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"[ERROR] Could not list backups: {ex.Message}");
        }
    }

    private void BackupAll_Click(object sender, RoutedEventArgs e)
        => BackgroundWork.Handler(BackupAllAsync, "BackupPage.BackupAll");

    private async Task BackupAllAsync()
    {
        var name = BackupNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        BackupAllBtn.IsEnabled = false;
        OpBanner.Visibility = Visibility.Visible;
        OpMsg.Text = "Starting backup…";

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);

        BackupResult result;
        try
        {
            result = await Task.Run(() => BackupService.BackupAllAsync(name, log));
        }
        finally
        {
            OpBanner.Visibility = Visibility.Collapsed;
            BackupAllBtn.IsEnabled = true;
        }

        BackupNameBox.Text = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        await LoadBackupsAsync();

        // The result used to be discarded entirely: a backup that copied nothing, or dumped zero
        // databases because the server was down, still showed "completed successfully".
        EngineHost.Instance.Append($"[Backup] {result.Summary}");
        await ShowResultAsync(result.Ok ? (result.Partial ? "Backup finished with warnings" : "Backup complete")
                                        : "Backup failed",
                              result.Summary);
    }

    private void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;
        BackgroundWork.Handler(() => RestoreAsync(btn, path), "BackupPage.Restore");
    }

    private async Task RestoreAsync(Button btn, string path)
    {
        // Restore now really writes to disk and really imports databases. It must be confirmed.
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore this backup?",
            Content = "Sites in this backup will be restored into your web root. Any existing folder "
                    + "with the same name is renamed to '<name>.pre-restore-<timestamp>' first, so "
                    + "nothing is deleted.\n\nDatabases in the backup will be imported over the "
                    + "current contents of databases with the same name. This cannot be undone.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        btn.IsEnabled = false;
        try
        {
            Action<string> log = msg => EngineHost.Instance.Append($"[Restore] {msg}");
            var result = await Task.Run(() => BackupService.RestoreAsync(path, log));
            await ShowResultAsync(result.Ok ? (result.Partial ? "Restore finished with warnings" : "Restore complete")
                                            : "Restore failed",
                                  result.Summary);
        }
        catch (Exception ex)
        {
            await ShowResultAsync("Restore failed", ex.Message);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            try { BackupService.DeleteBackup(id); }
            catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); }
            _ = LoadBackupsAsync();
        }
    }

    private async Task ShowResultAsync(string title, string body)
    {
        try
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                },
                CloseButtonText = "OK",
            }.ShowAsync();
        }
        catch { /* another dialog is already open — the log line above still carries the detail */ }
    }
}

}
