; Inno Setup 6 script for Project Nest Explorer
; Build the app first:
;   dotnet publish src/ProjectExplorer.WinForms/ProjectExplorer.WinForms.csproj
;     /p:PublishProfile=win-x64-release
; Output lands in publish\ at the repo root, then run this script.

#define AppName      "Project Nest Explorer"
#define AppVersion   "1.0.3"
#define AppPublisher "HxM Blazor Software LLC"
#define AppExeName   "ProjectNest.exe"
#define AppId        "{{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://blaznaccess.com/landing/project-nest
AppSupportURL=mailto:support@blaznaccess.com
AppUpdatesURL=https://github.com/bmusical/ProjectExplorer/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\installer-output
OutputBaseFilename=ProjectNest-{#AppVersion}-Setup
SetupIconFile=..\src\ProjectExplorer.WinForms\Assets\app.ico
LicenseFile=..\LICENSE-EULA.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Inno Setup 6: unified privilege model
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
; Windows 10 1809+ required (needed for .NET 10 self-contained)

; Inno Setup 6: show changelog/notes page in wizard (optional — leave blank to skip)
; InfoAfterFile=..\CHANGELOG.txt

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2025 HxM Blazor Software LLC

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start automatically with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Single-file self-contained executable (produced by dotnet publish)
Source: "..\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Registry]
Root: HKCU; Subkey: "Software\HxM Blazor Software LLC\{#AppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave user data in %APPDATA%\ProjectExplorer alone — don't delete projects.json on uninstall
Type: filesandordirs; Name: "{app}"
