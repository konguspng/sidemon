#define MyAppName "SideMon"
#define MyAppVersion "4.0.0"
#define MyAppExeName "SideMon.exe"

[Setup]
AppId={{B7E1C2D4-9A3F-4E5B-8C6D-2F1A0B9E7D53}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
OutputDir=.
OutputBaseFilename=SideMon-4.0.0-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\SidebarDiagnostics\Sidebar.ico
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; remove the pre-rename executable if upgrading an old install in place
Type: files; Name: "{app}\SidebarDiagnostics.exe"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "schtasks.exe"; Parameters: "/delete /f /tn ""SideMonStartup"""; Flags: runhidden; RunOnceId: "DelStartupTask"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    { stop any running copy, old or new name, so files can be replaced }
    Exec('taskkill.exe', '/f /im SidebarDiagnostics.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    { drop existing startup tasks (old and new names); the app recreates the
      task pointing at the new install path on first launch when Run At
      Startup is enabled }
    Exec('schtasks.exe', '/delete /f /tn "SidebarStartup"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('schtasks.exe', '/delete /f /tn "SideMonStartup"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;
