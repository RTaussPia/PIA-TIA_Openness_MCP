# Export Excel

**Beispiel im Chat** – „Exportiere diese Übersicht als Excel-Datei.“

## Prompt

Leite aus dem Chat einen kurzen Dateititel, passende Spaltenüberschriften und die zugehörigen Datenzeilen ab. Verwende eine eindeutige, bereits abgestimmte Übersicht direkt. Sind Inhalt, Spalten oder Zuordnungen unklar, schlage eine Tabelle vor und frage gezielt nach. Übernimm belegte Angaben und kennzeichne unklare Werte entsprechend.

Erstelle genau ein Excel-Blatt. Schreibe die Spaltenüberschriften in die erste Zeile und die Daten unmittelbar darunter. Gib den Pfad zur erstellten Datei aus. Der Dateiname enthält den Titel aus dem Chat und die lokale Exportzeit im Format `_YYYY-MM-DD_HH-mm.xlsx`. Der Bindestrich zwischen Stunde und Minute ist in Windows-Dateinamen zulässig.

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `ExportExcel` | `PIA-Custom/Server/ExcelExportTool.cs` | Schreibt `title`, `columns` und `rows` als `.xlsx` unter `Documents/PIA-MCP/Exports` auf dem MCP-Rechner. Übergib 1 bis 64 eindeutige Überschriften und höchstens 5.000 gleich breite Datenzeilen. Zellwerte sind Text, Zahlen, Wahrheitswerte oder `null`. | implementiert |
| `GetProjectKnowledge` | `PIA-Custom/Server/ProjectKnowledgeTool.cs` | Liefert diesen Prompt über `document=excelExport`. | implementiert |

## Prüffall

- **Ausgangszustand** – Im Chat liegt eine bestätigte Übersicht der Maschinenstatus-Aufrufe vor.
- **Eingabe** – „Exportiere diese Übersicht als Excel-Datei.“
- **Erwartetes Ergebnis** – Ein Blatt mit den Spalten „Steuerkreis bzw. Zone“, „Station“, „Aufgerufen“ und „Aufgerufen in“. Die Daten stehen direkt unter der Kopfzeile. Der Dateiname enthält den aus dem Kontext abgeleiteten Titel und die Exportzeit.
