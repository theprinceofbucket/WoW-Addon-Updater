# WoW-Addon-Updater – Fenster-Version

## Start

Kopiere **nur `WoW-Addon-Updater.exe`** in deinen WoW-Ordner direkt neben `Wow.exe` und starte sie per Doppelklick. Skript und Addon-Katalog sind in der EXE enthalten. Keine zusätzliche Installation, keine Pfadeingabe und keine Administratorrechte sind normalerweise erforderlich. Windows PowerShell und .NET Framework müssen auf Windows verfügbar sein.

Die EXE verwendet ihren eingebauten Katalog mit 21 Addon-Paketen. Eine vorhandene `addons.json` der älteren Skript-Version wird nicht gelesen oder verändert. Die Auswahl erfolgt über die Haken im Fenster. Neue Katalogversionen werden mit einer aktualisierten EXE ausgeliefert.

Das Fenster verwendet standardmäßig ein dunkles Design. Auf unterstützten Windows-Versionen wird auch die Titelleiste dunkel dargestellt.

## Die Knöpfe

### Menü ⋯ und Sprache

Oben links öffnet **⋯** das Menü. Unter **Sprache / Language** kann zwischen Deutsch und Englisch gewechselt werden. Die Auswahl wird pro Windows-Benutzer unter `%LOCALAPPDATA%\WoWAddonUpdater\language.txt` gespeichert; die EXE braucht daneben keine Einstellungsdatei. Bei aktiven Vorgängen ist der Sprachwechsel gesperrt. Oberfläche, eigene Statusmeldungen und Protokolltexte werden übersetzt; Windows- und Serverfehlermeldungen können in der vom jeweiligen System gelieferten Sprache erscheinen.

### Nach Updates suchen

Diese Funktion prüft manuell das neueste öffentliche Release von `theprinceofbucket/WoW-Addon-Updater` auf GitHub. Ohne veröffentlichtes Release wird eine entsprechende Information angezeigt. Entwürfe, Vorab-Releases, identische Versionen und Downgrades werden nicht installiert. Unterstützt werden Release-Tags wie `v0.5.1` (drei numerische Versionsbestandteile).

Vor dem Download erscheint eine Nachfrage. Das Release muss genau ein Asset namens **WoW-Addon-Updater.exe** mit einer von GitHub gelieferten SHA-256-Prüfsumme enthalten. Downloadadresse, Dateigröße, Prüfsumme, Produktname und Versionsangabe werden kontrolliert. Die Prüfung schützt gegen einen beschädigten oder falschen Download, ersetzt aber keine digitale Signatur und setzt Vertrauen in das GitHub-Repository voraus.

Die Anwendung bleibt eine einzelne EXE. Für ihren eigenen Austausch legt sie kurzzeitig einen Ordner `.wow-updater-self-…` neben der EXE an: mit neuer EXE, einer Kopie als Update-Helfer und einer Sicherung der bisherigen EXE. Der Helfer wartet auf das Ende des Updaters, ersetzt die Datei und startet die neue Version. Bei einem Startfehler versucht er die vorherige EXE wiederherzustellen. Nach erfolgreichem Start wird der Hilfsordner entfernt. Nach einem Absturz oder fehlgeschlagenem Update können Hilfsdateien zur Fehlerdiagnose erhalten bleiben. Addons und WTF werden durch das Self-Update nicht verändert. Für einen geschützten WoW-Ordner muss der Updater weiterhin mit ausreichenden Schreibrechten gestartet sein.

**Teststand vor Veröffentlichung:** Ein echter Download eines neuen GitHub-Releases kann erst getestet werden, sobald ein neueres Release freigegeben und veröffentlicht wurde. Lokal wurden Versionsvergleich, Prüfsummenprüfung, Austausch, Neustart, Abbruch und Bereinigung mit Testdateien geprüft. Es erfolgt keine automatische Veröffentlichung durch den Updater.

