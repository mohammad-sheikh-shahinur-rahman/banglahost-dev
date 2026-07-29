using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

public class FileNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public ObservableCollection<FileNode> Children { get; set; } = new();
    
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE7C3"; // Folder or Document
    public Brush IconBrush => new SolidColorBrush(IsDirectory ? Colors.Goldenrod : Colors.LightSteelBlue);
}

public sealed partial class FilesPage : Page
{
    private string _currentFilePath = "";
    private bool _isDirty = false;

    public FilesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadRoot();
    }

    private void LoadRoot()
    {
        var root = Config.Load().SitesRoot;
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);

        FileTree.RootNodes.Clear();
        var rootNode = new TreeViewNode { Content = new FileNode { Name = "Sites", FullPath = root, IsDirectory = true }, HasUnrealizedChildren = true, IsExpanded = true };
        FileTree.RootNodes.Add(rootNode);
        PopulateNode(rootNode);
    }

    private void PopulateNode(TreeViewNode node)
    {
        if (node.Content is not FileNode fn || !fn.IsDirectory) return;
        
        node.Children.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(fn.FullPath).OrderBy(d => d))
            {
                node.Children.Add(new TreeViewNode { Content = new FileNode { Name = Path.GetFileName(dir), FullPath = dir, IsDirectory = true }, HasUnrealizedChildren = true });
            }
            foreach (var file in Directory.GetFiles(fn.FullPath).OrderBy(f => f))
            {
                node.Children.Add(new TreeViewNode { Content = new FileNode { Name = Path.GetFileName(file), FullPath = file, IsDirectory = false } });
            }
            node.HasUnrealizedChildren = false;
        }
        catch { /* Access denied etc */ }
    }

    private void FileTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.HasUnrealizedChildren)
        {
            PopulateNode(args.Node);
        }
    }

    private void FileTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && node.Content is FileNode fn)
        {
            if (fn.IsDirectory) return;
            OpenFile(fn.FullPath);
        }
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        
        _currentFilePath = path;
        CurrentFilePath.Text = path;
        PlaceholderText.Visibility = Visibility.Collapsed;
        
        var ext = Path.GetExtension(path).ToLower();
        var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };
        
        if (imageExts.Contains(ext))
        {
            TextEditor.Visibility = Visibility.Collapsed;
            ImageViewer.Visibility = Visibility.Visible;
            ImageViewer.Source = new BitmapImage(new Uri(path));
            SaveBtn.IsEnabled = false;
        }
        else
        {
            ImageViewer.Visibility = Visibility.Collapsed;
            TextEditor.Visibility = Visibility.Visible;
            try
            {
                var text = File.ReadAllText(path);
                TextEditor.Text = text;
                _isDirty = false;
                SaveBtn.IsEnabled = false;
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                TextEditor.Text = $"Error reading file: {ex.Message}";
                SaveBtn.IsEnabled = false;
            }
        }
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isDirty && !string.IsNullOrEmpty(_currentFilePath))
        {
            _isDirty = true;
            SaveBtn.IsEnabled = true;
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath) || !_isDirty) return;
        
        try
        {
            File.WriteAllText(_currentFilePath, TextEditor.Text);
            _isDirty = false;
            SaveBtn.IsEnabled = false;
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"  [FAIL] Save failed: {ex.Message}");
        }
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => LoadRoot();
    private void GoToRootBtn_Click(object sender, RoutedEventArgs e) => LoadRoot();

    private FileNode? GetSelectedNode()
    {
        if (FileTree.SelectedItem is TreeViewNode node) return node.Content as FileNode;
        return FileTree.SelectedItem as FileNode;
    }

    private string GetTargetDirectory()
    {
        var fn = GetSelectedNode();
        if (fn != null)
        {
            if (fn.IsDirectory) return fn.FullPath;
            return Path.GetDirectoryName(fn.FullPath) ?? Config.Load().SitesRoot;
        }
        return Config.Load().SitesRoot;
    }

    private async Task<string?> PromptForNameAsync(string title)
    {
        var tb = new TextBox { PlaceholderText = "Enter name", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = tb,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        
        if (this.Content == null || this.XamlRoot == null) return null;
        dialog.XamlRoot = this.XamlRoot;
        var result = await BanglaHost.App.Services.DialogQueue.ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? tb.Text.Trim() : null;
    }

    private async void NewFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var name = await PromptForNameAsync("New Folder");
        if (string.IsNullOrEmpty(name)) return;

        var targetDir = GetTargetDirectory();
        var newPath = Path.Combine(targetDir, name);
        try
        {
            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
                LoadRoot();
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"  [FAIL] Create folder failed: {ex.Message}\n");
        }
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void NewFileBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var name = await PromptForNameAsync("New File");
        if (string.IsNullOrEmpty(name)) return;

        var targetDir = GetTargetDirectory();
        var newPath = Path.Combine(targetDir, name);
        try
        {
            if (!File.Exists(newPath))
            {
                File.WriteAllText(newPath, "");
                LoadRoot();
                OpenFile(newPath);
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            EngineHost.Instance.Append($"  [FAIL] Create file failed: {ex.Message}\n");
        }
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void RenameBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var node = GetSelectedNode();
        if (node == null) return;

        var oldName = Path.GetFileName(node.FullPath);
        var tb = new TextBox { Text = oldName, Width = 300 };
        tb.SelectAll();

        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = tb,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        
        if (this.Content == null || this.XamlRoot == null) return;
        dialog.XamlRoot = this.XamlRoot;
        var result = await BanglaHost.App.Services.DialogQueue.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            var newName = tb.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;

            var parentDir = Path.GetDirectoryName(node.FullPath);
            if (parentDir == null) return;
            var newPath = Path.Combine(parentDir, newName);

            try
            {
                if (node.IsDirectory)
                    Directory.Move(node.FullPath, newPath);
                else
                    File.Move(node.FullPath, newPath);
                
                LoadRoot();
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                EngineHost.Instance.Append($"  [FAIL] Rename failed: {ex.Message}\n");
            }
        }
    }
        catch (OperationCanceledException) { }
        catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node == null) return;
        try
        {
            if (node.IsDirectory) Directory.Delete(node.FullPath, true);
            else File.Delete(node.FullPath);
            LoadRoot();
        }
        catch { }
    }

    private void ZipBtn_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node == null || !node.IsDirectory) return;
        var zipPath = node.FullPath + ".zip";
        try { ZipFile.CreateFromDirectory(node.FullPath, zipPath); LoadRoot(); } catch { }
    }

    private void UnzipBtn_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        if (node == null || node.IsDirectory || !node.FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        var extractPath = node.FullPath.Substring(0, node.FullPath.Length - 4);
        try { ZipFile.ExtractToDirectory(node.FullPath, extractPath); LoadRoot(); } catch { }
    }

    private void RevealBtn_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedNode();
        var path = node?.FullPath ?? Config.Load().SitesRoot;
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}

}
