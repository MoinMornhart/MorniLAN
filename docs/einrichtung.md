# MorniLAN einrichten

Ohne Konsole und ohne Befehle. Du brauchst zwei Dateien von der [Release-Seite](https://github.com/MoinMornhart/MorniLAN/releases).

## 1. Admin-PC (dein PC)

1. `MorniLAN-Admin-Setup-<Version>.exe` herunterladen und ausführen. Es braucht keine Admin-Rechte.
   - Option **„Mit Windows starten“**: Das Panel läuft dann unsichtbar im Infobereich (unten rechts) und ist immer erreichbar.
2. Das Panel öffnet sich. Oben steht ein gelber Hinweis **„Firewall einrichten“**. Klick auf den Knopf und bestätige die Windows-Abfrage mit **Ja**.
   - Freigegeben werden nur Geräte aus deinem Heimnetz und aus Tailscale.
3. Unter **„Neuen PC hinzufügen“** stehen die Adressen dieses PCs. Die brauchst du nur, wenn die automatische Suche nicht klappt.

## 2. Verwalteter PC

1. `MorniLAN-Geraete-Setup-<Version>.exe` herunterladen und ausführen. Windows fragt einmal nach Admin-Rechten.
2. Die Seite **„Admin-PC“** kannst du im Heimnetz leer lassen. Nur wenn der PC woanders steht, z. B. über Tailscale, trägst du dort eine Adresse aus dem Admin-Panel ein.
3. Am Ende **„MorniLAN öffnen“** anhaken. Das Fenster zeigt groß einen **Pairing-Code**, z. B. `K7Q2-M9XD`.

Das Setup richtet alles Weitere selbst ein:
- den Dienst **MorniLAN Agent**: startet automatisch mit Windows und bei Abstürzen neu,
- die Firewall-Freigabe für die Suche im Heimnetz,
- einen Eintrag unter *Einstellungen → Apps* zum Deinstallieren.

## 3. Koppeln

Im Admin-Panel erscheint nach wenigen Sekunden **„… möchte gekoppelt werden“**. Code eingeben → **Koppeln**. Der PC steht danach grün auf **Online**.

## Wenn etwas nicht klappt

- **Admin-Panel → Diagnose** zeigt Server, Firewall-Regeln, Netzwerk und die letzten Log-Einträge. Rot heißt: da hakt es.
- **Windows-SmartScreen** („Der Computer wurde durch Windows geschützt“): Die Setups sind noch nicht signiert. **Weitere Informationen → Trotzdem ausführen**.
- **Der PC findet das Panel nicht:** Liegen beide im selben Netz (Adressen beginnen gleich, z. B. `192.168.178.`)? Hängt der PC vielleicht im Gast-WLAN? Notfalls die Adresse im Geräte-Setup eintragen (Setup einfach erneut ausführen).
- **Neu koppeln:** Im Panel zweimal „Entkoppeln“ klicken. Der PC zeigt sofort einen neuen Code.

## Deinstallieren

*Einstellungen → Apps → „MorniLAN für Geräte“ bzw. „MorniLAN Admin“ → Deinstallieren.* Dienst und Firewall-Regeln werden entfernt. Kopplung und Logs bleiben in `%ProgramData%\MorniLAN` bzw. `%LOCALAPPDATA%\MorniLAN` erhalten, damit eine Neuinstallation ohne neues Pairing auskommt. Wer sie nicht mehr braucht, löscht die Ordner.
