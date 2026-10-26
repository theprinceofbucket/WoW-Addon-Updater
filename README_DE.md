# WoW Addon Updater

[English](README.md) | [Deutsch](README_DE.md)

**Version 0.5.0 · Windows · World of Warcraft WotLK 3.3.5a**

Ein portabler Updater mit dunkler Oberfläche in Deutsch und Englisch. Er prüft eine hinterlegte Auswahl von 21 Addon-Paketen und kann ausgewählte Pakete installieren oder updaten. Die Addons stammen von GitHub, GitLab und ausgewählten ZIP-/Webseitenquellen. Nicht für aktuelle Retail- oder Classic-Clients gedacht.

## Herunterladen und starten

1. Auf der GitHub-Seite unter **Releases** die Datei **WoW-Addon-Updater.exe** herunterladen. Der GitHub-Knopf „Code → Download ZIP“ liefert den Quellcode, nicht die fertige EXE.
2. Die EXE in den WoW-Ordner direkt neben **Wow.exe** kopieren.
3. Updater starten. Vor dem Installieren oder Updaten WoW schließen.

Es ist keine Pfadeingabe nötig. Windows PowerShell 5.1 und .NET Framework 4.8 werden vorausgesetzt. Für geschützte Installationen, etwa unter „Programme (x86)“, kann der Updater Administratorrechte benötigen. Die EXE ist nicht digital signiert; Windows kann deshalb eine Warnung anzeigen.

## Bedienung

Über **⋯** oben links kann die Sprache zwischen **Deutsch und Englisch** gewechselt werden. Die Einstellung wird pro Windows-Benutzer gespeichert. Direkt im Menü gibt es **Nach Updates suchen**: Der Updater prüft nach manueller Anforderung das neueste öffentliche GitHub-Release dieses Projekts und fragt vor einer Installation nach. Ohne Release wird nichts installiert. Details zu Prüfsummen, temporärem Helfer und Wiederherstellung stehen in der ausführlichen Anleitung.

Für künftige Self-Updates muss ein reguläres Release mit einem Tag wie `v0.5.1` genau ein Asset `WoW-Addon-Updater.exe` enthalten. Dessen Produktversion muss zum Tag passen; GitHub muss für das Asset einen SHA-256-Digest liefern. Es werden keine Vorab-Releases oder Downgrades installiert. Die Anwendung bleibt eine einzelne portable EXE; der Helfer für den laufenden Dateiaustausch wird vorübergehend im WoW-Ordner angelegt und nach erfolgreichem Neustart entfernt.

**Teststand der Selbstaktualisierung:** Am 26.10.2026 wurde die Online-Updateprüfung mit Version 0.5.0 vom Projektbetreiber erfolgreich getestet: Der Updater stellte die Verbindung zu GitHub her, las die öffentlichen Release-Informationen und meldete korrekt „Der Updater ist aktuell.“ Lokale Tests decken EXE-Austausch, Neustart, Abbruch und Bereinigung ab. Der vollständige Online-Ablauf mit Download und Installation einer neueren öffentlichen Version wurde noch nicht getestet.

- **Addons auslesen / prüfen:** Erkennt bekannte installierte Addons, setzt deren Haken und vergleicht sie mit den heruntergeladenen Paketen. Dieser Schritt verändert keine installierten Addon-Dateien.
- **Auswahl installieren / updaten:** Installiert ausgewählte fehlende oder ungeprüfte Pakete und erkannte Updates. Bereits aktuell geprüfte Pakete werden übersprungen. Downloads aus der Prüfung werden innerhalb der Sitzung wiederverwendet.
- **Abbrechen:** Fordert einen sicheren Abbruch an. Ein bereits begonnener Dateiaustausch wird abgeschlossen oder zurückgerollt.
- **WoW starten:** Entfernt den Updater-Downloadcache, übergibt den Spielstart an den normalen Windows-Desktop, wenn der Updater erhöht läuft, und schließt das Fenster nach erfolgreicher Startanforderung. Windows-Explorer muss dazu mit normalen Benutzerrechten laufen. Bei Fehlern bleibt der Updater geöffnet. Den WoW-eigenen Cache und WTF löscht diese Funktion nicht.
- **Mehr:** Backups wiederherstellen, deren Speicherverbrauch sehen, alle Backups nach Bestätigung löschen und das Protokoll unter Dokumente/WoWUpdaterLogs speichern.

