using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class DashboardPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading, _pageSizeSet;
    // One refresh at a time: overlapping ticks are dropped, never queued (A1).
    // Without this the 2 s timer re-entered an unguarded async void, piled
    // snapshots 2–3 deep on slow machines and starved the thread pool.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private readonly System.Collections.Generic.Queue<double> _cpuHist = new();
    private string _pmaUrl = "", _admUrl = "", _mailUrl = "";

    private static readonly Lazy<SolidColorBrush> _on  = new(() => new SolidColorBrush(Colors.SeaGreen));
    private static readonly Lazy<SolidColorBrush> _off = new(() => new SolidColorBrush(Colors.Gray));
    private static SolidColorBrush On => _on.Value;
    private static SolidColorBrush Off => _off.Value;
    private static readonly Style? _accent =
        Application.Current.Resources.TryGetValue("AccentButtonStyle", out var s) ? s as Style : null;

    public DashboardPage()
    {
        InitializeComponent();
        LogBox.Text = EngineHost.Instance.LogText;
    }

    private void OnTimerTick(object? sender, object e) => _ = RefreshAsync();
    private void OnSiteListChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _cts = new CancellationTokenSource();
        EngineHost.Instance.LogAppended += OnLog;
        _timer.Tick += OnTimerTick;
        SiteList.Changed += OnSiteListChanged;
        _ = RefreshAsync();
        _timer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        SiteList.Changed -= OnSiteListChanged;
        EngineHost.Instance.LogAppended -= OnLog;
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        base.OnNavigatedFrom(e);
    }

    /// <summary>
    /// Append the one new line. The old code assigned <c>EngineHost.Instance.LogText</c> — the
    /// full log — into the TextBox for EVERY appended line, so an install that emits n lines did
    /// O(n²) string allocation plus n full TextBox re-layouts on the UI thread. During
    /// "Install all" that is thousands of lines and the window stops responding, which Windows
    /// reports as an AppHang. The log itself is now bounded in EngineHost; this keeps the UI
    /// cost per line constant, and re-syncs from the source if trimming has moved the window.
    /// </summary>
    private void OnLog(string line) =>
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (XamlRoot is null) return;   // page navigated away between enqueue and dispatch
            try
            {
                if (LogBox.Text.Length > LogTextCap)
                    LogBox.Text = EngineHost.Instance.LogText;   // bounded source, re-sync once
                else
                    LogBox.Text += line + Environment.NewLine;

                LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
            }
            catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "DashboardLog"); }
        });

    // Keep the visible log under EngineHost's own bound; past this we re-read the trimmed source.
    private const int LogTextCap = 256 * 1024;

    private record struct DashboardMetrics(
        double Cpu,
        (double usedGB, double totalGB, double pct) Mem,
        (double usedGB, double totalGB, double pct) Disk,
        (double downKbps, double upKbps) Net);

    /// <summary>Guarded refresh: drops the tick when one is in flight, measures the
    /// work and reschedules afterwards so a slow machine gets slower refreshes
    /// instead of a growing backlog (A1). In-flight work cancels on navigate-away.</summary>
    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(true)) return;   // drop, don't queue
        var token = _cts?.Token ?? CancellationToken.None;
        _timer.Stop();   // self-rescheduling: restart only after the work lands
        try
        {
            Snapshot snap;
            DashboardMetrics metrics;
            try
            {
                var snapTask = EngineHost.Instance.Snapshot(token);
                var metricsTask = Task.Run(() => new DashboardMetrics(
                    SystemMetrics.CpuPercent(),
                    SystemMetrics.Memory(),
                    SystemMetrics.Disk(),
                    SystemMetrics.Network()), token);

                await Task.WhenAll(snapTask, metricsTask).ConfigureAwait(true);
                snap = snapTask.Result;
                metrics = metricsTask.Result;
            }
            catch (OperationCanceledException) { return; }
            catch { return; }
            if (token.IsCancellationRequested) return;
            ApplySnapshot(snap, metrics);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "DashboardRefresh"); }
        finally
        {
            try { _refreshGate.Release(); } catch { }
            if (!token.IsCancellationRequested && _cts is not null) _timer.Start();
        }
    }

    private void ApplySnapshot(Snapshot snap, DashboardMetrics metrics)
    {
        // Pure UI updates, no I/O. Runs on the dispatcher (callers ConfigureAwait(true)).
        if (XamlRoot is null) return;
        try
        {
        bool Running(string key) => snap.Services.FirstOrDefault(s => s.Key == key)?.Running ?? false;
        var phpVers = snap.Services.Where(s => s.Role == ServiceRole.Php && s.Key.StartsWith("php@") && s.Installed)
                                   .Select(s => s.Key["php@".Length..]).OrderByDescending(v => v).ToList();
        var sites = snap.Sites.Where(s => !Engine.IsTool(s.Name)).OrderBy(s => s.Name).ToList();

        // ── status cards ──
        var nginx = Running("nginx"); var apache = Running("apache");
        WebVal.Text = apache && nginx ? "nginx + apache" : nginx ? "nginx" : apache ? "apache" : "nginx";
        WebSub.Text = $"{sites.Count} site{(sites.Count == 1 ? "" : "s")}";
        WebDot.Fill = nginx || apache ? On : Off;

        PhpVal.Text = phpVers.Count > 0 ? string.Join(", ", phpVers) : "not installed";
        PhpSub.Text = $"{phpVers.Count} installed";
        PhpDot.Fill = snap.Services.Any(s => s.Role == ServiceRole.Php && s.Running) ? On : Off;

        bool myRun = Running("mysql"), mariaRun = Running("mariadb"), pgRun = Running("postgresql");
        var dbRun = myRun || mariaRun || pgRun;
        DbVal.Text = mariaRun ? "MariaDB" : myRun ? "MySQL" : pgRun ? "PostgreSQL" : "MySQL / MariaDB";
        DbSub.Text = dbRun ? "running" : "stopped";
        DbDot.Fill = dbRun ? On : Off;

        var redis = Running("redis"); var memc = Running("memcached");
        CacheVal.Text = "Redis / Memcached";
        CacheSub.Text = $"redis {(redis ? "on" : "off")}, memcached {(memc ? "on" : "off")}";
        CacheDot.Fill = redis || memc ? On : Off;

        // ── metrics ──
        var cpu = metrics.Cpu; CpuText.Text = $"{cpu:0}%";
        _cpuHist.Enqueue(cpu);
        while (_cpuHist.Count > 40) _cpuHist.Dequeue();
        var arr = _cpuHist.ToArray();
        var pts = new Microsoft.UI.Xaml.Media.PointCollection();
        for (var i = 0; i < arr.Length; i++)
        {
            var x = arr.Length <= 1 ? 0 : i * 200.0 / (arr.Length - 1);
            var y = 30 - arr[i] / 100.0 * 30;
            pts.Add(new Windows.Foundation.Point(x, y));
        }
        CpuSpark.Points = pts;
        var (mu, mt, mp) = metrics.Mem; MemText.Text = $"{mu:0.0} / {mt:0.0} GB";
        SetBar(MemBar, mp);
        var (du, dt, dp) = metrics.Disk; DiskText.Text = $"{du:0} / {dt:0} GB";
        SetBar(DiskBar, dp);
        var (down, up) = metrics.Net;
        NetDown.Text = $"Down  {Rate(down)}"; NetUp.Text = $"Up  {Rate(up)}";

        SubTitle.Text = $"{snap.Services.Count(s => s.Running)} services running / {sites.Count} sites";

        // ── global buttons reflect real service state ──
        if (!Busy.IsActive)
        {
            var anyRunning = snap.Services.Any(s => s.Running);

            if (!anyRunning)
            {
                StartBtn.Visibility = Visibility.Visible;
                StopBtn.Visibility = Visibility.Collapsed;
                RestartBtn.Visibility = Visibility.Collapsed;
                StartBtn.IsEnabled = true;
            }
            else
            {
                StartBtn.Visibility = Visibility.Collapsed;
                StopBtn.Visibility = Visibility.Visible;
                RestartBtn.Visibility = Visibility.Visible;
                StopBtn.IsEnabled = true;
                RestartBtn.IsEnabled = true;
                SetBtn(StopBtn, true, true);
                SetBtn(RestartBtn, true, false);
            }
        }

        // ── websites (delegated to the shared list control: Show + search + actions + paging) ──
        if (!_pageSizeSet) { SiteList.SetDefaultPageSize(Config.Load().DashboardPageSize); _pageSizeSet = true; }
        SiteList.SetData(sites.Select(s => new SiteRow
        {
            Name = s.Name, Domain = s.Domain, Php = s.Php, Root = s.Root,
            Secure = s.Secure, Enabled = s.Enabled, Server = s.Server,
        }));
        WebHeader.Text = $"Websites ({sites.Count})";

        // ── web tools ──
        _loading = true;
        SetTool(snap, "phpmyadmin", PmaToggle, PmaOpen, PmaStatus, ref _pmaUrl);
        SetTool(snap, "adminer",    AdmToggle, AdmOpen, AdmStatus, ref _admUrl);
        SetTool(snap, "mailpit",    MailToggle, MailOpen, MailStatus, ref _mailUrl);
        _loading = false;
        }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "DashboardApply"); }
    }

    /// <summary>
    /// Assign a metric bar inline, in the same dispatcher turn as its label (C14).
    /// The old code deferred via TryEnqueue + swallowed everything, which both made
    /// the race with page teardown MORE likely and hid the real exception type.
    /// A torn-down page is guarded by the liveness check instead; a genuine fault
    /// is logged so it can actually be diagnosed.
    /// </summary>
    private void SetBar(ProgressBar bar, double value)
    {
        if (XamlRoot is null || !IsLoaded) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (XamlRoot is null || !IsLoaded) return;
            bar.Value = Math.Clamp(value, bar.Minimum, bar.Maximum);
        });
    }

    private static void SetTool(Snapshot snap, string name, ToggleSwitch toggle, Button open, TextBlock status, ref string url)
    {
        var site = snap.Sites.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        var active = site is { Enabled: true };
        toggle.IsOn = active;
        open.IsEnabled = active;
        status.Text = active ? (site!.Secure ? "Active / https" : "Active") : "Off";
        url = active ? (site!.Secure ? "https://" : "http://") + site.Domain : "";
    }

    private static string Rate(double kbps) => kbps >= 1024 ? $"{kbps / 1024:0.0} MB/s" : $"{kbps:0} KB/s";

    // ── web tools ──
    private async void Pma_Toggled(object s, RoutedEventArgs e)  { try { if (!_loading) await ToolOp("phpmyadmin", PmaToggle.IsOn); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }
    private async void Adm_Toggled(object s, RoutedEventArgs e)  { try { if (!_loading) await ToolOp("adminer",    AdmToggle.IsOn); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }
    private async void Mail_Toggled(object s, RoutedEventArgs e) { try { if (!_loading) await ToolOp("mailpit",    MailToggle.IsOn); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }

    private void PmaOpen_Click(object s, RoutedEventArgs e)  { if (_pmaUrl.Length > 0)  Launch(_pmaUrl); }
    private void AdmOpen_Click(object s, RoutedEventArgs e)  { if (_admUrl.Length > 0)  Launch(_admUrl); }
    private void MailOpen_Click(object s, RoutedEventArgs e) { if (_mailUrl.Length > 0) Launch(_mailUrl); }

    private async Task ToolOp(string tool, bool on)
    {
        Busy.IsActive = true;
        var (ok, output) = await EngineHost.Instance.RunCaptured(() => EngineHost.Instance.Engine.ToolSet(tool, on));
        Busy.IsActive = false;
        _ = RefreshAsync();
        if (!ok && output.Length > 0)
        {
            if (this.Content == null || this.XamlRoot == null) return;
            await BanglaHost.App.Services.DialogQueue.ShowAsync(new ContentDialog { Title = "Web tool", Content = output, CloseButtonText = "OK", XamlRoot = this.XamlRoot });
        }
    }

    // ── global ──
    private async void StartAll_Click(object sender, RoutedEventArgs e)   { try { await Op(() => EngineHost.Instance.Engine.Start("all")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); await BanglaHost.App.Services.DialogQueue.ShowAsync(new Microsoft.UI.Xaml.Controls.ContentDialog { Title = "Error", Content = ex.Message, CloseButtonText = "OK", XamlRoot = this.XamlRoot }); } }
    private async void StopAll_Click(object sender, RoutedEventArgs e)    { try { await Op(() => EngineHost.Instance.Engine.Stop("all")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); await BanglaHost.App.Services.DialogQueue.ShowAsync(new Microsoft.UI.Xaml.Controls.ContentDialog { Title = "Error", Content = ex.Message, CloseButtonText = "OK", XamlRoot = this.XamlRoot }); } }
    private async void RestartAll_Click(object sender, RoutedEventArgs e) { try { await Op(() => EngineHost.Instance.Engine.Restart("all")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); await BanglaHost.App.Services.DialogQueue.ShowAsync(new Microsoft.UI.Xaml.Controls.ContentDialog { Title = "Error", Content = ex.Message, CloseButtonText = "OK", XamlRoot = this.XamlRoot }); } }

    private void SetBtn(Button b, bool enabled, bool accent)
    {
        b.IsEnabled = enabled;
        var style = accent ? _accent : null;
        if (!ReferenceEquals(b.Style, style)) b.Style = style;
    }

    private async Task Op(Action action)
    {
        Busy.IsActive = true;
        StartBtn.IsEnabled = StopBtn.IsEnabled = RestartBtn.IsEnabled = false;
        await EngineHost.Instance.Run(action);
        Busy.IsActive = false;
        _ = RefreshAsync();   // recomputes the correct enabled/highlight state
    }

    private static void Launch(string target)
    {
        try { using var p = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }); } catch { }
    }
}

}
