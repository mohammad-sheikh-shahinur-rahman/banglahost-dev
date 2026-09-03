using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class DatabaseExplorerPage : Page
{
    private string _currentDb = "";

    public DatabaseExplorerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadDatabases();
    }

    private string RunSql(string sql, string db = "")
    {
        var exe = Tools.MysqlClientExe();
        if (exe == null) return "MySQL client not found.";
        
        var cfg = Config.Load();
        var auth = string.IsNullOrEmpty(cfg.RootPassword) ? "-u root" : $"-u root -p\"{cfg.RootPassword}\"";
        
        var tmpSql = Path.Combine(Paths.Tmp, $"query_{Guid.NewGuid():N}.sql");
        File.WriteAllText(tmpSql, sql);
        
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"{auth} -t {(string.IsNullOrEmpty(db) ? "" : db)} < \"{tmpSql}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        try
        {
            using var p = Process.Start(psi)!;
            var _errT = p.StandardError.ReadToEndAsync(); var outText = p.StandardOutput.ReadToEnd(); var errText = _errT.Result;
            p.WaitForExit();
            File.Delete(tmpSql);
            return p.ExitCode == 0 ? outText : $"Error:\n{errText}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private void LoadDatabases()
    {
        var outText = RunSql("SHOW DATABASES;");
        var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        var dbs = new List<string>();
        foreach (var l in lines)
        {
            if (l.Contains("+----") || l.Contains("| Database |")) continue;
            var db = l.Replace("|", "").Trim();
            if (!string.IsNullOrEmpty(db)) dbs.Add(db);
        }
        DbList.ItemsSource = dbs;
    }

    private void DbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DbList.SelectedItem is string db)
        {
            _currentDb = db;
            var outText = RunSql("SHOW TABLES;", db);
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            var tables = new List<string>();
            foreach (var l in lines)
            {
                if (l.Contains("+----") || l.Contains($"| Tables_in_{db}")) continue;
                var tbl = l.Replace("|", "").Trim();
                if (!string.IsNullOrEmpty(tbl)) tables.Add(tbl);
            }
            TableList.ItemsSource = tables;
            ResultBox.Text = "";
            QueryBox.Text = "";
        }
    }

    private void TableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TableList.SelectedItem is string tbl)
        {
            QueryBox.Text = $"SELECT * FROM `{tbl}` LIMIT 100;";
            Execute_Click(null, null!);
        }
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        var sql = QueryBox.Text.Trim();
        if (string.IsNullOrEmpty(sql)) return;
        ResultBox.Text = RunSql(sql, _currentDb);
    }
}
