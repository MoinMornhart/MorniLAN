# CLAUDE.md – MorniLAN

Kontext für Claude Code. Die vollständige Anforderung steht in [docs/anforderungen.md](docs/anforderungen.md); dort nachlesen, bevor ein Meilenstein geplant wird.

## Arbeitsweise (vom Nutzer vorgegeben)

- Sprache mit dem Nutzer: **Deutsch**.
- In kleinen, testbaren **Meilensteinen** arbeiten. Vorher Rückfragen stellen, wenn etwas unklar ist.
- Nach jedem Meilenstein zeigen, was fertig ist, plus eine **kurze Test-Checkliste**. Erst nach Freigabe durch den Nutzer weitermachen.
- Pro Meilenstein ein Branch `feature/mN-<name>` von `dev`, danach `--no-ff` nach `dev` mergen. `main` = stabil, wird erst nach Abnahme durch den Nutzer aus `dev` gemergt.
- Commits nach **Conventional Commits** (`feat:`, `fix:`, `chore:`, `ci:`, `docs:` …), deutsch formuliert.
- Unit-Tests für Logik (Steam-Parsing, Freigabe-Regeln, Pairing …). Tests zuerst in einer Hyper-V-VM mit Windows 11, nie direkt auf dem echten Freundes-PC.
- Git-Identität im Repo: `MoinMornhart` / `297179352+MoinMornhart@users.noreply.github.com` (noreply, öffentliches Repo). `tools/setup-dev.ps1` setzt das.

## Getroffene Entscheidungen

| Thema | Entscheidung |
|---|---|
| Windows-Edition des Freundes-PCs | Home **und** Pro unterstützen, Erkennung zur Laufzeit (`WindowsEditionInfo`) |
| Sperren | Home/Pro: App Control (WDAC), Enterprise/Education: AppLocker, **immer** zusätzlich ein Prozess-Wächter |
| UI | **Avalonia 12** (Fluent, Dark Mode). Nutzer: „egal, was besser ist“ |
| Fernzugriff | **Sunshine + Moonlight** als Standard (RDP würde den Freund abmelden) |
| GitHub | **öffentlich**: https://github.com/MoinMornhart/MorniLAN |
| Lizenz | MIT |
| Proxmox-Quickstart | nein |
| Verteilung (Wunsch 2026-10-05) | **Zwei Apps**: „MorniLAN Admin“ (Admin-PC) und „MorniLAN für Geräte“ (Agent + Launcher in einem Paket). Einrichtung muss **einfach** sein: Download, Doppelklick, Assistent, keine Konsole. Umsetzung in M9/M10, siehe [docs/anforderungen.md](docs/anforderungen.md#verteilung--einrichtung-wunsch-des-nutzers-2026-10-05). |
| Auto-Update | **Eigener Updater über die Inno-Setups** statt Velopack (Nutzer 2026-10-05, aus M9 vorgezogen): Apps lesen GitHub-Releases, laden das passende Setup, prüfen die SHA-256 aus dem `digest`-Feld (Pflicht) und installieren still. Geräte: **automatisch (nie während eines Steam-Spiels) und per Knopf im Panel**. **Betas werden immer angeboten.** Rollback fehlt noch (M9). |
| Kommunikation (M2) | ASP.NET Core + SignalR über TLS, **Kestrel im Admin-Panel** (47950/TCP), der Agent verbindet sich ausgehend. LAN-Erkennung: Admin-Panel sendet UDP-Broadcast (47951/UDP), der Agent lauscht. Tailscale: Adresse (IP/MagicDNS) manuell eintragen, keine Tailscale-API. Pairing-Code erscheint im Agent-Log (später auch im Launcher) und wird im Admin-Panel eingegeben, danach gegenseitiges Zertifikat-Pinning. Heartbeat alle 15 s, offline nach 45 s. |

## Stand

- **M1 Grundgerüst: fertig und abgenommen** (2026-10-05), nach `main` gemergt, CI grün (42 Tests).
- **M2 Verbindung: fertig und abgenommen** (2026-10-05), nach `main` gemergt, CI grün (113 Tests inkl. Ende-zu-Ende über TLS und LAN-Suche). **Am 2026-10-05 mit zwei echten PCs im LAN getestet, alles grün**: Pairing (auch falscher Code), Online/CPU/RAM/Laufwerk, Agent beenden/neu starten, WLAN trennen, Panel neu starten, Entkoppeln → automatische Neu-Anfrage ohne feste Adresse. **Noch nicht getestet: Tailscale** (auf dem Test-PC nicht installiert). Der Nutzer hat M2 trotzdem freigegeben, der Tailscale-Test wird nachgeholt.
- **M2.5 Einrichtung ohne Konsole: fertig und abgenommen** (2026-10-05), nach `main` gemergt, Release **v0.2.0** (vorher Betas 1–3; beta.1 scheiterte am Build-Skript, beta.2 hatte den Pipe-Rechtefehler). Vom Nutzer vor M3 gezogen: „alles in der App, nichts mehr mit Befehlszeile“. Auf MORNI per Doppelklick installiert, Dienst läuft, Launcher zeigte den Code, gekoppelt. Noch nicht ausdrücklich getestet: Neustart von MORNI, Admin-Setup auf dem Haupt-PC, Autostart/Infobereich. Entschieden: Download über **GitHub-Release**, Geräte-PC mit **klassischem Inno-Setup-Installer**, im Panel **Firewall-Knopf, Adressen zum Abschreiben, Diagnose, Autostart + Infobereich**. Anleitung: [docs/einrichtung.md](docs/einrichtung.md). Vorabversionen (Tag mit `-beta.N`) zeigt GitHub nicht als „neueste Version“, dem Nutzer immer den direkten Link geben.
- **M3 Programme & Steam erkennen: M3.1 fertig (Version 0.3.0, noch nicht abgenommen).** Rückfragen geklärt: **alles** erkennen (Programme aus Registry + Startmenü, Microsoft-Store-Apps, Steam, dazu Epic, EA, Ubisoft, GOG, Battle.net), **Steam-Cover + Programm-Icons**, Systemkomponenten ausblenden mit Schalter. M3.1 (Programme, Store, Steam, Bilder, Seite „Freigaben“) ist gebaut und mit der echten Liste von MORNI im Panel getestet (Live-Tests `-explicit only -method "*Live*"`). **M3.2 (Epic, EA, Ubisoft, GOG, Battle.net) kommt erst, wenn der Nutzer es sagt.**
- **Auto-Update: gebaut (in 0.3.0).** Der Nutzer hat es vor M3.2 gewünscht. Admin-Selbst-Update per Beta-Release getestet (siehe Verlauf); Geräte-Update nur per Unit-Tests, weil die Installation auf MORNI eine UAC-Bestätigung braucht.
- Werkstatt-Seite für den Nutzer (Live-Stand, Verlauf, Screenshots): https://claude.ai/artifact/8KnK2f1ihar6JpV3QGNM36 – bei jedem Schritt aktualisieren (Datenbank `site/status`, `log`, `milestones`).
- Testaufbau (Stand 2026-10-05, kann sich noch ändern): **Test-PC = „Freundes-PC“ ist der Rechner „MORNI“** (Windows 11 Pro, WLAN „MorniGaming“, 192.168.178.109). Dort ist das **Geräte-Setup v0.2.0-beta.3 installiert** (Dienst „MorniLAN Agent“ als LocalSystem, gekoppelt mit MOINMORNHART); diese Version hat noch keinen Updater, 0.3.0 muss einmal von Hand (mit UAC) installiert werden. Auf MORNI läuft zum Testen außerdem ein Admin-Panel (eigene Daten in `%LOCALAPPDATA%\MorniLAN\admin`). **Haupt-PC „MOINMORNHART“** (LAN-Kabel, 192.168.178.22, Repo in `%USERPROFILE%\Dev\MorniLAN`, eigene Claude-Sitzung) betreibt das Panel. Dazwischen hängt ein **Repeater im NAT-Modus (192.168.178.2)**, der Absenderadressen umschreibt. Windows 11 Home ist noch nicht getestet.
- Kleine offene Verbesserungen aus dem Test: Adressen aus dem eigenen Subnetz zuerst probieren (nach dem Entkoppeln dauerte es 12 s, weil erst .2 und 10.99.x ins Zeitlimit liefen). Dem Agent ein Log-Ziel geben, das er auch bei fremden Dateirechten beschreiben kann.
- **Offene Wünsche des Nutzers (erst angehen, wenn er es sagt):** M3.2 (Epic, EA, Ubisoft, GOG, Battle.net erkennen); **README verschönern** mit Screenshots/Bildern, Icon und einer schönen, verständlichen Erklärung (Wunsch 2026-10-05); **Start-Erlebnis wie eine Konsole** (Wunsch 2026-10-05): PC einschalten → MorniLAN geht sofort auf (vor bzw. statt des normalen Desktops), dort Profil wählen oder neu anlegen, Steam & Co. verknüpfen, danach ist die MorniLAN-App praktisch der Desktop. Passt zu Launcher als Shell-Ersatz und Einrichtungsassistent (M10); „Verknüpfen“ heißt: im echten Steam-Client anmelden lassen, MorniLAN liest oder speichert keine Zugangsdaten.
- Bewusst verschoben: mDNS (UDP-Broadcast + Suchanfrage reichen), Code-Signatur der Setups (SmartScreen-Warnung), Einrichtungsassistent für Benutzerkonto/Shell (M10).
- Danach kommen M3–M12 gemäß [docs/anforderungen.md](docs/anforderungen.md#meilensteine). Bei M12 nach der Reihenfolge der Extras fragen.

## Technik & Stolperfallen

- .NET 10 SDK, `global.json` mit `rollForward: latestFeature`. Lösung im neuen Format `MorniLAN.slnx`.
- Version **nur** in `Directory.Build.props` (`VersionPrefix`), Paketversionen nur in `Directory.Packages.props` (Central Package Management, also keine `Version=` in den csproj).
- **Arbeitskopie: `%USERPROFILE%\Dev\MorniLAN`** auf beiden PCs, **nicht** im iCloud-Ordner. Build-Ausgaben gehen über `ArtifactsPath` nach `%LOCALAPPDATA%\MorniLAN\artifacts`, `build.ps1 -Task Publish` nach `%LOCALAPPDATA%\MorniLAN\publish`. Auf CI (`CI=true`) landet alles in `./artifacts`.
- **iCloud und Git vertragen sich nicht**, wenn zwei PCs denselben Ordner synchronisieren. Am 2026-10-05 hat iCloud zweimal Git-Interna umbenannt (`.git/HEAD 2`, `index 2`) und Konfliktkopien angelegt (`CLAUDE 2.md`), danach war das Repo kaputt. Die alten Ordner `…\iCloudDrive\Morni Archiv\Projekte\MorniLAN` und `MorniLAN_icloud_alt` bleiben liegen, werden aber nicht mehr benutzt. **GitHub ist die Quelle.** Vor der Arbeit immer `git status` prüfen.
- Tests: **xunit.v3 4.x mit Microsoft Testing Platform** (Opt-in in `global.json` unter `test.runner`). Kein VSTest, keine `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet.collector`. Aufruf: `dotnet test --solution MorniLAN.slnx`, und zwar **im Repo-Ordner** (sonst wird `global.json` nicht gefunden → „MSB1001: Unbekannter Schalter --solution“). `build.ps1` macht das selbst.
- Verbindung (M2): Admin-Panel hostet Kestrel per `FrameworkReference Microsoft.AspNetCore.App` in der Avalonia-App (`Server/AdminServer.cs`), Tests referenzieren das Admin-Projekt. Zertifikate **nicht** mit `EphemeralKeySet` laden, SChannel braucht einen gespeicherten Schlüssel. Identitäten sind per DPAPI (CurrentUser) ans Konto gebunden: Wechselt der Agent zwischen Konsole (Benutzer) und Dienst (SYSTEM), entsteht ein neues Zertifikat → neues Pairing nötig.
- Der SignalR-Client nutzt nur WebSockets ohne Negotiate (`SkipNegotiation`), deshalb werden Client-Zertifikat und Prüf-Callback in `WebSocketConfiguration` gesetzt.
- Firewall: `tools/firewall.ps1 -Role Admin|Agent`. Windows fragt beim ersten Lauschen nach. „Abbrechen“ erzeugt Blockier-Regeln, die das Skript entfernt. Regeln gelten für jedes Profil (die Netze des Nutzers sind „Öffentlich“), aber nur für `LocalSubnet`.
- LAN-Suche: Broadcasts vom Kabel-LAN kommen beim Nutzer **nicht** im WLAN an (Router). Deshalb fragt der Agent selbst per Broadcast (WLAN → LAN geht), und das Panel antwortet per Unicast. UDP-Sockets mit `SIO_UDP_CONNRESET` aus, sonst bricht das Empfangen nach ICMP „Port nicht erreichbar“ ab (`NetworkInfo.OpenDiscoverySocket`).
- Agent läuft nur einmal pro PC (Mutex `Global\MorniLAN.Agent`). Läuft noch ein Konsolen-Agent, beendet sich der Dienst sofort wieder: Vor dem Installieren alte Konsolen-Agents schließen.
- Einrichtung (M2.5): Installer in `installer/*.iss` (Inno Setup 6, UTF-8 **mit BOM**, sonst kaputte Umlaute). Geräte-Setup: Dienst per `sc` (Pfad in Anführungszeichen), Firewall per `netsh` an die Agent-Exe gebunden, Admin-Adresse in `%ProgramData%\MorniLAN\data\agent-settings.json` (nur SYSTEM/Admins schreiben, sonst könnte der Freund den Agent umlenken). Admin-Setup pro Benutzer ohne Admin-Rechte; die Firewall richtet das Panel selbst ein (`MorniLAN.Admin.exe --setup-firewall` per UAC). Release: Tag `v<VersionPrefix>[-suffix]` → `.github/workflows/release.yml` (installiert Inno per choco, `gh release create`).
- Launcher ↔ Agent: Named Pipe `MorniLAN.Launcher`, nur lesend (`status` → JSON `AgentLocalStatus`). Authentifizierte Benutzer dürfen lesen/schreiben, aber keine Pipe-Instanz anlegen.
- Admin-Panel: Einzelinstanz (Mutex `Local\MorniLAN.Admin`, zweiter Start signalisiert `Local\MorniLAN.Admin.Show`), Autostart `HKCU\…\Run` mit `--minimized`, Einstellungen in `%LOCALAPPDATA%\MorniLAN\admin\settings.json`.
- PowerShell-Variablen sind unabhängig von Groß-/Kleinschreibung: Parameter und lokale Variable nie nur durch die Schreibung unterscheiden. `sed` mit Windows-Pfaden vermeiden (`\a` wird zum Steuerzeichen).
- CI (`.github/workflows/ci.yml`) läuft auf Pushes nach `main`, `dev`, `feature/**`, `fix/**` und auf PRs (windows-latest). Weitere Branch-Präfixe dort ergänzen.
- Agent-Namespaces nie `System` nennen (überdeckt `System.*`), deshalb heißt der Ordner `Platform/`.
- `.ps1`-Dateien als **UTF-8 mit BOM** speichern, sonst werden Umlaute in Windows PowerShell 5.1 kaputt dargestellt.
- Datenschutz-Regeln: keine Zugangsdaten (Steam, Discord …) lesen oder speichern, persönliche Dateien des Freundes nicht anfassen, Fernzugriff im Launcher immer sichtbar anzeigen. Notfall-Entsperrung muss es immer geben, das Admin-Konto darf nie ausgesperrt werden.

## Befehle

```powershell
./tools/setup-dev.ps1        # Rechner einrichten (SDK, Git-Identität, Build + Tests)
./build.ps1 -Task Test       # Build + Tests
./build.ps1 -Task Publish    # self-contained Exe-Dateien
./build.ps1 -Task Installer  # beide Setups (Inno Setup 6), -OutDir für anderen Ordner
git tag v0.2.0-beta.1; git push origin v0.2.0-beta.1   # GitHub baut und veröffentlicht die Setups
./tools/firewall.ps1 -Role Admin   # nur noch für Entwicklung, die Apps richten die Firewall selbst ein
dotnet run --project src/MorniLAN.Admin
dotnet run --project src/MorniLAN.Launcher
dotnet run --project src/MorniLAN.Agent   # Konsole; Logs: %ProgramData%\MorniLAN\logs
```
