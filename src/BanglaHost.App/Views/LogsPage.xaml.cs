using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace BanglaHost.App.Views
{

/// <summary>
/// Logs page — pick a specific log file, OR pick "All errors" to see every ERROR/WARN
/// line from every log file merged and highlighted in one view.
/// </summary>
public sealed partial class LogsPage : Page
{
    private const string AllErrors = "★ All errors (merged)";
    private static readonly string[] ErrorTokens =
    {
        "[error]", "error:", " error ", "ERROR", "PHP Fatal",
        "PHP Parse", "PHP Warning", "PHP Notice", "Exception", "Traceback",
        "[warn]",  "WARNING", "FATAL", "Segmentation fault",
    };

    private FileWatcherService? _watcher;

    public LogsPage()
    {
        InitializeComponent();
        this.Unloaded += (s, e) => { _watcher?.Dispose(); _watcher = null; };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Reload();

    private void Reload()
    {
        var current = FilePicker.SelectedItem as string;
        var items = new List<string> { AllErrors };
        items.AddRange(EngineHost.Instance.Engine.LogFiles());
        FilePicker.ItemsSource = items;
        if (current is not null && items.Contains(current)) FilePicker.SelectedItem = current;
        else FilePicker.SelectedIndex = 0;
    }

    private void File_Changed(object sender, SelectionChangedEventArgs e) => Load();
    private void Refresh_Click(object sender, RoutedEventArgs e) { Reload(); Load(); }
    private void Filter_Toggled(object sender, RoutedEventArgs e) => Load();
    private void Filter_TextChanged(object sender, TextChangedEventArgs e) => Load();

    private async void Load()
    {
        if (FilePicker.SelectedItem is not string name) return;
        
        _watcher?.Dispose();
        _watcher = null;

        LogText.Inlines.Clear();
        var errorsOnly = ErrorsOnly?.IsChecked == true;
        var q = FilterBox?.Text ?? "";

        // Collect raw (text, level) on a background thread — NEVER create WinUI objects
        // (SolidColorBrush, Run…) off the UI thread; that triggers a COMException crash.
        List<(string text, LogLevel level)> lines = new();
        try
        {
            lines = await Task.Run(() => name == AllErrors
                ? CollectAllErrors(q)
                : FilterLines(EngineHost.Instance.Engine.LogText(name, 1000), errorsOnly, q));
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            // Never leave the panel blank on failure — surface *why* so the user isn't staring
            // at an empty black box (the old behaviour when async void swallowed exceptions).
            LogText.Inlines.Add(new Run { Text = "Failed to read logs:\n" + ex.Message,
                                          Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed) });
            return;
        }

        if (lines.Count == 0)
        {
            var msg = errorsOnly
                ? "No errors or warnings in the log files right now. Uncheck \"Errors only\" to see everything."
                : "(no matching lines — try clearing the filter or picking another file)";
            LogText.Inlines.Add(new Run { Text = msg, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray) });
            return;
        }

        // Create brushes ONCE on the UI thread and reuse them for every Run.
        var neutralBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
        var errorBrush   = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
        var warnBrush    = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07));

        // Adding thousands of Runs one at a time to a live TextBlock is O(n²) — each Add
        // invalidates the layout pass. Build every Run first, then splice them in.
        var runs = new Run[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var brush = lines[i].level switch
            {
                LogLevel.Error   => errorBrush,
                LogLevel.Warning => warnBrush,
                _                => neutralBrush,
            };
            runs[i] = new Run { Text = lines[i].text + "\n", Foreground = brush };
        }
        foreach (var r in runs) LogText.Inlines.Add(r);
        Scroll.ChangeView(null, Scroll.ScrollableHeight, null);

        // Start Live Streaming if watching a specific file
        if (name != AllErrors)
        {
            var logPath = Path.Combine(Paths.Logs, name);
            if (File.Exists(logPath))
            {
                _watcher = new FileWatcherService(logPath);
                _watcher.OnNewLine += (newText) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // Safely check UI state before updating
                        if (LogText == null || FilePicker.SelectedItem as string != name) return;

                        var newLines = FilterLines(newText, ErrorsOnly?.IsChecked == true, FilterBox?.Text ?? "");
                        if (newLines.Count == 0) return;

                        foreach (var (text, level) in newLines)
                        {
                            var brush = level switch
                            {
                                LogLevel.Error   => errorBrush,
                                LogLevel.Warning => warnBrush,
                                _                => neutralBrush,
                            };
                            LogText.Inlines.Add(new Run { Text = text + "\n", Foreground = brush });
                        }
                        
                        // Keep text block from growing infinitely in live view
                        if (LogText.Inlines.Count > 2000)
                        {
                            while (LogText.Inlines.Count > 1500) LogText.Inlines.RemoveAt(0);
                        }

                        Scroll.ChangeView(null, Scroll.ScrollableHeight, null);
                    });
                };
            }
        }
    }

    /// <summary>Filter lines from a single log file. Runs on a background thread — returns
    /// plain data only, no WinUI objects.</summary>
    private static List<(string text, LogLevel level)> FilterLines(string raw, bool errorsOnly, string q)
    {
        var result = new List<(string, LogLevel)>();
        foreach (var line in raw.Split('\n'))
        {
            if (line.Length == 0) continue;
            if (q.Length > 0 && !line.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var kind = Classify(line);
            if (errorsOnly && kind == LogLevel.Normal) continue;
            result.Add((line.TrimEnd('\r'), kind));
        }
        return result;
    }

    /// <summary>Merge error/warn lines from every log file. Runs on a background thread —
    /// returns plain data only, no WinUI objects.</summary>
    private static List<(string text, LogLevel level)> CollectAllErrors(string q)
    {
        // A busy dev box can have tens of thousands of error/warn lines across all logs;
        // adding one Run per line was blowing up the TextBlock layout pass and left the
        // panel empty on-screen. Cap at the most-recent 1500 hits so rendering stays fast.
        const int MaxLines = 1500;
        const int PerFile  = 1000;
        var result = new List<(string, LogLevel)>();
        var engine = EngineHost.Instance.Engine;
        foreach (var f in engine.LogFiles())
        {
            string raw;
            try { raw = engine.LogText(f, PerFile); } catch { continue; }
            foreach (var line in raw.Split('\n'))
            {
                if (line.Length == 0) continue;
                var kind = Classify(line);
                if (kind == LogLevel.Normal) continue;
                if (q.Length > 0 && !line.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  && !f.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(($"[{f}] {line.TrimEnd('\r')}", kind));
            }
        }
        // Newest lines are most useful — trim the head, not the tail.
        if (result.Count > MaxLines) result.RemoveRange(0, result.Count - MaxLines);
        return result;
    }

    private enum LogLevel { Normal, Warning, Error }

    private static LogLevel Classify(string line)
    {
        foreach (var t in ErrorTokens)
        {
            if (line.Contains(t, StringComparison.OrdinalIgnoreCase))
            {
                if (t.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Notice", StringComparison.OrdinalIgnoreCase))
                    return LogLevel.Warning;
                return LogLevel.Error;
            }
        }
        return LogLevel.Normal;
    }


}

}
