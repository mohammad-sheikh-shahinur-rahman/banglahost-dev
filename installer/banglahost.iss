; BanglaHost (Windows) installer - Inno Setup. Produces a branded BanglaHost-Setup.exe
; that installs the unpackaged WinUI app to Program Files. Build with: iscc banglahost.iss
; (after `dotnet publish` puts the app under ..\publish\). Unsigned for now - users
; click "More info -> Run anyway" on SmartScreen (the Windows analog of macOS "Open Anyway").

#define MyAppName "BanglaHost"
#define MyAppVersion "1.6.5.0"
#define MyAppPublisher "IT Amadersomaj Inc"
#define MyAppExe "BanglaHost.App.exe"
#define MyAppURL "https://apps.microsoft.com/store/detail/9MWFKR8D8318?cid=DevShareMCLPCB"

[Setup]
AppId={{8F3A1C2E-9B4D-4E6F-A1B2-C3D4E5F60718}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName=C:\BanglaHost
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=dist
OutputBaseFilename=BanglaHost-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
WizardStyle=modern
WizardSizePercent=110
SetupIconFile=..\src\BanglaHost.App\Assets\AppIcon.ico
WizardImageFile=WizardImage.bmp,WizardImage-2x.bmp
WizardSmallImageFile=WizardSmallImage.bmp,WizardSmallImage-2x.bmp
WizardImageStretch=yes
UninstallDisplayIcon={app}\{#MyAppExe}
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to BanglaHost
WelcomeLabel2=A complete local web stack for Windows - the modern alternative to XAMPP, Laragon, WAMP and MAMP.%n%nThis installer sets up BanglaHost in your Program Files. Once installed you get:%n    •  Multiple PHP versions (7.4, 8.1 - 8.4) with ionCube support%n    •  nginx & Apache, MariaDB, MySQL, PostgreSQL, MongoDB%n    •  Redis, Memcached, Meilisearch%n    •  Node.js, Python, Composer, Go, Rust, Ollama%n    •  One-click WordPress and Laravel with auto database setup%n    •  Automatic HTTPS for *.test domains using mkcert%n    •  Cloudflare Tunnel for public sharing - no port forwarding%n    •  Built-in terminal, live resource monitor, backup & restore
FinishedHeadingLabel=BanglaHost is ready
FinishedLabelNoIcons=BanglaHost has been installed successfully. Launch it and start building - everything is set up.
FinishedLabel=BanglaHost has been installed successfully. Launch it and start building - everything is set up.
ClickFinish=Click Finish to complete the setup.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"
Name: "addtopath"; Description: "Add the banglahost CLI to PATH"; GroupDescription: "Command line:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef Bundle
Source: "..\payload\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch BanglaHost"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var rc: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM BanglaHost.App.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM banglahost.exe',     '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(700);
end;

procedure OpenWebsite(Sender: TObject);
var ErrorCode: Integer;
begin
  ShellExec('open', '{#MyAppURL}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard;
var Link: TNewStaticText;
begin
  WizardForm.WelcomeLabel2.Height := WizardForm.WelcomePage.Height - WizardForm.WelcomeLabel2.Top - ScaleY(34);
  Link := TNewStaticText.Create(WizardForm);
  Link.Parent := WizardForm.WelcomePage;
  Link.Caption := 'Get BanglaHost on Microsoft Store';
  Link.Cursor := crHand;
  Link.Font.Style := [fsUnderline];
  Link.Font.Color := clBlue;
  Link.OnClick := @OpenWebsite;
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
  Result := Pos(';' + Uppercase(ExpandConstant(Param)) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;





