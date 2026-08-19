using System;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class DomainsPage : Page
{
    public DomainsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadHosts();
    }

    private void LoadHosts()
    {
        var cfg = Config.Load();
        var tld = $".{cfg.Tld}";
        HostsList.ItemsSource = DomainService.GetHostsEntries()
            .Where(x => x.Domain.EndsWith(tld, StringComparison.OrdinalIgnoreCase) ||
                        x.Domain.Contains("localhost"))
            .ToList();
    }

    private async void AddHostBtn_Click(object sender, RoutedEventArgs e)
    {
        var ip = HostIpBox.Text.Trim();
        var domain = HostDomainBox.Text.Trim();
        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(domain)) return;

        AddHostBtn.IsEnabled = false;
        HostOpStatus.Text = "Updating HOSTS file... please accept UAC prompt if it appears.";
        
        var success = await Task.Run(() => DomainService.UpdateHostEntryAsync(ip, domain, true));
        
        AddHostBtn.IsEnabled = true;
        HostOpStatus.Text = success ? "Added successfully." : "Failed. UAC rejected or elevate tool missing.";
        
        if (success)
        {
            HostDomainBox.Text = "";
            LoadHosts();
        }
    }

    private async void RemoveHostBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            btn.IsEnabled = false;
            HostOpStatus.Text = "Updating HOSTS file... please accept UAC prompt if it appears.";
            
            // For removal, we don't strictly need IP, but the script might. Assuming the helper script removes all entries for the domain.
            var success = await Task.Run(() => DomainService.UpdateHostEntryAsync("127.0.0.1", domain, false));
            
            btn.IsEnabled = true;
            HostOpStatus.Text = success ? "Removed successfully." : "Failed.";
            
            if (success) LoadHosts();
        }
    }

    private async void CheckDnsBtn_Click(object sender, RoutedEventArgs e)
    {
        var domain = CheckDomainBox.Text.Trim();
        if (string.IsNullOrEmpty(domain)) return;

        CheckDnsBtn.IsEnabled = false;
        DnsResult.Text = "Resolving...";
        
        var res = await Task.Run(() => DomainService.CheckDnsResolutionAsync(domain));
        
        DnsResult.Text = res;
        CheckDnsBtn.IsEnabled = true;
    }

    private async void UpdateCfBtn_Click(object sender, RoutedEventArgs e)
    {
        var token = CfTokenBox.Text.Trim();
        var zone = CfZoneBox.Text.Trim();
        var domain = CfDomainBox.Text.Trim();
        var ip = CfIpBox.Text.Trim();

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(ip))
        {
            CfResult.Text = "All fields are required.";
            return;
        }

        UpdateCfBtn.IsEnabled = false;
        CfResult.Text = "Updating Cloudflare...";

        var success = await Task.Run(() => DomainService.UpdateCloudflareDnsAsync(token, zone, domain, ip));

        CfResult.Text = success ? "DNS updated successfully!" : "Failed to update DNS (check token and zone ID).";
        UpdateCfBtn.IsEnabled = true;
    }
}

}
