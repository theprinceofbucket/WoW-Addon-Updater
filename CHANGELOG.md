# Änderungen

## 0.5.1 – 28.10.2026

- ClassQoLTalentWarning ergänzt; 22 eingebaute Pakete in alphabetischer Reihenfolge.
- Unveränderte Tabellenzellen und Spaltenüberschriften werden bei Protokollmeldungen nicht mehr erneut gesetzt; die pauschale Tabellen-Neuzeichnung entfällt.
- Separates sichtbares Löschen des Tabellenhintergrunds entfernt; der native Doppelpuffer zeichnet den Hintergrund.
- Eigene Addons über `addons.custom.json` neben der EXE; bestehende Einträge werden nicht überschrieben, fehlerhafte Einträge werden protokolliert und übersprungen.
- Deaktivierte Beispiele und Erklärung unterstützter Quellen in `addons.custom.example.json` sowie beiden READMEs.
- Lokale Tests für Sortierung, Ordnererkennung, JSON-Fehler, doppelte Einträge und unnötige Tabellen-Neuzeichnungen bestanden. Der Projektbetreiber bestätigte am 28.10.2026, dass das Tabellenflackern behoben ist und eigene Addons einwandfrei funktionieren.
- Vorabversion vom 27.10.2026 als reguläres Release freigegeben; die getestete EXE bleibt unverändert.

## 0.5.0 – 26.10.2026

Erste öffentliche Version. Frühere Entwicklungsstände sind nicht enthalten.

- Deutsche und englische Windows-Oberfläche im dunklen Design.
- Alphabetische Auswahl von 21 hinterlegten Addon-Paketen, einschließlich WorldMapTooltipScale.
- Lokale Erkennung, Datei-Prüfung, Installation und Updates mit ZIP-Wiederverwendung.
- Anzeige installierter und verfügbarer TOC-Versionen.
- Abbrechen, farbige Aktionsknöpfe und Spielstart über den Windows-Desktop.
- Backup-Verwaltung und Protokollexport.
- Versionsnummer im Fenstertitel und in den EXE-Dateieigenschaften.
- Menü ⋯ mit Deutsch/Englisch und gespeicherter Sprachauswahl.
- Manuelle Suche nach neueren Updater-Releases auf GitHub, Bestätigung vor Installation, SHA-256-Prüfung und temporärer Update-Helfer.
- Lokale Tests für Release-Metadaten, beschädigte Downloads, Versionsvergleich, EXE-Austausch, Neustart, Abbruch und Bereinigung.

Bekannte Grenzen: Downloads können Anbieterlimits unterliegen; nicht jeder Addon-Autor pflegt Versionsangaben. Der Start eines Spiels aus einem erhöhten Updater hängt von der lokalen Windows-Konfiguration ab. Die EXE ist nicht digital signiert.
