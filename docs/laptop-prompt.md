# Prompt für Claude Code auf dem Laptop

Den Text im Block unten komplett in Claude Code auf dem Laptop einfügen. Claude Code vorher im Projektordner öffnen:
`%USERPROFILE%\iCloudDrive\Morni Archiv\Projekte\MorniLAN`

```text
Wir machen am Projekt MorniLAN weiter, das ich auf meinem anderen PC mit dir begonnen habe.
Projektordner: %USERPROFILE%\iCloudDrive\Morni Archiv\Projekte\MorniLAN
GitHub (öffentlich): https://github.com/MoinMornhart/MorniLAN, Arbeits-Branch: dev

1. Lies zuerst CLAUDE.md und docs/anforderungen.md komplett. Dort stehen Arbeitsweise,
   alle bisherigen Entscheidungen, der Stand und die Stolperfallen. Frag mich nichts erneut,
   was dort schon entschieden ist.

2. Prüfe das Repo:
   - Der Ordner wird per iCloud synchronisiert. Stelle sicher, dass alle Dateien lokal
     vorhanden sind (keine reinen Online-Platzhalter) und dass `git status` und
     `git fetch origin` sauber laufen.
   - Wenn .git kaputt oder unvollständig ist: Ordner sichern und das Repo frisch von GitHub
     an dieselbe Stelle klonen, danach `git switch dev`.
   - Stelle sicher, dass ich auf `dev` bin und auf dem Stand von origin/dev.

3. Richte den Laptop ein: führe `./tools/setup-dev.ps1` aus. Es installiert bei Bedarf das
   .NET 10 SDK, setzt die Git-Identität, baut alles und führt die Tests aus (erwartet: 42 bestanden).
   Zeig mir, falls etwas fehlschlägt, und behebe es.

4. Meilenstein 1 abnehmen: Starte nacheinander Admin-Panel, Launcher und Agent
   (siehe "Befehle" in CLAUDE.md) und gib mir die kurze Test-Checkliste für M1.
   Wenn ich M1 freigebe: dev nach main mergen (--no-ff), pushen und prüfen, dass die CI grün ist.

5. Danach Meilenstein 2 (Verbindung Agent <-> Admin, Pairing, Heartbeat, erst LAN, dann Tailscale).
   Stell mir vorher die nötigen Rückfragen, insbesondere zur Kommunikation:
   Der Vorschlag in CLAUDE.md ist SignalR über TLS mit Pairing-Code und Zertifikat-Pinning.
   Arbeite wie gehabt auf einem Branch feature/m2-verbindung und halte CLAUDE.md
   (Abschnitt "Stand") nach jedem Meilenstein aktuell.
```
