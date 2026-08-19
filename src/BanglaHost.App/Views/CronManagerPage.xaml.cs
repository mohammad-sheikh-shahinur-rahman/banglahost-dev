using System.IO;
using System.Linq;
using System.ComponentModel;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public class CronSiteModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string PhpVer { get; set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsQueueOn
    {
        get => CronService.IsRunning(Name, "queue");
        set { }
    }

    public bool IsScheduleOn
    {
        get => CronService.IsRunning(Name, "schedule");
        set { }
    }

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQueueOn)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsScheduleOn)));
    }
}

public sealed partial class CronManagerPage : Page
{
    public CronManagerPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var cfg = Config.Load();
        try
        {
            var snap = await BanglaHost.App.Services.EngineHost.Instance.Snapshot();
            var sites = snap.Sites
                .Where(s => !BanglaHost.Core.Engine.IsTool(s.Name))
                .Where(s => s.Root != null && File.Exists(Path.Combine(s.Root.EndsWith("public") || s.Root.EndsWith("public/") || s.Root.EndsWith("public\\") ? Path.GetDirectoryName(s.Root)! : s.Root, "artisan")))
                .Select(s => new CronSiteModel { Name = s.Name, Target = s.Root, PhpVer = string.IsNullOrEmpty(s.Php) ? cfg.DefaultPhp : s.Php })
                .ToList();
                
            SiteList.ItemsSource = sites;
        }
        catch { }
    }

    private void Queue_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is string name)
        {
            if (SiteList.ItemsSource is System.Collections.Generic.List<CronSiteModel> list)
            {
                var site = list.FirstOrDefault(s => s.Name == name);
                if (site == null) return;
                
                var root = site.Target.EndsWith("public") || site.Target.EndsWith("public/") || site.Target.EndsWith("public\\") ? Path.GetDirectoryName(site.Target)! : site.Target;
                var phpExe = Tools.PhpExe(site.PhpVer);
                if (phpExe == null) return;

                if (ts.IsOn) CronService.StartWorker(name, root, phpExe, "queue");
                else CronService.StopWorker(name, "queue");
            }
        }
    }

    private void Schedule_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is string name)
        {
            if (SiteList.ItemsSource is System.Collections.Generic.List<CronSiteModel> list)
            {
                var site = list.FirstOrDefault(s => s.Name == name);
                if (site == null) return;
                
                var root = site.Target.EndsWith("public") || site.Target.EndsWith("public/") || site.Target.EndsWith("public\\") ? Path.GetDirectoryName(site.Target)! : site.Target;
                var phpExe = Tools.PhpExe(site.PhpVer);
                if (phpExe == null) return;

                if (ts.IsOn) CronService.StartWorker(name, root, phpExe, "schedule");
                else CronService.StopWorker(name, "schedule");
            }
        }
    }
}
