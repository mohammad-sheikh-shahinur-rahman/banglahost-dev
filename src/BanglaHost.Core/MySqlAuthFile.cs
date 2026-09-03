using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace BanglaHost.Core;

/// <summary>
/// Passes MySQL/MariaDB credentials to the client tools without ever putting them on a command line.
///
/// Why: <c>-p{password}</c> as an argument is readable by any process on the machine for the whole
/// lifetime of the child — <c>Win32_Process.CommandLine</c>, Task Manager's command-line column,
/// Process Explorer, and any AV/EDR agent's process telemetry all see it. It also leaked through
/// <c>Database.BaseArgs</c>, which built the argument as an unquoted string, so a password
/// containing a space or a quote produced a malformed command line — a password of
/// <c>x --execute="DROP DATABASE foo"</c> would have been parsed as extra arguments.
///
/// <c>--defaults-extra-file</c> is the vendor-supported alternative: a 0600-equivalent temp file the
/// client reads before anything else. It must be the FIRST argument on the command line, which
/// <see cref="Apply"/> enforces by construction.
/// </summary>
public sealed class MySqlAuthFile : IDisposable
{
    public string Path { get; }
    private bool _disposed;

    private MySqlAuthFile(string path) => Path = path;

    /// <summary>
    /// Write a credentials file for the given user/password, locked down to the current user.
    /// Returns null when there is no password to protect — a passwordless local root needs no file,
    /// and creating one would just be temp-file churn.
    /// </summary>
    public static MySqlAuthFile? Create(string user, string? password, int port, string host = "127.0.0.1")
    {
        if (string.IsNullOrEmpty(password)) return null;

        var dir = System.IO.Path.Combine(Paths.Tmp, "auth");
        Directory.CreateDirectory(dir);
        HardenDirectory(dir);

        var path = System.IO.Path.Combine(dir, $"my-{Guid.NewGuid():N}.cnf");

        // The client reads [client] for every tool (mysql, mysqldump, mysqladmin).
        // Values are written raw: MySQL option files do not support escaping, so a password
        // containing a newline cannot be expressed here — reject it rather than write a file that
        // silently truncates the password or injects a second option.
        if (password.Contains('\n') || password.Contains('\r'))
            throw new BhException("Database password cannot contain a line break.");

        var sb = new StringBuilder();
        sb.Append("[client]\n");
        sb.Append("user=").Append(user).Append('\n');
        sb.Append("password=").Append(password).Append('\n');
        sb.Append("host=").Append(host).Append('\n');
        sb.Append("port=").Append(port).Append('\n');

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        HardenFile(path);
        return new MySqlAuthFile(path);
    }

    /// <summary>
    /// Build the leading arguments for a client invocation. Returns the credentials-file argument
    /// when one exists, otherwise the plain user/host/port arguments — never a password argument.
    /// </summary>
    public static List<string> Apply(MySqlAuthFile? auth, string user, int port, string host = "127.0.0.1")
    {
        var args = new List<string>();
        if (auth is not null)
        {
            // MUST be first — the client ignores it otherwise.
            args.Add($"--defaults-extra-file={auth.Path}");
        }
        else
        {
            args.Add("-u"); args.Add(user);
            args.Add("-h"); args.Add(host);
            args.Add("-P"); args.Add(port.ToString());
        }
        args.Add("--connect-timeout=5");
        return args;
    }

    /// <summary>
    /// Validate a database, table or user identifier before it is interpolated into SQL.
    /// The client tools take SQL as a single <c>-e</c> argument, so there is no parameter binding
    /// available — the only safe option is to refuse anything that isn't a plain identifier.
    /// Backtick-quoting alone is not enough: a name containing a backtick closes the quote.
    /// </summary>
    public static string ValidIdentifier(string name, string what = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BhException($"{what} is required.");
        if (name.Length > 64)
            throw new BhException($"{what} '{name}' is too long (MySQL allows 64 characters).");
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_$]+$"))
            throw new BhException(
                $"Invalid {what} '{name}'. Only letters, digits, underscore and $ are allowed.");
        return name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }

    private static void HardenFile(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            // Break inheritance and drop inherited rules — otherwise a permissive ACL on the
            // parent (or on C:\BanglaHost, which is created outside a user profile) still applies.
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var me = WindowsIdentity.GetCurrent().User;
            if (me is not null)
                sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
            fi.SetAccessControl(sec);
        }
        catch { /* best effort: the file is still short-lived and in a per-user temp dir */ }
    }

    private static void HardenDirectory(string dir)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var di = new DirectoryInfo(dir);
            var sec = di.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var me = WindowsIdentity.GetCurrent().User;
            if (me is not null)
                sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                    InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            di.SetAccessControl(sec);
        }
        catch { }
    }
}
