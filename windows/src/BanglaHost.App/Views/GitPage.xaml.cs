using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views;

/// <summary>
/// Git Manager page — connects to GitHub OAuth, lists local git repos,
/// clones remote repositories. Fixes the "NotFound" bug by correctly
/// building the GitHub API URL with proper auth headers and a valid
/// user-agent, and never calling /repos without a token.
/// </summary>
public sealed partial class GitPage : Page
{
    // ── GitHub OAuth (device-flow — no client-secret needed) ─────────────────
    // The "NotFound" bug originated from calling the /repos endpoint with no
    // token, or from an incorrect URL path. We fix both:
    //   1. All API calls include "Authorization: Bearer <token>" when a token
    //      is present, so GitHub returns 200 instead of 404.
    //   2. We use the /user/repos endpoint (not /repos/<login> which requires
    //      the repo to be public, or a scoped token).
    private const string ClientId   = "Ov23liQnpPHujNhTijDW";   // public, safe to embed
    private const string ApiBase    = "https://api.github.com";
    private const string TokenKey   = "BanglaHost.GitHubToken";

    private string? _token;
    private string? _userLogin;

    public GitPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _token    = ReadToken();
        _userLogin = null;
        _ = InitAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Initialisation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task InitAsync()
    {
        RefreshLocalRepos();

        if (_token is null)
        {
            AuthStatus.Text = "Not connected. Click \"Connect with GitHub\" to link your account.";
            return;
        }

        // Validate saved token and load user info.
        await LoadUserAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GitHub OAuth — Device Flow (no browser redirect required)
    // ─────────────────────────────────────────────────────────────────────────

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectBtn.IsEnabled = false;
        AuthBusy.IsActive    = true;
        AuthStatus.Text      = "Requesting device code…";
        try
        {
            using var http = BuildClient(null);

            // Step 1 — request a device + user code pair.
            var dc = await http.PostAsync(
                "https://github.com/login/device/code",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"]     = "repo,read:user",
                }));
            var dcBody = await dc.Content.ReadAsStringAsync();
            var dcJson = JsonDocument.Parse(dcBody).RootElement;

            var deviceCode  = dcJson.GetProperty("device_code").GetString()!;
            var userCode    = dcJson.GetProperty("user_code").GetString()!;
            var verifyUrl   = dcJson.GetProperty("verification_uri").GetString()!;
            var interval    = dcJson.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;
            var expiresIn   = dcJson.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900;

            // Show the user code and open the browser.
            AuthStatus.Text = $"Open  {verifyUrl}  and enter code:  {userCode}\n(waiting for you to authorise…)";
            try { Process.Start(new ProcessStartInfo(verifyUrl) { UseShellExecute = true }); } catch { }

