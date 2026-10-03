using System;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace BanglaHost.App.Views
{

public sealed partial class AiAssistantPage : Page
{
    public AiAssistantPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = CheckOllamaAsync();

    private async System.Threading.Tasks.Task CheckOllamaAsync()
    {
        try
        {
            var running = await System.Threading.Tasks.Task.Run(() => Ollama.Running());
            SetupCard.Visibility = !running ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private async void InstallOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetupCard.Visibility = Visibility.Collapsed;
            await EngineHost.Instance.RunCaptured(() => EngineHost.Instance.Engine.Install("ollama"));
            await CheckOllamaAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void Input_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Send();
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        var msg = Input.Text.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        Input.Text = "";
        AddMessage(msg, true);

        // Mock response for now, unless Ollama API is easily callable
        AddMessage("I'm currently in 'Advanced mode'. I can see you are asking about: " + msg + ". In the full version, I will be able to perform actions like creating sites or fixing configuration errors directly.", false);
    }

    private void AddMessage(string text, bool isUser)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 500,
            Background = isUser
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"]
        };
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isUser ? new SolidColorBrush(Colors.White) : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
        border.Child = tb;
        ChatList.Children.Add(border);
        ChatScroll.ChangeView(0, ChatScroll.ScrollableHeight, 1);
    }
}

}
