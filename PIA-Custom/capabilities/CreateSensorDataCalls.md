# Create sensor data calls

## Prompt

Bereite im geöffneten TIA-Portal-Projekt einen Aufruf vor, der einen Sensorwert in einem festgelegten Intervall oder bei einem festgelegten Ereignis an die IAS sendet. Führe den Benutzer mit gezielten Rückfragen durch die offenen Punkte.

1. **Ziel bestimmen.** Ermittle die PLC, die Station, den Zielbaustein und die gewünschte Einfügestelle. Prüfe die Programmiersprache an der Einfügestelle und verwende dieselbe Sprache, sofern der Benutzer keine andere vorgibt. Frage nach, wenn der Einbauort oder die Sprache unklar ist.
2. **Sensorwert ermitteln.** Suche den angegebenen Wert in PLC-Variablentabellen, FB- und FC-Schnittstellen sowie Datenbausteinen. Grenze die Suche anhand der Anlage, der Station und der genannten Baugruppe ein. Zeige bei mehreren Treffern die vollständigen Symbolpfade mit Datentyp zur Auswahl. Frage bei fehlendem Treffer nach einer genaueren Bezeichnung.
3. **Auslöser und Beschaltung klären.** Prüfe die Schnittstellen der im Projekt eingesetzten Sensorbausteine. Übernimm bereits genannte Angaben und frage die für die Beschaltung noch fehlenden Informationen ab. Dazu gehören je nach Schnittstelle der zu sendende Wert, der Auslöser (Intervall oder Ereignis), die Grenzwerte, der Bauteiltyp, die Einheit, der Datentyp und der Umgang mit relevanten Ausgängen.

   Kläre bei einem Wertwechsel, welcher Wert den Versand auslöst und ob beim ersten Aufruf bereits gesendet werden soll.

4. **IAS-Beschaltung ermitteln.** Verbinde standardmäßig den Ausgang `Message` von `FC_MqttBuildSensor` direkt mit dem Eingang `Message` von `FC_MqttSendSensorValue`, sofern die geprüften Schnittstellen zusammenpassen und der Benutzer keine andere Beschaltung vorgibt. Ermittle `MachineName`, `ModuleName`, `StationName`, `RuntimeData` und `RuntimeBuffer` aus bestehenden IAS-Aufrufen. Beginne im Zielbaustein und folge dem Aufrufpfad zu den nächstgelegenen IAS-Aufrufen derselben Station. Nutze deren Beschaltung als Referenz. Frage den Benutzer bei fehlenden oder widersprüchlichen Referenzen nach den betroffenen Signalen.
5. **Änderung vorschauen und freigeben lassen.** Zeige vor jeder Projektänderung die PLC, den Zielbaustein, die Einfügestelle, die Programmiersprache, den vollständigen Symbolpfad, den Auslöser, die vorgesehenen Aufrufe und die Beschaltung. Zeige bei verfügbarer Vorschaufunktion den geplanten Code oder Netzwerkentwurf. Warte, bis alle Rückfragen geklärt sind und der Benutzer die aktuelle Fassung ausdrücklich freigegeben hat. Zeige bei Korrekturen die aktualisierte Fassung erneut zur Freigabe.

   Zeige in der Vorschau aussagekräftige Titel für neue Netzwerke und Kommentare für neu angelegte Variablen und Schnittstellendeklarationen. Liste unbeschaltete Eingänge und Eingänge mit bewusst leeren Werten einzeln mit Netzwerk, Baustein und Pin auf und kennzeichne sie zur manuellen Prüfung und gegebenenfalls Nachbeschaltung.

   Lies nach der Umsetzung die freigegebenen Netzwerktitel und Kommentare zusammen mit der Beschaltung zurück.

6. **Freigegebene Logik erstellen und prüfen.** Setze die freigegebene Änderung über den PIA-MCP im Zielbaustein um. Lies die geänderte Stelle zurück und vergleiche Aufrufe, Auslöser und Beschaltung mit der Vorschau. Übersetze danach die betroffene PLC-Software im Offlinezustand und melde die Fehler und Warnungen. Melde, was geprüft werden konnte und was offen bleibt. Fehlt ein erforderlicher MCP-Aufruf, benenne die Lücke und frage nach Freigabe für die Erweiterung.

### Typische Fragen und erwartete Ausgabe

