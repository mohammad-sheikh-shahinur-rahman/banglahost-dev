using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class PhpIniPage : Page
{
    private string _currentVersion = "";
    private bool _loading = false;

    public PhpIniPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        VersionBox.ItemsSource = BanglaHost.Core.Services.PhpVersions.Where(v => Tools.PhpExe(v) != null).ToList();
        var defaultPhp = Config.Load().DefaultPhp;
        if (VersionBox.Items.Contains(defaultPhp))
            VersionBox.SelectedItem = defaultPhp;
        else if (VersionBox.Items.Count > 0)
            VersionBox.SelectedIndex = 0;
            
        LoadIniAsync();
    }

    private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionBox.SelectedItem is string v)
        {
            _currentVersion = v;
            LoadIniAsync();
        }
    }

    private async void LoadIniAsync()
    {
        if (string.IsNullOrEmpty(_currentVersion)) return;
        
        try
        {
            _loading = true;
            
            var iniPath = Php.IniPath(_currentVersion);
            if (File.Exists(iniPath))
            {
                var text = await File.ReadAllTextAsync(iniPath);
                IniEditor.Text = text;
            
            // Sync Quick Settings
            UploadMaxBox.Text = ExtractIniValue(text, "upload_max_filesize") ?? "2M";
            PostMaxBox.Text = ExtractIniValue(text, "post_max_size") ?? "8M";
            MemoryLimitBox.Text = ExtractIniValue(text, "memory_limit") ?? "128M";
            MaxExecTimeBox.Text = ExtractIniValue(text, "max_execution_time") ?? "30";
            
            var displayErrors = ExtractIniValue(text, "display_errors");
            if (displayErrors?.Equals("On", StringComparison.OrdinalIgnoreCase) == true)
                DisplayErrorsBox.SelectedIndex = 0;
            else
                DisplayErrorsBox.SelectedIndex = 1;
        }
        
        SaveStatus.Text = "";
        _loading = false;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception)
        {
            SaveStatus.Text = "Error loading ini";
            _loading = false;
        }
    }

    private string? ExtractIniValue(string text, string key)
    {
        var match = Regex.Match(text, $@"^[ \t]*{key}[ \t]*=[ \t]*(.+?)[ \t]*(;|$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim('"', '\'') : null;
    }

    private string ReplaceIniValue(string text, string key, string value)
    {
        var pattern = $@"^[ \t]*{key}[ \t]*=.*$";
        if (Regex.IsMatch(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            return Regex.Replace(text, pattern, $"{key} = {value}", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }
        // If not found, append it
        return text.TrimEnd() + $"\n{key} = {value}\n";
    }

    private void QuickSetting_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        SyncQuickToEditor();
    }

    private void QuickSettingCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SyncQuickToEditor();
    }

    private void SyncQuickToEditor()
    {
        var text = IniEditor.Text;
        if (!string.IsNullOrWhiteSpace(UploadMaxBox.Text)) text = ReplaceIniValue(text, "upload_max_filesize", UploadMaxBox.Text);
        if (!string.IsNullOrWhiteSpace(PostMaxBox.Text)) text = ReplaceIniValue(text, "post_max_size", PostMaxBox.Text);
        if (!string.IsNullOrWhiteSpace(MemoryLimitBox.Text)) text = ReplaceIniValue(text, "memory_limit", MemoryLimitBox.Text);
        if (!string.IsNullOrWhiteSpace(MaxExecTimeBox.Text)) text = ReplaceIniValue(text, "max_execution_time", MaxExecTimeBox.Text);
        
        if (DisplayErrorsBox.SelectedItem is ComboBoxItem item)
        {
            text = ReplaceIniValue(text, "display_errors", item.Content.ToString()!);
        }
        
        var oldPos = IniEditor.SelectionStart;
        IniEditor.Text = text;
        IniEditor.SelectionStart = oldPos;
    }

    private void PresetDev_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        UploadMaxBox.Text = "256M";
        PostMaxBox.Text = "256M";
        MemoryLimitBox.Text = "512M";
        MaxExecTimeBox.Text = "300";
        DisplayErrorsBox.SelectedIndex = 0; // On
        _loading = false;
        SyncQuickToEditor();
    }

    private void PresetProd_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        UploadMaxBox.Text = "64M";
        PostMaxBox.Text = "64M";
        MemoryLimitBox.Text = "256M";
        MaxExecTimeBox.Text = "60";
        DisplayErrorsBox.SelectedIndex = 1; // Off
        _loading = false;
        SyncQuickToEditor();
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentVersion)) return;
        var text = IniEditor.Text;
        var iniPath = Php.IniPath(_currentVersion);
        
        try
        {
            await File.WriteAllTextAsync(iniPath, text);
            var reloaded = Php.IniReload(_currentVersion);
            SaveStatus.Text = reloaded ? "Saved and restarted" : "Saved (not running)";
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            SaveStatus.Text = "Error saving";
            EngineHost.Instance.Append($"  [FAIL] Failed to save php.ini: {ex.Message}");
        }
        
        try
        {
            await System.Threading.Tasks.Task.Delay(3000);
            SaveStatus.Text = "";
        }
        catch { }
    }
}

}
