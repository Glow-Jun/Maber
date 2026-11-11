; Maber 安装包脚本（Inno Setup 6）
; 产品名固定为 Maber，安装后的主程序为 Maber.exe

#define MyAppName "Maber"
; 版本由 installer/build-exe.ps1 通过 /DMyAppVersion 传入；手工编译时用下面的默认值
#ifndef MyAppVersion
  #define MyAppVersion "0.3.0"
#endif
#define MyAppExeName "Maber.exe"

[Setup]
AppId={{A3C2F1D4-7E5B-4A9C-8F21-6B7D9E2A4C10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Maber
DefaultDirName={autopf}\Maber
DefaultGroupName=Maber
DisableProgramGroupPage=yes
DisableDirPage=no
; 默认安装到 Program Files（需要管理员）；允许 /CURRENTUSER 按用户安装
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
OutputDir=..
OutputBaseFilename=Maber-Setup-{#MyAppVersion}
SetupIconFile=..\src\Maber.App\Assets\Maber.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; publish\app 由 installer/build-exe.ps1 生成
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Maber"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall Maber"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Maber"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Maber"; Flags: nowait postinstall skipifsilent
; 文件关联图标变更后刷新 Explorer 图标缓存
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait

[Registry]
; ProgID：Markdown 文档的图标与打开命令
Root: HKA; Subkey: "Software\Classes\Maber.Markdown"; ValueType: string; ValueName: ""; ValueData: "Markdown 文档"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Maber.Markdown"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Markdown 文档"
Root: HKA; Subkey: "Software\Classes\Maber.Markdown\DefaultIcon"; ValueType: string; ValueData: "{app}\Assets\MaberMarkdown.ico,0"
Root: HKA; Subkey: "Software\Classes\Maber.Markdown\shell\open\command"; ValueType: string; ValueData: """{app}\Maber.exe"" ""%1"""

; 让 Maber 出现在 .md / .markdown 的“打开方式”候选里（不抢占当前默认）
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Maber.Markdown"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Maber.Markdown"; ValueData: ""; Flags: uninsdeletevalue

; 应用注册：选择其他应用 / 打开方式列表
Root: HKA; Subkey: "Software\Classes\Applications\Maber.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Maber"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Maber.exe\DefaultIcon"; ValueType: string; ValueData: "{app}\Assets\MaberMarkdown.ico,0"
Root: HKA; Subkey: "Software\Classes\Applications\Maber.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Maber.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\Maber.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Maber.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""

; 默认应用能力注册：Windows 设置 → 默认应用 / 推荐应用
Root: HKA; Subkey: "Software\Classes\Maber\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Maber"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Maber\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Maber — Markdown 阅读器"
Root: HKA; Subkey: "Software\Classes\Maber\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "Maber.Markdown"
Root: HKA; Subkey: "Software\Classes\Maber\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "Maber.Markdown"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Maber"; ValueData: "Software\Classes\Maber\Capabilities"; Flags: uninsdeletevalue

[Code]
function HasDotNet10Desktop(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  // findstr 命中返回 0，未命中返回 1；不引入临时文件与编码问题
  if Exec('cmd.exe',
          '/c dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 10." >nul 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode = 0;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasDotNet10Desktop() then
  begin
    if MsgBox('Maber 需要 .NET 10 Desktop Runtime（x64）。' + #13#10 +
              '未检测到该运行时，安装后程序可能无法启动。' + #13#10#13#10 +
              '是否仍然继续安装？', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
