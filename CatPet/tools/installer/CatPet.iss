; 猫咪桌宠 安装包脚本（Inno Setup 6）
; 由 CatPet\tools\pack.ps1 调用：
;   ISCC.exe /DMySourceDir=<发布目录> /DMyVersion=1.0.0 /DMyOutputDir=<输出目录> CatPet.iss

#ifndef MySourceDir
  #define MySourceDir "..\..\dist\CatPet-绿色版"
#endif
#ifndef MyVersion
  #define MyVersion "1.0.0"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\..\dist"
#endif

[Setup]
AppId={{7C2B5E31-8D4A-4E0B-9A6C-1F3D2A5B9C10}
AppName=猫咪桌宠
AppVersion={#MyVersion}
AppPublisher=essay
AppPublisherURL=https://github.com/essayZW
AppSupportURL=https://github.com/essayZW
AppUpdatesURL=https://github.com/essayZW
DefaultDirName={autopf}\CatPet
DefaultGroupName=猫咪桌宠
DisableProgramGroupPage=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=CatPet-Setup-{#MyVersion}
SetupIconFile=..\..\src\CatPet\app.ico
UninstallDisplayIcon={app}\CatPet.exe
UninstallDisplayName=猫咪桌宠
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 自包含运行时 + 149MB 素材，需要 64 位 Windows 10 及以上
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog



[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
; 整个发布目录（exe + 自包含运行时 + mod 素材）
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 升级/重装时先清掉旧素材和精灵图缓存：
; 素材改版后如果残留旧目录，会被自动发现成多余动画；旧缓存还会让新素材不生效
Type: filesandordirs; Name: "{app}\mod"
Type: filesandordirs; Name: "{app}\cache"

[Icons]
Name: "{group}\猫咪桌宠"; Filename: "{app}\CatPet.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 猫咪桌宠"; Filename: "{uninstallexe}"
Name: "{autodesktop}\猫咪桌宠"; Filename: "{app}\CatPet.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Code]
// 自启不在这里直接写注册表：安装程序可能以管理员权限运行，那样写的是管理员用户的 HKCU，
// 而程序运行时读的是当前用户，两边就对不上了。改成留一个标记文件，由程序首次启动时
// 用"它自己所属的用户"写注册表，这样安装选项和设置里的开关永远是同一个来源。
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('autostart') then
    SaveStringToFile(ExpandConstant('{app}\autostart.on'), '1', False);
end;

[UninstallRun]
; 卸载时清掉自启项（不管当初是安装器还是设置里开的）
Filename: "{cmd}"; Parameters: "/c reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v CatPet /f"; Flags: runhidden

[Run]
Filename: "{app}\CatPet.exe"; Description: "立即启动猫咪桌宠"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只删程序目录里的东西，用户设置在 %LOCALAPPDATA%\CatPet，卸载时保留
Type: filesandordirs; Name: "{app}\mod"
Type: files; Name: "{app}\autostart.on"
