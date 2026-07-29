using System;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class SslPage : Page
{
    public SslPage()
    {
        InitializeComponent();
        DomainBox.TextChanged += (s, e) => 
        {
            var isLocal = DomainBox.Text.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
            LetsEncryptPanel.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
            GenLeBtn.IsEnabled = !isLocal;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadCerts();
    }

    private void LoadCerts()
    {
        SslList.ItemsSource = SslService.GetLocalCertificates();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadCerts();

    private async void GenLocalBtn_Click(object sender, RoutedEventArgs e)
    {
        var domain = DomainBox.Text.Trim();
        if (string.IsNullOrEmpty(domain)) return;

        SetBusy(true, "Generating mkcert certificate...");
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);
        
        var success = await Task.Run(() => SslService.GenerateLocalCertAsync(domain, log));
        
        SetBusy(false);
        if (success)
        {
            DomainBox.Text = "";
            LoadCerts();
        }
    }

    private async void GenLeBtn_Click(object sender, RoutedEventArgs e)
    {
        var domain = DomainBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email)) return;

        SetBusy(true, "Requesting Let's Encrypt certificate...");
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);
        
        var success = await Task.Run(() => SslService.GenerateLetsEncryptAsync(domain, email, log));
        
        SetBusy(false);
        if (success)
        {
            DomainBox.Text = "";
            EmailBox.Text = "";
            LoadCerts();
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            SslService.DeleteCert(domain);
            LoadCerts();
        }
    }

    private void SetBusy(bool busy, string msg = "")
    {
        if (busy)
        {
            OpBanner.Visibility = Visibility.Visible;
            OpMsg.Text = msg;
            GenLocalBtn.IsEnabled = false;
            GenLeBtn.IsEnabled = false;
        }
        else
        {
            OpBanner.Visibility = Visibility.Collapsed;
            GenLocalBtn.IsEnabled = true;
            GenLeBtn.IsEnabled = true;
        }
    }
}

}