1. **Addons auslesen / prüfen:** Erkennt die hinterlegten Addons unter `Interface\AddOns`, setzt die Haken bei installierten Paketen und prüft diese online. Die Pakete werden heruntergeladen und mit den vorhandenen Dateien verglichen. Es wird nichts installiert. Die Erkennung ersetzt die bisherige Hakenauswahl.
2. **Auswahl installieren / updaten:** Verarbeitet nur angehakte Pakete mit erkanntem Update-/Reparaturbedarf sowie angehakte Pakete, die noch nicht erfolgreich geprüft wurden (zum Beispiel neue Installationen). Bereits als aktuell geprüfte Pakete werden vollständig übersprungen. Die Archive aus dem Prüflauf werden wiederverwendet; ein erneuter Download ist innerhalb der Sitzung normalerweise nicht nötig. WoW muss dafür geschlossen sein.
3. **Abbrechen:** Fordert einen geordneten Abbruch an. Downloads und Vorbereitungen werden an der nächsten Abbruchstelle beendet. Eine laufende Serverantwort kann noch bis zum Netzwerk-Timeout dauern. Hat der eigentliche Dateiaustausch bereits begonnen, wird dieses Paket vollständig abgeschlossen oder bei Fehlern zurückgerollt. Weitere Pakete werden nicht gestartet. Bereits abgeschlossene Updates bleiben erhalten.

Noch nicht installierte Addons kannst du manuell anhaken und direkt installieren. Beim Öffnen des Fensters erfolgt nur die lokale Ordnererkennung; die Online-Prüfung startest du mit Knopf 1. Die Auswahl wird bei jedem Programmstart anhand der installierten Ordner neu gesetzt.

## Anzeige

Die Knöpfe zeigen den nächsten Schritt farbig: **Abbrechen wird rot**, solange ein Vorgang läuft und der Abbruch angefordert werden kann. **Installieren / updaten wird blau**, wenn angehakte Pakete ein erkanntes Update benötigen. **WoW starten wird grün**, wenn der letzte Vorgang erfolgreich abgeschlossen wurde und keine erkannten Updates oder ungeprüften ausgewählten Pakete mehr offen sind. Bei Fehlern oder Abbruch bleibt der Startknopf neutral; auch das Abwählen eines erkannten Updates erzeugt keinen grünen Erfolgszustand.

Die Tabelle zeigt **Installiert** (Version aus dem lokalen Hauptaddon) und **Verfügbar** (Version aus dem geprüften Download). Fehlt ein gültiger `## Version`-Eintrag oder enthält er einen Platzhalter, erscheint „Nicht angegeben“. Bei Paketen mit mehreren Modulen zeigt die Zeile die Hauptversion; der Tooltip enthält die Angaben der erkannten Module. Die Updateentscheidung basiert weiterhin auf den Dateien, nicht allein auf Versionsnummern. „Verfügbar“ kann auch den angezeigten Cache-Stand beschreiben.

## WoW starten und Mehr

**WoW starten** neben „Abbrechen“ entfernt den Sitzungscache sowie den Warperia-Cache unter `%LOCALAPPDATA%\WoWAddonUpdater\Downloads`, startet die `Wow.exe` neben der EXE und schließt danach den Updater. Bei einem Start- oder Bereinigungsfehler bleibt das Fenster mit einer Meldung offen. Während eines laufenden Vorgangs ist der Knopf gesperrt. Der WoW-eigene Ordner `Cache`, Addons und Sicherungen werden nicht gelöscht.

Durch das bewusste Löschen des Warperia-Caches beim Spielstart wird beim nächsten Prüflauf wieder ein Download benötigt. Die 24-Stunden-Wiederverwendung gilt nur, solange der Cache erhalten bleibt; ein bestehendes Downloadlimit wird dadurch nicht aufgehoben.

