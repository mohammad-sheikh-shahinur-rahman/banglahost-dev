using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public sealed partial class ExplorerPage : Page
{
    private string _engine = "";       // "mariadb" / "mysql" / "postgresql" / "sqlite"
    private List<string> _allTables = new();
    private string _selectedDb = "";
    private string _selectedTable = "";

    public ExplorerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadEngines();

    // ── engines ───────────────────────────────────────────────────────────────
    private async Task LoadEngines()
    {
        Busy.IsActive = true;
        var engines = await Task.Run(() =>
        {
            var list = new List<(string label, string key)>();
            if (DbServer.Running())
            {
                var active = DbServer.ActiveEngine() ?? "mysql";
                list.Add((active == "mariadb" ? "MariaDB" : "MySQL", active));
            }
            if (PgServer.Running()) list.Add(("PostgreSQL", "postgresql"));
            return list;
        });

        var prevKey = (EngineBox.SelectedItem as ComboBoxItem)?.Tag as string;
        EngineBox.Items.Clear();
        foreach (var (label, key) in engines)
            EngineBox.Items.Add(new ComboBoxItem { Content = label, Tag = key });

        if (engines.Count == 0)
        {
            StatusText.Text = "no database server running — start MariaDB/MySQL or PostgreSQL on the Databases tab";
            DbBox.Items.Clear();
            TableList.ItemsSource = null;
            PmaBtn.IsEnabled = false;
        }
        else
        {
            var idx = engines.FindIndex(e => e.key == prevKey);
            EngineBox.SelectedIndex = idx >= 0 ? idx : 0;
            PmaBtn.IsEnabled = true;
            StatusText.Text = "";
        }
        Busy.IsActive = false;
    }

    private async void Engine_Changed(object s, SelectionChangedEventArgs e)
    {
        try
        {
        if ((EngineBox.SelectedItem as ComboBoxItem)?.Tag is not string key) { _engine = ""; return; }
        _engine = key;
        await LoadDatabases();
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    // ── databases ─────────────────────────────────────────────────────────────
    private async Task LoadDatabases()
    {
        if (_engine.Length == 0) return;
        Busy.IsActive = true;
        var prev = _selectedDb;
        var dbs = await DbExplorer.GetDatabases(_engine);
        DbBox.Items.Clear();
        foreach (var d in dbs) DbBox.Items.Add(d);
        if (dbs.Count > 0)
        {
            var idx = dbs.IndexOf(prev);
            DbBox.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            TableList.ItemsSource = null;
            ShowEmpty("No user databases found. Create one on the Databases tab.");
        }
        Busy.IsActive = false;
    }

    private async void Db_Changed(object s, SelectionChangedEventArgs e)
    {
        try
        {
        if (DbBox.SelectedItem is not string db) { _selectedDb = ""; return; }
        _selectedDb = db;
        ContextLabel.Text = $"— on {db} ({EngineLabel()})";
        await LoadTables();
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    // ── tables ────────────────────────────────────────────────────────────────
    private async Task LoadTables()
    {
        if (_engine.Length == 0 || _selectedDb.Length == 0) return;
        Busy.IsActive = true;
        _allTables = await DbExplorer.GetTables(_engine, _selectedDb);
        RenderTables();
        if (_allTables.Count == 0)
            ShowEmpty($"'{_selectedDb}' has no tables yet.");
        else
            ShowEmpty($"Pick a table on the left, or type SQL above and hit Run.");
        Busy.IsActive = false;
    }

    private void RenderTables()
    {
        var q = (TableSearch.Text ?? "").Trim();
        var filtered = q.Length == 0
            ? _allTables
            : _allTables.Where(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        TableList.ItemsSource = filtered;
    }

    private void TableSearch_Changed(object s, TextChangedEventArgs e) => RenderTables();

    private async void Table_Changed(object s, SelectionChangedEventArgs e)
    {
        try
        {
        if (TableList.SelectedItem is not string t) return;
        _selectedTable = t;
        var quoted = _engine == "postgresql" ? $"\"{t}\"" : $"`{t}`";
        var query = $"SELECT * FROM {quoted} LIMIT 200;";
        QueryBox.Text = query;
        await RunQuery(query);
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    // ── query runner ──────────────────────────────────────────────────────────
    private async void Run_Click(object s, RoutedEventArgs e)
    {
        try
        {
        var q = (QueryBox.Text ?? "").Trim();
        if (q.Length == 0) return;
        await RunQuery(q);
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void ShowTables_Click(object s, RoutedEventArgs e)
    {
        try
        {
        string q = _engine switch
        {
            "postgresql" => "SELECT tablename FROM pg_tables WHERE schemaname = 'public';",
            _            => "SHOW TABLES;",
        };
        QueryBox.Text = q;
        await RunQuery(q);
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async Task RunQuery(string query)
    {
        if (_engine.Length == 0) { ShowError("Pick an engine first."); return; }
        Busy.IsActive = true;
        ResultInfo.Text = "";
        var r = await (_engine switch
        {
            "postgresql" => DbExplorer.QueryPostgresAsync(query, _selectedDb.Length > 0 ? _selectedDb : "postgres"),
            _            => DbExplorer.QueryMysqlAsync(query, _selectedDb),
        });
        Busy.IsActive = false;

        if (!string.IsNullOrEmpty(r.Error) && r.Rows.Count == 0 && r.Columns.Length == 0)
        {
            ShowError(r.Error);
            return;
        }
        RenderResults(r);
        ResultInfo.Text = $"{r.Rows.Count} row(s)"
                        + (string.IsNullOrEmpty(r.Error) ? "" : $" * {r.Error.Split('\n')[0]}");
    }

    // ── result rendering ──────────────────────────────────────────────────────
    private void RenderResults(DbResult r)
    {
        EmptyMsg.Visibility = Visibility.Collapsed;
        ErrorMsg.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Visible;

        var grid = ResultGrid;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        if (r.Columns.Length == 0)
        {
            ShowEmpty("Query ran, but returned no columns.");
            return;
        }

        for (int c = 0; c < r.Columns.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int r_ = 0; r_ < r.Rows.Count; r_++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerBg = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        var stroke   = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        // header row
        for (int c = 0; c < r.Columns.Length; c++)
        {
            var cell = new Border
            {
                Background = headerBg,
                BorderBrush = stroke,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(10, 8, 14, 8),
                Child = new TextBlock
                {
                    Text = r.Columns[c],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.NoWrap,
                },
            };
            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }

        // data rows
        for (int i = 0; i < r.Rows.Count; i++)
        {
            var row = r.Rows[i];
            for (int c = 0; c < r.Columns.Length; c++)
            {
                var text = c < row.Length ? row[c] : "";
                var cell = new Border
                {
                    BorderBrush = stroke,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(10, 6, 14, 6),
                    Child = new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.NoWrap,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Cascadia Mono, Segoe UI Mono"),
                        Opacity = string.IsNullOrEmpty(text) ? 0.4 : 1.0,
                    },
                };
                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
    }

    private void ShowEmpty(string msg)
    {
        EmptyMsg.Text = msg;
        EmptyMsg.Visibility = Visibility.Visible;
        ErrorMsg.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string msg)
    {
        ErrorMsg.Text = msg;
        ErrorMsg.Visibility = Visibility.Visible;
        EmptyMsg.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Collapsed;
    }

    private string EngineLabel() => _engine switch
    {
        "mariadb" => "MariaDB",
        "mysql" => "MySQL",
        "postgresql" => "PostgreSQL",
        _ => _engine,
    };

    // ── toolbar buttons ───────────────────────────────────────────────────────
    private async void Refresh_Click(object s, RoutedEventArgs e)
    {
        try { await LoadEngines(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void OpenPma_Click(object s, RoutedEventArgs e)
    {
        var tld = Config.Load().Tld;
        var host = _engine == "postgresql" ? $"http://adminer.{tld}" : $"http://phpmyadmin.{tld}";
        try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = host, UseShellExecute = true }); } catch { }
    }
}

}
