#define MyAppName "Sound Party Companion"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.1"
#endif
#define MyAppPublisher "Sound Party Companion contributors"
#define MyAppExeName "SoundPartyCompanion.App.exe"

[Setup]
AppId={{6A14EADB-08F4-4BB7-B39C-917EB8BB3250}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\SoundPartyCompanion
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir=..\artifacts\installer
OutputBaseFilename=SoundPartyCompanion-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\SoundPartyCompanion.App\Assets\app.ico
CloseApplications=yes
RestartApplications=no
AppMutex=Local\SoundPartyCompanion.App.v1.Mutex
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; Explicit payload allowlist: application and license notices, never user data.
Source: "..\artifacts\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\licenses\dotnet-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\licenses\dotnet-THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\licenses\windowsdesktop-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\licenses\windowsdesktop-THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\licenses\inno-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SoundPartyCompanion"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Check: ShouldRegisterStartup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'SoundPartyCompanion';
  FileAttributeReparsePoint = $400;

function IsSafeDataPath(Path: String): Boolean;
var
  FileInfo: TFindRec;
begin
  Result := False;
  { Use Inno's Unicode/path-aware wrapper; match only this exact path. }
  if FindFirst(Path, FileInfo) then begin
    try
      Result := (FileInfo.Attributes and FileAttributeReparsePoint) = 0;
    finally
      FindClose(FileInfo);
    end;
  end;
end;

function ShouldRegisterStartup: Boolean;
begin
  { Preserve existing startup state; the app reconciles saved preference on launch. }
  if FileExists(ExpandConstant('{localappdata}\SoundPartyCompanion\settings.json')) then
    Result := RegValueExists(HKCU, RunKey, RunValue)
  else
    Result := True;
end;

procedure RemoveOwnedStartup;
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, RunKey, RunValue, Command) and
    (CompareText(Trim(Command), '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" --startup') = 0) then
    RegDeleteValue(HKCU, RunKey, RunValue);
end;

procedure DeleteKnownFile(Path: String);
begin
  if IsSafeDataPath(Path) then
    if not DeleteFile(Path) then
      Log('A known local data file could not be removed; it was preserved.');
end;

procedure RemoveKnownUserData;
var
  DataDirectory, LogsDirectory: String;
  Index: Integer;
begin
  DataDirectory := ExpandConstant('{localappdata}\SoundPartyCompanion');
  if not IsSafeDataPath(DataDirectory) then begin
    Log('Known user-data cleanup skipped: root absent, linked or inaccessible.');
    Exit;
  end;
  Log('Known user-data cleanup started; exact file allowlist only.');
  DeleteKnownFile(DataDirectory + '\settings.json');
  DeleteKnownFile(DataDirectory + '\settings.json.tmp');
  DeleteKnownFile(DataDirectory + '\speaker-selection.json');
  DeleteKnownFile(DataDirectory + '\speaker-selection.json.tmp');
  LogsDirectory := DataDirectory + '\Logs';
  if IsSafeDataPath(LogsDirectory) then begin
    DeleteKnownFile(LogsDirectory + '\application.log');
    for Index := 1 to 3 do
      DeleteKnownFile(LogsDirectory + '\application.log.' + IntToStr(Index));
    RemoveDir(LogsDirectory);
  end;
  { Remove only empty directories; preserve unknown files and exported ZIPs. }
  RemoveDir(DataDirectory);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveOwnedStartup;
  if CurUninstallStep = usPostUninstall then begin
    if UninstallSilent then Exit;
    if SuppressibleMsgBox('是否删除本地设置、设备选择缓存和应用日志？' + #13#10 +
      '选择“否”可在重新安装后保留设置。已导出的诊断 ZIP 不会删除。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      RemoveKnownUserData;
  end;
end;

