#ifndef AppVersion
  #error AppVersion must be supplied by the release build.
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by the release build.
#endif
#ifndef RuntimeArchive
  #error RuntimeArchive must be supplied by the release build.
#endif
#ifndef ModelFile
  #error ModelFile must be supplied by the release build.
#endif

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
Name: "autostart"; Description: "Запускать Voice Input при входе в Windows"; GroupDescription: "Дополнительно:"; Flags: checkedonce
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimeArchive}"; DestDir: "{localappdata}\VoiceInput\downloads"; DestName: "transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz"; Flags: ignoreversion
Source: "{#ModelFile}"; DestDir: "{localappdata}\VoiceInput\models"; DestName: "gigaam-v3-e2e-rnnt-Q4_K_M.gguf"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Voice Input"; Filename: "{app}\VoiceInput.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0
Name: "{group}\Удалить Voice Input"; Filename: "{uninstallexe}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0
Name: "{autodesktop}\Voice Input"; Filename: "{app}\VoiceInput.App.exe"; WorkingDir: "{app}"; IconFilename: "{app}\VoiceInput.App.exe"; IconIndex: 0; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Voice Input"; ValueData: """{app}\VoiceInput.App.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\VoiceInput.App.exe"; Description: "Запустить Voice Input"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\VoiceInput"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('autostart')) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Voice Input');
end;
