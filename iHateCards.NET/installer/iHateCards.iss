; Inno Setup — установщик iHateCards с выбором места установки.
;
; Спрашивает при запуске: ставить «только для меня» (папка пользователя, без
; прав администратора и с работающим автообновлением) или «для всех» — и даёт
; изменить папку на любую другую.
;
; Собирается на Windows: ISCC.exe installer\iHateCards.iss
; (в проекте это делает GitHub Actions — .github/workflows/windows-installer.yml)

#define MyAppName "iHateCards"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#define MyAppPublisher "iHatePDF"
#define MyAppExeName "iHateCards.exe"

[Setup]
AppId={{8B6E3C1A-52F7-4A11-9A6C-1B9D53F0AC2E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://ihatepdf.ru/

; По умолчанию — установка в папку пользователя: не требует прав администратора
; и позволяет программе обновляться самой. Диалог в начале даёт выбрать
; установку для всех пользователей, а страница выбора папки — любой каталог.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
UsePreviousAppDir=yes

OutputDir=..\publish
OutputBaseFilename=iHateCards-{#MyAppVersion}-setup
SetupIconFile=..\src\iHateCards\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
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
; Ассоциация .hate — в ветке того пользователя (или машины), куда ставили
Root: HKA; Subkey: "Software\Classes\.hate"; ValueType: string; ValueName: ""; ValueData: "iHateCards.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\iHateCards.Project"; ValueType: string; ValueName: ""; ValueData: "Проект iHateCards"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Предупреждаем, если выбрана папка, куда обычный пользователь писать не может:
// тогда обновления будут просить права администратора.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if IsAdminInstallMode then
      Log('Установка для всех пользователей: обновления будут запрашивать права администратора');
  end;
end;
