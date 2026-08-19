using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public class NpmScriptModel
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
}

public sealed partial class NpmRunnerPage : Page
{
    private Process? _runningProcess;
    private string _currentRoot = "";

    public NpmRunnerPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await LoadProjects();
    }

    private async Task LoadProjects()
    {
        ProjectBox.Items.Clear();
        try
        {
            var snap = await BanglaHost.App.Services.EngineHost.Instance.Snapshot();
            foreach (var site in snap.Sites)
            {
                var root = site.Root?.EndsWith("public") == true ? Path.GetDirectoryName(site.Root) : site.Root;
                if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "package.json")))
                {
                    ProjectBox.Items.Add(new ComboBoxItem { Content = site.Name, Tag = root });
                }
            }
        }
        catch { }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadProjects();

    private void ProjectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScriptsList.ItemsSource = null;
        if (ProjectBox.SelectedItem is ComboBoxItem item && item.Tag is string root)
        {
            _currentRoot = root;
            var pkgJson = Path.Combine(root, "package.json");
            if (File.Exists(pkgJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(pkgJson));
                    if (doc.RootElement.TryGetProperty("scripts", out var scriptsEl))
                    {
                        var scripts = new List<NpmScriptModel>();
                        foreach (var prop in scriptsEl.EnumerateObject())
                        {
                            scripts.Add(new NpmScriptModel { Name = prop.Name, Command = prop.Value.GetString() ?? "" });
                        }
                        ScriptsList.ItemsSource = scripts;
                    }
                }
                catch { }
            }
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string scriptName)
        {
            StopProcess();
            OutputBox.Text = $"Running npm run {scriptName}...\n";
            StopBtn.IsEnabled = true;

            var nodeDir = Tools.NodeBinDir();
            var npm = nodeDir != null ? Path.Combine(nodeDir, "npm.cmd") : "npm.cmd";
            
            var psi = new ProcessStartInfo
            {
                FileName = npm,
                Arguments = $"run {scriptName}",
                WorkingDirectory = _currentRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            if (nodeDir != null)
            {
                psi.EnvironmentVariables["PATH"] = nodeDir + ";" + Environment.GetEnvironmentVariable("PATH");
            }

            try
            {
                _runningProcess = Process.Start(psi);
                if (_runningProcess != null)
                {
                    _runningProcess.OutputDataReceived += (s, ev) => { if (ev.Data != null) DispatcherQueue.TryEnqueue(() => OutputBox.Text += ev.Data + "\n"); };
                    _runningProcess.ErrorDataReceived += (s, ev) => { if (ev.Data != null) DispatcherQueue.TryEnqueue(() => OutputBox.Text += ev.Data + "\n"); };
                    _runningProcess.BeginOutputReadLine();
                    _runningProcess.BeginErrorReadLine();
                    
                    await _runningProcess.WaitForExitAsync();
                }
            }
            catch (Exception ex) { OutputBox.Text += $"\nError: {ex.Message}"; }
            finally { StopBtn.IsEnabled = false; _runningProcess = null; }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopProcess();
    }

    private void StopProcess()
    {
        if (_runningProcess != null && !_runningProcess.HasExited)
        {
            try { _runningProcess.Kill(true); } catch { }
        }
        _runningProcess = null;
        StopBtn.IsEnabled = false;
    }
}
