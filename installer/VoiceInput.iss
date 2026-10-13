#ifndef AppVersion
  #error AppVersion must be supplied by the release build.
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by the release build.
#endif
; RuntimeArchive and ModelFile are only supplied for the optional offline build with the local engine.

[Setup]
AppId={{D87D0CDE-BDE2-4C9E-B5EC-C69F389FE6FD}
AppName=Voice Input
AppVersion={#AppVersion}
AppVerName=Voice Input {#AppVersion}
AppPublisher=Pavel Logachev
AppPublisherURL=https://github.com/pavel-logachev/voice-input
AppSupportURL=https://github.com/pavel-logachev/voice-input/issues
AppUpdatesURL=https://github.com/pavel-logachev/voice-input/releases
DefaultDirName={localappdata}\Programs\Voice Input
DefaultGroupName=Voice Input
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern dynamic
SetupIconFile=..\assets\VoiceInput.ico
UninstallDisplayIcon={app}\VoiceInput.App.exe
LicenseFile=..\LICENSE
InfoBeforeFile=README-RU.txt
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
OutputBaseFilename=VoiceInput-Setup-{#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Pavel Logachev
VersionInfoDescription=Voice Input installer
VersionInfoProductName=Voice Input
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать Voice Input при входе в Windows"; GroupDescription: "Дополнительно:"
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef RuntimeArchive
Source: "{#RuntimeArchive}"; DestDir: "{localappdata}\VoiceInput\downloads"; DestName: "transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz"; Flags: ignoreversion
#endif
#ifdef ModelFile
Source: "{#ModelFile}"; DestDir: "{localappdata}\VoiceInput\models"; DestName: "gigaam-v3-e2e-rnnt-Q4_K_M.gguf"; Flags: ignoreversion
#endif
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Voice Input"; Filename: "{app}\VoiceInput.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0
Name: "{group}\Удалить Voice Input"; Filename: "{uninstallexe}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0
Name: "{autodesktop}\Voice Input"; Filename: "{app}\VoiceInput.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0; Tasks: desktopicon

[Registry]
; The value itself is written by the code below; this entry only makes the uninstaller remove it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Voice Input"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\VoiceInput.App.exe"; Description: "Запустить Voice Input"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Removes settings and the encrypted API key together with the program.
Type: filesandordirs; Name: "{localappdata}\VoiceInput"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'Voice Input';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D87D0CDE-BDE2-4C9E-B5EC-C69F389FE6FD}_is1';

var
  Updating: Boolean;
  AutostartWasOn: Boolean;
  TasksPrepared: Boolean;

// Autostart is also switched in the application's settings, so on an update the registry is the truth:
// an update must never turn autostart off (or on) by itself.
function InitializeSetup(): Boolean;
begin
  Updating := RegKeyExists(HKCU, UninstallKey);
  AutostartWasOn := RegValueExists(HKCU, RunKey, RunValue);
  Result := True;
end;

// On an update the checkbox starts from what the user has now.
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and Updating and (not TasksPrepared) then
  begin
    TasksPrepared := True;
    if AutostartWasOn then
      WizardSelectTasks('autostart')
    else
      WizardSelectTasks('!autostart');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Keep: Boolean;
begin
  if CurStep <> ssPostInstall then
    exit;

  // A silent update shows no checkbox, so it leaves autostart exactly as it was.
  if Updating and WizardSilent then
    Keep := AutostartWasOn
  else
    Keep := WizardIsTaskSelected('autostart');

  if Keep then
    RegWriteStringValue(HKCU, RunKey, RunValue, '"' + ExpandConstant('{app}\VoiceInput.App.exe') + '"')
  else
    RegDeleteValue(HKCU, RunKey, RunValue);
end;
