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

PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

DisableDirPage=no
DisableProgramGroupPage=no
WizardStyle=modern

OutputDir=..\out
OutputBaseFilename=DAISY-Braille-Toolkit-Setup
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "danish"; MessagesFile: "compiler:Languages\Danish.isl"

[CustomMessages]
english.ShortcutsGroup=Shortcuts:
danish.ShortcutsGroup=Genveje:
english.DesktopShortcut=Create a desktop shortcut
danish.DesktopShortcut=Opret genvej på skrivebordet
english.StartMenuShortcut=Create a Start Menu shortcut
danish.StartMenuShortcut=Opret genvej i Startmenuen
english.StartApp=Start {#MyAppName}
danish.StartApp=Start {#MyAppName}

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"
Name: "startmenuicon"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:StartApp}"; Flags: nowait postinstall skipifsilent
