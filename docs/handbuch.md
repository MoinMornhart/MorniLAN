# MorniLAN – Admin-Handbuch

Alles, was du mit MorniLAN machen kannst, in Ruhe erklärt. Für die **Installation und das Koppeln** siehe
[docs/einrichtung.md](einrichtung.md) – dieses Handbuch setzt einen gekoppelten PC voraus.

Kurz zur Idee: Dein Freund meldet sich an seinem PC mit seinem **eigenen Windows-Konto** an und sieht statt des
Desktops einen aufgeräumten **Launcher** mit seinen Spielen und Apps. Du steuerst alles von deinem **Admin-Panel** aus.
MorniLAN liest **nie** Passwörter oder persönliche Dateien und zeigt jeden Fernzugriff deutlich am PC an.

## Inhalt

- [Der Einrichtungs-Assistent](#der-einrichtungs-assistent)
- [Die Übersicht](#die-übersicht)
- [Freigaben: was im Launcher erscheint](#freigaben-was-im-launcher-erscheint)
- [Aufräumen: unnötige Apps ausblenden](#aufräumen-unnötige-apps-ausblenden)
- [Profile](#profile)
- [Windows-Sperren je Konto](#windows-sperren-je-konto)
- [Launcher als Desktop (Kiosk)](#launcher-als-desktop-kiosk)
- [Fernzugriff](#fernzugriff)
- [Aktionen aus der Ferne](#aktionen-aus-der-ferne)
- [Updates](#updates)
- [Notfall-Entsperrung](#notfall-entsperrung)
- [Diagnose und Fehlersuche](#diagnose-und-fehlersuche)
- [Was MorniLAN bewusst NICHT tut](#was-mornilan-bewusst-nicht-tut)

---

## Der Einrichtungs-Assistent

Beim allerersten Start (solange noch kein PC gekoppelt ist) öffnet das Panel einen **Assistenten**, der dich in zwei
Schritten durchführt:

1. **Firewall freigeben** – damit die Geräte dein Panel im Heimnetz (und über Tailscale) finden. Windows fragt einmal
   nach Administrator-Rechten.
2. **Ersten PC koppeln** – du gibst den Code ein, den der Launcher auf dem verwalteten PC groß anzeigt. Sobald der PC
   gekoppelt ist, springt der Assistent von selbst auf **Fertig**.

Du kannst den Assistenten jederzeit **überspringen** und später unter **Einstellungen → Einrichtung → „Einrichtung
starten"** erneut aufrufen.

## Die Übersicht

Die Startseite zeigt:

- **Serverstatus** (Port, Name im Netzwerk, Zertifikat-Fingerabdruck).
- **Pairing-Anfragen** – ein neuer PC, der gekoppelt werden möchte (Code eingeben → **Koppeln**).
- **Gekoppelte PCs** als Karten: online/offline, zuletzt gesehen, CPU/RAM/Laufwerk, Update-Stand und **Entkoppeln**.
- Hinweise auf **Hilfe-Anfragen** (wenn dein Freund im Launcher „Hilfe anfordern" drückt) und auf ein verfügbares
  **Panel-Update**.

## Freigaben: was im Launcher erscheint

Seite **Freigaben**. Oben wählst du den PC, darunter ggf. das **Profil**, für das die Schalter gelten.

- MorniLAN findet Spiele aus **Steam, Epic, GOG, Ubisoft Connect, EA app und Battle.net**, dazu installierte Programme
  und Microsoft-Store-Apps – mit Icons und Covern.
- **Standard: alles frei.** Du **sperrst gezielt** einzelne Einträge über den Schalter. „Sichtbare freigeben/sperren"
  wirkt auf alles, was Suche und Filter gerade zeigen.
- **Eigener Eintrag …** fügt ein Programm hinzu, das MorniLAN nicht selbst findet (Name + Pfad zur `.exe`); das Icon
  holt sich der PC aus der Datei.
- Freigaben gelten **pro PC**, mit Profilen auch **pro Profil** (eine Profilregel geht der PC-Regel vor). Der Stand wird
  sofort übertragen (oder beim nächsten Verbinden); unten steht, ob der PC ihn schon hat (**„✓ Der PC hat die aktuellen
  Freigaben"**).

> Gesperrt heißt: taucht nicht mehr im Launcher auf und lässt sich dort nicht starten. Die **harte** Windows-Sperre
> (App Control) ist ein eigener, noch kommender Schritt – siehe [Fahrplan](../README.md#fahrplan).

## Aufräumen: unnötige Apps ausblenden

Die Programmliste eines echten PCs ist voll von Rauschen (Laufzeiten, Treiber-Tools, Hintergrund-Updater). MorniLAN
blendet erkannte **Systemkomponenten** schon automatisch aus. Was trotzdem stört, erkennt die Karte **„Aufräumen"**:

1. Sie sagt z. B. **„21 Einträge sehen unnötig aus"**. Klick auf **Vorschläge ansehen**.
2. Du siehst die Liste mit vorausgewählten Haken (einzelne kannst du abwählen) und blendest sie mit **„Ausgewählte
   ausblenden"** in einem Rutsch aus.
3. Zum Zurückholen: **„Ausgeblendete anzeigen"** und **„Alle wieder einblenden"**; jede Zeile hat außerdem einen
   eigenen **Ausblenden/Einblenden**-Knopf.

**Ausgeblendet ≠ gesperrt**: Die Einträge verschwinden nur aus Liste und Launcher-Kacheln, bleiben aber installiert und
nutzbar. Reine Aufräumung, jederzeit umkehrbar.

## Profile

Im Launcher fragt MorniLAN **„Wer spielt?"**. Profile trennen „Zuletzt gespielt", Farben und persönliche Einstellungen.

- Anlegen/Löschen im Panel (Seite Freigaben) **oder** am PC im Launcher. Mit **„Am PC neue Profile anlegen"** erlaubst
  oder sperrst du das Anlegen am Gerät.
- Dein Freund richtet sein Profil im Launcher selbst ein: **Farbe, Hintergrund-Thema, eigenes Hintergrundbild und
  Profilbild (Avatar)**.
- Optionales **Profil-Passwort**: schützt nur die Profilauswahl im Launcher (kein Windows-Login), wird nie im Klartext
  gespeichert. Ein vergessenes Passwort setzt du im Panel über **„Passwort zurücksetzen"** zurück.

## Windows-Sperren je Konto

Seite Freigaben, Karte **„Windows-Sperren"**. Hier wählst du, welche **Windows-Konten** eingeschränkt werden.

- **Dein eigenes Admin-Konto wird nie eingeschränkt.** Administratorkonten tauchen zwar auf, lassen sich aber nicht
  anhaken („Administrator – wird nie eingeschränkt").
- Pro eingeschränktem Konto kannst du Windows-Bereiche sperren: **Einstellungen/Systemsteuerung, Eingabeaufforderung/
  PowerShell, Registry-Editor, Task-Manager, Programme installieren**.
- Durchgesetzt wird das über **Benutzer-Richtlinien** und einen **Prozess-Wächter**, der gesperrte Programme nur in
  eingeschränkten Konten beendet.
- **„Sperren jetzt aktivieren"** wendet alles sofort an. Im Notfall hebt die [Notfall-Entsperrung](#notfall-entsperrung)
  am PC alles auf.

## Launcher als Desktop (Kiosk)

Zusätzlicher Schalter je Standardkonto: **„Launcher als Desktop"**. Damit ersetzt der Launcher die Windows-Shell – beim
Anmelden startet direkt der Launcher, **kein Desktop, keine Taskleiste, kein Startmenü**. Dein Freund kommt da nicht
heraus.

- **Nur für Standardkonten**, nie fürs Admin-Konto.
- Herunterfahren, Neustart und Abmelden gehen weiter über den Knopf im Launcher.
- Wirkt **bei der nächsten Anmeldung** des Kontos. Ausschalten (Schalter aus oder [Notfall-Entsperrung](#notfall-entsperrung))
  stellt den normalen Windows-Desktop wieder her – ebenfalls zur nächsten Anmeldung.
- Findet der Agent den Launcher nicht, bleibt der Kiosk **aus** (lieber kein Kiosk als ein schwarzer Bildschirm).

## Fernzugriff

Seite **Fernzugriff**. Du siehst den Bildschirm deines Freundes und kannst ihn steuern – transparent und mit Zustimmung.

- **Verbinden/Trennen.** Mit dem Häkchen **„ohne Rückfrage erlauben"** startet der Zugriff sofort; sonst fragt der PC
  deinen Freund jedes Mal (er sieht **Erlauben/Ablehnen** im Launcher).
- Am PC zeigt der Launcher **immer einen deutlichen roten Balken**, wenn zugegriffen wird.
- Mit **Maus/Tastatur sperren** kannst du während der Sitzung die Eingabe deines Freundes deaktivieren.
- Technik: Das Bild läuft über **Sunshine** (auf dem verwalteten PC) und **Moonlight** (bei dir). Fehlt Sunshine,
  richtet MorniLAN es beim ersten Fernzugriff **automatisch per winget ein** („wird eingerichtet …").

> Fürs eigentliche Bild-Streaming brauchst du einmal [Moonlight](https://moonlight-stream.org) auf deinem PC.

## Aktionen aus der Ferne

Seite **Aktionen**. Alles mit Verlauf, der zeigt, ob es geklappt hat.

- **Programm installieren/deinstallieren** per winget (Suchfeld, z. B. „Discord").
- **Aus einer Adresse installieren**: eine `https`-Adresse auf eine `.exe`/`.msi` + Name, wird still installiert.
- **Nachricht schicken**: erscheint beim Freund als Pop-up unten rechts (wie bei einer Fernwartung).
- **Neu starten / Herunterfahren** mit einer Minute Vorwarnung.

## Updates

Ab 0.3.0 aktualisiert sich alles selbst; Details in [einrichtung.md → Updates](einrichtung.md#updates). Kurz:

- Das **Panel** prüft beim Start und alle 6 h; **„Jetzt aktualisieren"** lädt (mit Prüfsumme), installiert und startet
  neu. Schlägt ein Update fehl, startet sich das Panel von selbst wieder.
- **Verwaltete PCs** aktualisieren sich selbst, **nie während eines Spiels**, und prüfen nach dem Neustart, ob die neue
  Version wirklich läuft. Ein Erfolg erscheint kurz grün auf der PC-Karte.

## Notfall-Entsperrung

Der wichtigste Satz: **Du sperrst dich nie selbst aus.** Falls doch mal etwas klemmt, gibt es am verwalteten PC im
**Startmenü** den Eintrag **„MorniLAN Notfall-Entsperrung"**:

- Hebt **alle** Sperren sofort auf und stellt den normalen Windows-Desktop wieder her (auch den Kiosk).
- Funktioniert **ohne Panel** und ohne Netz. Windows fragt nach dem **Administrator-Passwort**.
- Danach aktivierst du die Sperren im Panel wieder, wenn du möchtest (**„Sperren jetzt aktivieren"**).

## Diagnose und Fehlersuche

Seite **Diagnose** zeigt Serverstatus, Firewall-Regeln, Netzwerk, gekoppelte PCs und die letzten Log-Einträge. **Rot =
da hakt es.** Häufige Fälle:

| Problem | Das hilft |
|---|---|
| PC findet das Panel nicht | Liegen beide im selben Netz (gleiche Adress-Anfänge)? Gast-WLAN vermeiden. Sonst im Launcher unter **„Panel nicht gefunden?"** die Adresse des Admin-PCs eingeben (steht im Panel unter „Neuen PC hinzufügen"). |
| Andere PCs erreichen das Panel nicht | **Firewall einrichten** im Panel (Übersicht/Diagnose), Windows-Abfrage mit **Ja**. |
| SmartScreen-Warnung beim Setup | Setups sind noch nicht signiert: **Weitere Informationen → Trotzdem ausführen**. |
| Neu koppeln | Im Panel zweimal **Entkoppeln**; der PC zeigt sofort einen neuen Code. |
| Über Tailscale / anderes Netz | Adresse des Admin-PCs im Geräte-Setup **oder** im Launcher eingeben. |

Logs liegen unter `%LOCALAPPDATA%\MorniLAN\logs` (Panel) bzw. `%ProgramData%\MorniLAN` (Gerät).

## Was MorniLAN bewusst NICHT tut

- **Keine Passwörter lesen oder speichern** (weder Windows, Steam, Discord noch sonst etwas).
- **Keine persönlichen Dateien deines Freundes** öffnen oder übertragen.
- **Kein heimlicher Fernzugriff** – er ist am PC immer deutlich sichtbar, und ohne „ohne Rückfrage erlauben" wird jedes
  Mal gefragt.
- **Keine Cloud, keine Portfreigaben am Router** – alles läuft direkt im Heimnetz oder über dein Tailscale.
- **Dein Admin-Konto wird nie eingeschränkt**, und die Notfall-Entsperrung existiert immer.
