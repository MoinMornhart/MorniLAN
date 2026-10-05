# Verbindung Agent ↔ Admin-Panel

Stand: Meilenstein 2.

## Überblick

- Das **Admin-Panel** betreibt den Server: Kestrel + SignalR über TLS auf **TCP 47950**.
- Der **Agent** verbindet sich **ausgehend** zum Panel. Beim Freund ist also kein eingehender Port für die Verbindung nötig.
- **LAN:** Die Suche läuft über **UDP 47951**, und zwar in zwei Richtungen:
  - Das Panel sendet alle 3 Sekunden einen Broadcast (Beacon) mit Name, Port und Zertifikat-Fingerabdruck.
  - Der Agent fragt alle 5 Sekunden selbst per Broadcast „Ist hier ein Panel?“. Das Panel antwortet **direkt** an den Agent.

  Der zweite Weg ist nötig, weil viele Router Broadcasts vom Kabel-LAN nicht ins WLAN weiterreichen. Getestet am 2026-10-05: Haupt-PC am Kabel, Test-PC im WLAN, keine Beacons angekommen.
- **Tailscale:** Beim Pairing meldet das Panel alle eigenen Adressen, auch die Tailscale-IP (100.x.y.z). Steht der PC später nicht mehr im selben LAN, probiert der Agent diese Adressen der Reihe nach durch. Wurde nie im LAN gekoppelt, kann man die Adresse fest eintragen (siehe unten).

## Identität und Pairing

Beide Seiten erzeugen beim ersten Start ein eigenes selbstsigniertes Zertifikat (RSA 2048). Es liegt als DPAPI-geschützte Datei vor, die nur das eigene Windows-Konto entschlüsseln kann (beim Dienst: LocalSystem).

| Seite | Ablage |
|---|---|
| Admin-Panel | `%LOCALAPPDATA%\MorniLAN\admin\` (`admin-identity.bin`, `devices.json`), Logs in `%LOCALAPPDATA%\MorniLAN\logs` |
| Agent | `%ProgramData%\MorniLAN\data\` (`agent-identity.bin`, `agent-state.json`), Logs in `%ProgramData%\MorniLAN\logs`. Als Dienst ist der Ordner nur für SYSTEM und Administratoren zugänglich. |

Ablauf beim ersten Kontakt:

1. Der Agent verbindet sich per TLS und zeigt dabei sein Client-Zertifikat.
2. Das Panel kennt den Fingerabdruck nicht und antwortet mit „Pairing nötig“.
3. Der Agent erzeugt einen **Pairing-Code** (8 Zeichen, z. B. `K7Q2-M9XD`) und zeigt ihn im Log bzw. in der Konsole an. Später erscheint er auch im Launcher und im Einrichtungsassistenten.
4. Der Agent schickt einen **Beweis** für den Code, aber nicht den Code selbst: HMAC über beide Zertifikat-Fingerabdrücke, Schlüssel per PBKDF2 aus dem Code.
5. Im Panel erscheint die Karte „… möchte gekoppelt werden“. Der Admin tippt den Code ein.
6. Stimmt der Code, schickt das Panel seinen Gegenbeweis. Der Agent prüft ihn und bestätigt. Erst jetzt speichert das Panel den PC, und der Agent speichert den Fingerabdruck des Panels (**Pinning**).
7. Ab jetzt verbinden sich beide nur noch mit genau diesem Gegenüber. Ein anderes Panel mit anderem Zertifikat wird abgewiesen.

Weil beide Fingerabdrücke in den Beweis eingehen, passt er nicht mehr, sobald sich jemand dazwischenschaltet. Nach 5 falschen Codes wird die Anfrage verworfen, und der Agent erzeugt einen neuen Code.

## Online-Status

- Der Agent schickt alle **15 s** einen Heartbeat mit CPU, RAM und freiem Platz auf dem Systemlaufwerk.
- Das Panel zeigt den PC als **offline**, wenn die Verbindung weg ist oder **45 s** lang kein Lebenszeichen kam.
- Bricht die Verbindung ab, versucht der Agent es nach 2, 5, 10 und dann alle 30 s erneut. Taucht ein Beacon auf, versucht er es sofort.
- „Entkoppeln“ im Panel: Der Agent vergisst den Pin, erzeugt einen neuen Code und bittet erneut um Pairing.

## Firewall

```powershell
# als Administrator
./tools/firewall.ps1 -Role Admin   # auf dem Admin-PC: TCP 47950 (LAN + Tailscale), UDP 47951 (Suchanfragen aus dem LAN)
./tools/firewall.ps1 -Role Agent   # auf dem Freundes-PC: UDP 47951 aus dem LAN (Beacons)
```

Die Regeln gelten für jedes Netzwerkprofil, also auch für „Öffentlich“, aber nur für Geräte aus dem eigenen Subnetz. Wurde die Windows-Abfrage „Zugriff gestatten?“ mit *Abbrechen* beantwortet, hat Windows eine Blockier-Regel angelegt. Das Skript entfernt sie.

## Feste Adresse (Tailscale ohne gemeinsames LAN)

Im Geräte-Setup auf der Seite „Admin-PC“ eintragen (landet in `%ProgramData%\MorniLAN\data\agent-settings.json`), oder für Entwickler in `appsettings.json` neben `MorniLAN.Agent.exe`:

```json
"MorniLAN": { "Connection": { "AdminHost": "admin-pc.tail1234.ts.net" } }
```

Alternativ beim Start: `MorniLAN.Agent.exe --MorniLAN:Connection:AdminHost=100.101.102.103`.

## Einrichten

Seit M2.5 ohne Konsole: siehe [einrichtung.md](einrichtung.md). Das Geräte-Setup schreibt die Adresse des Admin-PCs nach `%ProgramData%\MorniLAN\data\agent-settings.json`, die Firewall richten Setup bzw. Panel selbst ein.

Für Entwickler geht weiterhin `dotnet run --project src/MorniLAN.Agent` als Konsole, `tools/firewall.ps1` setzt dann die Regeln.
