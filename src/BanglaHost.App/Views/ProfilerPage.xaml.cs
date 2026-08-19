using System;
using System.IO;
using System.Linq;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class ProfilerPage : Page
{
    public ProfilerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        RefreshList();
    }

    private void RefreshList()
    {
        ProfileBox.Items.Clear();
        var tmp = Paths.Tmp;
        if (!Directory.Exists(tmp)) return;
        
        var files = Directory.GetFiles(tmp, "cachegrind.out.*")
                             .Select(f => new FileInfo(f))
                             .OrderByDescending(f => f.LastWriteTime)
                             .ToList();
                             
        foreach (var f in files)
        {
            ProfileBox.Items.Add(f.Name);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshList();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var tmp = Paths.Tmp;
        if (!Directory.Exists(tmp)) return;
        var files = Directory.GetFiles(tmp, "cachegrind.out.*");
        foreach (var f in files) try { File.Delete(f); } catch { }
        RefreshList();
        FuncList.ItemsSource = null;
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is string filename)
        {
            var path = Path.Combine(Paths.Tmp, filename);
            if (File.Exists(path))
            {
                try
                {
                    var data = ProfilerParser.ParseCachegrind(path);
                    FuncList.ItemsSource = data.Take(100).ToList();
                }
                catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "Profiler"); }
            }
        }
    }
}