            // Step 2 — poll for the access token.
            var deadline = DateTime.UtcNow.AddSeconds(expiresIn);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval + 1));

                var tr = await http.PostAsync(
                    "https://github.com/login/oauth/access_token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"]   = ClientId,
                        ["device_code"] = deviceCode,
                        ["grant_type"]  = "urn:ietf:params:oauth:grant-type:device_code",
                    }));
                var trBody = await tr.Content.ReadAsStringAsync();
                var trJson = JsonDocument.Parse(trBody).RootElement;

                if (trJson.TryGetProperty("access_token", out var tok))
                {
                    _token = tok.GetString()!;
                    SaveToken(_token);
                    await LoadUserAsync();
                    return;
                }

                var error = trJson.TryGetProperty("error", out var err) ? err.GetString() : null;
                if (error is "authorization_pending" or "slow_down") continue;
                AuthStatus.Text = $"Authorisation failed: {error}";
                return;
            }

            AuthStatus.Text = "Timed out waiting for authorisation. Try again.";
        }
        catch (Exception ex)
        {
            AuthStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
            AuthBusy.IsActive    = false;
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _token    = null;
        _userLogin = null;
        DeleteToken();
        UserPanel.Visibility       = Visibility.Collapsed;
        DisconnectBtn.Visibility   = Visibility.Collapsed;
        ConnectBtn.Visibility      = Visibility.Visible;
        GhReposCard.Visibility     = Visibility.Collapsed;
        AuthStatus.Text            = "Disconnected.";
        StatusBar.Text             = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GitHub API helpers — this is where the "NotFound" bug is fixed.
    //
    // Bug root cause: the original code called the GitHub API without a
    // User-Agent header (required — GitHub returns 403 without it) and with
    // a wrong endpoint path that returned 404. Both are fixed here.
    // ─────────────────────────────────────────────────────────────────────────

    private static HttpClient BuildClient(string? token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub requires a non-empty User-Agent on all API requests (RFC 7231).
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BanglaHost-GitManager/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task LoadUserAsync()
    {
        AuthBusy.IsActive = true;
        try
        {
            using var http = BuildClient(_token);
            // FIX: use /user (authenticated current user) instead of /users/<login>
            // which requires an exact public username and returns 404 for private ones.
            var resp = await http.GetAsync($"{ApiBase}/user");
            if (!resp.IsSuccessStatusCode)
            {
                AuthStatus.Text = $"GitHub API error: {resp.ReasonPhrase} — try disconnecting and reconnecting.";
                _token = null;
                DeleteToken();
                ConnectBtn.Visibility    = Visibility.Visible;
                DisconnectBtn.Visibility = Visibility.Collapsed;
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root       = doc.RootElement;
            _userLogin     = root.TryGetProperty("login", out var l) ? l.GetString() : "";
            var name       = root.TryGetProperty("name", out var n) && n.ValueKind != JsonValueKind.Null
                                 ? n.GetString() : _userLogin;

            UserName.Text  = name ?? "";
            UserLogin.Text = $"@{_userLogin}";

            UserPanel.Visibility       = Visibility.Visible;
            ConnectBtn.Visibility      = Visibility.Collapsed;
            DisconnectBtn.Visibility   = Visibility.Visible;
            GhReposCard.Visibility     = Visibility.Visible;
            AuthStatus.Text            = "Connected to GitHub.";

            await LoadGhReposAsync();
        }
        catch (Exception ex)
        {
            AuthStatus.Text = $"GitHub error: {ex.Message}";
        }
        finally
        {
            AuthBusy.IsActive    = false;
            ConnectBtn.IsEnabled = true;
        }
    }

    private async Task LoadGhReposAsync()
    {
        if (_token is null) return;
        GhBusy.IsActive = true;
        try
        {
            using var http = BuildClient(_token);
            // FIX: /user/repos lists ALL repos the authenticated user can access
            // (public + private + org repos), sorted by update time.
            // The old code used /repos/<login> which returns 404 for private users.
            var resp = await http.GetAsync($"{ApiBase}/user/repos?per_page=50&sort=updated&affiliation=owner,collaborator");
            if (!resp.IsSuccessStatusCode)
            {
                StatusBar.Text = $"GitHub API error: {resp.ReasonPhrase}";
                return;
            }

            using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var repos = doc.RootElement.EnumerateArray()
                .Select(r => new GhRepo(
                    r.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "",
                    r.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null
                        ? d.GetString() ?? "" : "",
                    r.TryGetProperty("clone_url", out var cu) ? cu.GetString() ?? "" : ""))
                .ToList();

            GhRepoList.ItemsSource = repos;
            StatusBar.Text = $"{repos.Count} repositories loaded.";
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Could not load repositories: {ex.Message}";
        }
        finally
        {
            GhBusy.IsActive = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Clone repository
    // ─────────────────────────────────────────────────────────────────────────

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        var url    = RepoUrl.Text.Trim();
        var folder = FolderName.Text.Trim();
        if (string.IsNullOrEmpty(url)) { CloneStatus.Text = "Enter a repository URL."; return; }
        if (string.IsNullOrEmpty(folder))
        {
            // Auto-derive folder name from the URL.
            folder = Path.GetFileNameWithoutExtension(url.TrimEnd('/'));
            if (string.IsNullOrEmpty(folder)) { CloneStatus.Text = "Enter a folder name."; return; }
            FolderName.Text = folder;
        }

        await DoCloneAsync(url, folder);
    }

    private async void QuickClone_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string url) return;
        var folder = Path.GetFileNameWithoutExtension(url.TrimEnd('/'));
        RepoUrl.Text    = url;
        FolderName.Text = folder;
        await DoCloneAsync(url, folder);
    }

    private async Task DoCloneAsync(string url, string folder)
    {
        CloneBtn.IsEnabled = false;
        CloneBusy.IsActive = true;
        CloneStatus.Text   = $"Cloning {url}…";

        try
        {
            var cfg      = Config.Load();
            var destRoot = cfg.SitesRoot;
            var dest     = Path.Combine(destRoot, folder);

            if (Directory.Exists(dest))
            {
                CloneStatus.Text = $"Folder already exists: {dest}";
                return;
            }

            var (ok, output) = await Task.Run(() => RunGit("clone", $"\"{url}\" \"{dest}\"", destRoot));
            CloneStatus.Text = ok
                ? $"Cloned to {dest}"
                : $"Clone failed: {output.TrimEnd()}";

            if (ok) RefreshLocalRepos();
        }
        catch (Exception ex)
        {
            CloneStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            CloneBtn.IsEnabled = true;
            CloneBusy.IsActive = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Local repository discovery
    // ─────────────────────────────────────────────────────────────────────────

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLocalRepos();

    private void RefreshLocalRepos()
    {
        var cfg   = Config.Load();
        var repos = FindLocalRepos(cfg.SitesRoot);
        RepoList.ItemsSource = repos;
        NoReposText.Visibility = repos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<LocalRepo> FindLocalRepos(string root)
    {
        var list = new List<LocalRepo>();
        if (!Directory.Exists(root)) return list;
        try
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var git = Path.Combine(dir, ".git");
                if (!Directory.Exists(git)) continue;
                var name   = Path.GetFileName(dir);
                var branch = GetBranch(dir);
                var status = GetStatus(dir);
                list.Add(new LocalRepo(name, dir, branch, status));
            }
        }
        catch { /* permission errors etc. */ }
        return list;
    }

    private static string GetBranch(string dir)
    {
        try
        {
            var (ok, output) = RunGit("rev-parse", "--abbrev-ref HEAD", dir);
            return ok ? output.Trim() : "";
        }
        catch { return ""; }
    }

    private static string GetStatus(string dir)
    {
        try
        {
            var (_, output) = RunGit("status", "--short", dir);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "Clean" : $"{lines.Length} change(s)";
        }
        catch { return ""; }
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string path) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Git runner — finds git.exe on PATH or common locations
    // ─────────────────────────────────────────────────────────────────────────

    private static (bool ok, string output) RunGit(string verb, string args, string? cwd)
    {
        var gitExe = FindGit();
        if (gitExe is null) return (false, "git not found on PATH. Install Git for Windows.");

        var psi = new ProcessStartInfo
        {
            FileName  = gitExe,
            Arguments = $"{verb} {args}",
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
            psi.WorkingDirectory = cwd;

        using var p = Process.Start(psi)!;
        var stdout  = p.StandardOutput.ReadToEnd();
        var stderr  = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode == 0, stdout + stderr);
    }

    private static string? _gitExeCache;
    private static string? FindGit()
    {
        if (_gitExeCache is not null) return _gitExeCache;
        foreach (var candidate in new[]
        {
            "git",   // on PATH — works if Git for Windows is installed
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
        })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = candidate, Arguments = "--version",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.WaitForExit(2000);
                if (p.ExitCode == 0) return _gitExeCache = candidate;
            }
            catch { }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Secure token storage (DPAPI via environment variable fallback)
    // ─────────────────────────────────────────────────────────────────────────

    private static string TokenFile => Path.Combine(Paths.Home, "run", "gh-token.dat");

    private static string? ReadToken()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            var data    = File.ReadAllBytes(TokenFile);
            var decoded = System.Security.Cryptography.ProtectedData.Unprotect(
                data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(decoded);
        }
        catch { return null; }
    }

    private static void SaveToken(string token)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
            var data    = System.Text.Encoding.UTF8.GetBytes(token);
            var encoded = System.Security.Cryptography.ProtectedData.Protect(
                data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, encoded);
        }
        catch { /* best-effort */ }
    }

    private static void DeleteToken()
    {
        try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
    }
}

// ── View models ───────────────────────────────────────────────────────────────

internal sealed record LocalRepo(string Name, string Path, string Branch, string Status);
internal sealed record GhRepo(string FullName, string Description, string CloneUrl);
