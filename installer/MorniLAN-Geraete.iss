; Installer für den verwalteten PC: Agent (Windows-Dienst) + Launcher.
; Bauen: ./build.ps1 -Task Installer (übergibt AppVersion, SourceDir, OutputDir)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif

#define ServiceName "MorniLAN.Agent"
#define FirewallRule "MorniLAN Agent Suche (LAN)"

[Setup]
AppId={{8C2F6E4B-6D2B-4E7A-9F3A-1B5D7C9E2A41}
AppName=MorniLAN für Geräte
AppVersion={#AppVersion}
AppVerName=MorniLAN für Geräte {#AppVersion}
AppPublisher=MoinMornhart
AppPublisherURL=https://github.com/MoinMornhart/MorniLAN
AppSupportURL=https://github.com/MoinMornhart/MorniLAN/issues
DefaultDirName={autopf}\MorniLAN
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=MorniLAN-Geraete-Setup-{#AppVersion}
SetupIconFile=..\assets\mornilan.ico
UninstallDisplayIcon={app}\Launcher\MorniLAN.Launcher.exe
UninstallDisplayName=MorniLAN für Geräte
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Ein offener Launcher wird fürs Update geschlossen und danach wieder geöffnet (er meldet sich dafür an)
RestartApplications=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#SourceDir}\MorniLAN.Agent\MorniLAN.Agent.exe"; DestDir: "{app}\Agent"; Flags: ignoreversion
Source: "{#SourceDir}\MorniLAN.Agent\appsettings.json"; DestDir: "{app}\Agent"; Flags: ignoreversion
Source: "{#SourceDir}\MorniLAN.Launcher\*"; Excludes: "*.pdb"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\MorniLAN"; Filename: "{app}\Launcher\MorniLAN.Launcher.exe"; Comment: "Status und Pairing-Code"

[Registry]
; Launcher bei jeder Anmeldung starten. In Administratorkonten beendet er sich mit --autostart sofort wieder,
; dort bleibt der normale Desktop.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MorniLAN Launcher"; ValueData: """{app}\Launcher\MorniLAN.Launcher.exe"" --autostart"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Launcher\MorniLAN.Launcher.exe"; Description: "MorniLAN öffnen (zeigt den Pairing-Code)"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  AdminPage: TInputQueryWizardPage;

function Run(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Result := Code;
end;

function Sc(const Params: String): Integer;
begin
  Result := Run(ExpandConstant('{sys}\sc.exe'), Params);
end;

function Netsh(const Params: String): Integer;
begin
  Result := Run(ExpandConstant('{sys}\netsh.exe'), Params);
end;

procedure StopService;
begin
  // net stop wartet, bis der Dienst wirklich steht (sc stop nicht).
  Run(ExpandConstant('{sys}\net.exe'), 'stop "{#ServiceName}"');
end;

function SettingsFile: String;
begin
  Result := ExpandConstant('{commonappdata}\MorniLAN\data\agent-settings.json');
end;

function IsValidHost(const Value: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := True;
  for I := 1 to Length(Value) do
  begin
    C := Value[I];
    if not (((C >= 'a') and (C <= 'z')) or ((C >= 'A') and (C <= 'Z')) or ((C >= '0') and (C <= '9'))
            or (C = '.') or (C = '-') or (C = ':')) then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure InitializeWizard;
var
  Previous: String;
begin
  AdminPage := CreateInputQueryPage(wpSelectDir,
    'Admin-PC',
    'Wie findet dieser PC das Admin-Panel?',
    'Normalerweise findet MorniLAN das Admin-Panel im Heimnetz von selbst. Lass das Feld dann einfach leer.' + #13#10#13#10 +
    'Nur wenn das nicht klappt, z. B. über Tailscale oder in einem anderen Netz: Trag hier die Adresse des Admin-PCs ein. ' +
    'Sie steht im Admin-Panel unter „Neuen PC hinzufügen“.');
  AdminPage.Add('Adresse des Admin-PCs (optional):', False);
  if RegQueryStringValue(HKLM, 'Software\MorniLAN', 'AdminHost', Previous) then
    AdminPage.Values[0] := Previous;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = AdminPage.ID) and not IsValidHost(Trim(AdminPage.Values[0])) then
  begin
    MsgBox('Die Adresse darf nur Buchstaben, Ziffern, Punkte, Bindestriche und Doppelpunkte enthalten, z. B. 192.168.178.22 oder admin-pc.tail1234.ts.net.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Dienst anhalten, damit die Exe ersetzt werden kann (Update).
  StopService;
  Result := '';
end;

procedure WriteSettings;
var
  Host: String;
begin
  Host := Trim(AdminPage.Values[0]);
  ForceDirectories(ExtractFileDir(SettingsFile));
  SaveStringToFile(SettingsFile,
    '{ "MorniLAN": { "Connection": { "AdminHost": "' + Host + '" } } }', False);
  RegWriteStringValue(HKLM, 'Software\MorniLAN', 'AdminHost', Host);
end;

procedure InstallService;
var
  Exe, BinPath: String;
begin
  Exe := ExpandConstant('{app}\Agent\MorniLAN.Agent.exe');
  // Pfad in Anführungszeichen, sonst wäre der Dienst über "C:\Program.exe" angreifbar.
  BinPath := 'binPath= "\"' + Exe + '\""';
  if Sc('query "{#ServiceName}"') = 0 then
    Sc('config "{#ServiceName}" ' + BinPath + ' start= delayed-auto')
  else
    Sc('create "{#ServiceName}" ' + BinPath + ' start= delayed-auto DisplayName= "MorniLAN Agent"');
  Sc('description "{#ServiceName}" "Verbindet diesen PC mit dem MorniLAN Admin-Panel."');
  // Absturz: nach 5 s, 10 s, 30 s neu starten.
  Sc('failure "{#ServiceName}" reset= 86400 actions= restart/5000/restart/10000/restart/30000');

  Netsh('advfirewall firewall delete rule name="{#FirewallRule}"');
  Netsh('advfirewall firewall add rule name="{#FirewallRule}" dir=in action=allow protocol=UDP localport=47951 ' +
        'remoteip=localsubnet profile=any program="' + Exe + '" description="MorniLAN: Admin-Panel im Heimnetz finden"');

  Sc('start "{#ServiceName}"');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteSettings;
    InstallService;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopService;
    Sc('delete "{#ServiceName}"');
    Netsh('advfirewall firewall delete rule name="{#FirewallRule}"');
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\MorniLAN');
  end;
end;
