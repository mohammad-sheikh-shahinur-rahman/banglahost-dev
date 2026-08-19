using System;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views;

public sealed partial class DocumentationPage : Page
{
    public DocumentationPage()
    {
        this.InitializeComponent();
    }

    private async void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.facebook.com/groups/1716873689636854"));
    }
}
