[Setup]
AppName=BanglaHost Local Web Server
AppVersion=1.3.4.0
AppPublisher=IT Amadersomaj Inc
DefaultDirName=C:\BanglaHost
DisableDirPage=yes
DefaultGroupName=BanglaHost
OutputDir=Installer
OutputBaseFilename=BanglaHost_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=src\BanglaHost.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\BanglaHost.App.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\BanglaHost"; Filename: "{app}\BanglaHost.App.exe"
Name: "{commondesktop}\BanglaHost"; Filename: "{app}\BanglaHost.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BanglaHost.App.exe"; Description: "{cm:LaunchProgram,BanglaHost}"; Flags: nowait postinstall skipifsilent