Die Liste ist alphabetisch sortiert. Während laufender Vorgänge bleiben die Haken gesperrt, Scrollen ist weiterhin möglich. Rot markiert den verfügbaren Abbruch, Blau erkannte ausgewählte Updates, Grün den Spielstart nach erfolgreichem Abschluss ohne offene Updates. Versionsangaben stammen aus den TOC-Dateien; der eigentliche Vergleich verwendet Datei-Prüfsummen.

## Unterstützte Addons

AllStats, AtlasLoot, Bagnon (optional), Cheese, ClassicAPI, CompactRaidFrame, CompactRaidFrame HealEx, DBM Warmane, Details WotLK, Leatrix Maps, Leatrix Plus, MinimapButtonButton, NotPlater, NPCScan, PallyPower, Postal, Questie 3.3.5a, TomTom, WeakAuras WotLK, WhatsTraining WotLK und WorldMapTooltipScale.

APIDocumentation gehört zum WeakAuras-Paket. Questie wird als **Questie-335** installiert, NotPlater als **NotPlater-3.3.5**. Neu gewünschte Addons werden durch manuelles Anhaken installiert. Quellen, Branches und Ordnerzuordnungen stehen in `addons.example.json` und sind in die EXE eingebettet. Eine externe `addons.json` ändert den Katalog der EXE nicht.

## Sicherungen und Grenzen

Ersetzte Addon-Ordner werden unter `Interface/AddOns_UpdaterBackups` gesichert. WTF-Einstellungen werden weder verändert noch mitgesichert. Eine zusätzliche eigene Sicherung von Interface und WTF wird empfohlen. Bei Paketen mit mehreren Ordnern müssen für eine vollständige Wiederherstellung gegebenenfalls mehrere Ordner desselben Sicherungsstands gewählt werden.

Warperia kann Downloads begrenzen. Der Updater umgeht keine Limits. Ein geprüfter Warperia-Download wird bis zu 24 Stunden lokal wiederverwendet; dabei kann ein älterer Quellenstand angezeigt werden. „WoW starten“ löscht diesen Cache, sodass beim nächsten Prüfen erneut Downloads nötig sind. Die Prüfung anderer Quellen lädt grundsätzlich Archive; es gibt keine reine Versionsabfrage. Die Addon-Verarbeitung erfolgt nacheinander.

Version 0.5.0 ist eine frühe Veröffentlichung. Downloadanbieter und Addon-Autoren können Strukturen ändern. Ein erfolgreicher Dateivergleich garantiert keine fehlerfreie Funktion im Spiel. Fehler bitte mit Updater-Version, Addon-Name und Protokoll melden; persönliche Pfade vor Veröffentlichung des Protokolls entfernen.

## Selbst bauen

Im Quellcode-Ordner unter Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Gui\Build.ps1
```

Das Skript verwendet den mit .NET Framework bereitgestellten C#-Compiler und erzeugt `WoW-Addon-Updater.exe`. Backend und Katalog werden eingebettet; zur Nutzung reicht die EXE. Versionsangaben stehen in `Gui/VersionInfo.cs` und müssen bei einer neuen Veröffentlichung gemeinsam angepasst werden.

Die ausführliche Bedienungsanleitung steht in [EXE-Anleitung.md](EXE-Anleitung.md). Änderungen dieser Veröffentlichung stehen in [CHANGELOG.md](CHANGELOG.md).

## Entstehung und Danksagung

Dieses Projekt wurde von **theprinceofbucket mit Unterstützung von OpenAI ChatGPT/Codex** erstellt. Die KI unterstützte bei Planung, Programmierung, Fehlersuche, Tests und Dokumentation. Entscheidungen, praktische Erprobung und Rückmeldungen erfolgten durch den Projektbetreiber. Dies ist ein unabhängiges Community-Projekt, kein offizielles Produkt von OpenAI oder Blizzard.

Vielen Dank an die Autorinnen und Autoren der unterstützten Addons. Deren Projekte bleiben eigenständig und unterliegen ihren jeweiligen Lizenzen.

## Lizenz

Der Quellcode dieses Updaters wird unter der [MIT-Lizenz](LICENSE) veröffentlicht. Diese Lizenz gilt nicht automatisch für die heruntergeladenen Addons. Im Repository werden keine Addon-Archive mitgeliefert.
