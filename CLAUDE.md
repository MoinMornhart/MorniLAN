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
- **M2 Verbindung: in Arbeit** auf `feature/m2-verbindung`.
- Testaufbau: Admin-Panel auf dem Haupt-PC des Nutzers. Als „Freundes-PC“ dient sein alter PC mit **Windows 11 Home** im WLAN. Bis der bereitsteht, laufen Agent und Admin zusammen auf dem Laptop.
- Danach kommen M3–M12 gemäß [docs/anforderungen.md](docs/anforderungen.md#meilensteine). Bei M12 nach der Reihenfolge der Extras fragen.

## Technik & Stolperfallen

- .NET 10 SDK, `global.json` mit `rollForward: latestFeature`. Lösung im neuen Format `MorniLAN.slnx`.
- Version **nur** in `Directory.Build.props` (`VersionPrefix`), Paketversionen nur in `Directory.Packages.props` (Central Package Management, also keine `Version=` in den csproj).
- Der Projektordner liegt in **iCloud Drive**. Build-Ausgaben gehen deshalb über `ArtifactsPath` nach `%LOCALAPPDATA%\MorniLAN\artifacts`, `build.ps1 -Task Publish` nach `%LOCALAPPDATA%\MorniLAN\publish`. Auf CI (`CI=true`) landet alles in `./artifacts`.
- Tests: **xunit.v3 4.x mit Microsoft Testing Platform** (Opt-in in `global.json` unter `test.runner`). Kein VSTest, keine `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`/`coverlet.collector`. Aufruf: `dotnet test --solution MorniLAN.slnx`.
- CI (`.github/workflows/ci.yml`) läuft auf Pushes nach `main`, `dev`, `feature/**`, `fix/**` und auf PRs (windows-latest). Weitere Branch-Präfixe dort ergänzen.
- Agent-Namespaces nie `System` nennen (überdeckt `System.*`), deshalb heißt der Ordner `Platform/`.
- `.ps1`-Dateien als **UTF-8 mit BOM** speichern, sonst werden Umlaute in Windows PowerShell 5.1 kaputt dargestellt.
- Datenschutz-Regeln: keine Zugangsdaten (Steam, Discord …) lesen oder speichern, persönliche Dateien des Freundes nicht anfassen, Fernzugriff im Launcher immer sichtbar anzeigen. Notfall-Entsperrung muss es immer geben, das Admin-Konto darf nie ausgesperrt werden.

## Befehle

```powershell
./tools/setup-dev.ps1        # Rechner einrichten (SDK, Git-Identität, Build + Tests)
./build.ps1 -Task Test       # Build + Tests
dotnet run --project src/MorniLAN.Admin
dotnet run --project src/MorniLAN.Launcher
dotnet run --project src/MorniLAN.Agent   # Konsole; Logs: %ProgramData%\MorniLAN\logs
```