| Frage im Chat | Vorgehen und erwartete Ausgabe |
| --- | --- |
| „Ich möchte einen Sensorwert senden.“ | Frage nach dem zu sendenden Wert, dem Intervall und dem Einbauort, soweit diese Angaben fehlen. Ermittle die vorhandene Programmiersprache und kläre die offenen Ein- und Ausgänge. Zeige die Vorschau und warte auf die Freigabe. |
| „Sende alle 5 Sekunden den Wert von `ForceNm` von der Presse `PressingUnit` im Status-DB.“ | Suche `ForceNm` mit Bezug zu `PressingUnit`, insbesondere im genannten Status-DB. Prüfe den vollständigen Symbolpfad und den Datentyp. Übernimm das Intervall von fünf Sekunden. Kläre nur noch offene Angaben und zeige die Vorschau zur Freigabe. |

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `GetProjectStructure` | `PIA-Custom/Server/ProjectStructureTool.cs` | PLC, Stationsordner und Zielbaustein eingrenzen. | implementiert |
| `CheckLibraryUsage` | `PIA-Custom/Server/LibraryUsageTool.cs` | Eingesetzte IAS-Sensorbausteine und vorhandene Aufrufe ermitteln, soweit sie über die Bibliothek zugeordnet werden können. | implementiert |
| `GetCrossReferences`, `DescribeBlockLogic` | Upstream-MCP, im PIA-Custom-Server registriert | Aufrufpfad, Programmiersprache und lesbare Beschaltung bestehender IAS-Aufrufe prüfen. | registriert |
| `GetPlcTags` | Upstream-MCP, im PIA-Custom-Server registriert | PLC-Variablentabellen gezielt nach Namen durchsuchen. | registriert |
| `GetPlcBlockInterface` | `PIA-Custom/Server/PlcInspectionTools.cs` | Deklarationen von FBs, FCs und DBs lesen oder einen konkreten Memberpfad einschließlich referenzierter UDTs prüfen. | implementiert |
| `GetPlcCallConnections` | `PIA-Custom/Server/PlcInspectionTools.cs` | Aufrufe in FBD- und LAD-Netzwerken mit ihren Pins und Verbindungen auslesen. SCL-Aufrufe bei Bedarf mit `DescribeBlockLogic` prüfen. | implementiert |
| `PlanPlcFbdAppend`, `ApplyPlcFbdAppend`, `GetPlcBlockSource` | `PIA-Custom/Server/PlcFbdTools.cs`, `PIA-Custom/Server/PlcSourceTools.cs` | Neue FBD-Netzwerke mit Deklarationen, Titeln und Kommentaren vorschauen, offene Eingänge ausweisen, freigegebenen Plan einfügen und zurücklesen. | implementiert |
| `PlanPlcSclAppend`, `ApplyPlcSclAppend`, `GetPlcBlockSource` | `PIA-Custom/Server/PlcFbdTools.cs`, `PIA-Custom/Server/PlcSourceTools.cs` | Ein einzelnes SCL-Netzwerk in einem vorhandenen FBD-Funktionsbaustein vorschauen, Titel, Kommentare und offene Eingänge ausweisen, den freigegebenen Plan einfügen und zurücklesen. | implementiert |
| `CompilePlcSoftware` | `PIA-Custom/Server/PlcCompileTool.cs` | Die betroffene PLC-Software nach dem Einfügen offline übersetzen und Fehler und Warnungen melden. | implementiert |

## Prüffall

- **Ausgangszustand** – Ein geöffnetes Testprojekt enthält einen eindeutigen `ForceNm`-Wert im Status-DB von `PressingUnit` und einen bestehenden IAS-Aufruf in der Zielstation.
- **Eingabe** – „Sende alle 5 Sekunden den Wert von `ForceNm` von der Presse `PressingUnit` im Status-DB.“
- **Erwartetes Ergebnis** – Der vollständige Symbolpfad und Datentyp werden ermittelt. Die Vorschau zeigt das Intervall von fünf Sekunden, die aus der Station abgeleitete IAS-Beschaltung und die Verbindung über `Message`. Erst nach ausdrücklicher Freigabe wird die Logik eingefügt und zurückgelesen.
- **Zusätzlicher Prüffall** – Liefert die Suche mehrere `ForceNm`-Treffer oder fehlt eine IAS-Referenz, zeigt die Fähigkeit die Treffer oder fehlenden Signale und wartet auf die Klärung.
- **SCL-Prüffall** – Bei einem bestätigten Wertwechsel zeigt die Vorschau genau ein SCL-Netzwerk im gewählten FBD-Funktionsbaustein. Sie nennt das auslösende Signal, das Verhalten beim ersten Aufruf, die Verbindungen der IAS-Bausteine und die offenen Eingänge. Ein fehlender Signalpfad führt zur Rückfrage.
- **Prüfstand** – Die vorhandenen Toolnamen wurden über `tools/list` geprüft. Der vollständige Sensorablauf ist noch am Testprojekt zu verifizieren.
