using System;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views;

public sealed partial class ContactSupportPage : Page
{
    public ContactSupportPage()
    {
        this.InitializeComponent();
    }

    private async void Email_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        await Launcher.LaunchUriAsync(new Uri("mailto:shahinalam3546@gmail.com"));
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void Community_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        await Launcher.LaunchUriAsync(new Uri("https://www.facebook.com/groups/1716873689636854"));
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }
}
