#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif

#define MyAppName "EchoMinutes"
#define MyAppExeName "MeetingTransfer.App.exe"
#define MyAppPublisher "luckykevvv"
#define MyAppUrl "https://github.com/luckykevvv/echo-minutes"

[Setup]
AppId={{A62FE98E-47D0-43E7-91B5-6FA9E9833CEB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=echo-minutes-setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Windows installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
chinesesimplified.DesktopIcon=创建桌面快捷方式
english.ShortcutsGroup=Additional shortcuts:
chinesesimplified.ShortcutsGroup=附加快捷方式：
english.LaunchApp=Launch {#MyAppName}
chinesesimplified.LaunchApp=启动 {#MyAppName}
english.DotNetMissing=EchoMinutes requires Microsoft .NET 8 Desktop Runtime (x64). Install it before continuing. Open the official download page now?
chinesesimplified.DotNetMissing=EchoMinutes 需要 Microsoft .NET 8 桌面运行时（x64）。请先安装该运行时。是否现在打开官方下载页面？
english.DotNetInstallFirst=Microsoft .NET 8 Desktop Runtime (x64) was not detected. Install it, then continue setup.
chinesesimplified.DotNetInstallFirst=未检测到 Microsoft .NET 8 桌面运行时（x64）。请安装后再继续。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
function HasDotNetDesktopRuntime8At(const DotNetRoot: String): Boolean;
var
  FindRec: TFindRec;
  SearchPath: String;
begin
  Result := False;
  if DotNetRoot = '' then
    Exit;

  SearchPath := AddBackslash(DotNetRoot) +
    'shared\Microsoft.WindowsDesktop.App\8.*';
  if FindFirst(SearchPath, FindRec) then
  begin
    repeat
    begin
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        FindClose(FindRec);
        Result := True;
        Exit;
      end;
    end
    until not FindNext(FindRec);
    FindClose(FindRec);
  end;
end;

function HasDotNetDesktopRuntime8: Boolean;
var
  DotNetRoot: String;
begin
  Result := False;

  if RegQueryStringValue(
       HKLM64,
       'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost',
       'Path', DotNetRoot) and HasDotNetDesktopRuntime8At(DotNetRoot) then
  begin
    Result := True;
    Exit;
  end;

  if HasDotNetDesktopRuntime8At(ExpandConstant('{pf64}\dotnet')) or
     HasDotNetDesktopRuntime8At(ExpandConstant('{localappdata}\Microsoft\dotnet')) or
     HasDotNetDesktopRuntime8At(GetEnv('DOTNET_ROOT_X64')) or
     HasDotNetDesktopRuntime8At(GetEnv('DOTNET_ROOT')) then
    Result := True;
end;

procedure InitializeWizard;
var
  ErrorCode: Integer;
begin
  if (not WizardSilent) and (not HasDotNetDesktopRuntime8) then
  begin
    if MsgBox(CustomMessage('DotNetMissing'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec(
        'open',
        'https://dotnet.microsoft.com/download/dotnet/8.0/runtime',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if HasDotNetDesktopRuntime8 then
    Result := ''
  else
    Result := CustomMessage('DotNetInstallFirst');
end;
