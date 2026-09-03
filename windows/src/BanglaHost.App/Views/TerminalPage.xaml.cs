using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.Storage.Pickers;
using WinRT.Interop;
using BanglaHost.Core;

namespace BanglaHost.App.Views
{
    public sealed partial class TerminalPage : Page
    {
        private static readonly Lazy<SolidColorBrush> _statusReady = new(() => new SolidColorBrush(Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)));
        private static readonly Lazy<SolidColorBrush> _statusMissing = new(() => new SolidColorBrush(Color.FromArgb(0xFF, 0x64, 0x74, 0x8B)));
        private static SolidColorBrush StatusReady => _statusReady.Value;
        private static SolidColorBrush StatusMissing => _statusMissing.Value;

        private string _workingDir = string.Empty;

        public TerminalPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _workingDir = ResolveDefaultDir();
            WorkingDirBox.Text = _workingDir;
            RefreshShellAvailability();
        }

        private static string ResolveDefaultDir()
        {
            try
            {
                var sites = @"C:\BanglaHost\sites";
                if (Directory.Exists(sites)) return sites;
                var home = @"C:\BanglaHost";
                if (Directory.Exists(home)) return home;
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void OnWorkingDirChanged(object sender, TextChangedEventArgs e)
        {
            _workingDir = WorkingDirBox.Text?.Trim() ?? string.Empty;
        }

        private async void OnBrowse(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPath = await BanglaHost.App.Services.Picker.FolderAsync();
                if (folderPath != null)
                {
                    _workingDir = folderPath;
                    WorkingDirBox.Text = _workingDir;
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            _workingDir = ResolveDefaultDir();
            WorkingDirBox.Text = _workingDir;
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = EnsureWorkingDir();
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnPowerShell(object sender, RoutedEventArgs e) =>
            LaunchShell(BanglaHost.Core.SystemExe.PowerShell, "-NoLogo -NoExit");

        private void OnPwsh7(object sender, RoutedEventArgs e)
        {
            var path = FindPwsh7();
            if (path == null)
            {
                ShowError("PowerShell 7 (pwsh.exe) not found. Install it from https://aka.ms/powershell.");
                return;
            }
            LaunchShell(path, "-NoLogo -NoExit");
        }

        private void OnCmd(object sender, RoutedEventArgs e) =>
            LaunchShell(BanglaHost.Core.SystemExe.Cmd, "/K");

        private void OnGitBash(object sender, RoutedEventArgs e)
        {
            var path = FindGitBash();
            if (path == null)
            {
                ShowError("Git Bash not found. Install Git for Windows from https://git-scm.com/download/win.");
                return;
            }
            LaunchShell(path, "--login -i");
        }

        private void OnWsl(object sender, RoutedEventArgs e) =>
            LaunchShell(System.IO.Path.Combine(Environment.SystemDirectory, "wsl.exe"), string.Empty);

        private void LaunchShell(string fileName, string arguments)
        {
            try
            {
                ErrorBar.IsOpen = false;
                var dir = EnsureWorkingDir();

                // Prefer Windows Terminal (wt.exe) â€” modern UX, tabs, and it reliably opens a visible window.
                var wt = FindWindowsTerminal();
                if (wt != null)
                {
                    var wtArgs = $"-d \"{dir}\" \"{fileName}\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = wt,
                        Arguments = wtArgs,
                        UseShellExecute = true,
                        WorkingDirectory = dir,
                    });
                    return;
                }

                // Fallback: `cmd /c start` forces a new visible console window even from a GUI parent.
                var startArgs = $"/c start \"BanglaHost\" /D \"{dir}\" \"{fileName}\"" +
                                (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);
                using var p2 = Process.Start(new ProcessStartInfo
                {
                    // Absolute path (B11) — this spawns a visible shell.
                    FileName = BanglaHost.Core.SystemExe.Cmd,
                    Arguments = startArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = dir,
                });
            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private static string? FindWindowsTerminal()
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wtStore = Path.Combine(localApp, "Microsoft", "WindowsApps", "wt.exe");
            if (File.Exists(wtStore)) return wtStore;
            return FindOnPath("wt.exe");
        }

        private string EnsureWorkingDir()
        {
            var dir = string.IsNullOrWhiteSpace(_workingDir) ? ResolveDefaultDir() : _workingDir;
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch { dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            }
            return dir;
        }

        private void ShowError(string message)
        {
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }

        private void RefreshShellAvailability()
        {
            PowerShellStatus.Fill = StatusReady;

            var pwsh7 = FindPwsh7();
            if (pwsh7 != null)
            {
                Pwsh7Status.Fill = StatusReady;
                Pwsh7StatusText.Text = "Ready";
            }
            else
            {
                Pwsh7Status.Fill = StatusMissing;
                Pwsh7StatusText.Text = "Not installed";
                Pwsh7Sub.Text = "Install from aka.ms/powershell";
            }

            CmdStatus.Fill = StatusReady;

            var gitBash = FindGitBash();
            if (gitBash != null)
            {
                GitBashStatus.Fill = StatusReady;
                GitBashStatusText.Text = "Ready";
            }
            else
            {
                GitBashStatus.Fill = StatusMissing;
                GitBashStatusText.Text = "Not installed";
                GitBashSub.Text = "Install Git for Windows to enable";
            }

            if (IsWslAvailable())
            {
                WslStatus.Fill = StatusReady;
                WslStatusText.Text = "Ready";
            }
            else
            {
                WslStatus.Fill = StatusMissing;
                WslStatusText.Text = "Not installed";
                WslSub.Text = "Enable WSL: wsl --install";
            }
        }

        private static string? FindPwsh7()
        {
            foreach (var p in new[]
            {
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                @"C:\Program Files\PowerShell\7-preview\pwsh.exe",
            })
            {
                if (File.Exists(p)) return p;
            }
            return FindOnPath("pwsh.exe");
        }

        private static string? FindGitBash()
        {
            foreach (var p in new[]
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
            })
            {
                if (File.Exists(p)) return p;
            }
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var user = Path.Combine(localApp, @"Programs\Git\bin\bash.exe");
            return File.Exists(user) ? user : null;
        }

        private static bool IsWslAvailable()
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "wsl.exe"));
        }

        private static string? FindOnPath(string exe)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