Der Updater darf zum Schreiben in geschützte WoW-Ordner weiterhin als Administrator laufen. Zum Spielstart bittet er über die Desktop-Automation den bereits laufenden Windows-Explorer, Wow.exe mit dessen normalen Benutzerrechten und dem WoW-Ordner als Arbeitsverzeichnis zu öffnen. Er kopiert dafür keine Explorer-Zugriffstoken mehr. Der Windows-Desktop muss normal (nicht erhöht) laufen. Die Übergabe bestätigt die Startanforderung, nicht den vollständigen Spielstart. Bei Fehlern bleibt der Updater offen und zeigt den betroffenen Schritt sowie den Windows-Fehlercode. Eine bei Wow.exe gesetzte Kompatibilitätsoption „Programm als Administrator ausführen“ bitte deaktivieren. Windows-Einstellungen und WTF-Dateien werden nicht verändert. Grundlage: [Microsofts Execute-in-Explorer-Verfahren](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643).

**Mehr ▼** unterhalb der Hauptknöpfe blendet eine zusätzliche Leiste ein:

- **Backup wiederherstellen:** Öffnet eine Auswahl mit Sicherungszeitpunkt, Addon-Ordner, Version und Größe. Ein ausgewählter Addon-Ordner wird nach Bestätigung wiederhergestellt. Der aktuelle Ordner wird vorher als neues Backup mit dem Zusatz `_restore_…` gesichert. Die ausgewählte Sicherung bleibt erhalten. WoW muss geschlossen sein. Pakete mit mehreren Addon-Ordnern gegebenenfalls aus demselben Sicherungsstand einzeln wiederherstellen. `WTF`-Einstellungen werden nicht zurückgesetzt.
- **Alle Backups löschen:** Zeigt Umfang und Zielordner und fordert vor dem endgültigen Löschen eine Bestätigung an. Gelöscht wird ausschließlich `Interface\AddOns_UpdaterBackups`. Die belegte Größe dieses Ordners steht auch direkt in der Leiste.
- **Protokoll speichern:** Exportiert alle Meldungen der aktuellen Updater-Sitzung als UTF-8-Textdatei mit Zeitstempel unter `Dokumente\WoWUpdaterLogs`. Windows' tatsächlicher Dokumente-Ordner wird verwendet, auch wenn er umgeleitet ist. Die Datei enthält den Spielpfad und Fehlermeldungen; vor dem Weitergeben gegebenenfalls prüfen.

Die Zusatzaktionen sind während laufender Updates gesperrt. Ordnerverknüpfungen werden bei Backup- und Cache-Verwaltung aus Sicherheitsgründen abgelehnt. Bei fehlenden Schreibrechten wird eine Fehlermeldung angezeigt; der Updater erhöht seine Rechte nicht automatisch.

- **Installiert · noch nicht geprüft:** Lokaler Addon-Ordner mit TOC vorhanden.
- **Nicht installiert:** Keiner der hinterlegten Hauptordner erkannt.
- **Aktuell:** Dateien stimmen mit dem heruntergeladenen Paket überein.
- **Update / Reparatur verfügbar:** Dateien unterscheiden sich oder Paketbestandteile fehlen. Das kann auch eine lokale Änderung sein; es ist kein Vergleich numerischer Versionsnummern.
- **Fehler · siehe Protokoll:** Quelle fehlgeschlagen. Andere Pakete werden weiter verarbeitet.
- **Cache · …:** Vergleich mit einem zwischengespeicherten Warperia-Paket; keine neue Online-Prüfung. Dessen Downloadzeit steht im Protokoll.
- **Abgebrochen:** Das aktuelle Paket wurde vor der Installation abgebrochen.

Pakete wie DBM, Details und WeakAuras werden jeweils als eine Zeile angezeigt und inklusive ihrer enthaltenen Module installiert. APIDocumentation gehört zu WeakAuras. Die Erkennung betrachtet die hinterlegten Hauptordner; beliebige umbenannte Addons und unbekannte Addons werden nicht automatisch zugeordnet. Bagnon ist ebenfalls auswählbar.

## Sicherungen und Dateien

