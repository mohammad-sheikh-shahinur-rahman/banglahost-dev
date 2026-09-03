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

    // ── output buffering (C6) ────────────────────────────────────────────
    // The old handler did LogViewer.Text += line per output line: O(n²) string
    // copies plus a full TextBox relayout on the UI thread per line — thousands
    // per npm/composer install. Lines now queue off-thread and flush in batches
    // with a cap, so one install costs a handful of assignments, not thousands.
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pending = new();
    private int _flushQueued;
    private readonly System.Collections.Generic.LinkedList<string> _lines = new();
    private const int MaxLines = 5000;

    private void ResetLog()
    {
        while (_pending.TryDequeue(out _)) { }
        _lines.Clear();
        LogViewer.Text = "";
    }

    private void AppendLog(string line)
    {
        _pending.Enqueue(line);
        // One flush per burst, not one per line.
        if (System.Threading.Interlocked.Exchange(ref _flushQueued, 1) == 0)
            DispatcherQueue?.TryEnqueue(FlushLog);
    }

    private void FlushLog()
    {
        System.Threading.Interlocked.Exchange(ref _flushQueued, 0);
        if (_pending.IsEmpty) return;
        while (_pending.TryDequeue(out var line))
        {
            _lines.AddLast(line);
            if (_lines.Count > MaxLines) _lines.RemoveFirst();
        }
        LogViewer.Text = string.Join('\n', _lines);
    }

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
            AppendLog($"Error loading sites: {ex.Message}\n");
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
            AppendLog($"Error: {ex.Message}\n");
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
            AppendLog($"Error: {ex.Message}\n");
        }
    }

    private async Task RunCmdAsync(string cmd, string args)
    {
        if (string.IsNullOrEmpty(_selectedPath)) return;

        ResetLog();
        AppendLog($"> {cmd} {args}");
        ComposerBtn.IsEnabled = false;
        NpmBtn.IsEnabled = false;

        try
        {
            // Absolute cmd (B11). cmd/args are fixed literals from the buttons above,
            // never free text — there is no injection surface here by construction.
            var psi = new ProcessStartInfo
            {
                FileName = BanglaHost.Core.SystemExe.Cmd,
                WorkingDirectory = _selectedPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add($"{cmd} {args}");

            using var p = Process.Start(psi);
            if (p != null)
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) AppendLog(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendLog(e.Data); };

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                await p.WaitForExitAsync();
                AppendLog($"\nProcess exited with code {p.ExitCode}");
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            AppendLog($"\nError: {ex.Message}\n");
        }

        ComposerBtn.IsEnabled = true;
        NpmBtn.IsEnabled = true;
    }
}
