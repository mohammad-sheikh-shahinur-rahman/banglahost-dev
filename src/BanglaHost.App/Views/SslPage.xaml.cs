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

        // Let's Encrypt issuance needs a public ACME client (not shipped).
        // Keep the button ENABLED so it always does something useful: explains + opens the
        // certs folder. A permanently disabled button reads as a dead/broken control.
        GenLeBtn.IsEnabled = !isLocal;
        ToolTipService.SetToolTip(GenLeBtn,
            "Public domains need a win-acme/certbot certificate — click for steps.");
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
        if (string.IsNullOrEmpty(domain))
        {
            OpBanner.Visibility = Visibility.Visible;
            OpMsg.Text = "Enter a domain first (e.g. mysite.test).";
            return;
        }

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
        OpBanner.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(email))
        {
            OpMsg.Text = "Enter both domain and email first.";
            return;
        }

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => OpMsg.Text = msg);
        await SslService.GenerateLetsEncryptAsync(domain, email, log);
        // Guide the user to the manual path + open the folder so the button always helps.
        try
        {
            var dlg = new ContentDialog
            {
                Title = "Let's Encrypt — manual steps",
                Content = "BanglaHost doesn't ship an ACME client yet.\n\n1. Run win-acme or certbot for " + domain + "\n2. Copy the .pem certificate + key into:\n" + Paths.Certs + "\n3. Press Refresh below — the cert appears in Installed Certificates.",
                PrimaryButtonText = "Open certs folder",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
            };
            if (this.Content == null || this.XamlRoot == null) return;
            if (await BanglaHost.App.Services.DialogQueue.ShowAsync(dlg) == ContentDialogResult.Primary)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Paths.Certs, UseShellExecute = true,
                    });
                }
                catch { }
            }
        }
        catch { }
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
