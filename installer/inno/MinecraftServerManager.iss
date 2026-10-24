; Setup wizard for Minecraft Server Manager (Inno Setup 6.3 or later).
;
; Build with scripts\build-release.ps1, or directly:
;   ISCC.exe /DAppVersion=1.0.0 /DPublishDir=<published app folder> /DOutputDir=<output folder> MinecraftServerManager.iss
;
; Silent install:   MinecraftServerManager-<version>-Setup.exe /VERYSILENT [/PORT=8080] [/REMOTEACCESS=1] [/OPENMINECRAFTPORT=0]
; Silent uninstall: "C:\Program Files\Minecraft Server Manager\unins000.exe" /VERYSILENT

#ifndef AppVersion
  #define AppVersion "1.0.5"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

#define AppName "Minecraft Server Manager"
#define ServiceName "MinecraftServerManager"
#define ExeName "MinecraftServerManager.exe"
#define RegKey "Software\Minecraft Server Manager"
#define ProjectUrl "https://github.com/davegarner/minecraft-server-management"

[Setup]
AppId={{40CCD350-F3FF-47AD-A037-69959704C3A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/wiki
AppUpdatesURL={#ProjectUrl}/releases
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=MinecraftServerManager-{#AppVersion}-Setup
SetupIconFile=..\..\src\MinecraftServerManager\app.ico
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Messages]
FinishedLabel=Setup has installed [name] and started it as a Windows service.%n%nOpen the panel in your web browser to create your administrator account and install Minecraft.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM; Subkey: "{#RegKey}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "{#RegKey}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"
Root: HKLM; Subkey: "{#RegKey}"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKLM; Subkey: "{#RegKey}"; ValueType: string; ValueName: "PanelPort"; ValueData: "{code:GetPanelPort}"

[INI]
; A Start menu shortcut that opens the panel in the default browser.
Filename: "{autoprograms}\{#AppName}.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:{code:GetPanelPort}/"
Filename: "{autoprograms}\{#AppName}.url"; Section: "InternetShortcut"; Key: "IconFile"; String: "{app}\{#ExeName}"
Filename: "{autoprograms}\{#AppName}.url"; Section: "InternetShortcut"; Key: "IconIndex"; String: "0"

[Run]
Filename: "http://localhost:{code:GetPanelPort}/"; Description: "Open {#AppName} in your web browser"; Flags: postinstall shellexec nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\uninstall-service.ps1"" -RemoveFirewallRules"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[UninstallDelete]
Type: files; Name: "{autoprograms}\{#AppName}.url"

[Code]
var
  PortPage: TInputQueryWizardPage;
  NetworkPage: TInputOptionWizardPage;
  IsUpgrade: Boolean;
  ExistingPort: String;

function PowerShellExe(): String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function InitializeSetup(): Boolean;
begin
  IsUpgrade := RegQueryStringValue(HKLM64, '{#RegKey}', 'PanelPort', ExistingPort);
  if not IsUpgrade then
    ExistingPort := '';
  Result := True;
end;

procedure InitializeWizard();
begin
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Web panel', 'Which port should the web panel use?',
    'You''ll open the panel in a web browser at http://localhost:<port>. Keep the default unless another program already uses port 8080.');
  PortPage.Add('&Panel port:', False);
  PortPage.Values[0] := ExpandConstant('{param:PORT|8080}');

  NetworkPage := CreateInputOptionPage(PortPage.ID,
    'Network access', 'Who should be able to connect?',
    'Windows Firewall rules are added for the options you select. You can change these later.',
    False, False);
  NetworkPage.Add('Allow Minecraft players to connect (open TCP port 25565)');
  NetworkPage.Add('Allow other computers to open the web panel (listen on the network and open the panel port)');
  NetworkPage.Values[0] := ExpandConstant('{param:OPENMINECRAFTPORT|1}') <> '0';
  NetworkPage.Values[1] := ExpandConstant('{param:REMOTEACCESS|0}') = '1';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // An upgrade keeps the existing port and firewall choices.
  Result := IsUpgrade and ((PageID = PortPage.ID) or (PageID = NetworkPage.ID));
end;

function GetPanelPort(Param: String): String;
begin
  if IsUpgrade then
    Result := ExistingPort
  else
    Result := Trim(PortPage.Values[0]);
end;

function IsValidPort(Value: String): Boolean;
var
  Port: Integer;
begin
  Port := StrToIntDef(Trim(Value), 0);
  Result := (Port >= 1) and (Port <= 65535);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = PortPage.ID) and not IsValidPort(PortPage.Values[0]) then
  begin
    MsgBox('Enter a port number between 1 and 65535.', mbError, MB_OK);
    Result := False;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if IsUpgrade then
    Result := Result + 'Upgrade:' + NewLine + Space + 'The running service is stopped, updated and started again.' + NewLine + Space + 'Your settings, worlds and backups are kept.'
  else
  begin
    Result := Result + 'Web panel:' + NewLine + Space + 'http://localhost:' + GetPanelPort('') + NewLine;
    if NetworkPage.Values[1] then
      Result := Result + Space + 'Reachable from other computers (firewall rule added)' + NewLine;
    Result := Result + NewLine + 'Minecraft players:' + NewLine;
    if NetworkPage.Values[0] then
      Result := Result + Space + 'TCP port 25565 opened in Windows Firewall'
    else
      Result := Result + Space + 'No firewall rule added';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not IsUpgrade and not IsValidPort(PortPage.Values[0]) then
  begin
    Result := 'The panel port must be a number between 1 and 65535.';
    exit;
  end;
  // On an upgrade, stop the service (and so the Minecraft server, cleanly) so its files can be replaced.
  Exec(PowerShellExe(), '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Stop-Service -Name {#ServiceName} -ErrorAction SilentlyContinue; ' +
    'Get-Process -Name MinecraftServerManager -ErrorAction SilentlyContinue | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure ReportFailure(Step: String; ResultCode: Integer);
begin
  Log(Format('%s failed with exit code %d.', [Step, ResultCode]));
  SuppressibleMsgBox(Format('%s failed (exit code %d).'#13#10#13#10 +
    'The files were installed, but the service may not be running. See the "Troubleshooting" page in the wiki, ' +
    'or run install-service.ps1 from the installation folder in an administrator PowerShell window.', [Step, ResultCode]),
    mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Args: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;

  // 1. Machine-wide settings (panel address and data folder). Existing settings are kept on upgrade.
  Args := 'configure --keep-existing --port ' + GetPanelPort('');
  if (not IsUpgrade) and NetworkPage.Values[1] then
    Args := Args + ' --remote-access';
  if not Exec(ExpandConstant('{app}\{#ExeName}'), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    ReportFailure('Saving the panel settings', ResultCode);

  // 2. Register (or update) and start the Windows service, and add the chosen firewall rules.
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\install-service.ps1') + '"' +
    ' -PanelPort ' + GetPanelPort('') + ' -WaitForPanel';
  if not IsUpgrade then
  begin
    if NetworkPage.Values[0] then
      Args := Args + ' -OpenMinecraftPort';
    if NetworkPage.Values[1] then
      Args := Args + ' -OpenPanelPort';
  end;
  if not Exec(PowerShellExe(), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    ReportFailure('Setting up the Windows service', ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;
  DataDir := ExpandConstant('{commonappdata}\{#AppName}');
  if DirExists(DataDir) and (not UninstallSilent) and
     (MsgBox('Also delete the panel''s settings and sign-in details?' + #13#10#13#10 +
             'Your Minecraft worlds and backups are kept either way.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(DataDir, True, True, True);
end;
