; Installer für den Admin-PC: Admin-Panel, pro Benutzer, ohne Admin-Rechte.
; Die Firewall richtet das Panel selbst ein (Knopf „Firewall einrichten“).
; Bauen: ./build.ps1 -Task Installer

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif

[Setup]
AppId={{3F7A1D52-9B4C-4E86-A2D1-6C0E8B5F4A93}
AppName=MorniLAN Admin
AppVersion={#AppVersion}
AppVerName=MorniLAN Admin {#AppVersion}
AppPublisher=MoinMornhart
AppPublisherURL=https://github.com/MoinMornhart/MorniLAN
AppSupportURL=https://github.com/MoinMornhart/MorniLAN/issues
DefaultDirName={localappdata}\Programs\MorniLAN Admin
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=MorniLAN-Admin-Setup-{#AppVersion}
SetupIconFile=..\assets\mornilan.ico
UninstallDisplayIcon={app}\MorniLAN.Admin.exe
UninstallDisplayName=MorniLAN Admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Verknüpfung auf dem Desktop"; Flags: unchecked
Name: "autostart"; Description: "Mit Windows starten (läuft unsichtbar im Infobereich)"

[Files]
Source: "{#SourceDir}\MorniLAN.Admin\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\MorniLAN Admin"; Filename: "{app}\MorniLAN.Admin.exe"
Name: "{autodesktop}\MorniLAN Admin"; Filename: "{app}\MorniLAN.Admin.exe"; Tasks: desktopicon

[Registry]
; Gleicher Eintrag, den auch der Schalter „Mit Windows starten“ im Panel setzt.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MorniLAN Admin"; ValueData: """{app}\MorniLAN.Admin.exe"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MorniLAN.Admin.exe"; Description: "MorniLAN Admin starten"; Flags: postinstall nowait skipifsilent
; Nach einem stillen Update (aus dem Panel heraus) startet das Panel von selbst wieder
Filename: "{app}\MorniLAN.Admin.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM MorniLAN.Admin.exe /F"; Flags: runhidden; RunOnceId: "StopAdmin"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
