using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public enum DeployMethod { FTP, SFTP, SSH, GitHub, Docker, VPS, CloudflareTunnel, Panel }

public class DeployProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Profile";
    public DeployMethod Method { get; set; } = DeployMethod.FTP;
    public string SourceDir { get; set; } = "";
    
    // Remote Settings
    public string Host { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = "";
    public string RemotePath { get; set; } = "/var/www/html";
    public string ExtraArgs { get; set; } = "";
    
    // GitHub specific
    public string RepoUrl { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string PersonalAccessToken { get; set; } = "";
}

public static class DeployService
{
    private static string ProfileDir => Path.Combine(Paths.Home, "deploy_profiles");

    public static void Init()
    {
        if (!Directory.Exists(ProfileDir))
            Directory.CreateDirectory(ProfileDir);
    }

    public static DeployProfile[] GetProfiles()
    {
        Init();
        var files = Directory.GetFiles(ProfileDir, "*.json");
        var list = new System.Collections.Generic.List<DeployProfile>();
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var p = JsonSerializer.Deserialize<DeployProfile>(json);
                if (p != null) list.Add(p);
            }
            catch { }
        }
        return list.ToArray();
    }

    public static void SaveProfile(DeployProfile p)
    {
        Init();
        var path = Path.Combine(ProfileDir, $"{p.Id}.json");
        var json = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static void DeleteProfile(string id)
    {
        var path = Path.Combine(ProfileDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static async Task<bool> DeployAsync(DeployProfile profile, Action<string> log)
    {
        log($"Starting deployment via {profile.Method} for profile '{profile.Name}'");
        if (!Directory.Exists(profile.SourceDir))
        {
            log($"Source directory does not exist: {profile.SourceDir}");
            return false;
        }

        try
        {
            switch (profile.Method)
            {
                case DeployMethod.GitHub:
                    return await DeployGitHubAsync(profile, log);
                case DeployMethod.Docker:
                    return await DeployDockerAsync(profile, log);
                case DeployMethod.CloudflareTunnel:
                    return await DeployCloudflareAsync(profile, log);
                case DeployMethod.SSH:
                case DeployMethod.SFTP:
                case DeployMethod.VPS:
                    return await DeploySshAsync(profile, log);
                case DeployMethod.FTP:
                    log("FTP requires a third-party CLI like WinSCP or curl. Implementing curl FTP upload...");
                    return await DeployCurlFtpAsync(profile, log);
                default:
                    log($"Method {profile.Method} is not fully implemented yet.");
                    return false;
            }
        }
        catch (Exception ex)
        {
            log($"Deployment failed with exception: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunProcessAsync(string exe, string args, string cwd, Action<string> log)
    {
        log($"$ {exe} {args}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null) return false;

        p.OutputDataReceived += (s, e) => { if (e.Data != null) log(e.Data); };
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) log($"ERR: {e.Data}"); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();
        log($"Exit code: {p.ExitCode}");
        return p.ExitCode == 0;
    }

    private static async Task<bool> DeployGitHubAsync(DeployProfile p, Action<string> log)
    {
        var exe = "git"; // Assume git is in PATH
        log("Pushing to GitHub...");
        var add = await RunProcessAsync(exe, "add .", p.SourceDir, log);
        if (!add) return false;
        await RunProcessAsync(exe, "commit -m \"Automated deploy from BanglaHost\"", p.SourceDir, log);
        return await RunProcessAsync(exe, "push origin main", p.SourceDir, log);
    }

    private static async Task<bool> DeployDockerAsync(DeployProfile p, Action<string> log)
    {
        var exe = "docker";
        log("Building and pushing Docker image...");
        var imgName = string.IsNullOrEmpty(p.ExtraArgs) ? $"banglahost-{p.Name.ToLower()}" : p.ExtraArgs;
        var build = await RunProcessAsync(exe, $"build -t {imgName} .", p.SourceDir, log);
        if (!build) return false;
        return await RunProcessAsync(exe, $"push {imgName}", p.SourceDir, log);
    }

    private static async Task<bool> DeployCloudflareAsync(DeployProfile p, Action<string> log)
    {
        var exe = Path.Combine(Paths.Bin, "cloudflared", "cloudflared.exe");
        if (!File.Exists(exe))
        {
            log("cloudflared not found. Please install it first.");
            return false;
        }
        log("Starting Cloudflare Tunnel...");
        // Actually deploying a tunnel is a long-running process, we'd spawn it and leave it running.
        // For a one-off deploy script, we might just apply routing.
        return await RunProcessAsync(exe, $"tunnel route dns {p.ExtraArgs} {p.Host}", p.SourceDir, log);
    }

    private static async Task<bool> DeploySshAsync(DeployProfile p, Action<string> log)
    {
        // Require ssh/scp to be in PATH (Windows 10/11 includes OpenSSH)
        log("Uploading files via SCP...");
        // scp -r SourceDir\* user@host:/path
        var args = $"-r -P {p.Port} . {p.Username}@{p.Host}:{p.RemotePath}";
        return await RunProcessAsync("scp", args, p.SourceDir, log);
    }

    private static async Task<bool> DeployCurlFtpAsync(DeployProfile p, Action<string> log)
    {
        // Using system curl to upload via FTP
        var exe = Path.Combine(Environment.SystemDirectory, "curl.exe");
        // For directories, this requires iterating or zipping. Let's zip it first.
        log("Zipping directory for FTP upload...");
        var zipPath = Path.Combine(Paths.Tmp, $"deploy_{p.Id}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(p.SourceDir, zipPath);
        
        log("Uploading zip via FTP...");
        var url = $"ftp://{p.Host}:{p.Port}{p.RemotePath}/deploy.zip";
        var args = $"-T \"{zipPath}\" -u {p.Username}:{p.Password} {url}";
        var res = await RunProcessAsync(exe, args, Paths.Tmp, log);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        return res;
    }
}
