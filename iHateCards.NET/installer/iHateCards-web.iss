; Inno Setup — «веб-установщик» iHateCards: сам файл маленький и не содержит
; программу вообще — при запуске он скачивает актуальную версию из
; публичного репозитория обновлений (тот же updates.json, что читает
; встроенный автообновлятор) и распаковывает её. Поэтому:
;   - этот .exe можно один раз выложить на сайт/в чат и больше не трогать —
;     он сам не устаревает, потому что ничего не «зашито», всегда тянет latest;
;   - при первом запуске пользователь получает свежую версию на момент запуска
;     установщика, а не на момент его сборки.
; После установки программа обновляется как обычно — сама, через тот же
; updates.json (см. Update/UpdateChecker.cs).
;
; Собирается на Windows: ISCC.exe installer\iHateCards-web.iss
; (в проекте это делает GitHub Actions — .github/workflows/windows-installer.yml)

#define MyAppName "iHateCards"
#define MyAppPublisher "iHatePDF"
#define MyAppExeName "iHateCards.exe"
#define ManifestUrl "https://github.com/AmajaWeed/ihatecards-updates/releases/latest/download/updates.json"

[Setup]
; Тот же AppId, что и у обычного установщика — так это тот же продукт для
; Windows (повторный запуск переустанавливает поверх, деинсталлятор общий).
AppId={{8B6E3C1A-52F7-4A11-9A6C-1B9D53F0AC2E}
AppName={#MyAppName}
AppVersion=Online
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://ihatepdf.ru/

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
UsePreviousAppDir=yes

OutputDir=..\publish
OutputBaseFilename=iHateCards-web-setup
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

; Файлов программы здесь нет — их скачивает [Code] на шаге ssInstall.
; UninstallDelete ниже подчищает то, что скачали, при удалении программы
; (сам Inno не отслеживает файлы, которые не перечислены в [Files]).
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.hate"; ValueType: string; ValueName: ""; ValueData: "iHateCards.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\iHateCards.Project"; ValueType: string; ValueName: ""; ValueData: "Проект iHateCards"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\iHateCards.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadedVersion: String;

// Скачивает и распаковывает актуальную версию поверх AppDir: читает
// updates.json, скачивает пакет win-x64, сверяет SHA-256 (та же проверка,
// что и в самом автообновляторе — Update/UpdateInstaller.cs), распаковывает
// и оставляет маркер .web-installer-version с полученной версией.
function DownloadAndInstall(AppDir: String): Boolean;
var
  ScriptPath, LogPath, Line, Content: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  Log: TStringList;
  i: Integer;
begin
  Result := False;
  ScriptPath := ExpandConstant('{tmp}\ihatecards-install.ps1');
  LogPath := ExpandConstant('{tmp}\ihatecards-install.log');

  Content :=
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    '$ProgressPreference = ''SilentlyContinue''' + #13#10 +
    '$dest = ''' + AppDir + '''' + #13#10 +
    '$tmp = Join-Path $env:TEMP (''ihatecards_'' + [guid]::NewGuid().ToString() + ''.zip'')' + #13#10 +
    'try {' + #13#10 +
    '  $manifest = Invoke-RestMethod -Uri ''{#ManifestUrl}'' -UseBasicParsing' + #13#10 +
    '  $pkg = $manifest.packages.''win-x64''' + #13#10 +
    '  if (-not $pkg) { throw ''manifest has no win-x64 package'' }' + #13#10 +
    '  Invoke-WebRequest -Uri $pkg.url -OutFile $tmp -UseBasicParsing' + #13#10 +
    '  $hash = (Get-FileHash -Path $tmp -Algorithm SHA256).Hash' + #13#10 +
    '  if ($hash.ToUpper() -ne $pkg.sha256.ToUpper()) { throw ("sha256 mismatch: expected " + $pkg.sha256 + '', got '' + $hash) }' + #13#10 +
    '  if (Test-Path $dest) { Get-ChildItem -Path $dest -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue } else { New-Item -ItemType Directory -Path $dest -Force | Out-Null }' + #13#10 +
    '  Expand-Archive -Path $tmp -DestinationPath $dest -Force' + #13#10 +
    '  Set-Content -Path (Join-Path $dest ''.web-installer-version'') -Value $manifest.latest -Encoding UTF8 -NoNewline' + #13#10 +
    '  "OK:" + $manifest.latest | Out-File -FilePath ''' + LogPath + ''' -Encoding UTF8' + #13#10 +
    '} catch {' + #13#10 +
    '  ("ERR:" + $_.Exception.Message) | Out-File -FilePath ''' + LogPath + ''' -Encoding UTF8' + #13#10 +
    '  exit 1' + #13#10 +
    '} finally {' + #13#10 +
    '  if (Test-Path $tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }' + #13#10 +
    '}' + #13#10;

  SaveStringToFile(ScriptPath, Content, False);

  WizardForm.StatusLabel.Caption := 'Скачивание и распаковка последней версии…';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
       '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      MsgBox('Не удалось запустить PowerShell — он нужен, чтобы скачать программу. ' +
             'Установите обновление Windows PowerShell и запустите установщик ещё раз.',
             mbCriticalError, MB_OK);
      Exit;
    end;

    Line := '';
    if FileExists(LogPath) and LoadStringFromFile(LogPath, Content) then
      Line := Content;

    if (ResultCode = 0) and (Copy(Line, 1, 3) = 'OK:') then
    begin
      DownloadedVersion := Trim(Copy(Line, 4, Length(Line) - 3));
      Result := True;
    end
    else
    begin
      if Copy(Line, 1, 4) = 'ERR:' then
        Line := Copy(Line, 5, Length(Line) - 4)
      else if Line = '' then
        Line := 'код ошибки ' + IntToStr(ResultCode);
      MsgBox('Не удалось скачать iHateCards.' + #13#10 + #13#10 + Line + #13#10 + #13#10 +
             'Проверьте подключение к интернету и попробуйте снова.',
             mbCriticalError, MB_OK);
    end;
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
    DeleteFile(ScriptPath);
    DeleteFile(LogPath);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  UninstallKey: String;
begin
  if CurStep = ssInstall then
  begin
    if not DownloadAndInstall(ExpandConstant('{app}')) then
      Abort;
  end;
  if CurStep = ssPostInstall then
  begin
    // Скачанная версия известна только после загрузки — Inno сам прописал
    // бы её из AppVersion (здесь это условное «Online»), поэтому правим
    // DisplayVersion в ветке деинсталлятора вручную, чтобы «Установка и
    // удаление программ» показывала настоящий номер версии.
    if DownloadedVersion <> '' then
    begin
      UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{8B6E3C1A-52F7-4A11-9A6C-1B9D53F0AC2E}}_is1';
      RegWriteStringValue(HKA, UninstallKey, 'DisplayVersion', DownloadedVersion);
    end;
  end;
end;
