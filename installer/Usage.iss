; Inno Setup script for Usage.
;
; Produces a normal Windows installer: it asks where to install, creates a Start Menu entry so the app can be
; found by typing its name, and registers a proper uninstall entry in Add or Remove Programs. That last part is
; why the app itself has no "uninstall" menu item. Windows already owns that job.
;
; Per-user by default, so there is no UAC prompt and nothing is installed behind anyone's back. The user can
; still choose an all-users install from the privileges dialog, which puts it in Program Files.
;
; Build with scripts\Build-Installer.ps1 rather than calling ISCC by hand, so the payload is always a freshly
; published self-contained exe.

#define AppName "Usage"
#define AppVersion "1.0.0"
#define AppPublisher "MeltTheManual"
#define AppUrl "https://github.com/MeltTheManual/Usage-Taskbar"
#define AppExe "Usage.exe"

[Setup]
; Never change AppId. It is how Windows recognises an existing install and offers to upgrade it in place.
AppId={{8F3B2C41-9D6E-4A57-B8E2-1C7A5D0F3E9B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; {autopf} follows the privilege choice: Program Files for an all-users install, the per-user Programs folder
; otherwise. The directory page stays enabled on purpose, because people expect to be asked.
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\out\installer
OutputBaseFilename=Usage-Setup-{#AppVersion}
SetupIconFile=..\src\Usage.App\assets\Usage.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; The payload is already a compressed single file, so claiming otherwise would be misleading.
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Usage taskbar readout for Claude Code and Codex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\out\release\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; This is the entry that makes the app findable from the search bar.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
{ Stops a running copy the polite way, by setting the same quit event the Quit menu item uses. Killing the
  processes would work too, but the watcher exists to restart the UI, so a kill can race with it. --quit tells
  the watcher to stand down first. }
procedure StopRunningUsage(ExePath: String);
var
  ResultCode: Integer;
begin
  if FileExists(ExePath) then
  begin
    Exec(ExePath, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    { The watcher only checks the quit event every three seconds, so give it room to notice and exit before
      anyone tries to overwrite or delete the file it is running from. }
    Sleep(4000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningUsage(ExpandConstant('{app}\{#AppExe}'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningUsage(ExpandConstant('{app}\{#AppExe}'));
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app writes these at runtime, not the installer, so Inno does not know about them and would otherwise
      leave a dead startup entry pointing at a file that no longer exists. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Usage');
    DelTree(ExpandConstant('{localappdata}\Usage'), True, True, True);
  end;
end;
