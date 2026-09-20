; ============================================================
;  Инсталлятор RHI — русская локализация
;  Компилируется через Inno Setup 6 (ISCC.exe).
;
;  Пути параметризованы препроцессором ISPP: значения по
;  умолчанию — локальные пути автора. Их можно переопределить
;  из командной строки:
;    ISCC.exe /DPublishDir="C:\путь\к\publish\RHI" ^
;             /DIconFile="C:\путь\к\icon.ico" ^
;             /DInstallerOutDir="C:\путь\к\Installers" ^
;             "RHI Setup.iss"
;  Либо просто запустите build-setup.bat из корня репозитория —
;  он сам опубликует приложение и соберёт инсталлятор.
; ============================================================

#define MyAppName "RHI"
; Версия в заголовке установщика — синхронизируйте с версией приложения вручную.
#define MyAppVersion "2.0.1"
#define MyAppPublisher "RankFTW"
#define MyAppURL "www.github.com/rankftw"
#define MyAppExeName "RHI.exe"

#ifndef PublishDir
#define PublishDir "C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI"
#endif

#ifndef IconFile
#define IconFile "C:\Users\Mark\OneDrive\Documents\RDXC\icon.ico"
#endif

#ifndef InstallerOutDir
#define InstallerOutDir "C:\Users\Mark\OneDrive\Documents\RDXC\Installers"
#endif

[Setup]
; Примечание: значение AppId уникально идентифицирует это приложение.
; Не используйте тот же AppId в инсталляторах других приложений.
; (Чтобы сгенерировать новый GUID: Tools | Generate GUID внутри IDE.)
AppId={{05342962-55F5-4BA3-BFD3-DBA01D2B8BCB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
;AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; "ArchitecturesAllowed=x64compatible" — установка невозможна
; нигде, кроме x64 и Windows 11 on Arm.
ArchitecturesAllowed=x64compatible
; "ArchitecturesInstallIn64BitMode=x64compatible" — на x64 и Windows 11 on Arm
; установка идёт в 64-битном режиме: нативная папка Program Files
; и 64-битный вид реестра.
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
; Раскомментируйте для установки без прав администратора (только текущий пользователь).
;PrivilegesRequired=lowest
OutputDir={#InstallerOutDir}
OutputBaseFilename=RHI-Setup
SetupIconFile={#IconFile}
SolidCompression=yes
WizardStyle=modern dynamic

[Languages]
; Русский — единственный язык мастера установки (диалог выбора языка не показывается).
; Все стандартные тексты мастера (приветствие, лицензия, папки, завершение,
; удаление) берутся из встроенного Russian.isl.
; Чтобы вернуть английский, раскомментируйте следующую строку — тогда при запуске
; появится диалог выбора языка:
;Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Примечание: не используйте "Flags: ignoreversion" для общих системных файлов

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\RHI"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsRhiRunning(): Boolean;
var
  WMI: Variant;
  Procs: Variant;
begin
  Result := False;
  try
    WMI := CreateOleObject('WbemScripting.SWbemLocator');
    WMI := WMI.ConnectServer('.', 'root\cimv2');
    Procs := WMI.ExecQuery('SELECT * FROM Win32_Process WHERE Name="RHI.exe"');
    Result := (Procs.Count > 0);
  except
  end;
end;

function InitializeSetup(): Boolean;
var
  SignalDir, SignalPath: String;
  WaitCount: Integer;
begin
  Result := True;

  // Сигналим только если RHI реально запущен
  if not IsRhiRunning() then Exit;

  // Записываем файл сигнала завершения в %LocalAppData%\RHI\
  SignalDir := ExpandConstant('{localappdata}\RHI');
  if not DirExists(SignalDir) then
    ForceDirectories(SignalDir);
  SignalPath := SignalDir + '\rhi_shutdown_requested';
  SaveStringToFile(SignalPath, 'update', False);

  // Ждём до 10 секунд, пока RHI закроется сам
  WaitCount := 0;
  while (WaitCount < 20) and IsRhiRunning() do
  begin
    Sleep(500);
    WaitCount := WaitCount + 1;
  end;

  // Если всё ещё запущен, стандартный CloseApplications InnoSetup разберётся сам
end;
