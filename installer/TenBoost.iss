; Установщик TenBoost. Собирается в CI: iscc /DAppVersion=x.y.z /DPayload=<папка> installer\TenBoost.iss
; Файл в UTF-8 с BOM: без BOM Inno Setup испортит кириллицу.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Payload
  #define Payload "..\build\payload"
#endif

[Setup]
AppId={{8C1F4E2A-5B7D-4E39-9A61-2F0B7C3D9E14}
AppName=TenBoost
AppVersion={#AppVersion}
AppVerName=TenBoost {#AppVersion}
AppPublisher=TenBoost
AppPublisherURL=https://github.com/tenvpn/tenboost
AppSupportURL=https://github.com/tenvpn/tenboost/issues
DefaultDirName={autopf}\TenBoost
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Ни одной страницы до установки: от запуска до «Включено» пользователь жмёт только UAC.
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
ShowLanguageDialog=no
WizardStyle=modern
OutputDir=..\out
OutputBaseFilename=TenBoost-Setup
Compression=lzma2/ultra64
SolidCompression=yes
UninstallDisplayName=TenBoost
UninstallDisplayIcon={app}\TenBoost.exe
CloseApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\TenBoost"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TenBoost"; ValueData: """{app}\TenBoost.exe"" --tray"; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\TenBoost"; Filename: "{app}\TenBoost.exe"

[Run]
Filename: "{app}\TenBoost.exe"; Flags: nowait runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\TenBoost"

[Code]
const
  ServiceName = 'TenBoost';
  ErrorServiceDoesNotExist = 1060;

{ Другие программы на WinDivert: службы zapret/zapret2 из win-bundle, GoodbyeDPI и их процессы.
  Сам драйвер windivert у них общий с нами и конфликтом не считается. }
function ForeignServices: TArrayOfString;
begin
  SetArrayLength(Result, 5);
  Result[0] := 'winws1';
  Result[1] := 'winws2';
  Result[2] := 'zapret';
  Result[3] := 'zapret2';
  Result[4] := 'GoodbyeDPI';
end;

function ForeignProcesses: TArrayOfString;
begin
  SetArrayLength(Result, 3);
  Result[0] := 'winws.exe';
  Result[1] := 'winws2.exe';
  Result[2] := 'goodbyedpi.exe';
end;

function RunHidden(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log(FileName + ' ' + Params + ' -> ' + IntToStr(Code));
  Result := Code;
end;

function Sc(const Params: String): Integer;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), Params);
end;

function ServiceExists(const Name: String): Boolean;
begin
  Result := Sc('query "' + Name + '"') <> ErrorServiceDoesNotExist;
end;

{ net stop ждёт фактической остановки, в отличие от sc stop. }
procedure StopService(const Name: String);
begin
  RunHidden(ExpandConstant('{sys}\net.exe'), 'stop "' + Name + '" /y');
end;

function ProcessRunning(const Image: String): Boolean;
begin
  Result := RunHidden(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq ' + Image + '" /NH | find /I "' + Image + '" >nul') = 0;
end;

procedure KillProcess(const Image: String);
begin
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM "' + Image + '"');
end;

function DetectConflicts(var Services, Processes: TArrayOfString): String;
var
  I: Integer;
  Names: TArrayOfString;
begin
  Result := '';
  SetArrayLength(Services, 0);
  SetArrayLength(Processes, 0);
  Names := ForeignServices;
  for I := 0 to GetArrayLength(Names) - 1 do
    if ServiceExists(Names[I]) then
    begin
      SetArrayLength(Services, GetArrayLength(Services) + 1);
      Services[GetArrayLength(Services) - 1] := Names[I];
      Result := Result + '  • служба ' + Names[I] + #13#10;
    end;
  Names := ForeignProcesses;
  for I := 0 to GetArrayLength(Names) - 1 do
    if ProcessRunning(Names[I]) then
    begin
      SetArrayLength(Processes, GetArrayLength(Processes) + 1);
      Processes[GetArrayLength(Processes) - 1] := Names[I];
      Result := Result + '  • программа ' + Names[I] + #13#10;
    end;
end;

procedure StopOwnComponents;
begin
  KillProcess('TenBoost.exe');
  if ServiceExists(ServiceName) then
    StopService(ServiceName);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Found: String;
  Services, Processes: TArrayOfString;
  I: Integer;
begin
  Result := '';
  { Свою службу останавливаем первой: иначе наш же winws2.exe примем за чужой. }
  StopOwnComponents;

  Found := DetectConflicts(Services, Processes);
  if Found = '' then
    exit;

  if SuppressibleMsgBox(
       'На компьютере уже работает программа, которая перехватывает сетевые пакеты:' + #13#10#13#10 + Found + #13#10 +
       'Две такие программы одновременно ломают интернет.' + #13#10#13#10 +
       'Остановить их и выключить их автозапуск? TenBoost их не удаляет: включить обратно можно в «Службах» Windows.',
       mbConfirmation, MB_YESNO, IDYES) <> IDYES then
  begin
    Result := 'Установка отменена. Остановите эти программы и запустите установку снова:' + #13#10 + Found;
    exit;
  end;

  for I := 0 to GetArrayLength(Services) - 1 do
  begin
    StopService(Services[I]);
    Sc('config "' + Services[I] + '" start= disabled');
  end;
  for I := 0 to GetArrayLength(Processes) - 1 do
    KillProcess(Processes[I]);
end;

procedure RegisterService;
var
  BinPath: String;
begin
  { Путь в кавычках внутри binPath: без них служба получает уязвимый «unquoted service path». }
  BinPath := '"\"' + ExpandConstant('{app}') + '\TenBoost.Service.exe\""';
  if ServiceExists(ServiceName) then
    Sc('config ' + ServiceName + ' binPath= ' + BinPath + ' start= auto')
  else
    Sc('create ' + ServiceName + ' binPath= ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "TenBoost"');
  Sc('description ' + ServiceName + ' "Ускоритель YouTube и Discord"');
  Sc('failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/30000/restart/60000');
  Sc('start ' + ServiceName);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Services, Processes: TArrayOfString;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopOwnComponents;
    Sc('delete ' + ServiceName);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Драйвер windivert общий: снимаем, только если им больше никто не пользуется. }
    if DetectConflicts(Services, Processes) = '' then
    begin
      Sc('stop windivert');
      Sc('delete windivert');
    end
    else
      Log('windivert оставлен: есть другие программы на WinDivert');
  end;
end;
