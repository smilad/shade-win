; Inno Setup script for Shade (Windows).
; Build: open in Inno Setup Compiler (iscc.exe Shade.iss).
; Output: installer\Output\ShadeSetup.exe

#define MyAppName "Shade"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "g3ntrix"
#define MyAppURL "https://github.com/g3ntrix/Shade"
#define MyAppExeName "Shade.exe"

[Setup]
AppId={{E2A40C12-7D33-4A89-A2C9-9D6A7B31D6F5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\shade_macos\LICENSE
OutputBaseFilename=ShadeSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked
Name: "startupicon"; Description: "Run on Windows startup"; GroupDescription: "Auto-start"; Flags: unchecked

[Files]
Source: "..\publish\Shade.exe";       DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\shade-core.exe";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}";    Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}";    Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
