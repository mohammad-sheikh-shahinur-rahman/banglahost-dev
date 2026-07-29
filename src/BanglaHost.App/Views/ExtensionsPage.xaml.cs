using System;
using System.Collections.Generic;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class ExtensionsPage : Page
{
    private bool _loading;
    private List<ExtRow> _allExts = new();

    public ExtensionsPage()
    {
        InitializeComponent();
        foreach (var v in BanglaHost.Core.Services.PhpVersions) PhpBox.Items.Add(v);
        PhpBox.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Refresh();

    private string SelectedVersion => PhpBox.SelectedItem?.ToString() ?? "8.4";

    private void Refresh()
    {
        _loading = true;
        try
        {
            var ext = EngineHost.Instance.Engine.PhpExtensions(SelectedVersion);
            _allExts = ext.Select(e => new ExtRow(e)).ToList();
            ApplyFilter();
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ExtList.ItemsSource = null;
        }
        finally { _loading = false; }
    }
    
    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim().ToLowerInvariant();
        var filtered = _allExts.Where(e => string.IsNullOrEmpty(query) || e.DisplayName.ToLowerInvariant().Contains(query)).ToList();
        
        ExtList.ItemsSource = filtered;
        StatusText.Text = $"{filtered.Count} extensions";
    }

    private void Php_Changed(object s, SelectionChangedEventArgs e) => Refresh();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void Ext_Toggled(object s, RoutedEventArgs e)
    {
        if (_loading || s is not ToggleSwitch ts || ts.DataContext is not ExtRow row) return;
        
        RestartRing.IsActive = true;
        RestartRing.Visibility = Visibility.Visible;
        StatusText.Text = "Applying and restarting php-cgi...";
        ts.IsEnabled = false;

        await EngineHost.Instance.RunCaptured(() => 
        {
            EngineHost.Instance.Engine.PhpExtToggle(SelectedVersion, row.Name, ts.IsOn);
        });
        
        Refresh();
        
        ts.IsEnabled = true;
        RestartRing.IsActive = false;
        RestartRing.Visibility = Visibility.Collapsed;
    }

    public class ExtRow
    {
        private readonly Php.ExtensionInfo _info;
        
        public ExtRow(Php.ExtensionInfo info)
        {
            _info = info;
            Enabled = info.Enabled;
        }
        
        public string Name => _info.Name;
        public string DisplayName => _info.Name;
        public bool Enabled { get; set; }
        public bool Available => _info.Available;
        public Visibility IsZendVisibility => _info.IsZend ? Visibility.Visible : Visibility.Collapsed;
        
        public string AvailableText
        {
            get
            {
                if (_info.Name == "ioncube") return "Zend extension — required for WHMCS";
                if (!_info.Available) return "Not bundled — enabling triggers auto-fetch from PECL";
                if (_info.MissingDeps.Length > 0) return $"Needs also: {string.Join(", ", _info.MissingDeps)}";
                return _info.IsZend ? "Zend extension — loaded before opcache" : "Installed";
            }
        }
    }
}

}
