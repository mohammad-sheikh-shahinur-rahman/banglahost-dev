using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

public sealed partial class DatabaseExplorerPage : Page
{
    private string _currentDb = "";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseExplorerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
        => BackgroundWork.Handler(LoadDatabasesAsync, "SqlStudio.LoadDatabases");

    /// <summary>
    /// Run SQL through the command-line client.
    ///
    /// The previous implementation wrote the SQL to a temp file and passed
    /// <c>… -t &lt; "C:\…\query.sql"</c> as the argument string. With <c>UseShellExecute = false</c>
    /// there is no shell to interpret <c>&lt;</c>: the client received three literal arguments —
    /// <c>-t</c>, <c>&lt;</c> and the path — so it treated <c>&lt;</c> as a database name and exited
    /// with an error. SQL Studio could never execute a single query. (It also left every temp file
    /// behind on the error path, and put the root password on the command line.)
    ///
    /// The SQL now goes in over stdin, which is what the redirect was trying to express.
    /// </summary>
    private static async Task<string> RunSqlAsync(string sql, string db = "", CancellationToken ct = default)
    {
        var engine = DbServer.ActiveEngine();
        if (engine is null) return "Database server is not running. Start MySQL/MariaDB first.";

        var exe = Tools.MysqlClientFor(engine);
        if (exe is null) return "MySQL client not found.";

        if (!string.IsNullOrEmpty(db))
        {
            try { MySqlAuthFile.ValidIdentifier(db, "database name"); }
            catch (Exception ex) { return ex.Message; }
        }

        var bin = Path.GetDirectoryName(exe)!;
        var pdir = Path.GetFullPath(Path.Combine(bin, "..", "lib", "plugin"));

        using var auth = MySqlAuthFile.Create("root", Config.Load().RootPassword, DbServer.Port);
        var args = MySqlAuthFile.Apply(auth, "root", DbServer.Port);
        if (Directory.Exists(pdir)) args.Add($"--plugin-dir={pdir}");
        args.Add("-t");                                  // ASCII table output, which the parsers below expect
        if (!string.IsNullOrEmpty(db)) args.Add(db);

        try
        {
            var res = await ProcRunner.RunAsync(
                exe, args, workingDir: bin, timeoutMs: 120_000,
                stdin: sql.EndsWith(";") ? sql + "\n" : sql + ";\n", ct: ct).ConfigureAwait(false);

            if (res.TimedOut) return "Error:\nQuery timed out after 120 seconds and was cancelled.";
            if (res.ExitCode == 0) return res.StdOut;
            var err = string.Join('\n', res.StdErr.Split('\n').Where(l => !l.Contains("ssl-verify-server-cert")));
            return $"Error:\n{err.Trim()}";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task LoadDatabasesAsync()
    {
        var outText = await RunSqlAsync("SHOW DATABASES;");
        var dbs = ParseSingleColumn(outText, header: "Database");
        DbList.ItemsSource = dbs;
    }

    /// <summary>Strip the client's ASCII table borders and header row.</summary>
    private static List<string> ParseSingleColumn(string outText, string header)
    {
        var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var items = new List<string>();
        foreach (var l in lines)
        {
            if (l.StartsWith("+--") || l.Contains("+----")) continue;
            var v = l.Replace("|", "").Trim();
            if (v.Length == 0) continue;
            if (string.Equals(v, header, StringComparison.OrdinalIgnoreCase)) continue;
            if (v.StartsWith("Tables_in_", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(v);
        }
        return items;
    }

    private void DbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DbList.SelectedItem is not string db) return;
        _currentDb = db;
        BackgroundWork.Handler(async () =>
        {
            var outText = await RunSqlAsync("SHOW TABLES;", db);
            TableList.ItemsSource = ParseSingleColumn(outText, header: $"Tables_in_{db}");
            ResultBox.Text = "";
            QueryBox.Text = "";
        }, "SqlStudio.LoadTables");
    }

    private void TableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TableList.SelectedItem is string tbl)
        {
            // Backtick-quoting alone does not make a name safe; a backtick inside it closes the
            // quote. The name came from SHOW TABLES, but validate anyway — it costs nothing.
            try { MySqlAuthFile.ValidIdentifier(tbl, "table name"); }
            catch (Exception ex) { ResultBox.Text = ex.Message; return; }

            QueryBox.Text = $"SELECT * FROM `{tbl}` LIMIT 100;";
            Execute_Click(this, new RoutedEventArgs());
        }
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        var sql = QueryBox.Text.Trim();
        if (string.IsNullOrEmpty(sql)) return;

        BackgroundWork.Handler(async () =>
        {
            // One query at a time: the old code ran RunSql synchronously on the UI thread, so a slow
            // query froze the window; making it async means a user can now queue several by
            // double-clicking, and the last writer would win at random.
            if (!await _gate.WaitAsync(0)) { ResultBox.Text = "A query is already running…"; return; }
            try
            {
                ExecuteBtnSetEnabled(false);
                ResultBox.Text = "Running…";
                ResultBox.Text = await RunSqlAsync(sql, _currentDb);
            }
            finally
            {
                ExecuteBtnSetEnabled(true);
                _gate.Release();
            }
        }, "SqlStudio.Execute");
    }

    private void ExecuteBtnSetEnabled(bool on)
    {
        if (this.FindName("ExecuteBtn") is Button b) b.IsEnabled = on;
    }
}
