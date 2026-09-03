using System;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace BanglaHost.App.Views
{

public sealed partial class ProjectDetectionPage : Page
{
    public ProjectDetectionPage()
    {
        InitializeComponent();
    }

    private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var folderPath = await BanglaHost.App.Services.Picker.FolderAsync();
        if (folderPath != null)
        {
            PathBox.Text = folderPath;
            AnalyzeBtn_Click(sender, e);
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void AnalyzeBtn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        
        var path = PathBox.Text;
        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorText.Text = "Please enter a project directory.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var info = ProjectDetector.Detect(path);
            
            ResFramework.Text = info.Framework;
            ResLanguage.Text = info.Language;
            ResPkg.Text = info.PackageManager;
            ResDb.Text = info.Database;
            ResNode.Text = info.NodeVersion;
            ResPhp.Text = info.PhpVersion;
            
            ScoreText.Text = $"{info.HealthScore}/100";
            if (info.HealthScore >= 90)
                ScoreBadge.Background = new SolidColorBrush(Colors.SeaGreen);
            else if (info.HealthScore >= 70)
                ScoreBadge.Background = new SolidColorBrush(Colors.DarkOrange);
            else
                ScoreBadge.Background = new SolidColorBrush(Colors.Firebrick);
                
            ResNotes.Text = string.IsNullOrEmpty(info.HealthNotes) ? "Project looks healthy." : info.HealthNotes;
            ResMissing.ItemsSource = info.MissingDependencies;
            
            ResultsPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}

}
