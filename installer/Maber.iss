; Maber 安装包脚本（Inno Setup 6）
; 产品名固定为 Maber，安装后的主程序为 Maber.exe

#define MyAppName "Maber"
#define MyAppVersion "0.4.1"
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
; 默认按当前用户安装到 %LOCALAPPDATA%\Programs\Maber，不需要管理员权限
PrivilegesRequired=lowest
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Maber"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall Maber"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Maber"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Maber"; Flags: nowait postinstall skipifsilent
