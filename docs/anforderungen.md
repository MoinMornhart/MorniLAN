# MorniLAN – Anforderungen

Ursprüngliche Projektbeschreibung (Stand 2026-10-05). Die getroffenen Entscheidungen zu offenen Punkten stehen in [CLAUDE.md](../CLAUDE.md#getroffene-entscheidungen).

## Ziel

Ein Windows-PC, den ein Freund benutzt, soll vom PC des Admins aus verwaltet werden. Windows bleibt das Betriebssystem. Der Freund hat nur eingeschränkten Zugriff:

- Der Freund hat ein eigenes Windows-Konto als **Standardbenutzer ohne Admin-Rechte**. Darin nutzt er seine eigenen Konten (Steam, Discord usw.).
- Statt des normalen Desktops sieht er einen eigenen **Launcher**, in dem nur die vom Admin freigegebenen Apps und Spiele erscheinen.
- Der Admin kann von seinem PC aus über ein **Admin-Panel**:
  - Apps und Spiele freigeben oder sperren,
  - den Status des PCs sehen,
  - per Fernzugriff helfen (Bildschirm sehen und steuern, installieren, Updates).
- **Steam** ist integriert: Seine Steam-Spiele werden automatisch erkannt und im Launcher angezeigt, sofern sie freigegeben sind. Er startet sie direkt per Klick.
- Der PC kann im selben LAN stehen oder beim Freund zu Hause. Beides muss funktionieren, im zweiten Fall über **Tailscale**, also ohne Portfreigaben im Router.

## Architektur

### 1. MorniLAN.Agent (Freundes-PC)

- Windows-Dienst unter LocalSystem, startet automatisch.
- Verbindet sich mit dem Admin-Panel, empfängt Freigaben und Befehle und meldet den Status.
- Setzt Sperren durch:
  - erlaubte Programme per AppLocker oder WDAC, je nach verfügbarer Windows-Edition. Er prüft die Edition und wählt passend, mit einem Fallback über einen Prozess-Wächter, der nicht erlaubte Prozesse beendet;
  - Windows-Richtlinien per Registry bzw. Gruppenrichtlinien für den Benutzer: Systemsteuerung/Einstellungen eingeschränkt, keine Eingabeaufforderung/PowerShell/Regedit, kein Task-Manager (optional), keine Software-Installation.
- Liest Steam aus:
  - `libraryfolders.vdf` und `appmanifest_*.acf` parsen und installierte Spiele samt Name, AppID, Größe und Icon an das Panel melden;
  - Spielstart über `steam://rungameid/<AppID>`.
- Führt Admin-Befehle aus: Programm installieren (z. B. per winget), Neustart, Nachricht an den Benutzer, Sperre jetzt aktivieren.
- Lokales Logging (Serilog) mit Log-Rotation.

### 2. MorniLAN.Launcher (Oberfläche für den Freund)

- Wird als **Shell-Ersatz** für das Konto des Freundes eingetragen, über Shell Launcher bzw. den Registry-Shell-Eintrag pro Benutzer. Der Explorer startet für ihn also nicht.
- Modernes, schönes Vollbild-UI mit Kacheln für freigegebene Apps und Spiele, mit Suche und einem Bereich für „Zuletzt gespielt“.
- Eine Leiste mit Uhrzeit, Lautstärke, WLAN, Abmelden/Neustart/Herunterfahren und einem **„Hilfe anfordern“**-Button, der dem Admin eine Benachrichtigung schickt.
- Zeigt **deutlich sichtbar** an, wenn gerade eine Fernzugriffs-Sitzung läuft (Transparenz gegenüber dem Freund).
- Kommuniziert nur lokal mit dem Agent (Named Pipe) und braucht keine Admin-Rechte.

### 3. MorniLAN.Admin (Admin-Panel)

- Desktop-App mit:
  - **Übersicht:** online/offline, aktuell laufendes Programm/Spiel, CPU/RAM/Speicher, letzte Aktivität.
  - **Freigaben:**
    - Liste aller installierten Programme und Steam-Spiele auf dem Freundes-PC, freigegeben per Schalter;
    - eigene Einträge hinzufügen (EXE-Pfad, Name, Icon).
  - **Fernzugriff:** Button „Verbinden“, der die Remote-Sitzung startet.
  - **Aktionen:** Programm installieren (winget-Suche), Nachricht senden, Neustart, Sperren.
  - Optional, später: Zeitlimits/Sperrzeiten, Verlauf.

## Kommunikation & Sicherheit

- Verbindung: gRPC oder ASP.NET Core mit SignalR über TLS. Der Agent baut die Verbindung **nach außen** auf, damit beim Freund kein eingehender Port nötig ist.
- **Pairing:** Bei der Ersteinrichtung zeigt der Agent einen Pairing-Code. Danach erfolgt die Authentifizierung über Zertifikate bzw. Schlüsselpaare. Keine Passwörter im Klartext.
- **Netzwerk:**
  - Im LAN finden sich die Geräte automatisch per mDNS/UDP-Broadcast.
  - Remote läuft alles über die Tailscale-IP bzw. den MagicDNS-Namen.
- **Fernzugriff:** Bevorzugt Sunshine (Host) + Moonlight (Client), alternativ RDP, falls der Freundes-PC Windows Pro hat. → Entschieden: Sunshine + Moonlight.
- Keine Zugangsdaten von Steam oder anderen Konten des Freundes speichern oder auslesen. Seine persönlichen Dateien bleiben unangetastet.
- **Notfall-Entsperrung:** Es muss einen sicheren Weg geben, alle Sperren aufzuheben, falls etwas schiefgeht, z. B. mit einem lokalen Admin-Konto und einem Recovery-Befehl. Das eigene Admin-Konto darf **niemals** ausgesperrt werden.

## Git, Releases & Auto-Update

### Git-Workflow

- Branches: `main` (stabil) und `dev` (Entwicklung), dazu Feature-Branches pro Meilenstein.
- Commits nach Conventional Commits und automatischer Changelog.
- `.gitignore` für .NET, README mit Screenshots, LICENSE (MIT).
- GitHub Actions:
  - Build und Tests bei jedem Push bzw. Pull Request;
  - beim Tag `v*` werden automatisch Release-Builds und Installer erstellt und als GitHub Release veröffentlicht.
- Versionierung nach SemVer. Die Version steht zentral an einer Stelle (`Directory.Build.props`) und wird überall angezeigt.

### Auto-Update (für alle drei Komponenten)

- Agent, Launcher und Admin-Panel prüfen regelmäßig auf neue Versionen in den GitHub Releases (Update-Manifest oder Velopack, Vorschlag: Velopack).
- Der Agent aktualisiert sich selbst und den Launcher im Hintergrund:
  - Download prüfen (Hash bzw. Signatur), dann Dienst stoppen, ersetzen und neu starten;
  - wenn der neue Start fehlschlägt, folgt ein automatischer **Rollback** auf die alte Version.
- Das Admin-Panel zeigt „Update verfügbar“ mit Changelog und aktualisiert per Klick.
- Im Admin-Panel lassen sich der Update-Kanal (Stable / Beta) einstellen und Updates auf dem Freundes-PC sofort auslösen oder pausieren.
- Updates **nie mitten im Spiel** neu starten. Der Agent wartet, bis kein Spiel läuft, oder bis Leerlauf ist.
- (Repo ist öffentlich, ein Token für die Releases ist daher nicht nötig.)

## Weitere Features (Meilenstein 12, Reihenfolge erfragen)

- **Windows-Updates & Treiber:** Status im Panel anzeigen und Updates aus der Ferne anstoßen.
- **Spiele-Updates:** Anzeigen, ob Steam-Spiele Updates laden; optional Downloads drosseln.
- **Dateien übertragen:** Per Drag & Drop vom Admin-Panel in einen freigegebenen Ordner auf dem Freundes-PC und umgekehrt.
- **Benachrichtigungen:** Hinweis auf dem Admin-PC, wenn der Freundes-PC online geht, „Hilfe anfordern“ gedrückt wird, ein gesperrtes Programm gestartet werden sollte oder ein Fehler auftritt; optional als Push über ntfy.sh oder Discord-Webhook.
- **Zeitregeln:** Spielzeit-Limits, Sperrzeiten (z. B. nachts) und Warnungen vor Ablauf.
- **Statistiken:** Spielzeit pro Spiel bzw. Tag, Diagramme im Panel.
- **Wake-on-LAN:** Den PC aus der Ferne einschalten (im LAN direkt, remote über ein Tailscale-Gerät im selben Netz).
- **Backups:** Wiederherstellungspunkt vor großen Änderungen; Export und Import der MorniLAN-Konfiguration.
- **Themes für den Launcher:** Farben, Hintergrundbild, Kachelgröße. Der Freund darf das Design selbst anpassen.
- **Mehrere PCs:** Das Admin-Panel kann später mehrere verwaltete PCs verwalten.
- **Fernsteuerung light:** Screenshot auf Knopfdruck, Prozessliste, Programm beenden.
- **Sprache:** Deutsch als Standard, Englisch vorbereitet (Ressourcen-Dateien).
- **Sicherheit:** signierte Updates; Admin-Panel optional mit PIN oder Windows Hello schützen; Audit-Log aller Admin-Aktionen.

## Tech-Stack

- .NET 10 (LTS), C#
- UI: Avalonia (entschieden), modernes Design, Dark Mode, Animationen
- Agent als Worker Service (Windows-Dienst)
- Kommunikation: gRPC oder SignalR
- Speicherung: SQLite für Freigaben und Verlauf
- Installer: Inno Setup oder WiX. Ein Installer für Agent und Launcher (Freundes-PC), einer für das Admin-Panel.
- Projektmappe mit `MorniLAN.Agent`, `MorniLAN.Launcher`, `MorniLAN.Admin`, `MorniLAN.Shared` und `MorniLAN.Tests`

## Meilensteine

1. **Grundgerüst:** Projektmappe, Shared-Modelle, README, Git, Build-Skript. ✅
2. **Verbindung:** Agent ↔ Admin mit Pairing, Online-Status und Heartbeat, zuerst im LAN, dann über Tailscale.
3. **Programme & Steam erkennen:** Der Agent meldet installierte Programme und Steam-Spiele, das Admin-Panel zeigt sie an.
4. **Freigaben:** Freigaben im Admin-Panel setzen, der Agent speichert sie, der Launcher zeigt nur freigegebene Einträge an.
5. **Launcher als Shell:** Shell-Ersatz für das Freundes-Konto einrichten, inklusive Spielstart über Steam.
6. **Sperren durchsetzen:** AppLocker/WDAC bzw. Prozess-Wächter und Richtlinien setzen, inklusive Notfall-Entsperrung.
7. **Fernzugriff:** Sunshine/Moonlight einbinden, „Verbinden“-Button und sichtbare Anzeige im Launcher.
8. **Aktionen:** winget-Installation, Nachrichten, Neustart, „Hilfe anfordern“.
9. **CI/CD & Auto-Update:** GitHub Actions, Releases, Selbst-Update mit Rollback.
10. **Installer & Einrichtungsassistent:** Ein Assistent, der auf dem Freundes-PC das Benutzerkonto anlegt, den Agent installiert, die Shell setzt und das Pairing durchführt.
11. **Feinschliff:** Design, Fehlerbehandlung, Logs, Doku für den Admin.
12. **Extras:** die Features aus „Weitere Features“, Reihenfolge erfragen.

## Testen

- Alles zuerst in einer **Hyper-V-VM mit Windows 11** testen, nicht direkt auf dem echten Freundes-PC.
- Für jeden Meilenstein eine kurze Test-Checkliste.
- Unit-Tests für die Logik: Steam-Parsing, Freigabe-Regeln, Pairing.
