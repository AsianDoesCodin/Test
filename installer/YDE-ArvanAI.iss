#ifndef SourceDir
  #define SourceDir "..\\dist\\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\\dist\\installer"
#endif

[Setup]
AppId={{C9CB6FA5-7668-4C56-9B6B-6A1E1B54A7A1}
AppName=Yellow's Dialog Editor - Arvan AI
AppVersion=0.1.0
AppPublisher=Yellow768 / Arvan extension build
AppPublisherURL=https://github.com/Yellow768/Yellows-Dialog-Editor
DefaultDirName={autopf}\YDE Arvan AI
DefaultGroupName=YDE Arvan AI
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=YDE-ArvanAI-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\YellowsDialogEditor-ArvanAI.exe
SetupLogging=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\YDE Arvan AI"; Filename: "{app}\YellowsDialogEditor-ArvanAI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\YDE Arvan AI"; Filename: "{app}\YellowsDialogEditor-ArvanAI.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\YellowsDialogEditor-ArvanAI.exe"; Description: "Launch YDE Arvan AI"; Flags: nowait postinstall skipifsilent
