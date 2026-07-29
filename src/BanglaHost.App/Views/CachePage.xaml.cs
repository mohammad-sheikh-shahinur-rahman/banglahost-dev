using System;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class CachePage : Page
{
    public CachePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var stats = await CacheService.GetRedisCacheStatsAsync();
        KeysLabel.Text = stats.KeyCount.ToString();
        MemLabel.Text = $"{stats.MemoryUsedBytes / 1024} KB";
        HitLabel.Text = $"{stats.HitRatio:F1}%";
        UptimeLabel.Text = stats.Uptime;

        OpcacheInfo.Text = await CacheService.GetOpcacheStatusAsync();
    }

    private async void RefreshKeys_Click(object sender, RoutedEventArgs e)
    {
        KeyList.ItemsSource = await CacheService.GetRedisKeysAsync("*");
    }

    private async void SearchKeys_Click(object sender, RoutedEventArgs e)
    {
        var pattern = KeyPatternBox.Text.Trim();
        if (string.IsNullOrEmpty(pattern)) pattern = "*";
        KeyList.ItemsSource = await CacheService.GetRedisKeysAsync(pattern);
    }

    private async void FlushAll_Click(object sender, RoutedEventArgs e)
    {
        await CacheService.FlushRedisCacheAsync();
        await RefreshAsync();
    }

    private async void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            await CacheService.DeleteRedisKeyAsync(key);
            await RefreshAsync();
        }
    }

    private async void ResetOpcache_Click(object sender, RoutedEventArgs e)
    {
        await CacheService.ResetOpcacheAsync();
        OpcacheInfo.Text = await CacheService.GetOpcacheStatusAsync();
    }
}
