# MorniLAN

[![CI](https://github.com/MoinMornhart/MorniLAN/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/MoinMornhart/MorniLAN/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

MorniLAN verwaltet einen Windows-PC, den ein Freund benutzt. Der Freund hat ein eigenes Standardkonto und sieht statt des Desktops einen **Launcher** mit genau den Apps und Spielen, die der Admin freigegeben hat. Der Admin steuert alles bequem vom eigenen PC aus: Freigaben, Status, Fernzugriff und Updates. Das funktioniert im selben LAN oder über **Tailscale**, ohne Portfreigaben.

> **Status:** frühe Entwicklung (Meilenstein 1 – Grundgerüst). Screenshots folgen, sobald Launcher und Admin-Panel Inhalte zeigen.

## Komponenten

| Projekt | Läuft auf | Aufgabe |
|---|---|---|
| `MorniLAN.Agent` | Freundes-PC, Windows-Dienst (LocalSystem) | Verbindung zum Admin-Panel, Status, Steam-Erkennung, Sperren durchsetzen, Befehle ausführen, Selbst-Update |
| `MorniLAN.Launcher` | Freundes-PC, Shell des Freundes-Kontos | Vollbild-Oberfläche mit freigegebenen Apps und Spielen, spricht nur lokal per Named Pipe mit dem Agent |
| `MorniLAN.Admin` | PC des Admins | Übersicht, Freigaben, Fernzugriff, Aktionen, Updates |
| `MorniLAN.Shared` | – | Gemeinsame Modelle, Protokoll und Konstanten |
| `MorniLAN.Tests` | – | Unit-Tests (xUnit v3) |

```
 Admin-PC                          Freundes-PC
┌──────────────┐  TLS (LAN oder   ┌─────────────────────┐  Named Pipe  ┌──────────────┐
│ MorniLAN     │◄─────────────────┤ MorniLAN.Agent      │◄────────────►│ MorniLAN     │
│ .Admin       │   Tailscale)     │ (Windows-Dienst)    │              │ .Launcher    │
└──────────────┘  Agent baut      │ AppControl/AppLocker│              │ (Shell)      │
        │         Verbindung auf  │ Prozess-Wächter     │              └──────────────┘
        │                         └─────────────────────┘
        └──── Moonlight ─────────► Sunshine (Fernzugriff)
```

## Technische Entscheidungen

| Thema | Wahl | Begründung |
|---|---|---|
| Laufzeit | .NET 10 (LTS), C# | aktuell, langfristig unterstützt |
| UI | Avalonia 12 (Fluent, Dark Mode) | unpackaged/self-contained, robust als Shell-Ersatz ohne Explorer, gut testbar |
| Fernzugriff | Sunshine + Moonlight | jede Windows-Edition, schnell, zeigt die laufende Sitzung des Freundes (RDP würde ihn abmelden) |
| Sperren | App Control (WDAC) bzw. AppLocker je nach Edition, dazu immer ein Prozess-Wächter | Home und Pro werden unterstützt, die Edition wird zur Laufzeit erkannt |
| Auto-Update | Velopack über GitHub Releases | Delta-Updates, Rollback, öffentliches Repo und damit kein Token nötig |

## Bauen

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`).
Neuen Entwicklungsrechner einrichten: `./tools/setup-dev.ps1`. Es installiert das SDK bei Bedarf, setzt die Git-Identität und führt Build und Tests aus.

```powershell
./build.ps1                 # Build (Release)
./build.ps1 -Task Test      # Build + Tests
./build.ps1 -Task Publish   # self-contained win-x64
```

Hinweis: Lokal landen Build-Ausgaben unter `%LOCALAPPDATA%\MorniLAN\artifacts` bzw. `…\publish` und **nicht** im Projektordner, da dieser in iCloud Drive liegt. Auf CI landet alles unter `./artifacts`.

### Einzeln starten (Entwicklung)

```powershell
dotnet run --project src/MorniLAN.Admin
dotnet run --project src/MorniLAN.Launcher
dotnet run --project src/MorniLAN.Agent      # als Konsolen-App; Logs in %ProgramData%\MorniLAN\logs
```

## Versionierung & Branches

- SemVer, die Version steht **nur** in [`Directory.Build.props`](Directory.Build.props) (`VersionPrefix`).
- `main` = stabil, `dev` = Entwicklung, `feature/*` = ein Branch pro Meilenstein.
- Commits nach [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `chore:` …).
- Tags `v*` erzeugen ab Meilenstein 9 automatisch Releases.

## Fahrplan

- [x] 1. Grundgerüst: Projektmappe, Shared-Modelle, README, Git, Build-Skript, CI
- [ ] 2. Verbindung Agent ↔ Admin mit Pairing und Heartbeat (LAN, dann Tailscale)
- [ ] 3. Programme und Steam-Spiele erkennen
- [ ] 4. Freigaben
- [ ] 5. Launcher als Shell
- [ ] 6. Sperren durchsetzen und Notfall-Entsperrung
- [ ] 7. Fernzugriff (Sunshine/Moonlight)
- [ ] 8. Aktionen (winget, Nachrichten, Neustart, Hilfe anfordern)
- [ ] 9. CI/CD und Auto-Update mit Rollback
- [ ] 10. Installer und Einrichtungsassistent
- [ ] 11. Feinschliff und Admin-Doku
- [ ] 12. Extras

## Datenschutz

MorniLAN liest oder speichert **keine** Zugangsdaten (Steam, Discord usw.) und lässt die persönlichen Dateien des Benutzers unangetastet. Eine laufende Fernzugriffs-Sitzung wird im Launcher immer deutlich angezeigt.

## Lizenz

[MIT](LICENSE)
