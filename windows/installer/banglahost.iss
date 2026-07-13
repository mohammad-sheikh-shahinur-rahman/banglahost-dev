; BanglaHost (Windows) installer - Inno Setup. Produces a branded BanglaHost-Setup.exe
; that installs the unpackaged WinUI app to Program Files. Build with: iscc banglahost.iss
; (after `dotnet publish` puts the app under ..\publish\). Unsigned for now - users
; click "More info -> Run anyway" on SmartScreen (the Windows analog of macOS "Open Anyway").

#define MyAppName "BanglaHost"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "BanglaHost | Mohammad Sheikh Shahinur Rahman"
#define MyAppExe "BanglaHost.App.exe"

[Setup]
AppId={{8F3A1C2E-9B4D-4E6F-A1B2-C3D4E5F60718}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL=https://github.com/mohammad-sheikh-shahinur-rahman/BanglaHost
AppUpdatesURL=https://github.com/mohammad-sheikh-shahinur-rahman/BanglaHost/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Inno 6 hides the Welcome page by default - show it so the branded intro + website link appear.
DisableWelcomePage=no
OutputDir=dist
OutputBaseFilename=BanglaHost-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
WizardStyle=modern
SetupIconFile=..\src\BanglaHost.App\Assets\AppIcon.ico
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
; Show the MIT license page (user must accept) — file lives at the repo root.
LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExe}
; The tray GUI hides-to-tray when asked to close, which fools Windows' Restart Manager (it sees the
; window vanish, assumes the app closed, but the process is still alive and holding BanglaHost.App.exe /
; Core.dll -> install stalls at "Closing applications..."). So we DON'T use the Restart Manager; instead
; PrepareToInstall (see [Code]) force-kills the processes before the file copy. We relaunch via [Run].
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Branded intro (mirrors the macOS installer's welcome screen).
WelcomeLabel1=Welcome to BanglaHost — your free local web server
; Branded finish screen.
FinishedHeadingLabel=BanglaHost is ready
FinishedLabelNoIcons=Thanks for installing BanglaHost — your own free local web server for Windows.%n%nBuilt with love by Mohammad Sheikh Shahinur Rahman.
FinishedLabel=Thanks for installing BanglaHost — your own free local web server for Windows.%n%nBuilt with love by Mohammad Sheikh Shahinur Rahman.
WelcomeLabel2=Your own free local web server for Windows - a clean alternative to XAMPP, Laragon, WAMP and MAMP.%n%nThis installs BanglaHost into your Program Files. It includes:%n%n      -   Multiple PHP versions (7.4, 8.1-8.6), per site%n      -   nginx & Apache, MariaDB / MySQL / PostgreSQL%n      -   Redis & Memcached, Node.js (multiple versions)%n      -   phpMyAdmin, Adminer, Mailpit, trusted HTTPS + *.test domains%n      -   One-click WordPress / PHP sites with auto database%n      -   Share any site publicly with one click (Cloudflare tunnel)%n%nCompletely free & open-source - built with love by BanglaHost | Mohammad Sheikh Shahinur Rahman.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"
Name: "addtopath"; Description: "Add the banglahost CLI to PATH"; GroupDescription: "Command line:"

[Files]
; dotnet publish output (self-contained) goes to ..\publish\ - copy it all.
; This includes BanglaHost.App.exe (GUI), banglahost.exe (CLI), banglahost-elevate.exe (UAC helper).
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef Bundle
; Bundled server binaries (nginx/php/mysql/redis/...) so the install needs NO runtime
; downloads - which is what stops antivirus flagging banglahost.exe as a downloader.
Source: "..\payload\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Registry]
; Append the install dir to the system PATH so `banglahost` works from any terminal.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch BanglaHost"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the servers and close the app/CLI before the files are removed (otherwise the running
; exes lock {app} and uninstall stalls, and nginx/php would keep holding ports 80/443).
Filename: "{app}\banglahost.exe"; Parameters: "stop all"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopServices"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM BanglaHost.App.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM banglahost.exe"; Flags: runhidden; RunOnceId: "KillCli"

[Code]
// Force-close BanglaHost before installing. The GUI hides-to-tray on close (so the Restart Manager
// can't reliably close it), so we kill it outright here, just before files are copied. The servers
// (nginx/php/mariadb/...) run as separate processes and keep your sites up; the GUI relaunches via [Run].
function PrepareToInstall(var NeedsRestart: Boolean): String;
var rc: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM BanglaHost.App.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM banglahost.exe',     '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(700);   // let the file handles release before the copy
end;

// Add a credit line near the bottom of the welcome page.
procedure InitializeWizard;
var Link: TNewStaticText;
begin
  // Free up a strip at the bottom of the welcome text for the credit.
  WizardForm.WelcomeLabel2.Height := WizardForm.WelcomePage.Height - WizardForm.WelcomeLabel2.Top - ScaleY(34);
  Link := TNewStaticText.Create(WizardForm);
  Link.Parent := WizardForm.WelcomePage;
  Link.Caption := 'BanglaHost | Mohammad Sheikh Shahinur Rahman';
  Link.Font.Style := [fsBold];
  Link.Left := WizardForm.WelcomeLabel2.Left;
  Link.Top := WizardForm.WelcomePage.Height - ScaleY(26);
end;

function NeedsAddPath(Param: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKLM,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  // Only add if our dir isn't already on PATH (case-insensitive, delimited match).
  Result := Pos(';' + Uppercase(ExpandConstant(Param)) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

// On uninstall, strip the install dir we appended to the system PATH (leave no dangling entry).
procedure RemoveFromSystemPath(Dir: string);
var Orig, Up, UpDir: string; P: Integer;
begin
  if not RegQueryStringValue(HKLM,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Orig) then exit;
  Up := Uppercase(Orig); UpDir := Uppercase(Dir);
  P := Pos(';' + UpDir, Up);
  if P > 0 then Delete(Orig, P, Length(';' + Dir))
  else begin
    P := Pos(UpDir + ';', Up);
    if P > 0 then Delete(Orig, P, Length(Dir + ';'))
    else begin
      P := Pos(UpDir, Up);
      if P > 0 then Delete(Orig, P, Length(Dir));
    end;
  end;
  if P > 0 then
    RegWriteExpandStringValue(HKLM,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', Orig);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var dataDir: string;
begin
  if CurUninstallStep = usUninstall then
    RemoveFromSystemPath(ExpandConstant('{app}'));

  if CurUninstallStep = usPostUninstall then
  begin
    dataDir := ExpandConstant('{localappdata}\BanglaHost');
    if DirExists(dataDir) then
      if MsgBox('Also delete your BanglaHost data?' + #13#10 + #13#10 +
                'This permanently removes all your sites, databases, certificates and the' + #13#10 +
                'downloaded servers (PHP, nginx, MySQL, ...) in:' + #13#10 + #13#10 +
                '    ' + dataDir + #13#10 + #13#10 +
                'Choose No to keep them for a future reinstall.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(dataDir, True, True, True);
  end;
end;
