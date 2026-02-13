#define MyAppName "DAISY Braille Toolkit"
#define MyAppPublisher "TheDBSdanmark"
#define MyAppURL "https://github.com/thebonden/daisy-braille-toolkit"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef MyAppExeName
  #define MyAppExeName "DAISY-Braille-Toolkit.exe"
#endif

[Setup]
AppId={{8C5B1F6A-6A7F-4E9F-9E9C-1C1B3B8C8D2F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Per-user install (ingen admin/UAC)
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

DisableProgramGroupPage=yes
WizardStyle=modern

OutputDir=..\out
OutputBaseFilename=DAISY-Braille-Toolkit-Setup
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: "desktopicon"; Description: "Opret genvej på skrivebordet"; GroupDescription: "Genveje:"

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent
