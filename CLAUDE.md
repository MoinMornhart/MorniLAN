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
| Auto-Update | Velopack über GitHub Releases (öffentlich → kein Token), noch umzusetzen in M9 |
| Kommunikation (M2) | ASP.NET Core + SignalR über TLS, **Kestrel im Admin-Panel** (47950/TCP), der Agent verbindet sich ausgehend. LAN-Erkennung: Admin-Panel sendet UDP-Broadcast (47951/UDP), der Agent lauscht. Tailscale: Adresse (IP/MagicDNS) manuell eintragen, keine Tailscale-API. Pairing-Code erscheint im Agent-Log (später auch im Launcher) und wird im Admin-Panel eingegeben, danach gegenseitiges Zertifikat-Pinning. Heartbeat alle 15 s, offline nach 45 s. |

## Stand

- **M1 Grundgerüst: fertig und abgenommen** (2026-10-05), nach `main` gemergt, CI grün (42 Tests).
- **M2 Verbindung: fertig**, auf `dev` gemergt, CI grün (108 Tests inkl. Ende-zu-Ende über TLS). Auf dem Laptop getestet (Agent + Admin auf demselben Rechner, Suche per Broadcast, Pairing über die echte UI). **Abnahme steht noch aus**: Test über zwei PCs im LAN, danach Tailscale. Details: [docs/verbindung.md](docs/verbindung.md).
- Testaufbau (Stand 2026-10-05, kann sich noch ändern): **Test-PC = „Freundes-PC“ ist der Rechner „MORNI“** (Windows 11 Pro, WLAN „MorniGaming“, 192.168.178.x), dort läuft der veröffentlichte Agent (`%LOCALAPPDATA%\MorniLAN\publish\MorniLAN.Agent`). Das **Admin-Panel läuft auf dem Haupt-PC** (LAN-Kabel, gleicher Router). Ein PC mit Windows 11 Home ist noch nicht getestet.
- Noch offen aus M2, bewusst verschoben: Pairing-Code im Launcher (braucht die Named Pipe, kommt mit M4/M5), Agent als echter Dienst (Installer in M10), mDNS (UDP-Broadcast reicht).
- Danach kommen M3–M12 gemäß [docs/anforderungen.md](docs/anforderungen.md#meilensteine). Bei M12 nach der Reihenfolge der Extras fragen.

## Technik & Stolperfallen

- .NET 10 SDK, `global.json` mit `rollForward: latestFeature`. Lösung im neuen Format `MorniLAN.slnx`.
- Version **nur** in `Directory.Build.props` (`VersionPrefix`), Paketversionen nur in `Directory.Packages.props` (Central Package Management, also keine `Version=` in den csproj).
- Der Projektordner liegt in **iCloud Drive**. Build-Ausgaben gehen deshalb über `ArtifactsPath` nach `%LOCALAPPDATA%\MorniLAN\artifacts`, `build.ps1 -Task Publish` nach `%LOCALAPPDATA%\MorniLAN\publish`. Auf CI (`CI=true`) landet alles in `./artifacts`.
- **iCloud und Git vertragen sich nicht**, wenn zwei PCs denselben Ordner synchronisieren. Am 2026-10-05 hat iCloud Konfliktkopien angelegt (`CLAUDE 2.md`, `Directory.Packages(1).props`) und sogar Git-Interna umbenannt (`.git/HEAD 2`, `index 2`), danach war das Repo kaputt. Der alte Ordner liegt als `MorniLAN_icloud_alt`, es wurde frisch geklont. **GitHub ist die Quelle.** Vor der Arbeit immer `git status` prüfen; bei Konfliktkopien nicht weiterarbeiten, sondern neu klonen.
- Tests: **xunit.v3 4.x mit Microsoft Testing Platform** (Opt-in in `global.json` unter `test.runner`). Kein VSTest, keine `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet.collector`. Aufruf: `dotnet test --solution MorniLAN.slnx`, und zwar **im Repo-Ordner** (sonst wird `global.json` nicht gefunden → „MSB1001: Unbekannter Schalter --solution“). `build.ps1` macht das selbst.
- Verbindung (M2): Admin-Panel hostet Kestrel per `FrameworkReference Microsoft.AspNetCore.App` in der Avalonia-App (`Server/AdminServer.cs`), Tests referenzieren das Admin-Projekt. Zertifikate **nicht** mit `EphemeralKeySet` laden, SChannel braucht einen gespeicherten Schlüssel. Identitäten sind per DPAPI (CurrentUser) ans Konto gebunden: Wechselt der Agent zwischen Konsole (Benutzer) und Dienst (SYSTEM), entsteht ein neues Zertifikat → neues Pairing nötig.
- Der SignalR-Client nutzt nur WebSockets ohne Negotiate (`SkipNegotiation`), deshalb werden Client-Zertifikat und Prüf-Callback in `WebSocketConfiguration` gesetzt.
- Firewall: `tools/firewall.ps1 -Role Admin|Agent`. Windows fragt beim ersten Lauschen nach. „Abbrechen“ erzeugt Blockier-Regeln, die das Skript entfernt.
- CI (`.github/workflows/ci.yml`) läuft auf Pushes nach `main`, `dev`, `feature/**`, `fix/**` und auf PRs (windows-latest). Weitere Branch-Präfixe dort ergänzen.
- Agent-Namespaces nie `System` nennen (überdeckt `System.*`), deshalb heißt der Ordner `Platform/`.
- `.ps1`-Dateien als **UTF-8 mit BOM** speichern, sonst werden Umlaute in Windows PowerShell 5.1 kaputt dargestellt.
- Datenschutz-Regeln: keine Zugangsdaten (Steam, Discord …) lesen oder speichern, persönliche Dateien des Freundes nicht anfassen, Fernzugriff im Launcher immer sichtbar anzeigen. Notfall-Entsperrung muss es immer geben, das Admin-Konto darf nie ausgesperrt werden.

## Befehle

```powershell
./tools/setup-dev.ps1        # Rechner einrichten (SDK, Git-Identität, Build + Tests)
./build.ps1 -Task Test       # Build + Tests
./build.ps1 -Task Publish    # self-contained Exe-Dateien (für den Test-PC), inkl. firewall.ps1
./tools/firewall.ps1 -Role Admin   # bzw. -Role Agent, als Administrator
dotnet run --project src/MorniLAN.Admin
dotnet run --project src/MorniLAN.Launcher
dotnet run --project src/MorniLAN.Agent   # Konsole; Logs: %ProgramData%\MorniLAN\logs
```
