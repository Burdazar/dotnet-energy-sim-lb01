
<!--
Verpflichtende Tags pro Eintrag:

- Done: Was wurde bearbeitet und welches Ergebnis liegt vor?
- KI: Werkzeug, Modell, Einsatzform und Umgang mit dem Ergebnis; bei keiner KI-Nutzung: keine.
- Artefact: Betroffene Dateien, CSV/Kurve, Screenshot, Dokumentation oder andere Ergebnisse.

Optionale Tags bei Relevanz:
- Comment: Entscheidung, Problem, Erkenntnis oder nächster Schritt.
- Test: Durchgeführter manueller oder automatisierter Test.
-->

# Entwicklungsjournal Nazar Burdeinyi

## 2026-10-05 – Grundaufbau vom Interaktiven Batteriespeicher-Simulator

### Done:
- 24-Stunden-Simulation mit 15-Minuten-Schritten, Ladezustand, Energiezählern und Fehlerzuständen umgesetzt.
- Interaktive Steuerung für Manuellbetrieb, Laden, Entladen, Pause/Fortsetzen sowie Fehler auslösen und zurücksetzen ergänzt.
- Geräte mit Leistungsbedarf können angeschlossen werden. 
- Konsolendashboard mit strukturierten Kennzahlen, Ladezustandsbalken, ausgerichteten Tastenhinweisen und CSV-Ausgabe.
### KI:
GitHub Copilot GPT-6 Luna als IDE-Agent für 100% der Implementierung. Implementierungen wurden händisch geprüft und verstanden.
### Artefact: 
`Program.cs`, `Battery/BatteryModel.cs`, `Simulation/BatterySimulator.cs`, `Simulation/SimulationOptions.cs`, `Simulation/DeviceLoad.cs`, `ConsoleUi/ConsoleDashboard.cs`, `Persistence/CsvSimulationLogger.cs`; CSV-Zieldatei `data/battery-run.csv`.
### Test: 
Erfolgreiche Ausführung der Simulation mit manueller Steuerung und CSV-Ausgabe; Kleine Änderungen im Zeitformat in der CSV-Datei müssen vorgenommen werden, um eine korekte Erstellung von Grafiken zu ermöglichen.

#
## 2026-10-06 – Anpassungen zum Batteriespeicher-Simulator

### Done:
- Konfigurierbare Schrittweite,  umgesetzt.
- Historische Kennzahlen für _min_, _max_, _avg_ hinzugefügt.
- Geräte können mit Leistungsbedarf und Steckdosennummer verwaltet und im pausierten Zustand einzeln, mehrfach oder vollständig getrennt werden. `P` bricht eine unvollständige Entladekonfiguration ab und setzt die Simulation fort.
- Struktur vom Konsolendashboard verbessert.
- Notifications optimiert.
- Zeitformat in der CSV-Ausgabe angepasst, um eine korrekte Erstellung von Grafiken zu ermöglichen.
- Einheitliches Meter-Interface für Batterieleistung, Gerätebedarf und Nettoleistung ergänzt, um Skalierbarkeit zu gewährleisten.
- Observable-Benachrichtigungen für Messwerte implementiert, damit externe Komponenten Aktualisierungen abonnieren können (+ Skalierbarkeit).
- Autoszenario implementiert, das die Simulation automatisch durchläuft und die CSV-Datei erstellt.
### KI:
GitHub Copilot GPT-6 Luna als IDE-Agent für 100% der Implementierung. Implementierungen wurden händisch geprüft und verstanden.
### Artefact: 
`Simulation/Meters/EnergyMeters.cs`, `Simulation/Meters/MeterReadingObservable.cs`, `Simulation/SimulationHistory.cs`, `AutoScenario.cs`.
### Test: 
Erfolgreiche Ausführung der Simulation mit manueller Steuerung und CSV-Ausgabe mit folgender Chartersellung.
