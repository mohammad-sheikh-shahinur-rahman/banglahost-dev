import sys

file = r"I:\BanglaHost\src\BanglaHost.App\Views\SiteListControl.xaml.cs"
with open(file, 'r', encoding='utf-8') as f:
    lines = f.readlines()

for i in range(len(lines)):
    if 'powershell.exe' in lines[i] and 'Set-Location' in lines[i]:
        # exact replacement without breaking C# syntax
        lines[i] = '        try { using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoExit -Command \\"Set-Location -LiteralPath \'{r.Root.Replace(\\"\'\\", \\"\'\'\\")}\'\\"", UseShellExecute = true })) { }; return; } catch { }\n'
        # wait, the quotes in Replace should be single quotes as chars, or double quotes but properly escaped for C#.
        # Actually it's easier to use @"" or just triple quotes in python.
        
with open(file, 'w', encoding='utf-8') as f:
    f.writelines(lines)
