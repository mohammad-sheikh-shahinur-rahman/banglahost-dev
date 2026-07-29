using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class PackageManagerPage : Page
{
    private string _selectedPath = "";

    public PackageManagerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            var snap = await EngineHost.Instance.Snapshot();
            var paths = snap.Sites
                .Select(s => s.Root)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .ToList();
            SiteList.ItemsSource = paths;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            LogViewer.Text += $"Error loading sites: {ex.Message}\n";
        }
    }

    private void SiteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SiteList.SelectedItem is string path)
        {
            _selectedPath = path;
            ComposerBtn.IsEnabled = true;
            NpmBtn.IsEnabled = true;
        }
    }

    private async void ComposerBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunCmdAsync("composer", "install");
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            LogViewer.Text += $"Error: {ex.Message}\n";
        }
    }

    private async void NpmBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunCmdAsync("npm", "install");
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            LogViewer.Text += $"Error: {ex.Message}\n";
        }
    }

    private async Task RunCmdAsync(string cmd, string args)
    {
        if (string.IsNullOrEmpty(_selectedPath)) return;

        LogViewer.Text = $"> {cmd} {args}\n";
        ComposerBtn.IsEnabled = false;
        NpmBtn.IsEnabled = false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd} {args}",
                WorkingDirectory = _selectedPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p != null)
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) DispatcherQueue?.TryEnqueue(() => LogViewer.Text += e.Data + "\n"); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) DispatcherQueue?.TryEnqueue(() => LogViewer.Text += e.Data + "\n"); };

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                await p.WaitForExitAsync();
                DispatcherQueue?.TryEnqueue(() => LogViewer.Text += $"\nProcess exited with code {p.ExitCode}\n");
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            LogViewer.Text += $"\nError: {ex.Message}\n";
        }

        ComposerBtn.IsEnabled = true;
        NpmBtn.IsEnabled = true;
    }
}
