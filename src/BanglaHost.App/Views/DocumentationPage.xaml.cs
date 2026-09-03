using System;
using Windows.System;
using Windows.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace BanglaHost.App.Views;

/// <summary>
/// In-app documentation. The page shows a grid of topic cards (the index); clicking one
/// renders that guide's full content inline (the reader) — no browser or internet needed.
/// Guide content is built programmatically from small block helpers (H2/P/Bullet/Step/Code/
/// Callout) so it stays readable and consistent, and every paragraph is text-selectable.
/// </summary>
public sealed partial class DocumentationPage : Page
{
    // Callout accent colors.
    private static readonly Color TipBlue  = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);
    private static readonly Color NoteAmber = Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B);

    public DocumentationPage()
    {
        this.InitializeComponent();
    }

    // ── navigation between the index and the reader ──────────────────────────
    private void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(key)) return;
            ShowGuide(key!);
        }
        catch { /* a guide should never crash the app */ }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        DetailView.Visibility = Visibility.Collapsed;
        IndexView.Visibility = Visibility.Visible;
    }

    private async void JoinCommunity_Click(object sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri("https://www.facebook.com/groups/1716873689636854")); }
        catch { }
    }

    private void ShowGuide(string key)
    {
        DetailPanel.Children.Clear();

        string title, subtitle;
        switch (key)
        {
            case "getting-started": title = "Getting Started";
                subtitle = "Install the stack, learn where BanglaHost keeps your files, and create your first site.";
                BuildGettingStarted(); break;
            case "database": title = "Database Management";
                subtitle = "Connect to MySQL/MariaDB or PostgreSQL and browse your data in the built-in SQL Studio.";
                BuildDatabase(); break;
            case "ssl": title = "Local SSL (HTTPS)";
                subtitle = "Give every .test site a trusted https:// address with mkcert — no browser warnings.";
                BuildSsl(); break;
            case "php": title = "Multiple PHP Versions";
                subtitle = "Run a different PHP version per site, toggle extensions, and tune php.ini.";
                BuildPhp(); break;
            case "workers": title = "Background Workers";
                subtitle = "Run Laravel queues, scheduled Cron jobs, and NPM build scripts alongside your sites.";
                BuildWorkers(); break;
            case "tunnel": title = "Live Cloudflare Tunnels";
                subtitle = "Share a local site on a public https:// URL — no port-forwarding, no router setup.";
                BuildTunnel(); break;
            default: return;
        }

        DetailTitle.Text = title;
        DetailSubtitle.Text = subtitle;
        IndexView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        try { DetailScroller.ChangeView(null, 0, null, true); } catch { }
    }

    // ── content builders ─────────────────────────────────────────────────────
    private void BuildGettingStarted()
    {
        H2("What BanglaHost is");
        P("BanglaHost is a complete local web stack for Windows — a modern alternative to XAMPP, Laragon, WAMP and MAMP. " +
          "It manages nginx and Apache, PHP, MySQL/MariaDB, PostgreSQL, Redis and more, and gives every project a friendly https://name.test address.");

        H2("First-run setup");
        P("The first time you open BanglaHost it checks whether the core stack (nginx, PHP, a database and mkcert) is installed. " +
          "If anything is missing it offers a one-click \u201cInstall now\u201d that downloads and starts everything for you — about a minute.");
        Callout("Tip", "If a download gets blocked, it's almost always antivirus. Accept the \u201cAdd exclusions\u201d prompt during setup, " +
                       "or add the C:\\BanglaHost folder to your antivirus's exclusion list by hand.", TipBlue);

        H2("Where your files live");
        P("Everything lives under C:\\BanglaHost. The folders you'll touch most often:");
        Code(
            "C:\\BanglaHost\\\n" +
            "  sites\\    your project web roots (one folder per site)\n" +
            "  bin\\      downloaded PHP, nginx, MariaDB, Node\u2026\n" +
            "  config\\   banglahost.json (all your settings)\n" +
            "  nginx\\    generated nginx config + per-site vhosts\n" +
            "  certs\\    mkcert SSL certificates\n" +
            "  logs\\     nginx / PHP / database logs\n" +
            "  run\\      pid & port files for each running service\n" +
            "  tmp\\      scratch space (safe to clear)");

        H2("Create your first site");
        Step(1, "Open the Sites tab and click Add site — or use Quick App for one-click WordPress / Laravel.");
        Step(2, "Type a name, e.g. myapp. Your site will live at https://myapp.test.");
        Step(3, "Pick a PHP version and project type (WordPress, Laravel, plain PHP, static HTML, Node, Python\u2026).");
        Step(4, "Choose nginx (right for almost everything) or Apache if you specifically need .htaccess support.");
        Step(5, "Leave HTTPS ticked so BanglaHost issues a trusted certificate, then click Add site.");
        P("BanglaHost creates the folder, writes the web-server config, adds the .test entry to your Windows hosts file, " +
          "issues an SSL certificate and starts serving. Click the site to open it in your browser.");
        Callout("Note", "The .test domain and its certificate only work while the core services are running. " +
                        "If a site doesn't load, use Start All (top of the window, or the tray icon).", NoteAmber);

        H2("Starting and stopping");
        Bullet("Start All / Stop All — from the Dashboard or the tray icon — bring the whole stack up or down at once.");
        Bullet("BanglaHost keeps running in the system tray when you close the window, so your sites stay up. You can turn that off in Settings.");
        Bullet("The Services tab lets you start, stop and restart each service (nginx, PHP, database\u2026) individually.");
    }

    private void BuildDatabase()
    {
        H2("Default connection details");
        P("BanglaHost runs one MySQL-compatible engine (MariaDB or MySQL) and, optionally, PostgreSQL. " +
          "On a fresh install the administrator account has no password — ideal for local development.");
        Code(
            "MySQL / MariaDB\n" +
            "  Host:      127.0.0.1   (or localhost)\n" +
            "  Port:      3306\n" +
            "  User:      root\n" +
            "  Password:  (empty)\n" +
            "\n" +
            "PostgreSQL\n" +
            "  Host:      127.0.0.1\n" +
            "  Port:      5432\n" +
            "  User:      postgres\n" +
            "  Password:  (empty)");
        Callout("Tip", "Some apps refuse an empty password. Set a root password on the Databases tab, then use that same password in your app's .env or wp-config.php.", TipBlue);

        H2("SQL Studio");
        P("The SQL Studio tab in the sidebar is a full database client built into BanglaHost — browse databases and tables, run queries, and read results without installing anything extra.");
        Bullet("Pick the engine (MySQL/MariaDB or PostgreSQL) at the top.");
        Bullet("Select a database, then a table, to see its rows.");
        Bullet("Type SQL in the query box and run it — results appear in a grid you can copy from.");

        H2("Creating a database");
        Step(1, "Open the Databases tab.");
        Step(2, "Click Create database and give it a name, e.g. myapp.");
        Step(3, "Point your app at it using the connection details above.");
        P("When you create a WordPress or Laravel site with Quick App, BanglaHost creates the database and wires up the credentials for you automatically.");

        H2("Connecting from your code");
        P("Use 127.0.0.1 as the host (not a Unix socket). A typical Laravel .env:");
        Code(
            "DB_CONNECTION=mysql\n" +
            "DB_HOST=127.0.0.1\n" +
            "DB_PORT=3306\n" +
            "DB_DATABASE=myapp\n" +
            "DB_USERNAME=root\n" +
            "DB_PASSWORD=");

        H2("phpMyAdmin & Adminer");
        P("Prefer a web UI? BanglaHost can serve phpMyAdmin and Adminer — open either from the Databases tab.");
        Callout("Note", "MySQL and MariaDB share port 3306, so only one can run at a time. Stop one before starting the other from the Services or Databases tab.", NoteAmber);
    }

    private void BuildSsl()
    {
        H2("How local HTTPS works");
        P("Public certificate authorities won't sign .test domains, so BanglaHost uses mkcert. mkcert creates a private certificate authority on your machine and installs it into Windows' trust store; " +
          "certificates it then issues for your sites are trusted by your browsers — no red warnings.");

        H2("Turning on HTTPS for a site");
        Bullet("New site: just leave the HTTPS box ticked in the Add site form.");
        Bullet("Existing site: toggle SSL on from the site's row on the Sites tab, or from the SSL tab.");
        P("BanglaHost issues the certificate into C:\\BanglaHost\\certs, updates the web-server config and reloads it. Your site is now reachable at https://name.test.");

        H2("First-time trust setup");
        P("The very first time, mkcert installs its local certificate authority. Windows shows a security prompt — accept it. " +
          "This is a one-time step; every certificate issued afterwards is trusted automatically.");
        Callout("Tip", "Installed the CA but the browser still warns? Fully quit and reopen the browser. Firefox keeps its own trust store — mkcert handles it, but a browser restart is still needed.", TipBlue);

        H2("Fixing \u201cNot secure\u201d or certificate warnings");
        Bullet("Make sure you're visiting https:// (not http://) and the exact .test name.");
        Bullet("Re-issue the certificate: toggle SSL off and back on for the site on the Sites tab.");
        Bullet("Confirm the mkcert root CA is installed — the SSL tab shows its status and can reinstall it.");
        Bullet("Restart the browser so it re-reads the Windows trust store.");

        H2("The .test domain");
        P("BanglaHost maps name.test to 127.0.0.1 by editing your Windows hosts file (C:\\Windows\\System32\\drivers\\etc\\hosts). " +
          "Editing that file requires administrator rights, so BanglaHost asks for elevation the first time — this is expected and safe.");
    }

    private void BuildPhp()
    {
        H2("Versions BanglaHost manages");
        P("BanglaHost can download and run PHP 7.4 and 8.0 through 8.4 side by side. Each site is pinned to the version you choose, " +
          "so a legacy app on 7.4 and a modern one on 8.3 happily coexist.");

        H2("Setting a site's PHP version");
        Bullet("When adding a site, pick the version from the PHP dropdown in the Add site form.");
        Bullet("To change it later, use the PHP Versions tab — select the site's version and BanglaHost rewrites the config and reloads.");

        H2("Installing a new version");
        Step(1, "Open the PHP Versions tab.");
        Step(2, "Click Install next to the version you want — BanglaHost downloads a portable build into C:\\BanglaHost\\bin.");
        Step(3, "It's immediately available in the PHP dropdown for new and existing sites.");

        H2("Extensions");
        P("The PHP Extensions tab lists every extension for the active version. Tick one to enable it (BanglaHost adds/uncomments the extension= line) and reloads PHP.");
        Bullet("Common ones — mysqli, pdo_mysql, gd, curl, mbstring, intl, zip, openssl — are on by default.");
        Bullet("ionCube Loader is supported for running encoded commercial code.");

        H2("Editing php.ini");
        P("The PHP INI Manager tab lets you edit settings without hunting for the file. Values worth raising for local development:");
        Code(
            "memory_limit = 512M\n" +
            "upload_max_filesize = 128M\n" +
            "post_max_size = 128M\n" +
            "max_execution_time = 120\n" +
            "display_errors = On");
        Callout("Note", "Each PHP version has its own php.ini. If you switch a site's version, you may need to re-apply an extension or setting for the new version.", NoteAmber);
        Callout("Tip", "After editing php.ini or toggling an extension, BanglaHost reloads PHP automatically. If a change doesn't take effect, restart PHP from the Services tab.", TipBlue);
    }

    private void BuildWorkers()
    {
        H2("What you can run");
        P("Long-running and scheduled tasks — queue workers, the Laravel scheduler and Node build/watch scripts — are managed from BanglaHost " +
          "so they start with your stack and keep running in the background.");

        H2("Laravel queue workers");
        P("The Background Workers tab runs and supervises a queue worker for a site. BanglaHost keeps it alive and restarts it if it stops.");
        Code("# what BanglaHost runs for you, per site:\nphp artisan queue:work --tries=3 --timeout=90");
        Callout("Tip", "After deploying code changes, restart the worker so it picks up the new code — queue workers hold your app in memory.", TipBlue);

        H2("Scheduled tasks (Cron)");
        P("The Background Workers tab is a Windows-friendly cron: add a schedule and a command and BanglaHost runs it — the local equivalent of a crontab line. " +
          "For Laravel, add one entry that ticks the scheduler every minute:");
        Code("* * * * *   php artisan schedule:run");
        Bullet("Set the interval — every minute, hourly, daily, or a custom cron expression.");
        Bullet("Point the command at the site's PHP and script.");
        Bullet("Watch output and the last-run status right in the tab.");

        H2("NPM scripts");
        P("The NPM Scripts tab reads a project's package.json and lists its scripts (dev, build, watch\u2026). Click one to run it; output streams live — great for Vite / Laravel Mix asset watching while you develop.");
        Step(1, "Open the NPM Scripts tab and pick the project.");
        Step(2, "Choose a script such as dev or build.");
        Step(3, "Click Run — leave dev/watch running while you work, then stop it when you're done.");
        Callout("Note", "These processes are children of BanglaHost. Quitting BanglaHost completely (not just closing it to the tray) stops them.", NoteAmber);
    }

    private void BuildTunnel()
    {
        H2("What a tunnel does");
        P("A Cloudflare Tunnel gives your local site a temporary public address like https://something.trycloudflare.com. " +
          "Cloudflare routes visitors to your machine over an outbound connection, so it works behind routers, firewalls and CG-NAT with nothing to configure.");

        H2("Sharing a site");
        Bullet("Sites tab: click Share on the site's row.");
        Bullet("Cloudflare Tunnel tab: pick the site and press Start.");
        P("BanglaHost launches cloudflared, waits for the public URL, and shows it with Copy and Open buttons. Stopping the tunnel (Stop, or Share again) takes the URL offline.");

        H2("WordPress over a tunnel");
        P("WordPress hard-codes its site URL in the database, which normally breaks a tunnel (links point back at the .test address). " +
          "BanglaHost drops in a small must-use plugin that detects tunnel traffic and rewrites URLs on the fly, so your WordPress site works over the public link with no manual changes. It's removed automatically when you stop the tunnel.");
        Callout("Note", "trycloudflare.com URLs are temporary — they change each time you start a tunnel and end when you stop it or close BanglaHost. Perfect for demos and webhook testing, not permanent hosting.", NoteAmber);

        H2("Troubleshooting");
        Bullet("No URL appears: press Stop then Start again — BanglaHost clears any stuck cloudflared process and retries.");
        Bullet("\u201cCouldn't share publicly\u201d: make sure the site loads locally first (Start All), then retry.");
        Bullet("Need the raw output? Check the tunnel log file under C:\\BanglaHost\\run.");
        Callout("Tip", "The person you share with needs nothing installed — just send them the https link. Keep BanglaHost open to keep the tunnel alive.", TipBlue);
    }

    // ── block helpers (each appends one element to DetailPanel) ───────────────
    private void H2(string text) => DetailPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
        Margin = new Thickness(0, 16, 0, 2),
    });

    private void P(string text) => DetailPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 15,
        LineHeight = 24,
        Opacity = 0.9,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    });

    private void Bullet(string text)
    {
        var g = new Grid { Margin = new Thickness(4, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new TextBlock { Text = "\u2022", FontSize = 15, Opacity = 0.9 };
        var tb = new TextBlock
        {
            Text = text, FontSize = 15, LineHeight = 24, Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(dot);
        g.Children.Add(tb);
        DetailPanel.Children.Add(g);
    }

    private void Step(int n, string text)
    {
        var g = new Grid { Margin = new Thickness(4, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var num = new TextBlock { Text = n + ".", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(TipBlue) };
        var tb = new TextBlock
        {
            Text = text, FontSize = 15, LineHeight = 24, Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(num);
        g.Children.Add(tb);
        DetailPanel.Children.Add(g);
    }

    private void Code(string text) => DetailPanel.Children.Add(new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 2, 0, 2),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13.5,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        },
    });

    private void Callout(string label, string text, Color accent)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14.5,
            LineHeight = 22,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        DetailPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 4, 0, 4),
            Child = panel,
        });
    }
}
