# Create subcycle calls

## Prompt

Bereite im geöffneten TIA-Portal-Projekt einen Aufruf von `FB_MqttEvaluateSubCycle` vor. Der Baustein zeichnet die Dauer von Teilprozessen und Bewegungen auf, etwa eines Schraubvorgangs oder einer Zylinderbewegung, und meldet sie an die PIA Industrial App Suite (Optimum). Die Signale stammen häufig aus Stationsabläufen wie S7 GRAPH oder aus standardisierten Strukturen in Stations-DBs.

1. **Ziel und Schnittstelle bestimmen.** Ermittle die PLC, die Station, den aufrufenden Baustein und die Einfügestelle. Lies die Schnittstelle des eingesetzten `FB_MqttEvaluateSubCycle` über den PIA-MCP im geöffneten Projekt. Verwende die ermittelten Pinnamen und Datentypen für die Planung. Bei unklarer Zuordnung prüfe die Bibliotheksversion und frage bei mehreren Treffern nach. Verwende die Programmiersprache an der Einfügestelle, sofern der Benutzer keine andere vorgibt.

2. **Zeitaufzeichnung und Versand festlegen.** Kläre, welcher Zeitraum aufgezeichnet werden soll. Prüfe passende Signale aus dem Stationsablauf und vorhandene standardisierte DB-Strukturen. Zeige bei mehreren Treffern die vollständigen Symbolpfade und Datentypen zur Auswahl.

   - `Active` startet die Zeitmessung beim Wechsel auf `TRUE` und beendet sie beim Wechsel auf `FALSE`. Der Wechsel auf `FALSE` löst den Versand aus, wenn die Versandfreigabe während der Messung gesetzt war.
   - Der Eingang für die Versandfreigabe muss während `Active = TRUE` mindestens einmal `TRUE` sein. Zu diesem Zeitpunkt übernimmt der Baustein die Eingangsdaten intern. Kläre, unter welchen Bedingungen gesendet werden soll und wann eine Aufzeichnung bewusst ohne Versand bleibt. Verwende für die Versandfreigabe den Pinnamen aus der ausgelesenen Projektschnittstelle.

3. **Übrige Beschaltung ermitteln.** Beginne im Zielbaustein und folge dem Aufrufpfad zu den nächstgelegenen IAS-Aufrufen derselben Station. Nutze deren Beschaltung als Referenz für die Parameter, die der eingesetzte Subcycle-Baustein deklariert.

   | Parameter | Beschaltungsregel |
   | --- | --- |
   | `MachineName`, `ModuleName`, `StationName`, `RuntimeData`, `RuntimeBuffer`, `RuntimeSettings` | Übernimm für vorhandene Pins passende Signale aus den nächstgelegenen IAS-Aufrufen derselben Station. Frage bei fehlenden oder widersprüchlichen Referenzen nach. |
   | `SubcycleName` | Schlage einen Namen aus dem Arbeitsschritt oder dem verwendeten Signal vor. Orientiere dich beim Deklarationsort an benachbarten Aufrufen. Fehlt eine Referenz, schlage eine Konstante direkt im aufrufenden Baustein vor. |
   | `Result` | Suche eine zum Teilprozess passende Statusstruktur vom deklarierten Typ. Frage bei fehlender Zuordnung nach. Bleibt die Zuordnung offen, zeige `Result` in der Vorschau als unbeschaltet. Prüfe vor der Umsetzung, ob die Einfügesprache und die Schnittstelle dies zulassen. |
   | `ProcessBreak` | Lasse den Eingang bei der Planung der Subtaktlogik vorerst außer Betracht. Führe ihn in der Liste zur manuellen Prüfung auf, wenn er unbeschaltet bleibt. |
   | `PartId`, `Type` | Prüfe die Beschaltung benachbarter IAS-Aufrufe und gemeinsame Bauteilinformationen aus Stations-DBs. Frage nach, wenn die Zuordnung unklar ist. |

4. **Änderung vorschauen und freigeben lassen.** Zeige Zielstelle, Programmiersprache, FB-Instanz, `SubcycleName` samt Deklarationsort und die vollständige Pinbeschaltung. Zeige, wann die Messung beginnt und endet, wann die Versandfreigabe gesetzt wird und wann ein Versand ausbleibt. Schlage Netzwerktitel und Kommentare zu neuen Deklarationen vor. Liste unbeschaltete und bewusst leer belegte Eingänge einzeln mit Netzwerk, Baustein und Pin zur manuellen Prüfung auf. Warte auf die ausdrückliche Freigabe der konkreten Vorschau.

