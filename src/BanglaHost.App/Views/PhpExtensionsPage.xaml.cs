using System;
using System.Collections.Generic;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed class ExtRow
{
    public required string Name { get; init; }
    public required bool Available { get; init; }
    public required bool Enabled { get; set; }
    public required bool IsZend { get; init; }
    public required string[] MissingDeps { get; init; }
    public string Description => Name == "ioncube" ? "WHMCS & Encoded PHP Apps Support" : "";
    public Visibility DescVis => string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IsZendVis => IsZend ? Visibility.Visible : Visibility.Collapsed;
    public string MissingDepsText => MissingDeps.Length > 0 ? $"Missing dependencies: {string.Join(", ", MissingDeps)}" : "";
    public Visibility MissingDepsVis => MissingDeps.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public double Opacity => Available ? 1.0 : 0.5;
    public string StatusText => Available ? (Enabled ? "Enabled" : "Disabled") : "Not installed (will download)";
}

public sealed partial class PhpExtensionsPage : Page
{
    private string _currentVersion = "";

    public PhpExtensionsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        EngineHost.Instance.OpChanged += OnOpChanged;
        RenderOp();
        
        VersionBox.ItemsSource = BanglaHost.Core.Services.PhpVersions.Where(v => Tools.PhpExe(v) != null).ToList();
        var defaultPhp = Config.Load().DefaultPhp;
        if (VersionBox.Items.Contains(defaultPhp))
            VersionBox.SelectedItem = defaultPhp;
        else if (VersionBox.Items.Count > 0)
            VersionBox.SelectedIndex = 0;
            
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        EngineHost.Instance.OpChanged -= OnOpChanged;
    }

    private void OnOpChanged() => DispatcherQueue?.TryEnqueue(() => { RenderOp(); if (EngineHost.Instance.CurrentOp is { Running: false }) Refresh(); });

    private void RenderOp()
    {
        var op = EngineHost.Instance.CurrentOp;
        if (op is null) { OpBanner.Visibility = Visibility.Collapsed; return; }
        OpBanner.Visibility = Visibility.Visible;
        OpName.Text = op.Name;
        OpMsg.Text = op.Message;
        OpBar.IsIndeterminate = op.Running && op.Progress < 0;
        OpBar.Value = op.Progress < 0 ? 0 : op.Progress;
        OpPct.Text = op.Running && op.Progress >= 0 ? $"{op.Progress:0}%" : op.Running ? "working…" : op.Success ? "done" : "failed";
        OpDismiss.Visibility = op.Running ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpDismiss_Click(object sender, RoutedEventArgs e) { EngineHost.Instance.DismissOp(); RenderOp(); }

    private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionBox.SelectedItem is string v)
        {
            _currentVersion = v;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (string.IsNullOrEmpty(_currentVersion)) return;
        
        var exts = Php.ListExtensions(_currentVersion);
        var rows = new List<ExtRow>();
        
        // Put ionCube at the top
        var ion = exts.FirstOrDefault(x => x.Name == "ioncube");
        if (ion != null)
        {
            rows.Add(new ExtRow
            {
                Name = ion.Name,
                Available = ion.Available,
                Enabled = ion.Enabled,
                IsZend = ion.IsZend,
                MissingDeps = ion.MissingDeps
            });
        }
        
        foreach (var ext in exts.Where(x => x.Name != "ioncube"))
        {
            rows.Add(new ExtRow
            {
                Name = ext.Name,
                Available = ext.Available,
                Enabled = ext.Enabled,
                IsZend = ext.IsZend,
                MissingDeps = ext.MissingDeps
            });
        }
        
        ExtList.ItemsSource = rows;
    }

    private async void ExtToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
        if (sender is ToggleSwitch ts && ts.Tag is string extName)
        {
            var enable = ts.IsOn;
            // Prevent recursive events during refresh
            var row = (ExtList.ItemsSource as List<ExtRow>)?.FirstOrDefault(x => x.Name == extName);
            if (row != null && row.Enabled == enable) return;
            
            await EngineHost.Instance.RunTracked($"Configure {extName} for PHP {_currentVersion}", async () =>
            {
                await Php.SetExtension(_currentVersion, extName, enable, msg => EngineHost.Instance.Append($"    {msg}"));
            });
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }
}

}
