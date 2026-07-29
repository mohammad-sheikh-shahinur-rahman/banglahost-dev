using System;
using System.Linq;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace BanglaHost.App.Views
{

public sealed partial class DeployPage : Page
{
    private DeployProfile? _currentProfile;

    public DeployPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        var profiles = DeployService.GetProfiles();
        ProfileList.ItemsSource = profiles;
        if (profiles.Length > 0 && _currentProfile == null)
            ProfileList.SelectedIndex = 0;
        else if (_currentProfile != null)
            ProfileList.SelectedItem = profiles.FirstOrDefault(p => p.Id == _currentProfile.Id);
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is DeployProfile p)
        {
            _currentProfile = p;
            NameBox.Text = p.Name;
            SourceBox.Text = p.SourceDir;
            HostBox.Text = p.Host;
            PortBox.Text = p.Port.ToString();
            UserBox.Text = p.Username;
            PassBox.Password = p.Password;
            RemotePathBox.Text = p.RemotePath;
            ExtraArgsBox.Text = p.ExtraArgs;

            foreach (ComboBoxItem item in MethodBox.Items)
            {
                if (item.Tag.ToString() == p.Method.ToString())
                {
                    MethodBox.SelectedItem = item;
                    break;
                }
            }

            EditorPanel.Visibility = Visibility.Visible;
            LogViewer.Text = "";
        }
        else
        {
            EditorPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentProfile = new DeployProfile { Name = "New Profile", SourceDir = Config.Load().SitesRoot };
        LoadProfiles();
        ProfileList.SelectedItem = null; // force clear
        ProfileList.SelectedItem = _currentProfile; // might not trigger if not in list, so manually update UI
        
        NameBox.Text = _currentProfile.Name;
        SourceBox.Text = _currentProfile.SourceDir;
        MethodBox.SelectedIndex = 0;
        EditorPanel.Visibility = Visibility.Visible;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = _currentProfile;
        if (p == null) return;
        
        p.Name = NameBox.Text;
        p.Method = (DeployMethod)Enum.Parse(typeof(DeployMethod), (string)((ComboBoxItem)MethodBox.SelectedItem).Tag);
        p.SourceDir = SourceBox.Text;
        p.Host = HostBox.Text;
        p.Port = int.TryParse(PortBox.Text, out var port) ? port : 22;
        p.Username = UserBox.Text;
        p.Password = PassBox.Password;
        p.RemotePath = RemotePathBox.Text;
        p.ExtraArgs = ExtraArgsBox.Text;
        
        DeployService.SaveProfile(p);
        LoadProfiles();
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfile == null) return;
        DeployService.DeleteProfile(_currentProfile.Id);
        _currentProfile = null;
        LoadProfiles();
    }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            SourceBox.Text = folder.Path;
        }
    }

    private async void DeployBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveBtn_Click(sender, e); // ensure saved
        if (_currentProfile == null) return;

        DeployBtn.IsEnabled = false;
        LogViewer.Text = "";

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => 
        {
            LogViewer.Text += msg + "\n";
            LogViewer.SelectionStart = LogViewer.Text.Length;
            LogViewer.SelectionLength = 0;
        });

        await Task.Run(() => DeployService.DeployAsync(_currentProfile, log));

        DispatcherQueue?.TryEnqueue(() => DeployBtn.IsEnabled = true);
    }
}

}