5. **Freigegebene Logik erstellen und prüfen.** Füge den Aufruf über den PIA-MCP ein, wenn der Schreibweg für Zielstelle und FB-Instanz unterstützt ist. Lies die geänderte Stelle zurück und vergleiche Instanz, Beschaltung, Titel und Kommentare mit der Vorschau. Übersetze die betroffene PLC-Software offline und melde Fehler, Warnungen und offene Prüfpunkte. Fehlt ein erforderlicher MCP-Aufruf, benenne die Lücke und frage nach der Freigabe für eine Erweiterung.

### Typische Fragen und erwartete Ausgabe

| Frage im Chat | Vorgehen und erwartete Ausgabe |
| --- | --- |
| „Zeichne die Dauer des Schraubvorgangs als Subtakt auf.“ | Ermittle Zielstation und Signal für den Schraubvorgang. Kläre die Versandfreigabe und schlage `SubcycleName` sowie die vollständige Beschaltung vor. |
| „Erfasse die Dauer der Zylinderbewegung und sende sie nur bei gültigen Bauteildaten.“ | Ermittle das Bewegungssignal und die Bedingung für gültige Bauteildaten. Zeige in der Vorschau, wann `Active` und die Versandfreigabe gesetzt werden und welche Bauteildaten der Baustein übernimmt. |
| „Bei Station 1000 Deckel fügen möchte ich das die Magnetkontrolle als Subcycle aufgezeichnet wird und zudem auch noch die einzelnen Zylinder Bewegungen die während dem Ablauf gemacht werden“ | Ermittle den Stationsablauf und die Signale für die Magnetkontrolle und jede einzelne Zylinderbewegung. Zeige je Subtakt den Namen, die Messgrenzen, die Versandfreigabe und die geplante Beschaltung. Kläre mehrdeutige Messgrenzen vor der Vorschau. |

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `GetProjectStructure` | `PIA-Custom/Server/ProjectStructureTool.cs` | PLC, Stationsordner und Zielbaustein eingrenzen. | implementiert |
| `GetPlcBlockInterface` | `PIA-Custom/Server/PlcInspectionTools.cs` | Für jeden Auftrag die Schnittstelle des eingesetzten Subcycle-Bausteins und bei Bedarf referenzierte Datentypen im geöffneten Projekt lesen. | implementiert |
| `CheckLibraryUsage` | `PIA-Custom/Server/LibraryUsageTool.cs` | Bibliothekstyp und Version bei unklarer Zuordnung prüfen. | implementiert |
| `GetPlcTags`, `GetCrossReferences`, `DescribeBlockLogic`, `GetPlcCallConnections`, `GetPlcGraph` | Upstream-MCP und `PIA-Custom/Server/` | Signale, Stationsabläufe, Einfügesprache und vorhandene IAS-Beschaltungen prüfen. | verfügbar |
| `PlanPlcFbdAppend`, `ApplyPlcFbdAppend`, `PlanPlcSclAppend`, `ApplyPlcSclAppend`, `GetPlcBlockSource` | `PIA-Custom/Server/PlcFbdTools.cs`, `PIA-Custom/Server/PlcSourceTools.cs` | Aufruf und Deklarationen vorschauen, nach Freigabe einfügen und zurücklesen. | Eignung für Zielstelle und FB-Instanz prüfen |
| `CompilePlcSoftware` | `PIA-Custom/Server/PlcCompileTool.cs` | Betroffene PLC-Software nach dem Einfügen offline übersetzen. | implementiert |

## Prüffall

- **Ausgangszustand** – Eine Station hat ein eindeutiges Signal für einen Arbeitsschritt, eine Bedingung für die Versandfreigabe und einen vorhandenen IAS-Aufruf als Referenz.
- **Eingabe** – „Erfasse die Dauer des Arbeitsschritts als Subtakt und sende ihn nur bei Versandfreigabe.“
- **Erwartetes Ergebnis** – Die Vorschau verwendet die vom MCP im Projekt gelesenen Pinnamen und Datentypen. Sie zeigt Signalpfade, FB-Instanz, `SubcycleName`, Beschaltung und offene Eingänge. Die Messung beginnt beim Wechsel von `Active` auf `TRUE`. Der Wechsel auf `FALSE` löst den Versand aus, wenn die Versandfreigabe während der Messung mindestens einmal `TRUE` war.
- **Zusätzlicher Prüffall** – Fehlt eine passende `Result`-Struktur, fragt die Fähigkeit nach. Bleibt die Zuordnung offen, weist die Vorschau den Eingang einzeln zur manuellen Prüfung aus.
- **Prüfstand** – Der konkrete Stationsaufruf und der Schreibweg für seine FB-Instanz sind noch zu prüfen.
