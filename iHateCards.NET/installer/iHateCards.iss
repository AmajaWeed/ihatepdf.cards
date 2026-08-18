; Inno Setup — установщик iHateCards (.NET/Avalonia, self-contained)
; Сборка приложения перед компиляцией установщика:
;   dotnet publish src/iHateCards/iHateCards.csproj -c Release -r win-x64 --self-contained -o publish\win-x64
; Затем скомпилировать этот скрипт в Inno Setup Compiler.

#define MyAppName "iHateCards"
#define MyAppVersion "1.0"
#define MyAppPublisher "iHatePDF"
#define MyAppExeName "iHateCards.exe"

[Setup]
AppId={{8B6E3C1A-52F7-4A11-9A6C-1B9D53F0AC2E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=iHateCards-Setup
SetupIconFile=..\src\iHateCards\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Ассоциация файлов .hate
Root: HKA; Subkey: "Software\Classes\.hate"; ValueType: string; ValueName: ""; ValueData: "iHateCards.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\iHateCards.Project"; ValueType: string; ValueName: ""; ValueData: "Проект iHateCards"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
