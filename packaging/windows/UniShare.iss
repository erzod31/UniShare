#ifndef MyAppVersion
#define MyAppVersion "0.6.16"
#endif

#ifndef PublishDir
  #error PublishDir must point to the self-contained Windows publish directory
#endif

#define MyAppName "UniShare"
#define MyAppPublisher "UniShare"
#define MyAppExeName "UniShare.Desktop.exe"

[Setup]
AppId={{BC1A70DC-8B2B-4B2B-8F2C-7A3EB751E3A7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\UniShare
DefaultGroupName=UniShare
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts
OutputBaseFilename=UniShare-Windows-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=force
CloseApplicationsFilter=*.*
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
english.Shortcuts=Shortcuts:
english.OpenUniShare=Open UniShare
spanish.DesktopIcon=Crear un acceso directo en el escritorio
spanish.Shortcuts=Accesos directos:
spanish.OpenUniShare=Abrir UniShare

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\UniShare"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\UniShare"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:OpenUniShare}"; Flags: nowait postinstall skipifsilent

[Registry]
; Si el usuario activó «Iniciar con Windows», elimina esa referencia al desinstalar.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "UniShare"; Flags: uninsdeletevalue

[UninstallDelete]
; Los datos personales permanecen deliberadamente en %LOCALAPPDATA%\UniShare.
Type: filesandordirs; Name: "{app}"
