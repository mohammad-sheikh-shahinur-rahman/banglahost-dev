using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class PhpManagerPage : Page
{
    public PhpManagerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        RefreshList();
    }

    private void RefreshList()
    {
        var phpDir = Path.Combine(Paths.Bin, "php");
        if (!Directory.Exists(phpDir)) return;
        var vers = Directory.GetDirectories(phpDir)
                            .Select(Path.GetFileName)
                            .Where(v => v!.StartsWith("8.") || v.StartsWith("7."))
                            .OrderByDescending(v => v)
                            .ToList();
        VersList.ItemsSource = vers;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var v = VerBox.Text.Trim();
        if (string.IsNullOrEmpty(v)) return;
        await DoInstall(v);
    }

    private async void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string v) await DoInstall(v);
    }

    private async Task DoInstall(string version)
    {
        Busy.IsActive = true;
        OutputBox.Text = $"Fetching PHP {version} from windows.php.net...\n";
        try
        {
            await Downloader.InstallPhp(version);
            OutputBox.Text += $"PHP {version} installed successfully!\n";
            RefreshList();
        }
        catch (Exception ex)
        {
            OutputBox.Text += $"Error: {ex.Message}\n";
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private void MakeDefault_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string v)
        {
            var cfgPath = Paths.ConfigJson;
            var cfg = Config.Load();
            cfg.DefaultPhp = v;
            
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(cfgPath, System.Text.Json.JsonSerializer.Serialize(cfg, opts));
            
            OutputBox.Text = $"Default PHP version set to {v}\n";
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string v)
        {
            try
            {
                var dir = Path.Combine(Paths.Bin, "php", v);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                OutputBox.Text = $"Removed PHP {v}\n";
                RefreshList();
            }
            catch (Exception ex)
            {
                OutputBox.Text = $"Failed to remove {v}: {ex.Message}\n";
            }
        }
    }
}
