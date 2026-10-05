# Prompt für Claude Code auf dem Laptop

Den Text im Block unten komplett in Claude Code einfügen. Er funktioniert auch dann, wenn iCloud den Projektordner noch nicht synchronisiert hat.

```text
Wir machen am Projekt "MorniLAN" weiter, das ich auf meinem anderen PC mit dir begonnen habe.
Name der App: MorniLAN. Sprich Deutsch mit mir.

KURZ ZUM PROJEKT
Ich verwalte einen Windows-PC, den ein Freund nutzt. Drei Komponenten (.NET 10, C#, Avalonia 12):
- MorniLAN.Agent: Windows-Dienst auf dem Freundes-PC (Status, Steam-Erkennung, Sperren, Befehle, Selbst-Update)
- MorniLAN.Launcher: Shell-Ersatz für das Konto des Freundes, zeigt nur freigegebene Apps/Spiele
- MorniLAN.Admin: Admin-Panel auf meinem PC (Freigaben, Status, Fernzugriff, Aktionen)
Plus MorniLAN.Shared und MorniLAN.Tests. Die vollständige Anforderung steht im Repo
in docs/anforderungen.md, Arbeitsweise, Entscheidungen, Stand und Stolperfallen in CLAUDE.md.

ENTSCHEIDUNGEN (nicht erneut fragen)
- Freundes-PC: Windows Home UND Pro unterstützen, Edition zur Laufzeit erkennen
- Sperren: App Control (WDAC) bzw. AppLocker je nach Edition + immer ein Prozess-Wächter
- UI: Avalonia 12 (Fluent, Dark Mode) | Fernzugriff: Sunshine + Moonlight
- GitHub: öffentlich, https://github.com/MoinMornhart/MorniLAN | Lizenz: MIT | kein Proxmox-Guide
- Auto-Update später mit Velopack über GitHub Releases
- Git-Identität im Repo: MoinMornhart / 297179352+MoinMornhart@users.noreply.github.com

ARBEITSWEISE
- Kleine, testbare Meilensteine. Vorher Rückfragen, danach zeigen, was fertig ist,
  plus kurze Test-Checkliste. Erst nach meiner Freigabe weiter.
- Branches: main (stabil), dev (Entwicklung), feature/mN-<name> pro Meilenstein,
  per --no-ff nach dev mergen. Conventional Commits auf Deutsch. Nach jedem Meilenstein
  committen, pushen und prüfen, dass die GitHub-Actions-CI grün ist.
- CLAUDE.md (Abschnitt "Stand") nach jedem Meilenstein aktualisieren.

SCHRITT 1 – PROJEKT HOLEN
Zielordner: %USERPROFILE%\iCloudDrive\Morni Archiv\Projekte\MorniLAN
GitHub ist die verlässliche Quelle. iCloud hat das Projekt evtl. noch nicht (vollständig) synchronisiert.
- Gibt es den Ordner nicht: git clone https://github.com/MoinMornhart/MorniLAN.git dorthin.
- Gibt es ihn, aber ohne funktionierendes .git oder unvollständig: in "MorniLAN_icloud_alt"
  umbenennen (nicht löschen) und frisch klonen. Sag mir Bescheid.
- Gibt es ihn mit funktionierendem .git: git fetch origin und auf den Stand von origin/dev bringen.
- Danach: git switch dev. Taucht später durch iCloud eine doppelte/ältere Kopie auf, gilt GitHub.

SCHRITT 2 – LAPTOP EINRICHTEN
Führe ./tools/setup-dev.ps1 aus. Es prüft Git und Speicherplatz, installiert bei Bedarf das
.NET 10 SDK (winget, sonst ohne Admin-Rechte ins Benutzerprofil), setzt die Git-Identität und
führt Build und Tests aus. Erwartet werden 42 bestandene Tests. Behebe Fehler und erklär sie mir.

SCHRITT 3 – MEILENSTEIN 1 ABNEHMEN
Starte Admin-Panel, Launcher und Agent (Befehle in CLAUDE.md) und gib mir die Test-Checkliste für M1.
Wenn ich M1 freigebe: dev nach main mergen (--no-ff), pushen, CI prüfen.

SCHRITT 4 – MEILENSTEIN 2
Verbindung Agent <-> Admin mit Pairing, Online-Status und Heartbeat, erst im LAN
(mDNS/UDP-Broadcast), dann über Tailscale. Stell mir vorher die nötigen Rückfragen.
Vorschlag aus CLAUDE.md: ASP.NET Core + SignalR über TLS, Kestrel im Admin-Panel, der Agent
verbindet sich ausgehend, Pairing-Code und danach gegenseitiges Zertifikat-Pinning.
Arbeite auf feature/m2-verbindung.
```
