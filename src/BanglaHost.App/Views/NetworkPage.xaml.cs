using System;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class NetworkPage : Page
{
    private DispatcherTimer _perfTimer;

    public NetworkPage()
    {
        InitializeComponent();
        _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
    }

    private async void OnPerfTimerTick(object? sender, object e)
    {
        try
        {
        await RefreshPerf();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
        _perfTimer.Tick += OnPerfTimerTick;
        RefreshPorts_Click(this, new RoutedEventArgs());
        InterfaceList.ItemsSource = NetworkInspector.GetInterfaceStats();
        await RefreshPerf();
        _perfTimer.Start();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _perfTimer.Stop();
        _perfTimer.Tick -= OnPerfTimerTick;
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        PortList.ItemsSource = NetworkInspector.GetListeningPorts();
    }

    private async Task RefreshPerf()
    {
        var snap = await Task.Run(() => NetworkInspector.GetPerformanceSnapshot());
        DispatcherQueue.TryEnqueue(() =>
        {
            CpuLabel.Text = $"{snap.CpuPercent:F1}%";
            MemLabel.Text = $"{snap.AvailableMemoryMB} MB";
            ThreadLabel.Text = $"{snap.ThreadCount} / {snap.HandleCount}";
        });
    }
}
