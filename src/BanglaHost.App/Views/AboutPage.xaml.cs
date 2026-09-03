using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.System;
using BanglaHost.App.Services;

namespace BanglaHost.App.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            
            try
            {
                VersionText.Text = $"Version {Updater.CurrentVersion}";
            }
            catch
            {
                VersionText.Text = "Version 1.0.0";
            }
        }

        private async void Email_Click(object sender, RoutedEventArgs e)
        {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("mailto:shahinalam3546@gmail.com"));
            } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

        private async void License_Click(object sender, RoutedEventArgs e)
        {
        try
        {
            await ShowDialog("License Information", "BanglaHost is commercial software. All rights reserved by IT Amadersomaj Inc.");
            } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

        private async void ThirdParty_Click(object sender, RoutedEventArgs e)
        {
        try
        {
            await ShowDialog("Third-party Licenses", "BanglaHost uses various open-source components including Nginx, PHP, MariaDB, and Redis. Licenses for these components can be found in their respective distribution directories.");
            } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

        private async void Privacy_Click(object sender, RoutedEventArgs e)
        {
        try
        {
            await ShowDialog("Privacy Policy", "BanglaHost operates locally on your machine. We do not collect, transmit, or store any of your local development data or telemetry.");
            } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

        private async System.Threading.Tasks.Task ShowDialog(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
