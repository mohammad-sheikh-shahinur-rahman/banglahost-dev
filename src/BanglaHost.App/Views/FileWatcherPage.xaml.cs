using System;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace BanglaHost.App.Views;

public sealed partial class FileWatcherPage : Page
{
    private FileWatcherService? _watcher;

    public FileWatcherPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_watcher != null)
        {
            _watcher.OnNewLine -= OnWatcherNewLine;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnWatcherNewLine(string text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LogViewer.Text += text;
            LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
        });
    }

    private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            FileBox.Text = file.Path;
            StartWatching(file.Path);
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void StartWatching(string path)
    {
        if (_watcher != null)
        {
            _watcher.OnNewLine -= OnWatcherNewLine;
            _watcher.Dispose();
        }
        LogViewer.Text = "";
        
        try
        {
            if (System.IO.File.Exists(path))
            {
                // Read last 10 lines
                var lines = System.IO.File.ReadAllLines(path);
                var start = Math.Max(0, lines.Length - 10);
                for (int i = start; i < lines.Length; i++)
                {
                    LogViewer.Text += lines[i] + "\n";
                }
            }

            _watcher = new FileWatcherService(path);
            _watcher.OnNewLine += OnWatcherNewLine;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            LogViewer.Text = $"Error opening file: {ex.Message}";
        }
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        LogViewer.Text = "";
    }
}
