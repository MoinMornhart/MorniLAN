# Changelog

Alle nennenswerten Änderungen an MorniLAN. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionierung nach [SemVer](https://semver.org/lang/de/).
Ab Meilenstein 9 wird dieser Changelog automatisch aus den Conventional Commits erzeugt.

## [Unreleased]

### Added
- Projektmappe mit Agent (Worker Service), Launcher und Admin (Avalonia 12), Shared und Tests.
- Zentrale Version in `Directory.Build.props`, zentrale Paketversionen in `Directory.Packages.props`.
- Shared-Modelle: `AppEntry`, `AppId`, `ApprovalSet` (Standard: gesperrt), `DeviceInfo`/`DeviceStatus`, `WindowsEditionInfo`, `AdminCommand` (polymorph per JSON).
- Agent: Windows-Dienst-Hosting, Serilog mit täglicher Log-Rotation (14 Tage, max. 10 MB je Datei), Erkennung der Windows-Edition.
- Build-Skript `build.ps1` und GitHub-Actions-CI (Build und Tests).
- Verbindung Agent ↔ Admin-Panel (M2): Kestrel + SignalR über TLS mit gegenseitigen, selbstsignierten Zertifikaten (DPAPI-geschützt), Pairing-Code mit gegenseitigem Beweis (PBKDF2 + HMAC über beide Fingerabdrücke), danach Zertifikat-Pinning.
- LAN-Erkennung per UDP-Broadcast (Port 47951); das Panel meldet seine Adressen inkl. Tailscale-IP, feste Adresse optional per `MorniLAN:Connection:AdminHost`.
- Heartbeat alle 15 s mit CPU, RAM und Systemlaufwerk; offline nach 45 s; automatischer Reconnect mit Wartezeit.
- Admin-Panel: Übersicht mit Serverstatus, Pairing-Anfragen (Code-Eingabe, max. 5 Versuche) und gekoppelten PCs (online/offline, Werte, Entkoppeln). Logs unter `%LOCALAPPDATA%\MorniLAN\logs`.
- `tools/firewall.ps1` für die nötigen Firewall-Regeln; `build.ps1 -Task Publish` legt es den Ausgaben bei.
