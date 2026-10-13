; AppId 永久固定；版本与输入输出路径由 build.ps1 传入。
#ifndef AppVersion
  #error AppVersion must be supplied by build.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by build.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by build.ps1
#endif

[Setup]
AppId={{A4D4B3CB-929F-44B1-B59E-5CF981751C22}
AppName=Translate
AppVersion={#AppVersion}
AppPublisher=Drowndreamer
AppPublisherURL=https://github.com/Drowndreamer/Translate
AppSupportURL=https://github.com/Drowndreamer/Translate/issues
AppUpdatesURL=https://github.com/Drowndreamer/Translate/releases
DefaultDirName={localappdata}\Programs\Translate
DefaultGroupName=Translate
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Translate-Setup-{#AppVersion}
SetupIconFile=..\src\Translate\Assets\translate.ico
UninstallDisplayIcon={app}\Translate.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
LicenseFile=..\LICENSE
; 只卸载应用目录内的已安装文件，不删除 LocalAppData\Translate 配置。

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Translate\Translate"; Filename: "{app}\Translate.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Translate"; Filename: "{app}\Translate.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Translate.exe"; Description: "启动 Translate"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; 等待同一路径的驻留实例正常退出，其他便携目录的实例不受影响。
Filename: "{app}\Translate.exe"; Parameters: "--shutdown"; Flags: runhidden waituntilterminated; RunOnceId: "ShutdownTranslate"
