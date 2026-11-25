# Compile PLC software

**Beispiel im Chat** – „Übersetze die PLC-Software für die geänderte Station und zeige Fehler und Warnungen.“

## Prompt

Ermittle das geöffnete TIA-Portal-Projekt und die gemeinte PLC. Frage bei mehreren möglichen PLCs nach der Auswahl. Übersetze die Software der ausgewählten PLC im aktuellen Projektstand, wenn der MCP ihren Offlinezustand bestätigt. Zeige den Übersetzungsstatus sowie Anzahl und Fundstellen der Fehler und Warnungen. Kennzeichne unvollständig gelesene Diagnosen. Speichere das Projekt nur nach ausdrücklicher Zustimmung.

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `GetProjectStructure` | `PIA-Custom/Server/ProjectStructureTool.cs` | Die gemeinte PLC ermitteln, falls sie noch nicht eindeutig ausgewählt wurde. | implementiert |
| `CompilePlcSoftware` | `PIA-Custom/Server/PlcCompileTool.cs` | Offlinezustand prüfen und Übersetzung mit Fehlern und Warnungen für die ausgewählte PLC ausführen. | implementiert |
| `GetExport`, `SaveProject` | Upstream-MCP, im PIA-Custom-Server registriert | Bei Bedarf alle Diagnoseseiten abholen und nur nach Zustimmung speichern. | registriert |

## Prüffall

- **Ausgangszustand** – Ein geöffnetes Projekt enthält eine eindeutig benannte Offline-PLC mit noch ungespeicherten Programmänderungen.
- **Eingabe** – „Übersetze die PLC-Software und zeige Fehler und Warnungen.“
- **Erwartetes Ergebnis** – Die Übersetzung erfasst den aktuellen Projektstand. Der Bericht enthält die Fehler- und Warnungsanzahl sowie verfügbare Fundstellen. Bei unbekanntem oder nicht bestätigtem Offlinezustand stoppt der Aufruf vor der Übersetzung.
