using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BanglaHost.App.Services;
using BanglaHost.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BanglaHost.App.Views
{

/// <summary>
/// Local Git dashboard: browses .git repositories under the Sites root and shells out to
/// the system git.exe for clone / pull / push / status / log. GitHub is optional — users
/// paste a Personal Access Token, which is stored in %LOCALAPPDATA%\BanglaHost\config\github.json.
/// Nothing is sent anywhere: we don't run an OAuth flow client-side (the OAuth *client secret*
/// belongs on a backend, never in a shipped desktop app — hardcoding one in the binary makes it
/// trivial to extract and impersonate BanglaHost). A user-provided PAT gives the same access
/// safely.
/// </summary>
public sealed partial class GitPage : Page
{
    private static string TokenFile => Path.Combine(Paths.Config, "github.json");
    private string? _token;

    public GitPage()
    {
        InitializeComponent();
        _token = LoadToken();
        UpdateGhStatus();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Refresh();

    // ── local repos ─────────────────────────────────────────────────────────
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            var root = Config.Load().SitesRoot;
            var repos = new List<GitRepo>();
            if (Directory.Exists(root))
            {
                foreach (var d in Directory.GetDirectories(root))
                {
                    if (!Directory.Exists(Path.Combine(d, ".git"))) continue;
                    repos.Add(new GitRepo { Name = Path.GetFileName(d), Path = d, Status = BriefStatus(d) });
                }
            }
            EmptyRepos.Visibility = repos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RepoList.ItemsSource = repos;
        }
        catch { }
    }

    // ── button handlers ────────────────────────────────────────────────────
    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = CloneUrl.Text.Trim();
            if (url.Length == 0) { ActionResult.Text = "Enter a repo URL first."; return; }
            var root = Config.Load().SitesRoot;
            Directory.CreateDirectory(root);
            await RunGit(root, "clone", url);
            CloneUrl.Text = "";
            Refresh();
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); }
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)   { try { await WithRepo(sender, r => RunGit(r.Path, "pull")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }
    private async void Push_Click(object sender, RoutedEventArgs e)   { try { await WithRepo(sender, r => RunGit(r.Path, "push")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }
    private async void Status_Click(object sender, RoutedEventArgs e) { try { await WithRepo(sender, r => RunGit(r.Path, "status")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }
    private async void Log_Click(object sender, RoutedEventArgs e)    { try { await WithRepo(sender, r => RunGit(r.Path, "log", "--oneline", "-n", "30")); } catch (OperationCanceledException) { /* ignore */ } catch (Exception ex) { EngineHost.Instance.Append($"[ERROR] {ex.Message}"); } }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is GitRepo r)
        { try { using var p = Process.Start(new ProcessStartInfo { FileName = r.Path, UseShellExecute = true }); } catch { } }
    }

    private async Task WithRepo(object sender, Func<GitRepo, Task> action)
    {
        if (sender is Button b && b.DataContext is GitRepo r) await action(r);
        Refresh();
    }

    // ── git.exe runner ─────────────────────────────────────────────────────
    private async Task RunGit(string cwd, params string[] args)
    {
        Busy.IsActive = true;
        try
        {
            var git = FindGit();
            if (git is null) { ActionResult.Text = "git.exe not found. Install Git for Windows first."; return; }
            var psi = new ProcessStartInfo
            {
                FileName = git,
                WorkingDirectory = cwd,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (_token is { Length: > 0 })
            {
                psi.Environment["GIT_ASKPASS"] = "echo";
                psi.Environment["GITHUB_TOKEN"] = _token;
            }
            using var p = Process.Start(psi)!;
            var outp = await p.StandardOutput.ReadToEndAsync();
            var err  = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var body = (outp + err).Trim();
            if (body.Length > 3000) body = body[^3000..];
            ActionResult.Text = $"$ git {string.Join(' ', args)}\n{body}";
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { ActionResult.Text = "Error: " + ex.Message; }
        finally { Busy.IsActive = false; }
    }

    private static string? FindGit()
    {
        foreach (var p in new[] {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
        }) if (File.Exists(p)) return p;
        return "git.exe"; // PATH lookup
    }

    private static string BriefStatus(string repoDir)
    {
        try
        {
            var head = File.Exists(Path.Combine(repoDir, ".git", "HEAD"))
                ? File.ReadAllText(Path.Combine(repoDir, ".git", "HEAD")).Trim() : "";
            var branch = head.StartsWith("ref: refs/heads/") ? head["ref: refs/heads/".Length..] : "detached";
            return $"branch: {branch}";
        }
        catch { return "local repository"; }
    }

    // ── GitHub token storage ───────────────────────────────────────────────
    private void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        var t = GhToken.Password?.Trim() ?? "";
        if (t.Length == 0) { ActionResult.Text = "Paste a GitHub personal access token first."; return; }
        try
        {
            Directory.CreateDirectory(Paths.Config);
            File.WriteAllText(TokenFile, JsonSerializer.Serialize(new { token = t }));
            _token = t;
            GhToken.Password = "";
            UpdateGhStatus();
            ActionResult.Text = "GitHub token saved locally.";
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception ex) { ActionResult.Text = "Save failed: " + ex.Message; }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
        _token = null;
        UpdateGhStatus();
        ActionResult.Text = "Signed out.";
    }

    private void UpdateGhStatus()
    {
        GhStatus.Text = _token is { Length: > 0 }
            ? $"Signed in. Token starts with '{_token[..Math.Min(6, _token.Length)]}...' — kept locally in {TokenFile}."
            : "Not signed in.";
    }

    private static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(TokenFile));
            return doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    public class GitRepo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Status { get; set; } = "";
    }
}

}
