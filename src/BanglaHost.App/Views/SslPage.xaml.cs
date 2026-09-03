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
        DomainBox.TextChanged += (s, e) => UpdateDomainKind();
        UpdateDomainKind();
    }

    /// <summary>
    /// Decide whether the typed domain is local.
    ///
    /// This used to hardcode ".test". A user who set their TLD to <c>.local</c> or <c>.dev</c> in
    /// Settings — which BanglaHost fully supports and uses everywhere else — was shown the public
    /// Let's Encrypt form for their own local sites.
    /// </summary>
    private void UpdateDomainKind()
    {
        var tld = "." + (Config.Load().Tld ?? "test").TrimStart('.');
        var text = DomainBox.Text?.Trim() ?? "";
        var isLocal = text.EndsWith(tld, StringComparison.OrdinalIgnoreCase)
                   || text.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
                   || text.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

        LetsEncryptPanel.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;

        // Let's Encrypt issuance is not implemented (SslService.LetsEncryptSupported == false).
        // The button used to be enabled and ran a two-second fake that logged "Simulation complete",
        // which every user read as a failed real attempt.
        GenLeBtn.IsEnabled = !isLocal && SslService.LetsEncryptSupported;
        if (!SslService.LetsEncryptSupported)
        {
            ToolTipService.SetToolTip(GenLeBtn,
                "Not available in this version. Issue a public certificate with win-acme or certbot "
                + "and copy the .pem files into the certs folder.");
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateDomainKind();
        BackgroundWork.Handler(LoadCertsAsync, "SslPage.LoadCerts");
    }

    /// <summary>Parsing every .pem is CPU + disk work; it ran on the UI thread on every navigation.</summary>
    private async Task LoadCertsAsync()
    {
        var certs = await Task.Run(() => SslService.GetLocalCertificates());
        SslList.ItemsSource = certs;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => BackgroundWork.Handler(LoadCertsAsync, "SslPage.Refresh");

    private void GenLocalBtn_Click(object sender, RoutedEventArgs e)
        => BackgroundWork.Handler(GenLocalAsync, "SslPage.GenLocal");

    private async Task GenLocalAsync()
    {
        var domain = DomainBox.Text.Trim();
        if (string.IsNullOrEmpty(domain)) return;

        SetBusy(true, "Generating mkcert certificate…");
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);

        bool success;
        try { success = await Task.Run(() => SslService.GenerateLocalCertAsync(domain, log)); }
        finally { SetBusy(false); }

        if (success)
        {
            DomainBox.Text = "";
            await LoadCertsAsync();
        }
        else
        {
            EngineHost.Instance.Append($"[SSL] Certificate generation for {domain} did not complete — see the message above.");
        }
    }

    private void GenLeBtn_Click(object sender, RoutedEventArgs e)
        => BackgroundWork.Handler(GenLeAsync, "SslPage.GenLetsEncrypt");

    private async Task GenLeAsync()
    {
        var domain = DomainBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email)) return;

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);
        OpBanner.Visibility = Visibility.Visible;
        await SslService.GenerateLetsEncryptAsync(domain, email, log);
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string domain)
        {
            try { SslService.DeleteCert(domain); }
            catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); }
            BackgroundWork.Handler(LoadCertsAsync, "SslPage.AfterDelete");
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
            UpdateDomainKind();      // restores the LE button to its real (disabled) state
        }
    }
}

}
