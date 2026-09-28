#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceRoot
  #define SourceRoot SourcePath + ".."
#endif
#ifndef PayloadRoot
  #define PayloadRoot SourceRoot + "\tools\installer-payload"
#endif
#ifdef TestMode
  #define AppName "ThermalScope Setup Test"
  #define AppId "{BC053406-02C1-493B-BB04-8BC59677D3DB}"
  #define OutputName "ThermalScope-Setup-Test"
  #define DesktopMutex "Local\ThermalScope.Desktop.18101"
#else
  #define AppName "ThermalScope"
  #define AppId "{12551032-21DC-4A95-91AD-9297F7261E19}"
  #define OutputName "ThermalScope-Setup-" + AppVersion + "-win-x64"
  #define DesktopMutex "Local\ThermalScope.Desktop.8088"
#endif

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ThermalScope contributors
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
LicenseFile={#SourceRoot}\LICENSE
InfoBeforeFile={#SourceRoot}\setup\Setup-info.txt
OutputDir={#SourceRoot}\dist
OutputBaseFilename={#OutputName}
SetupIconFile={#SourceRoot}\desktop\assets\thermal.ico
UninstallDisplayIcon={app}\app\desktop\ThermalScope.Desktop.exe
WizardStyle=modern dark polar includetitlebar
WizardSizePercent=110
Compression=lzma2
SolidCompression=yes
AppMutex={#DesktopMutex}
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
Uninstallable=yes

[Tasks]
Name: lan; Description: "Allow phone access on Private networks (local subnet, TCP 8088 only)"; GroupDescription: "Home network:"
Name: driver; Description: "Install the signed PawnIO sensor driver if missing (Internet required)"; GroupDescription: "Sensor access:"; Check: not DriverInstalled
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadRoot}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadRoot}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\setup\installed.flag"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\setup\Firewall.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "{#SourceRoot}\setup\Driver.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}\{#AppName}"; Filename: "{app}\app\desktop\ThermalScope.Desktop.exe"; WorkingDir: "{app}\app\desktop"
Name: "{autoprograms}\{#AppName}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\app\desktop\ThermalScope.Desktop.exe"; WorkingDir: "{app}\app\desktop"; Tasks: desktopicon

[Run]
Filename: "{app}\app\desktop\ThermalScope.Desktop.exe"; Description: "Launch {#AppName}"; WorkingDir: "{app}\app\desktop"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup\Firewall.ps1"" -Action Uninstall"; Flags: runhidden skipifdoesntexist; RunOnceId: RemoveOwnFirewallRule

[Code]
var
  TasksFailed, DriverRestart: Boolean;

function DriverInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

procedure RunSetupTask(const ScriptName, Arguments, TaskTitle: String);
var
  Code: Integer;
  Success: Boolean;
begin
  WizardForm.StatusLabel.Caption := TaskTitle;
  Success := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup\') + ScriptName + '" ' + Arguments,
    '', SW_SHOW, ewWaitUntilTerminated, Code);
  if Success and (Code = 3010) then DriverRestart := True
  else if (not Success) or (Code <> 0) then begin
    TasksFailed := True;
    Log(TaskTitle + ' failed. Exit code: ' + IntToStr(Code));
    if not WizardSilent then
      MsgBox(TaskTitle + ' could not be completed. The application is installed, but this optional task needs to be retried. See the setup scripts in the installation folder.', mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('lan') then RunSetupTask('Firewall.ps1', '-Action Install', 'Configuring Private-LAN access');
    if WizardIsTaskSelected('driver') and (not DriverInstalled) then RunSetupTask('Driver.ps1', '', 'Installing the signed sensor driver');
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart;
end;

function GetCustomSetupExitCode: Integer;
begin
  if TasksFailed then Result := 1 else Result := 0;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('Uninstall {#AppName}? Your recordings and sensor assignments will be kept in your user application-data folder. The shared PawnIO driver will not be removed.', mbConfirmation, MB_YESNO) = IDYES;
end;

[Messages]
WelcomeLabel1=Welcome to the {#AppName} Setup Wizard
WelcomeLabel2=Your rig. A little peace of mind.%n%nSetup will install [name/ver] on your computer. Recordings are stored separately in your user application-data folder and kept during upgrades and uninstall.%n%nClose ThermalScope before continuing so any recording is saved safely.
