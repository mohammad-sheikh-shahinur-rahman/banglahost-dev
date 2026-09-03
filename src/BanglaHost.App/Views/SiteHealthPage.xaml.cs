using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace BanglaHost.App.Views;

public sealed partial class SiteHealthPage : Page
{
    private string _selectedPath = "";

    public SiteHealthPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
        var snap = await EngineHost.Instance.Snapshot();
        SiteList.ItemsSource = snap.Sites.Select(s => s.Root).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void SiteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SiteList.SelectedItem is string path)
        {
            _selectedPath = path;
            RunBtn.IsEnabled = true;
            EmptyStateText.Visibility = Visibility.Collapsed;
            ReportPanel.Children.Clear();
            ReportPanel.Children.Add(new TextBlock { Text = "Ready to run diagnostics on " + path });
        }
    }

    private async void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        RunBtn.IsEnabled = false;
        ReportPanel.Children.Clear();
        
        await Task.Run(() => System.Threading.Thread.Sleep(500)); // fake delay for UX
        
        AddResult("Checking project directory", Directory.Exists(_selectedPath), "Directory exists", "Directory not found");
        
        if (!Directory.Exists(_selectedPath)) { RunBtn.IsEnabled = true; return; }

        var hasComposer = File.Exists(Path.Combine(_selectedPath, "composer.json"));
        var hasPackage = File.Exists(Path.Combine(_selectedPath, "package.json"));
        var hasEnvExample = File.Exists(Path.Combine(_selectedPath, ".env.example"));
        var hasEnv = File.Exists(Path.Combine(_selectedPath, ".env"));

        if (hasComposer)
        {
            AddResult("Composer Dependencies", Directory.Exists(Path.Combine(_selectedPath, "vendor")), "vendor folder exists", "vendor folder is missing. Run composer install.");
        }
        
        if (hasPackage)
        {
            AddResult("Node Dependencies", Directory.Exists(Path.Combine(_selectedPath, "node_modules")), "node_modules folder exists", "node_modules missing. Run npm install.");
        }

        if (hasEnvExample)
        {
            AddResult("Environment Variables", hasEnv, ".env file exists", "Missing .env file. Copy .env.example to .env.");
        }

        // Framework specific checks
        if (File.Exists(Path.Combine(_selectedPath, "artisan")))
        {
            AddResult("Laravel Cache", Directory.Exists(Path.Combine(_selectedPath, "bootstrap", "cache")), "Cache directory exists", "Missing bootstrap/cache directory");
            AddResult("Laravel Storage", Directory.Exists(Path.Combine(_selectedPath, "storage")), "Storage directory exists", "Missing storage directory");
        }
        
        if (File.Exists(Path.Combine(_selectedPath, "wp-config.php")))
        {
            AddResult("WordPress Config", true, "wp-config.php exists", "");
        }

        RunBtn.IsEnabled = true;
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void AddResult(string title, bool pass, string successMsg, string failMsg)
    {
        var tb = new TextBlock
        {
            Text = $"{(pass ? "[OK]" : "[FAIL]")} {title}: {(pass ? successMsg : failMsg)}",
            Foreground = new SolidColorBrush(pass ? Color.FromArgb(255, 34, 197, 94) : Color.FromArgb(255, 239, 68, 68)),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        ReportPanel.Children.Add(tb);
    }
}
