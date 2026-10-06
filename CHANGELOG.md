# Changelog

Alle nennenswerten Änderungen an MorniLAN. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionierung nach [SemVer](https://semver.org/lang/de/).
Ab Meilenstein 9 wird dieser Changelog automatisch aus den Conventional Commits erzeugt.

## [Unreleased]

### Added
- Programme und Spiele erkennen (M3.1): Der Agent meldet installierte Programme (Windows-Programmliste und Startmenü), Microsoft-Store-Apps und Steam-Spiele mit Icons und Steam-Covern. Systemkomponenten (Laufzeiten, Treiber-Tools, Windows-Werkzeuge) werden erkannt und ausgeblendet.
- Admin-Panel: Seite „Freigaben“ mit Spiele-Covern, Programmliste, Suche, Filter, „Systemkomponenten anzeigen“ und „Neu einlesen“.
- Die Liste wird nach dem Verbinden, alle 15 Minuten bei Änderungen und auf Wunsch übertragen; Bilder nur einmal, vom Panel geprüft.
- Automatische Updates über GitHub-Releases (aus M9 vorgezogen): Admin-Panel mit „Update verfügbar → Jetzt aktualisieren“, Geräte aktualisieren sich selbst (nie während eines Steam-Spiels) oder per Knopf im Panel. Setup-Prüfsumme (SHA-256) ist Pflicht. Betas werden mit angeboten.
- Weitere Launcher (M3.2): Spiele aus Epic Games, GOG, Ubisoft Connect, EA app und Battle.net werden erkannt, ohne Dubletten und ohne DLC, mit passendem Startweg (Launcher-Link oder EXE). Im Panel erscheinen sie bei den Spielen mit Launcher-Kennzeichen.
- Cover für Spiele ohne lokales Bild aus dem Steam-Shop (einmalig, zwischengespeichert; nur exakter Name oder anderer Editionszusatz).
- Freigaben (M4): Schalter an jedem Spiel und Programm, Filter „Gesperrt“, „Sichtbare freigeben/sperren“ und eigene Einträge (Name + EXE-Pfad, Icon kommt vom PC). Standard: alles frei, gesperrt wird gezielt. Gilt pro PC, wird sofort oder beim nächsten Verbinden übertragen; das Panel zeigt, ob der PC den Stand hat. Der Agent speichert die Freigaben lokal.
- Launcher: freigegebene Spiele (mit Cover) und Apps (mit Icon) als Kacheln, Klick startet sie (Steam-, Epic-, Ubisoft-Link, Store-App oder EXE). Durchgesetzt werden Sperren erst mit M6.
- Launcher als Vollbild (M5): startet bei jeder Anmeldung (nicht in Administratorkonten), lässt sich nicht per Alt+F4 schließen. „Wer spielt?“ mit Profilen (anlegen am PC, auch ohne Verbindung zum Panel), Suche, „Zuletzt gespielt“ je Profil, Leiste mit Hilfe-Knopf, Netzwerk, Lautstärke, Uhr und Herunterfahren/Neustart/Abmelden. Bedienung mit Maus, Pfeiltasten und Xbox-kompatiblem Controller (A starten, B zurück, Y Suche, Start Ausschalt-Menü).
- Profile im Panel: Liste je PC, anlegen und löschen, „Am PC neue Profile anlegen“ erlauben oder sperren, Freigaben je Profil (gehen den Freigaben des PCs vor).
- „Hilfe anfordern“: erscheint im Panel als Hinweis auf der Übersicht und holt das Fenster nach vorne; war das Panel aus, kommt die Anfrage beim nächsten Verbinden.
- Profile persönlicher: Der Nutzer richtet sein Profil im Launcher selbst ein – Farbe, Hintergrund-Thema (Gaming-Looks), eigenes Hintergrundbild und Profilbild (Avatar). Dazu ein freiwilliges Passwort, das nur die Profilauswahl im Launcher schützt (kein Windows-Login, nie im Klartext gespeichert). Der Admin kann ein vergessenes Passwort im Panel zurücksetzen.
- Aktionen (M8): Im Panel Programme per winget suchen und auf dem PC installieren oder deinstallieren, Programme aus einer https-Adresse (.exe/.msi) herunterladen und still installieren, eine Nachricht an den Freund schicken (erscheint im Launcher) und den PC neu starten/herunterfahren (mit einer Minute Vorwarnung). Jede Aktion zeigt im Verlauf, ob sie geklappt hat.
- Fernzugriff (M7, Steuerung): Im Panel eine Seite „Fernzugriff“ mit „Verbinden/Trennen“, der Einstellung „ohne Rückfrage erlauben“ (sonst fragt der PC den Freund jedes Mal) und einem Schalter, um während der Sitzung Maus/Tastatur des Freundes zu sperren. Am PC zeigt der Launcher immer einen deutlichen roten Balken, wenn zugegriffen wird, inklusive Erlauben/Ablehnen-Abfrage. Das eigentliche Streaming läuft über Sunshine + Moonlight (Sunshine muss auf dem Geräte-PC eingerichtet sein; sonst meldet MorniLAN das).
- Windows-Sperren (M6): Im Panel je Windows-Konto wählen, ob es eingeschränkt wird (Administratorkonten nie; nicht angehakte Konten nutzen Windows normal). Gesperrt werden können Einstellungen/Systemsteuerung, Eingabeaufforderung/PowerShell, Registry-Editor, Task-Manager und Programme installieren. Durchgesetzt per Benutzer-Richtlinie und Prozess-Wächter (beendet gesperrte Programme nur in eingeschränkten Konten). Notfall-Entsperrung am PC über das Startmenü mit Administrator-Passwort, auch ohne Panel. App Control (harte Windows-Sperre) ist als eigener Schritt mit Prüfmodus vorgesehen.
- Launcher als echter Desktop (Kiosk/Shell-Ersatz): Je Konto schaltbar – statt des Windows-Desktops (explorer.exe) startet der Launcher als Shell (kein Desktop, keine Taskleiste, kein Startmenü). Administratorkonten bleiben immer der normale Windows-Desktop; die Notfall-Entsperrung stellt den Desktop wieder her; fehlt der Launcher, bleibt der Kiosk aus statt eines schwarzen Bildschirms.
- Aufräumen: Das Panel erkennt unnötige Einträge (Laufzeiten/Redistributables, Treiber und Hersteller-Tools, Windows-/Store-Zubehör, Hintergrund-Programme) und schlägt sie zum Ausblenden vor; der Admin bestätigt gesammelt oder einzeln. Ausgeblendet heißt: raus aus Panel-Liste und Launcher-Kacheln, aber nicht gesperrt und jederzeit umkehrbar.
- Einrichtungs-Assistent: Im Panel führt beim ersten Start ein Assistent durch Firewall-Freigabe und das erste Pairing (mit Live-Status, automatischem Abschluss, überspringbar, später erneut startbar). Am Gerät kann man im Launcher die Adresse des Admin-PCs eingeben, falls die automatische Suche scheitert (z. B. Tailscale) – der Agent übernimmt sie sofort und dauerhaft.
- Fernzugriff: Sunshine (Streaming-Host) wird bei Bedarf automatisch per winget (`LizardByte.Sunshine`) eingerichtet, wenn der Admin den Fernzugriff verlangt und Sunshine fehlt.

### Changed
- Auto-Update gehärtet: Das Panel startet sich bei einem fehlgeschlagenen Update selbst wieder (nie ohne Panel); das Gerät prüft nach dem Neustart, ob die neue Version wirklich läuft, und versucht es sonst erneut; die Release-Prüfung bei GitHub nutzt ETag-Caching und weicht beim Stunden-Limit auf den letzten bekannten Stand aus; ein erfolgreiches Geräte-Update meldet das Panel kurz grün.

### Fixed
- Admin-Panel stürzte beim Beenden ab (Stapelüberlauf: Beenden schloss das Fenster, das Schließen rief wieder Beenden auf). Betraf „Beenden“ im Infobereich und das Selbst-Update.
- Selbst-Update wartet, bis das Panel wirklich beendet ist, statt fest 3 Sekunden; das Setup schließt ein hängendes Panel notfalls selbst.
- Agent probiert Panel-Adressen im eigenen Netz zuerst (schneller hinter Repeatern, mit VPN- oder Hyper-V-Adaptern).
- Stabilität (Review-Fixes): Sunshine-Installation kann nicht mehr hängen (Zeitlimit + Ausgabe wird geleert); der Fernzugriff-Zustand fährt sich nicht mehr fest; Sperren werden nicht mehr parallel gesetzt (kein Registry-Konflikt); ein kaputter Update-Zwischenspeicher wird verworfen statt dauerhaft Fehler zu werfen; doppelte Update-Anstöße werfen keine Ausnahme mehr; eine Adresse mit „:Port“ wird klar abgelehnt; der Assistent springt beim „Zurück“ nicht mehr ungewollt vor.

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
