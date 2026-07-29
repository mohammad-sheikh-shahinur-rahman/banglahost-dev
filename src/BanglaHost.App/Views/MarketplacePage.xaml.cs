using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views
{

public sealed partial class MarketplacePage : Page
{
    public MarketplacePage()
    {
        InitializeComponent();
        AppGrid.ItemsSource = new List<MarketApp>
        {
            new() { Name = "WordPress", Description = "The world's most popular CMS.", Icon = "\uE774", Type = "CMS" },
            new() { Name = "Laravel", Description = "A PHP framework for web artisans.", Icon = "\uE943", Type = "Framework" },
            new() { Name = "WHMCS", Description = "Web hosting billing & automation (Requires ionCube).", Icon = "\uEC05", Type = "Billing" },
            new() { Name = "Next.js", Description = "The React Framework for the Web.", Icon = "\uE943", Type = "Framework" },
            new() { Name = "Vue.js", Description = "The Progressive JavaScript Framework.", Icon = "\uE943", Type = "Framework" },
            new() { Name = "PrestaShop", Description = "The leading open-source e-commerce solution.", Icon = "\uE719", Type = "E-Commerce" }
        };
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is MarketApp app)
        {
            // Prompt for target directory
            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            btn.IsEnabled = false;
            btn.Content = "Installing...";
            
            // In a real app, we'd want to show a dialog with logs.
            // For now, fire and forget / show basic result
            var success = await System.Threading.Tasks.Task.Run(() => 
                BanglaHost.Core.InstallerService.InstallAppAsync(app.Name, folder.Path, msg => { /* log to output window / dialog */ })
            );

            btn.Content = success ? "Installed" : "Failed";
            if (success) btn.IsEnabled = false;
            else btn.IsEnabled = true;
        }
    }

    public class MarketApp
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "\uE719";
        public string Type { get; set; } = "App";
    }
}

}
