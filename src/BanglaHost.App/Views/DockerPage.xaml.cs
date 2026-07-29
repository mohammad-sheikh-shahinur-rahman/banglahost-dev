using System;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class DockerPage : Page
{
    public DockerPage()
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
        var installed = await DockerService.IsDockerInstalledAsync();
        if (!installed)
        {
            DockerStatusLabel.Text = "Docker is not installed.";
            return;
        }

        var running = await DockerService.IsDockerRunningAsync();
        DockerStatusLabel.Text = running ? "Docker is running" : "Docker is installed but not running.";

        if (running)
        {
            ContainerList.ItemsSource = await DockerService.ListContainersAsync();
            ImageList.ItemsSource = await DockerService.ListImagesAsync();
        }
    }

    private async void StartContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            await DockerService.StartContainerAsync(id);
            await RefreshAsync();
        }
    }

    private async void StopContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            await DockerService.StopContainerAsync(id);
            await RefreshAsync();
        }
    }

    private async void LogsContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            LogViewer.Text = await DockerService.GetContainerLogsAsync(id);
        }
    }

    private async void RemoveContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            await DockerService.RemoveContainerAsync(id);
            await RefreshAsync();
        }
    }

    private async void PullAndRun_Click(object sender, RoutedEventArgs e)
    {
        var image = PullImageBox.Text.Trim();
        if (string.IsNullOrEmpty(image)) return;

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() =>
        {
            LogViewer.Text += msg + "\n";
        });

        await Task.Run(async () =>
        {
            await DockerService.PullImageAsync(image, log);
            var name = image.Replace(":", "-").Replace("/", "-");
            await DockerService.RunContainerAsync(image, name);
        });

        await RefreshAsync();
    }
}
