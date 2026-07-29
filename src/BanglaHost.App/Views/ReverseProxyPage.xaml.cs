using System;
using System.Collections.Generic;
using System.Linq;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class ReverseProxyPage : Page
{
    // In a real implementation, these would be saved to Config and Nginx/Apache configs generated
    private static List<ProxyRule> _rules = new();

    public ReverseProxyPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        RefreshList();
    }

    private void RefreshList()
    {
        ProxyList.ItemsSource = null;
        ProxyList.ItemsSource = _rules;
    }

    private void AddProxy_Click(object sender, RoutedEventArgs e)
    {
        var domain = DomainBox.Text.Trim();
        var port = PortBox.Text.Trim();

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(port))
        {
            StatusLabel.Text = "Please enter both domain and port.";
            return;
        }

        if (_rules.Any(r => r.Domain == domain))
        {
            StatusLabel.Text = "Domain already has a proxy rule.";
            return;
        }

        _rules.Add(new ProxyRule { Domain = domain, Target = $"http://127.0.0.1:{port}" });
        StatusLabel.Text = $"Proxy for {domain} added successfully. Restart Nginx to apply.";
        
        DomainBox.Text = "";
        PortBox.Text = "";
        RefreshList();
    }

    private void RemoveProxy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            _rules.RemoveAll(r => r.Domain == domain);
            RefreshList();
            StatusLabel.Text = $"Removed {domain}. Restart Nginx to apply.";
        }
    }

    public class ProxyRule
    {
        public string Domain { get; set; } = "";
        public string Target { get; set; } = "";
    }
}
