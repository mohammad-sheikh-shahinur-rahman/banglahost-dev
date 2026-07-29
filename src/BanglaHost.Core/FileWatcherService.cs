using System;
using System.IO;

namespace BanglaHost.Core;

public class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _filePath;
    private long _lastPos = 0;
    
    public event Action<string>? OnNewLine;

    public FileWatcherService(string filePath)
    {
        _filePath = filePath;
        if (File.Exists(filePath))
        {
            _lastPos = new FileInfo(filePath).Length;
        }

        var dir = Path.GetDirectoryName(filePath);
        var file = Path.GetFileName(filePath);
        
        if (dir != null && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += Watcher_Changed;
        }
        else
        {
            _watcher = new FileSystemWatcher();
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType != WatcherChangeTypes.Changed) return;
        
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < _lastPos)
            {
                // File truncated
                _lastPos = 0;
            }
            
            fs.Seek(_lastPos, SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            var content = sr.ReadToEnd();
            _lastPos = fs.Position;
            
            if (!string.IsNullOrEmpty(content))
            {
                OnNewLine?.Invoke(content);
            }
        }
        catch { /* file locked temporarily */ }
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.Changed -= Watcher_Changed;
            _watcher.Dispose();
        }
    }
}
