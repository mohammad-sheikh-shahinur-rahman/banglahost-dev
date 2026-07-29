using System;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class QueuePage : Page
{
    public QueuePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var isRunning = await QueueService.IsRedisRunningAsync();
        RedisStatus.Text = isRunning ? "Running" : "Stopped";

        if (isRunning)
        {
            var queues = await QueueService.GetRedisQueuesAsync();
            QueueList.ItemsSource = queues;
            QueueCountLabel.Text = queues.Count.ToString();
            RedisInfoBox.Text = await QueueService.GetRedisInfoAsync();
        }
        else
        {
            QueueList.ItemsSource = null;
            QueueCountLabel.Text = "0";
            RedisInfoBox.Text = "Redis is not running. Start it from the Services page.";
        }
    }

    private async void FlushQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string queueName)
        {
            btn.IsEnabled = false;
            await QueueService.FlushQueueAsync(queueName);
            btn.IsEnabled = true;
            await RefreshAsync();
        }
    }
}
