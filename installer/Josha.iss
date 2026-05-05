; Josha installer script for Inno Setup 6.3+
; Build with:  iscc.exe installer\Josha.iss
; Expects ..\publish\ to be populated by ..\publish.ps1 first.

#define MyAppName        "Josha"
#define MyAppVersion     "1.0.1"
#define MyAppPublisher   "Tomáš Trachta"
#define MyAppExeName     "Josha.exe"
#define MyAppSourceDir   "..\publish"
; Stable AppId — keep this constant across versions so upgrades replace the
; previous install instead of installing side-by-side. Generate a new one only
; for an unrelated product.
#define MyAppId          "{{8B3A4F2D-7C9E-4B1A-9E2F-5D6A8C0B1234}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=Josha-Setup-{#MyAppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
MinVersion=10.0
SetupIconFile=Josha.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; Note: the app stores user data under C:\josha_data\ (settings, encrypted
; bookmarks, FTP sites, snapshots, logs). The uninstaller intentionally does
; NOT remove that directory so user data survives an uninstall/reinstall.
