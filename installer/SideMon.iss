#define MyAppName "SideMon"
#define MyAppVersion "4.2.0"
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
OutputBaseFilename=SideMon-4.2.0-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\SidebarDiagnostics\Sidebar.ico
DisableProgramGroupPage=yes
; Restart Manager's own app-detection (triggered by CloseApplications=yes) can
; stall the "Preparing to Install" page if the running app's window doesn't
; respond to RM's query promptly, even when the app itself isn't hung. The
; taskkill calls in CurStepChanged(ssInstall) below already force-close the
; app without needing its cooperation, so the built-in feature is redundant
; and disabled here to avoid that freeze.
CloseApplications=no
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
; shellexec (not plain CreateProcess) is required here: SideMon.exe's own manifest
; requires elevation, and only ShellExecute knows how to broker that UAC handshake.
; Without it, launching the freshly installed app immediately fails with error 740
; (ERROR_ELEVATION_REQUIRED), even though Setup itself is already running elevated.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

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

{ SideMon ships its own .NET runtime (self-contained) and never installs one
  system-wide, so there is nothing of .NET's to offer removing here.
  PawnIO is a separate, optionally-installed kernel driver (namazso.eu) that
  other hardware-monitoring tools may also depend on, so removing it is opt-in
  and defaults to No. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UninstallString: String;
  ExePath, Params: String;
  SpacePos: Integer;
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO', 'UninstallString', UninstallString) then
    begin
      if MsgBox('Also remove the PawnIO sensor driver?' + #13#10 + #13#10 +
                'PawnIO is a separate driver used for CPU/GPU sensor readings. Other hardware-monitoring apps on this PC may also depend on it, so only remove it if you are sure SideMon was the only thing using it.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        { UninstallString looks like: "C:\Program Files\PawnIO\uninstall.exe" -uninstall }
        UninstallString := Trim(UninstallString);
        if (Length(UninstallString) > 0) and (UninstallString[1] = '"') then
        begin
          SpacePos := Pos('"', Copy(UninstallString, 2, Length(UninstallString) - 1)) + 1;
          ExePath := Copy(UninstallString, 2, SpacePos - 2);
          Params := Trim(Copy(UninstallString, SpacePos + 1, Length(UninstallString)));
        end
        else
        begin
          SpacePos := Pos(' ', UninstallString);
          if SpacePos = 0 then
          begin
            ExePath := UninstallString;
            Params := '';
          end
          else
          begin
            ExePath := Copy(UninstallString, 1, SpacePos - 1);
            Params := Trim(Copy(UninstallString, SpacePos + 1, Length(UninstallString)));
          end;
        end;

        { run PawnIO's own uninstaller as registered; it may show its own brief UI }
        Exec(ExePath, Params, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;