Der bewährte Updater übernimmt Download, Entpacken, Zielordnerprüfung und Sicherungen. Ersetzte Addons liegen anschließend unter `Interface\AddOns_UpdaterBackups`. Questie wird als `Questie-335`, NotPlater als `NotPlater-3.3.5` installiert. Der Ordner `WTF` wird nicht bearbeitet.

Die EXE entpackt ihr internes Skript vorübergehend nach `%TEMP%\wow-addon-gui-…`. Dort speichert sie auch geprüfte GitHub-, GitLab- und ZIP-Pakete bis zum Schließen zwischen. Ein neuer Prüflauf verwendet für diese Quellen einen frischen Sitzungscache. Beim normalen Schließen wird der gesamte Sitzungsordner entfernt. Nach einem erzwungenen Abbruch können Reste bleiben. Alte Sicherungen werden nicht automatisch gelöscht.

Schließen während eines Vorgangs fordert ebenfalls den Abbruch an. Nach dessen Abschluss kannst du das Fenster schließen. Ein zweites Exemplar der EXE kann nicht gleichzeitig gestartet werden. Starte auch den alten Skript-Updater nicht gleichzeitig.

## Warperia-Downloadlimit und Cache

Warperia begrenzt wiederholte Downloads. Der Updater zeigt die entsprechende Meldung an und versucht nicht, diese Sperre zu umgehen. Wie lange eine vorhandene Sperre dauert, bestimmt Warperia.

Erfolgreich geprüfte Warperia-Pakete werden für 24 Stunden unter `%LOCALAPPDATA%\WoWAddonUpdater\Downloads` wiederverwendet. Dadurch benötigen Prüfung, anschließende Installation und erneute Prüfungen innerhalb dieses Zeitraums keinen weiteren Warperia-Download. Die lokale Installation wird weiterhin mit den Dateien im Cache verglichen. Änderungen auf Warperia werden während dieser Zeit nicht neu geprüft.

Der Warperia-Cache bleibt beim Schließen erhalten. Pro Warperia-Quelle gibt es eine ZIP und eine Prüfsummendatei; beim nächsten erfolgreichen Download werden sie ersetzt. Es entsteht nicht bei jeder Prüfung eine neue dauerhafte Kopie. Zurzeit betrifft das TomTom und NPCScan. Andere Quellen nutzen den oben beschriebenen Sitzungscache zwischen Prüfung und Installation. Der Cache darf bei geschlossenem Updater gelöscht werden, allerdings ist dann wieder ein Download nötig. Fehlende, beschädigte oder mehr als 24 Stunden alte Cache-Pakete werden erneut heruntergeladen.

Eine bereits bestehende Sperre wird durch den Cache nicht aufgehoben. Ohne vorher erfolgreich gespeichertes Paket muss der Download warten, bis Warperia ihn wieder zulässt. Es gibt keinen automatischen Wiederholungsversuch und keinen Rückgriff auf einen abgelaufenen Cache als vermeintlich aktuellen Stand.

## Stand der Prüfung

Die EXE wurde hier erstellt und gestartet. Ordnererkennung, dunkle Oberfläche, Prozessanbindung, Cache-Status und Abbruchstatus wurden lokal geprüft. Testarchive decken Cache-Wiederverwendung, Ablauf und beschädigte Cache-Dateien sowie Abbruch vor dem Download und während der Installation ab. Downloadlimits wurden mit lokalen Beispielantworten getestet, ohne neue Warperia-Downloads auszulösen. Ein kompletter Online-Durchlauf aller 21 Pakete und ein Spieltest der neuen EXE stehen noch aus.

Die EXE ist nicht digital signiert. Windows kann deshalb beim erstmaligen Start einen Herausgeberhinweis anzeigen.

## Quellcode

Die Oberfläche liegt in `Gui\Updater.cs`. `Gui\Build.ps1` erstellt die EXE mit dem unter Windows vorhandenen .NET-Framework-Compiler und bettet `Update-WoWAddons.ps1` sowie `addons.example.json` ein. Änderungen an diesen Dateien erfordern einen erneuten Build, bevor sie in der EXE wirksam werden.
