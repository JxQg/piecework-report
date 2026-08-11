#ifndef MyAppVersion
  #define MyAppVersion "2.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish-stage-2.1.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#define MyAppId "{{9D51CA1A-D80A-4E4C-9147-FD951F71F4AC}"
#define MyFileVersion MyAppVersion + ".0"
#define FirewallRule "PieceworkReport LAN Access"

[Setup]
AppId={#MyAppId}
AppName=计件工资管理
AppVersion={#MyAppVersion}
AppVerName=计件工资管理 {#MyAppVersion}
AppPublisher=PieceworkReport
VersionInfoVersion={#MyFileVersion}
VersionInfoCompany=PieceworkReport
VersionInfoProductName=PieceworkReport Setup
VersionInfoDescription=计件工资管理安装程序
VersionInfoOriginalFileName=PieceworkReport-Setup.exe
DefaultDirName={autopf}\PieceworkReport
DefaultGroupName=计件工资管理
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=PieceworkReport-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
SetupIconFile=..\src\PieceworkReport.Launcher\Assets\app-icon.ico
UninstallDisplayIcon={app}\PieceworkReport.Launcher.exe
AppMutex=Local\PieceworkReport.Launcher

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Dirs]
Name: "{commonappdata}\PieceworkReport"; Permissions: users-modify
Name: "{commonappdata}\PieceworkReport\config"; Permissions: users-modify
Name: "{commonappdata}\PieceworkReport\data"; Permissions: users-modify
Name: "{commonappdata}\PieceworkReport\logs"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\计件工资管理"; Filename: "{app}\PieceworkReport.Launcher.exe"
Name: "{autodesktop}\计件工资管理"; Filename: "{app}\PieceworkReport.Launcher.exe"
Name: "{autoprograms}\卸载计件工资管理"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\web\PieceworkReport.Web.exe"" enable=yes profile=private protocol=TCP"; Flags: runhidden waituntilterminated
Filename: "{app}\PieceworkReport.Launcher.exe"; Description: "启动计件工资管理"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePrivateFirewallRule"

[Code]
var
  DeleteBusinessData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteBusinessData := False;
  if UninstallSilent then
    exit;
  if MsgBox('是否同时永久删除正式业务数据、工资导出和备份？' + #13#10 + #13#10 +
    '默认建议选择“否”，以便重新安装后继续使用。', mbConfirmation, MB_YESNO) = IDYES then
  begin
    DeleteBusinessData := MsgBox('此操作不可撤销。再次确认永久删除 C:\ProgramData\PieceworkReport 中的全部数据？',
      mbError, MB_YESNO) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PieceworkReport');
    if DeleteBusinessData then
      DelTree(ExpandConstant('{commonappdata}\PieceworkReport'), True, True, True);
  end;
end;
