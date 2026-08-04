import sys

file = r"I:\BanglaHost\src\BanglaHost.App\Views\SiteListControl.xaml.cs"
with open(file, 'r', encoding='utf-8') as f:
    lines = f.readlines()

for i in range(len(lines)):
    if 'powershell.exe' in lines[i] and 'Set-Location' in lines[i]:
        # just replace the line manually
        lines[i] = '        try { using (Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoExit -Command \\"Set-Location -LiteralPath \'{r.Root.Replace(\\"\'\\", \\"\'\'\\")}\'\\"", UseShellExecute = true })) { }; return; } catch { }\n'

with open(file, 'w', encoding='utf-8') as f:
    f.writelines(lines)
