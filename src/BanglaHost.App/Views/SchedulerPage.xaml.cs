using System;
using System.IO;
using System.Text.Json;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views
{

public sealed partial class SchedulerPage : Page
{
    private static string ConfigFile => Path.Combine(Paths.Config, "scheduler.json");

    public SchedulerPage() { InitializeComponent(); Refresh(); }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var cron = CronBox.Text.Trim();
        var cmd  = CmdBox.Text.Trim();
        if (name.Length == 0 || cron.Length == 0 || cmd.Length == 0)
        {
            Result.Text = "Name, cron expression, and command are required.";
            return;
        }
        try
        {
            Directory.CreateDirectory(Paths.Config);
            var list = File.Exists(ConfigFile)
                ? JsonSerializer.Deserialize<System.Collections.Generic.List<Job>>(File.ReadAllText(ConfigFile)) ?? new()
                : new System.Collections.Generic.List<Job>();
            list.Add(new Job { Name = name, Cron = cron, Command = cmd });
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            NameBox.Text = CronBox.Text = ""; CmdBox.Text = "";
            Result.Text = $"Scheduled '{name}' at '{cron}'. (Note: sync to Windows Task Scheduler runs on the next BanglaHost service tick.)";
            Refresh();
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { Result.Text = "Save failed: " + ex.Message; }
    }

    private void Refresh()
    {
        try
        {
            if (!File.Exists(ConfigFile)) { JobsText.Text = "No jobs scheduled yet."; return; }
            var list = JsonSerializer.Deserialize<System.Collections.Generic.List<Job>>(File.ReadAllText(ConfigFile)) ?? new();
            JobsText.Text = list.Count == 0
                ? "No jobs scheduled yet."
                : string.Join("\n", list.ConvertAll(j => $"- {j.Name}  [{j.Cron}]  {j.Command}"));
        }
        catch { JobsText.Text = "No jobs scheduled yet."; }
    }

    private sealed class Job
    {
        public string Name { get; set; } = "";
        public string Cron { get; set; } = "";
        public string Command { get; set; } = "";
    }
}

}
