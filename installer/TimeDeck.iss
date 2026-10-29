; Build from the repository root with .\scripts\build-installer.ps1
; Inno Setup 6.3 or later is required for x64compatible.
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#define AppName "Time Deck"
#define AppExeName "TimeDeck.exe"
#define ProjectRoot AddBackslash(SourcePath) + ".."

[Setup]
; Keep this ID unchanged so future installers upgrade the same application.
AppId={{D49143B3-B2C9-4588-A55B-B3F5314058B4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=gavc
AppPublisherURL=https://github.com/gavc/timedeck
AppSupportURL=https://github.com/gavc/timedeck/issues
AppUpdatesURL=https://github.com/gavc/timedeck/releases
DefaultDirName={autopf}\TimeDeck
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ProjectRoot}\artifacts\installer
OutputBaseFilename=TimeDeck-Setup-{#AppVersion}-win-x64
SetupIconFile={#ProjectRoot}\src\TimeDeck.Windows\Assets\TimeDeck.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#ProjectRoot}\artifacts\TimeDeck\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Machine deployments must not start a user session under the installing account.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not IsAdminInstallMode

; Deliberately leave %LOCALAPPDATA%\TimeDeck and user backups untouched.
; Only per-user uninstall can safely clean the matching user's sign-in entry.
[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if (CurUninstallStep = usUninstall) and (not IsAdminInstallMode) then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'TimeDeck', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\{#AppExeName}') + '" --tray') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimeDeck');
end;
