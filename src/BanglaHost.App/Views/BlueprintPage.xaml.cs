using System;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views
{

public sealed partial class BlueprintPage : Page
{
    public BlueprintPage() => InitializeComponent();

    private async void Gen_Click(object sender, RoutedEventArgs e)
    {
        var name = ProjectName.Text.Trim();
        if (name.Length == 0) { await Info("Enter a project name first (lowercase letters, digits, hyphens)."); return; }
        var picked = (Blueprint.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        // Map the friendly blueprint label to the site type Engine.SiteAdd already understands.
        var type = picked switch
        {
            "WordPress (blog)"        => "wordpress",
            "Laravel API + Vite"      => "laravel",
            "Symfony (skeleton)"      => "symfony",
            "CodeIgniter 4"           => "codeigniter",
            "Slim REST API"           => "slim",
            "Blank PHP"               => "php",
            "Static HTML"             => "static",
            "Node app" or "Vue 3 + Vite" or "React (Vite)" or "Next.js" or "Nuxt 3"
                or "SvelteKit" or "Astro" or "Angular"    => "node",
            _ => "others",
        };
        Busy.IsActive = true; GenBtn.IsEnabled = false;
        Result.Text = $"Generating {picked}...";
        try
        {
            var (ok, output) = await EngineHost.Instance.RunCaptured(
                () => EngineHost.Instance.Engine.SiteAdd(name, php: "", root: null, server: "", type: type));
            Result.Text = ok ? $"Done. Open the Sites tab to see '{name}'." : "Failed. " + (output?.Trim() ?? "");
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { Result.Text = "Error: " + ex.Message; }
        finally { Busy.IsActive = false; GenBtn.IsEnabled = true; }
    }

    private Task Info(string body) =>
        new ContentDialog { Title = "Blueprint", Content = body, CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync().AsTask();
}

}
