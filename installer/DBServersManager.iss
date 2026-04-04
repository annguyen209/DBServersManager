; Inno Setup script for DB Servers Manager
; Requires published output in ..\DBServersManager\bin\Release\net8.0-windows\publish\

#define MyAppName "DB Servers Manager"
#define MyAppExeName "DBServersManager.exe"
#define MyAppVersion "1.0.0"
#define MyPublisher "AnzDev4Life"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\DBServersManager
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=DBServersManagerSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\DBServersManager\Assets\DBServersManager.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Launch app when Windows starts (current user)"; Flags: unchecked
Name: "runasadmin"; Description: "Always run app as administrator (may show UAC prompt each launch)"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\DBServersManager\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\DBServersManager\Assets\DBServersManager.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional startup with Windows (per-user)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "DBServersManager"; \
    ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

; Optional compatibility layer to force "Run as administrator"
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; \
    ValueType: string; ValueName: "{app}\{#MyAppExeName}"; \
    ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue; Tasks: runasadmin

[Run]
; Avoid elevation startup conflict; user can launch manually from desktop or Start Menu
;Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; \
;    Flags: nowait postinstall skipifsilent

