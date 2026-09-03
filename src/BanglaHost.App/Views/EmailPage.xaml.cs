using System;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class EmailPage : Page
{
    public EmailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        EmailLogList.ItemsSource = EmailService.GetLog();
        MailhogStatus.Text = EmailService.IsMailhogRunning() ? "MailHog is running." : "MailHog is stopped.";
    }

    private void SaveSmtpBtn_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SmtpProfile(
            Id: Guid.NewGuid().ToString(),
            Name: SmtpHostBox.Text,
            Host: SmtpHostBox.Text.Trim(),
            Port: int.TryParse(SmtpPortBox.Text, out var port) ? port : 587,
            Username: SmtpUserBox.Text.Trim(),
            Password: SmtpPassBox.Password,
            UseSsl: SslToggle.IsOn
        );
        EmailService.SaveProfile(profile);
        SmtpStatus.Text = "Profile saved.";
    }

    private async void TestSmtpBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var to = TestToBox.Text.Trim();
        if (string.IsNullOrEmpty(to)) { SmtpStatus.Text = "Enter a test recipient."; return; }

        var profile = new SmtpProfile(
            Id: "test",
            Name: "Test",
            Host: SmtpHostBox.Text.Trim(),
            Port: int.TryParse(SmtpPortBox.Text, out var port) ? port : 587,
            Username: SmtpUserBox.Text.Trim(),
            Password: SmtpPassBox.Password,
            UseSsl: SslToggle.IsOn
        );

        TestSmtpBtn.IsEnabled = false;
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => SmtpStatus.Text = msg);
        
        await Task.Run(() => EmailService.SendTestEmailAsync(profile, to, log));
        
        TestSmtpBtn.IsEnabled = true;
        EmailLogList.ItemsSource = EmailService.GetLog();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void StartMailhog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => MailhogStatus.Text = msg);
        await Task.Run(() => EmailService.StartMailhogAsync(log));
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void StopMailhog_Click(object sender, RoutedEventArgs e)
    {
        EmailService.StopMailhog();
        MailhogStatus.Text = "MailHog stopped.";
    }

    private void OpenMailhog_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("http://localhost:8025"));
    }
}
