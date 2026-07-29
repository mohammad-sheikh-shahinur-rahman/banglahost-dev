using System;
using System.IO;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class EnvManagerPage : Page
{
    private string _currentEnvFile = "";

    public EnvManagerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var snap = await EngineHost.Instance.Snapshot();
        SiteList.ItemsSource = snap.Sites.Select(s => s.Root).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
    }

    private void SiteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SiteList.SelectedItem is string path)
        {
            _currentEnvFile = Path.Combine(path, ".env");
            if (File.Exists(_currentEnvFile))
            {
                EnvEditor.Text = File.ReadAllText(_currentEnvFile);
                StatusLabel.Text = "Loaded.";
            }
            else
            {
                // Try .env.example if .env doesn't exist
                var example = Path.Combine(path, ".env.example");
                if (File.Exists(example))
                {
                    EnvEditor.Text = File.ReadAllText(example);
                    StatusLabel.Text = "Loaded from .env.example (save to create .env)";
                }
                else
                {
                    EnvEditor.Text = "";
                    StatusLabel.Text = "No .env found. Save to create.";
                }
            }
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentEnvFile))
        {
            StatusLabel.Text = "No site selected.";
            return;
        }

        try
        {
            File.WriteAllText(_currentEnvFile, EnvEditor.Text);
            StatusLabel.Text = "Saved successfully at " + DateTime.Now.ToShortTimeString();
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error saving: " + ex.Message;
        }
    }
}
