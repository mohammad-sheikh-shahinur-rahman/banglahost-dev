using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace BanglaHost.App.Views
{

/// <summary>One row on the Cloudflare Tunnel page — a site plus its live tunnel state.</summary>
public sealed class TunnelRow
{
    public required string Name { get; init; }
    public required string Domain { get; init; }
    public required bool Secure { get; init; }
    public required bool IsOn { get; init; }
    public required string? PublicUrl { get; init; }
    public string LocalUrl => (Secure ? "https://" : "http://") + Domain;
    public string ActionLabel => IsOn ? "Stop" : "Start";
    public Uri PublicUri => Uri.TryCreate(PublicUrl ?? "http://localhost", UriKind.Absolute, out var u) ? u : new Uri("http://localhost");
    public Visibility PublicVisibility => string.IsNullOrEmpty(PublicUrl) ? Visibility.Collapsed : Visibility.Visible;
    public SolidColorBrush DotBrush => new(IsOn ? Colors.LimeGreen : Color.FromArgb(0x80, 0x88, 0x88, 0x88));
}

/// <summary>Dedicated Cloudflare Tunnel page. One row per site; a Start/Stop button opens
/// a Cloudflare quick-tunnel (no account, no port-forward) and copies the public URL. Uses
/// plain Buttons rather than ToggleSwitch — inside a ListView data template, ToggleSwitch fires
/// Toggled during virtualization/recycle and would re-enter the handler, hard-crashing the app.</summary>
public sealed partial class CloudflareTunnelPage : Page
{
    private readonly HashSet<string> _busy = new();

    public CloudflareTunnelPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = Refresh();

    private async Task Refresh()
    {
        try
        {
            Snapshot snap;
            try { snap = await EngineHost.Instance.Snapshot(); }
            catch { Status.Text = "Couldn't read sites. Is the engine initialized?"; return; }

            var rows = snap.Sites
                .Where(s => !Engine.IsTool(s.Name))
                .OrderBy(s => s.Name)
                .Select(s => new TunnelRow
                {
                    Name      = s.Name,
                    Domain    = s.Domain,
                    Secure    = s.Secure,
                    IsOn      = SafeRunning(s.Name),
                    PublicUrl = SafeUrl(s.Name),
                })
                .ToList();

            Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            List.ItemsSource = rows;

            var live = rows.Count(r => r.IsOn);
            Status.Text = rows.Count == 0
                ? ""
                : live == 0 ? "No tunnels running." : live == 1 ? "1 tunnel live." : $"{live} tunnels live.";
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private static bool SafeRunning(string name) { try { return Tunnel.Running(name); } catch { return false; } }
    private static string? SafeUrl(string name)  { try { return Tunnel.Url(name);     } catch { return null; } }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || name.Length == 0) return;
        if (!_busy.Add(name)) return;
        Busy.IsActive = true;
        try
        {
            var isOn = SafeRunning(name);
            var wantOn = !isOn;
            var (_, output) = await EngineHost.Instance.RunCaptured(() =>
                EngineHost.Instance.Engine.Tunnel(wantOn ? "start" : "stop", name));

            if (wantOn)
            {
                // Give the tunnel a couple more seconds in case Engine.Tunnel returned before
                // cloudflared flushed the URL to disk.
                string? url = null;
                for (var i = 0; i < 15 && SafeRunning(name); i++)
                {
                    url = SafeUrl(name);
                    if (!string.IsNullOrEmpty(url)) break;
                    await Task.Delay(1000);
                }
                if (string.IsNullOrEmpty(url))
                {
                    var msg = string.IsNullOrWhiteSpace(output)
                        ? "The tunnel didn't return a public URL. Check Logs, then try again."
                        : output.Trim();
                    await Info("Couldn't share publicly", msg);
                }
                else
                {
                    try
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dp.SetText(url);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    }
                    catch { }
                    Status.Text = $"Live: {url} (copied to clipboard)";
                }
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { await Info("Couldn't complete that", ex.Message); }
        finally
        {
            _busy.Remove(name);
            Busy.IsActive = false;
            await Refresh();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var url = SafeUrl(name);
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            Status.Text = $"Copied: {url}";
        }
        catch { }
    }

    private Task Info(string title, string body)
    {
        if (this.XamlRoot is null) return Task.CompletedTask;
        return new ContentDialog { Title = title, Content = body, CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync().AsTask();
    }
}

}
