# Stand: MorniLAN (Übergabe 2026-10-24)

## Was es ist
Verwaltet einen Windows-PC, den ein Freund benutzt: Der Freund hat ein eigenes Standardkonto und sieht einen Launcher mit genau den freigegebenen Apps und Spielen. Philip steuert alles vom eigenen PC (Admin-Panel): Freigaben, Status, Fernzugriff, Updates – im LAN oder über Tailscale. Windows bleibt das Betriebssystem.

## Aktueller Stand
- **Wichtig: `main` und `dev` sind sehr unterschiedlich.** `main` steht auf **v0.2.0** (M1, M2, M2.5 abgenommen). Der Branch **`dev` ist 57 Commits weiter** (Stand 06.10.) mit Version 0.3.0; letztes Release ist **v0.3.0-beta.21** (Vorabversion, 06.10.).
- Auf `main` fertig: Grundgerüst, Verbindung Agent ↔ Admin (TLS, Pairing-Code, Heartbeat, LAN-Suche), Einrichtung ohne Konsole (zwei Setups, Firewall-Knopf, Diagnose). Am 05.10. mit zwei echten PCs im LAN getestet.
- Auf `dev` gebaut, aber **noch nicht von Philip abgenommen**: M3 Programme & Spiele erkennen (Steam, Epic, GOG, Ubisoft, EA, Battle.net), M4 Freigaben, M5 Vollbild-Launcher mit Profilen/Controller, Profile persönlich gestalten, M6 Windows-Sperren mit Notfall-Entsperrung, M7 Fernzugriff (Sunshine/Moonlight, automatische Kopplung), M8 Aktionen (winget, Nachricht, Neustart), Auto-Update (gehärtet), Kiosk/Shell-Ersatz, Aufräumen, Einrichtungs-Assistent, Admin-Handbuch.

## Zuletzt gemacht
- 2026-10-24: Cloud-Start-Hook für Philips Skills (auf `main`)
- 2026-10-06 (`dev`): Sunshine-Kopplung per PIN, Moonlight-Autostart, Kiosk-Shell-Fix, M3.2–M12 und Feinschliff, Betas bis 0.3.0-beta.21
- 2026-10-05 (`main`): M2.5 abgenommen, Release v0.2.0

## Offen / Nächste Schritte
- **Vor der Arbeit `dev` auschecken** (`git fetch origin dev && git checkout dev`) – dort steht in `CLAUDE.md` der ausführliche Stand. Auf `main` nur Abgenommenes.
- Philip muss M3–M8 und die Extras **testen und abnehmen**, danach `dev` → `main` mergen und eine stabile **v0.3.0** veröffentlichen.
- Noch nicht getestet: **Tailscale**, Windows 11 Home, Neustart des Test-PCs, Admin-Setup auf dem Haupt-PC, Autostart/Infobereich.
- Kleine Verbesserungen: Panel braucht ~5 s zum Beenden; Agent braucht ein Log-Ziel, das er auch bei fremden Dateirechten beschreiben kann.
- Offene GitHub-Issues #1–#3: „Befund prüfen“ in `.github/workflows/ci.yml` (Repo-Check).
- Bewusst verschoben: mDNS, Code-Signatur der Setups (SmartScreen-Warnung).
- Wunsch Philip: Start-Erlebnis wie eine Konsole (PC an → MorniLAN sofort da, Profil wählen). Laut `dev` über Kiosk-Shell weitgehend umgesetzt – Abnahme offen.

## Wichtige Infos
- Technik: .NET 10, C#, Avalonia 12 (Admin + Launcher), Agent als Windows-Dienst, SignalR über TLS, xunit.v3. Lösung `MorniLAN.slnx`.
- Ports: Admin-Panel lauscht auf **47950/TCP**, LAN-Erkennung **47951/UDP**.
- Befehle (PowerShell): `./tools/setup-dev.ps1` · `./build.ps1 -Task Test` · `./build.ps1 -Task Installer` (Inno Setup 6). Release: Tag `v<Version>[-beta.N]` pushen → GitHub baut beide Setups.
- Version nur in `Directory.Build.props`, Paketversionen nur in `Directory.Packages.props`.
- Branches: pro Meilenstein `feature/mN-<name>` von `dev`, `--no-ff` nach `dev`; `main` nur nach Abnahme.
- Arbeitskopie lokal unter `%USERPROFILE%\Dev\MorniLAN`, **nie in iCloud** (iCloud hat Git schon kaputt gemacht).
- Werkstatt-Seite für Philip: https://claude.ai/artifact/8KnK2f1ihar6JpV3QGNM36
- Volle Anforderung: `docs/anforderungen.md`; Stolperfallen: `CLAUDE.md`.

## Philips Vorgaben
- Repo **öffentlich** (MIT). Auto-Update: **ja** (über GitHub-Releases).
- Kleine, testbare Meilensteine; nach jedem Meilenstein Test-Checkliste zeigen und erst nach Freigabe weitermachen.
- Conventional Commits auf Deutsch. „Verfasst mit Claude 🤖“ ist im Repo **nicht festgelegt** – bei Bedarf Philip fragen.
- Einrichtung muss einfach sein: Download, Doppelklick, Assistent, keine Konsole.
- Datenschutz: keine Zugangsdaten (Steam, Discord …) lesen oder speichern; Fernzugriff im Launcher immer sichtbar; Notfall-Entsperrung muss es immer geben.
- `.ps1` und `.iss` als UTF-8 mit BOM speichern.

---
Diese Datei wird beim Arbeiten aktuell gehalten: nach jedem größeren Schritt „Aktueller Stand“ und „Offen“ anpassen.
