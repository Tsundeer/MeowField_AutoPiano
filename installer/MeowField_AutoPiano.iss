#ifndef MyAppVersion
  #define MyAppVersion "2.2.13"
#endif

#ifndef PublishDir
  #error PublishDir must be supplied by scripts\publish-win-x64.ps1.
#endif

#define MyAppName "MeowField_AutoPiano"
#define MyAppPublisher "薮猫"
#define MyAppURL "https://github.com/Tsundeer/MeowField_AutoPiano"
#define MyAppExeName "MeowField_AutoPiano.exe"
#define MyAppIcon "..\src\MeowField.App\Assets\MeowField_AutoPiano.ico"

[Setup]
AppId={{B13986BA-61E9-4D56-8A2B-3D2B01D17D33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=MeowField_AutoPiano-{#MyAppVersion}-win-x64-Setup
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
SetupLogging=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  { Never default to restarting Windows; the installer must not reboot the machine. }
  WizardForm.PreparingNoRadio.Checked := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpPreparing then begin
    { Force the non-restart choice even if the previous install left pending operations. }
    WizardForm.PreparingYesRadio.Checked := False;
    WizardForm.PreparingNoRadio.Checked := True;
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
