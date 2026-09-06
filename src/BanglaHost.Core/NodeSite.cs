using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace BanglaHost.Core;

/// <summary>One supervised process (frontend or backend) of a Node-app site.</summary>
public sealed class NodeProc
{
    public string Dir { get; set; } = "";
    public string Cmd { get; set; } = "";   // e.g. "npm run dev"
    public int Port { get; set; }
}

/// <summary>A Node-app site: a frontend (and optional backend) reverse-proxied behind nginx.</summary>
public sealed class NodeSiteConfig
{
    public string Name { get; set; } = "";
    public NodeProc Frontend { get; set; } = new();
    public NodeProc? Backend { get; set; }
    public string ApiPath { get; set; } = "/api";
}

/// <summary>
/// Node-app site type ï¿½ the analog of the mac <c>nodesite</c> engine. Supervises a
/// frontend (+ optional backend) Node process and fronts them with an nginx
/// reverse-proxy vhost (<c>/api</c> â†’ backend, everything else â†’ frontend). Config
/// lives at <c>node-sites\&lt;name&gt;.json</c>.
/// </summary>
public static class NodeSite
{
    private static string Dir => Path.Combine(Paths.Home, "node-sites");
    private static string ConfPath(string name) => Path.Combine(Dir, $"{name}.json");
    private static string RunFile(string name, string which) => Path.Combine(Paths.Run, $"nodesite-{name}-{which}.json");
    private static string LogFile(string name, string which) => Path.Combine(Paths.Logs, $"nodesite-{name}-{which}.log");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static IReadOnlyList<string> List() =>
        Directory.Exists(Dir)
            ? Directory.EnumerateFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n is not null).Cast<string>().OrderBy(n => n).ToList()
            : Array.Empty<string>();

    public static NodeSiteConfig? Load(string name)
    {
        try { return File.Exists(ConfPath(name)) ? JsonSerializer.Deserialize<NodeSiteConfig>(File.ReadAllText(ConfPath(name)), Opts) : null; }
        catch { return null; }
    }

    public static void Save(NodeSiteConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(ConfPath(cfg.Name), JsonSerializer.Serialize(cfg, Opts));
    }

    public static void Delete(string name)
    {
        try { File.Delete(ConfPath(name)); } catch { }
    }

    private static bool PortOpen(int port)
    {
        return NetUtils.IsListening(port, 400);
    }

    public static bool Running(string name)
    {
        var cfg = Load(name);
        return cfg is not null && PortOpen(cfg.Frontend.Port);
    }

    /// <summary>Spawn a process (cmd run via cmd.exe with the fnm node on PATH + PORT set).</summary>
    private static bool StartProc(string name, string which, NodeProc p)
    {
        if (PortOpen(p.Port)) return true;
        if (string.IsNullOrWhiteSpace(p.Cmd) || !Directory.Exists(p.Dir)) return false;

        var log = LogFile(name, which);
        Directory.CreateDirectory(Paths.Logs);
        var psi = new ProcessStartInfo
        {
            // Absolute path: a bare "cmd.exe" resolves via PATH and can be
            // planted by an earlier PATH entry (B11). The user-authored command
            // itself intentionally runs under cmd (npm scripts, .cmd shims).
            FileName = SystemExe.Cmd,
            Arguments = $"/d /c \"{p.Cmd} > \"{log}\" 2>&1\"",
            WorkingDirectory = p.Dir,
            UseShellExecute = false, CreateNoWindow = true,
            // Do NOT set RedirectStandardOutput/Error here: the shell-level > "log" 2>&1
            // redirect already captures output. Combining both causes COMException.
        };
        var nodeBin = Tools.NodeBinDir();
        if (nodeBin is not null)
            psi.Environment["PATH"] = nodeBin + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
        psi.Environment["PORT"] = p.Port.ToString();

        Process? proc;
        try { proc = Process.Start(psi); }

        catch (Exception) { return false; }

        if (proc is null) return false;

        JobManager.Add(proc);

        Directory.CreateDirectory(Paths.Run);
        File.WriteAllText(RunFile(name, which), JsonSerializer.Serialize(new { pid = proc.Id, port = p.Port }));
        return true;
    }

    private static void StopProc(string name, string which)
    {
        var f = RunFile(name, which);
        try
        {
            if (File.Exists(f))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                // Stored PID is the transient cmd.exe wrapper — only kill when it still is
                // cmd/node/npm/yarn, never an unrelated recycled PID.
                BanglaHost.Core.ProcessUtils.KillSafeChecked(
                    doc.RootElement.GetProperty("pid").GetInt32(),
                    "cmd", "cmd.exe", "node", "node.exe", "npm", "npm.exe",
                    "yarn", "yarn.exe", "bun", "bun.exe", "deno", "deno.exe");
            }
        }
        catch { }
        try { File.Delete(f); } catch { }
    }

    public static (bool ok, string msg) Start(string name)
    {
        var cfg = Load(name);
        if (cfg is null) return (false, $"no node-app '{name}'");
        var f = StartProc(name, "frontend", cfg.Frontend);
        var b = cfg.Backend is null || StartProc(name, "backend", cfg.Backend);
        // give them a moment to bind
        for (var i = 0; i < 16 && !PortOpen(cfg.Frontend.Port); i++) System.Threading.Thread.Sleep(500);
        return (f && b)
            ? (true, $"node-app {name} starting (frontend :{cfg.Frontend.Port}{(cfg.Backend is not null ? $", backend :{cfg.Backend.Port}" : "")})")
            : (false, $"failed to start (check logs\\nodesite-{name}-*.log)");
    }

    public static void Stop(string name)
    {
        StopProc(name, "frontend");
        StopProc(name, "backend");
    }

    /// <summary>The frontend/backend working directory for a site (empty if none).</summary>
    public static string ProcDir(string name, string which)
    {
        var cfg = Load(name);
        if (cfg is null) return "";
        return which == "backend" ? cfg.Backend?.Dir ?? "" : cfg.Frontend.Dir;
    }

    /// <summary>Path to a process's .env file (frontend or backend dir).</summary>
    public static string EnvPath(string name, string which)
    {
        var d = ProcDir(name, which);
        return d.Length == 0 ? "" : Path.Combine(d, ".env");
    }

    /// <summary>The tail of a process's log (best-effort; empty if none yet).</summary>
    public static string LogTail(string name, string which, int maxBytes = 40000)
    {
        try
        {
            var f = LogFile(name, which);
            if (!File.Exists(f)) return "";
            var bytes = File.ReadAllBytes(f);
            var start = Math.Max(0, bytes.Length - maxBytes);
            return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }
        catch { return ""; }
    }

    /// <summary>Run `npm install` in a process's dir (with the fnm node on PATH). Blocks; returns output.</summary>
    public static (bool ok, string output) Npm(string name, string which)
    {
        var dir = ProcDir(name, which);
        if (dir.Length == 0 || !Directory.Exists(dir)) return (false, $"no {which} directory");
        // npm is a .cmd shim so cmd is required; absolute path (B11), bounded wait
        // with concurrent pipe reads instead of the unbounded sequential pair (C2/C5).
        var res = ProcRunner.Run(SystemExe.Cmd, new[] { "/d", "/c", "npm install" },
            workingDir: dir, timeoutMs: 600_000,
            env: Tools.NodeBinDir() is { } nb
                ? new Dictionary<string, string> { ["PATH"] = nb + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "") }
                : null);
        if (res.TimedOut) return (false, "npm install timed out after 10 minutes");
        return (res.ExitCode == 0, res.All.Trim());
    }

    /// <summary>Render the nginx reverse-proxy vhost for a Node-app site.</summary>
    public static void RenderVhost(NodeSiteConfig cfg, string domain, Config appCfg)
    {
        var home = NginxConfig.Fwd(Paths.Home);
        var apiBlock = "";
        if (cfg.Backend is not null)
        {
            var api = cfg.ApiPath.Trim('/');
            apiBlock = $$"""

                location /{{api}}/ {
                    proxy_pass http://127.0.0.1:{{cfg.Backend.Port}};
                    proxy_set_header Host $host;
                    proxy_http_version 1.1;
                    proxy_set_header Upgrade $http_upgrade;
                    proxy_set_header Connection "upgrade";
                    proxy_read_timeout 600;
                }
        """;
        }
        var listen = $"    listen 127.0.0.1:{appCfg.HttpPort};";
        var cert = Path.Combine(Paths.Certs, $"{domain}.pem");
        var key  = Path.Combine(Paths.Certs, $"{domain}-key.pem");
        if (File.Exists(cert) && File.Exists(key))
            listen += $"\n    listen 127.0.0.1:{appCfg.HttpsPort} ssl;\n    ssl_certificate {NginxConfig.Fwd(cert)};\n    ssl_certificate_key {NginxConfig.Fwd(key)};";

        var body = $$"""
        # BanglaHost site: {{cfg.Name}}  ({{domain}})  php=- server=node
        server {
        {{listen}}
            server_name {{domain}};
            access_log {{home}}/logs/{{cfg.Name}}-access.log;
            error_log  {{home}}/logs/{{cfg.Name}}-error.log;
        {{apiBlock}}
            location / {
                proxy_pass http://127.0.0.1:{{cfg.Frontend.Port}};
                proxy_set_header Host $host;
                proxy_http_version 1.1;
                proxy_set_header Upgrade $http_upgrade;
                proxy_set_header Connection "upgrade";
                proxy_read_timeout 600;
            }
        }

        """;
        Directory.CreateDirectory(Paths.NginxSites);
        File.WriteAllText(Path.Combine(Paths.NginxSites, $"{cfg.Name}.conf"), body);
    }
}
