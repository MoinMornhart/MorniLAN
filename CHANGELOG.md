# Changelog

Alle nennenswerten Änderungen an MorniLAN. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionierung nach [SemVer](https://semver.org/lang/de/).
Ab Meilenstein 9 wird dieser Changelog automatisch aus den Conventional Commits erzeugt.

## [Unreleased]

## [0.2.0] – 2026-10-05

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
- `tools/firewall.ps1` für die nötigen Firewall-Regeln (für Entwickler; die Apps richten sie inzwischen selbst ein).
- Einrichtung ohne Konsole (M2.5):
  - `MorniLAN-Geraete-Setup`: installiert Agent (Windows-Dienst, Autostart, Neustart nach Absturz) und Launcher, setzt die Firewall-Regel, optional die Adresse des Admin-PCs; saubere Deinstallation.
  - `MorniLAN-Admin-Setup`: pro Benutzer ohne Admin-Rechte, optional Autostart und Desktop-Verknüpfung.
  - GitHub-Release-Workflow: ein Tag `v*` baut beide Setups und veröffentlicht sie.
  - Launcher zeigt Verbindungsstatus und den Pairing-Code groß an (lokaler Statuskanal per Named Pipe).
  - Admin-Panel: Knopf „Firewall einrichten“, Karte „Neuen PC hinzufügen“ mit Adressen zum Kopieren, Seite „Diagnose“, Autostart und Symbol im Infobereich.
  - App-Icon.

### Fixed
- LAN-Suche zwischen Kabel-LAN und WLAN: Der Agent fragt jetzt selbst nach dem Panel, das Panel antwortet direkt (Unicast). Vorher kamen die Beacons über den Router nicht im WLAN an.
- Beacon-Fehler werden geloggt, statt die Suche still zu beenden.
- Es kann nur noch ein Agent pro PC laufen.
- Firewall-Regeln gelten auch im Netzwerkprofil „Öffentlich“ (weiterhin nur aus dem eigenen Subnetz).
- Beacon enthält die eigenen Adressen des Panels, weil ein WLAN-Repeater im NAT-Modus die Absenderadresse umschreibt.
- Falsche Pairing-Codes, Ablehnungen und Entkoppeln stehen im Panel-Log.
- Virtuelle Netzwerkadapter (Hyper-V, VPN) werden erkannt und nach hinten sortiert.
- Launcher bekam als Standardbenutzer keinen Zugriff auf den Statuskanal (öffnet jetzt nur lesend).
- Versionsendung (z. B. `beta.1`) im Build-Skript.
