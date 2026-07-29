#ifndef MyAppVersion
  #define MyAppVersion "2.20.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\release\2.20.0\portable"
#endif

[Setup]
AppId={{87FA2833-4A39-4A69-A9C9-4BAFB4700906}
AppName=Taskato
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Taskato
DefaultGroupName=Taskato
OutputDir=..\artifacts\release\{#MyAppVersion}
OutputBaseFilename=Taskato-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\src\Taskato\Assets\icon.ico
UninstallDisplayIcon={app}\Taskato.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Taskato"; Filename: "{app}\Taskato.exe"
Name: "{autodesktop}\Taskato"; Filename: "{app}\Taskato.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："; Flags: unchecked

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "正在安装 Microsoft Edge WebView2 Runtime…"; \
  Check: not WebView2IsInstalled; Flags: waituntilterminated
Filename: "{app}\Taskato.exe"; Description: "启动 Taskato"; Flags: nowait postinstall skipifsilent

[Code]
function WebView2IsInstalled: Boolean;
begin
  Result :=
    RegKeyExists(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C72E9A7A7B}') or
    RegKeyExists(HKLM, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C72E9A7A7B}') or
    RegKeyExists(HKLM64, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C72E9A7A7B}');
end;

function OnDownloadProgress(
  const Url, FileName: String;
  const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not WebView2IsInstalled then
  begin
    try
      DownloadTemporaryFile(
        'https://go.microsoft.com/fwlink/p/?LinkId=2124703',
        'MicrosoftEdgeWebview2Setup.exe',
        '',
        @OnDownloadProgress);
    except
      Log('WebView2 Runtime 下载失败：' + GetExceptionMessage);
      MsgBox('无法下载 WebView2 Runtime，安装已取消。请检查网络后重试。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
