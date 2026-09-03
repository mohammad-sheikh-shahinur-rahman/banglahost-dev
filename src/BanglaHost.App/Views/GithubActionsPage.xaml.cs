using System;
using System.Threading.Tasks;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace BanglaHost.App.Views;

public sealed partial class GithubActionsPage : Page
{
    public GithubActionsPage()
    {
        InitializeComponent();
    }

    private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var folderPath = await BanglaHost.App.Services.Picker.FolderAsync();
            if (folderPath != null)
            {
                ProjectDirBox.Text = folderPath;
            }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private async void ScaffoldBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        var path = ProjectDirBox.Text.Trim();
        var typeItem = TypeBox.SelectedItem as ComboBoxItem;
        var type = typeItem?.Content?.ToString()?.ToLower() ?? "laravel";

        if (string.IsNullOrEmpty(path))
        {
            StatusLabel.Text = "Please select a project directory.";
            return;
        }

        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => LogViewer.Text += msg + "\n");
        log($"Scaffolding {type} workflow in {path}...");

        if (sender is Button btn) btn.IsEnabled = false;
        
        var success = await Task.Run(() => GithubActionsService.ScaffoldWorkflowAsync(path, type, log));
        
        if (sender is Button b) b.IsEnabled = true;
        StatusLabel.Text = success ? "Scaffold completed successfully." : "Scaffold failed or already exists.";
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private System.Threading.CancellationTokenSource? _pollingCts;
    private string _verificationUri = "";
    private string _accessToken = "";
    private const string ClientId = "Ov23lij3poqfbjeTvjSG";

    private static readonly System.Net.Http.HttpClient _http = CreateGitHubClient();

    private static System.Net.Http.HttpClient CreateGitHubClient()
    {
        var handler = new System.Net.Http.SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BanglaHost-App");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        Action<string> log = msg => DispatcherQueue?.TryEnqueue(() => LogViewer.Text += msg + "\n");
        log("Initiating GitHub Device Login...");

        try
        {
            var content = new System.Net.Http.StringContent($"{{\"client_id\":\"{ClientId}\"}}", System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("https://github.com/login/device/code", content);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var deviceCode = doc.RootElement.GetProperty("device_code").GetString();
                var userCode = doc.RootElement.GetProperty("user_code").GetString();
                _verificationUri = doc.RootElement.GetProperty("verification_uri").GetString() ?? "";
                var interval = doc.RootElement.GetProperty("interval").GetInt32();

                DispatcherQueue?.TryEnqueue(() =>
                {
                    DeviceCodeLabel.Text = userCode;
                    LoginPanel.Visibility = Visibility.Collapsed;
                    DeviceFlowPanel.Visibility = Visibility.Visible;
                });

                _pollingCts?.Cancel();
                _pollingCts?.Dispose();
                _pollingCts = new System.Threading.CancellationTokenSource();
                if (deviceCode is null) { log("Device code missing"); return; }
                _ = PollForTokenAsync(deviceCode, interval, _pollingCts.Token, log);
            }
            else
            {
                log($"Device code request failed: {response.ReasonPhrase}");
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            log($"Login error: {ex.Message}");
        }
    }

    private async Task PollForTokenAsync(string deviceCode, int intervalSeconds, System.Threading.CancellationToken ct, Action<string> log)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
            if (ct.IsCancellationRequested) break;

            var reqBody = $"{{\"client_id\":\"{ClientId}\",\"device_code\":\"{deviceCode}\",\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"}}";
            var content = new System.Net.Http.StringContent(reqBody, System.Text.Encoding.UTF8, "application/json");
            
            try
            {
                var response = await _http.PostAsync("https://github.com/login/oauth/access_token", content, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("error", out var errorProp))
                    {
                        var err = errorProp.GetString();
                        if (err == "authorization_pending") continue;
                        else if (err == "slow_down") intervalSeconds += 5;
                        else
                        {
                            log($"OAuth error: {err}");
                            break;
                        }
                    }
                    else if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                    {
                        _accessToken = tokenProp.GetString() ?? "";
                        log("Authorization successful!");
                        await FetchUserProfileAsync(log);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                log($"Polling error: {ex.Message}");
            }
        }
    }

    private async Task FetchUserProfileAsync(Action<string> log)
    {
        try
        {
            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
            
            var response = await _http.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var login = doc.RootElement.GetProperty("login").GetString();
                var avatar = doc.RootElement.TryGetProperty("avatar_url", out var a) ? a.GetString() : null;

                DispatcherQueue?.TryEnqueue(() =>
                {
                    UsernameLabel.Text = login;
                    if (!string.IsNullOrEmpty(avatar))
                    {
                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(avatar));
                        AvatarPic.ProfilePicture = bitmap;
                    }
                    DeviceFlowPanel.Visibility = Visibility.Collapsed;
                    ProfilePanel.Visibility = Visibility.Visible;
                    log($"Successfully logged in as {login}.");
                });
            }
            else
            {
                log($"Profile fetch failed: {response.ReasonPhrase}");
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex)
        {
            log($"Profile fetch error: {ex.Message}");
        }
    }

    private async void OpenGithubBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (!string.IsNullOrEmpty(_verificationUri))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_verificationUri));
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(DeviceCodeLabel.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            LogViewer.Text += "Copied code to clipboard and opened browser.\n";
        }
        } catch (OperationCanceledException) { }
    catch (Exception ex) { BanglaHost.App.Services.CrashLogger.Log(ex, "AsyncVoidUI"); }
    }

    private void CancelLoginBtn_Click(object sender, RoutedEventArgs e)
    {
        _pollingCts?.Cancel();
        DeviceFlowPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LogViewer.Text += "Login cancelled.\n";
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
        }
        catch { }
        finally
        {
            _pollingCts = null;
        }
    }

    private void SignOutBtn_Click(object sender, RoutedEventArgs e)
    {
        _accessToken = "";
        ProfilePanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LogViewer.Text += "Signed out of GitHub.\n";
    }
}
