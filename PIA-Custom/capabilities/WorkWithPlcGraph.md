# Work with PLC GRAPH

**Beispiel im Chat** – „Zeige mir den Ablauf der Schrittkette und erstelle eine Vorschau für eine zusätzliche Aktion in einem ausgewählten Schritt.“

## Prompt

Ermittle die PLC, den GRAPH-FB und die gemeinte Schrittkette. Lies die Initialschritte und folge den Verbindungen über Transitionen, Zweige und Sprünge. Erkläre die möglichen Abläufe mit ihren Bedingungen. Lies die Aktionen, Verriegelungen, Überwachungen und permanenten Operationen gezielt nach Bedarf. Unterscheide den programmierten Ablauf von aktuell aktiven Schritten an der Anlage.

Füge neue Logik ausschließlich als Aktion direkt in den freigegebenen GRAPH-Schritten ein. Vorgeschaltete und nachgeschaltete permanente Anweisungen sind als Einfügeort für neue Logik ausgeschlossen.

Ermittle bei einem Schreibauftrag die betroffenen Schritte und die gewünschten Aktionen. Zeige in der Vorschau Schrittnummer, Schrittname, Aktion, Aktionskennzeichner und Operanden. Zeige bei Bedarf auch neue Deklarationen mit Speicherort und Kommentar sowie Auswirkungen auf die Bausteinschnittstelle.

Liegt im Änderungsbereich eine alternative oder parallele Verzweigung, zeige die betroffenen Zweige mit Schrittnummern und Namen. Frage den Benutzer für den konkreten Fall immer, welche Schritte berücksichtigt werden sollen. Verwende eine bereits ausdrücklich erteilte Auswahl für denselben Fall. Wenn der Änderungsbereich unklar ist oder der MCP die gewünschte Aktion nicht abdeckt, benenne den offenen Punkt und kläre das weitere Vorgehen. Warte vor dem Einfügen auf die ausdrückliche Freigabe der konkreten Vorschau.

Übernimm nur den freigegebenen Plan im gespeicherten Offlineprojekt. Vergleiche den erneut exportierten GRAPH-Baustein mit dem erwarteten Stand und brich bei zusätzlichen Abweichungen ab. Übersetze anschließend die betroffene PLC-Software und melde die Ergebnisse. Speichere nur nach ausdrücklicher Zustimmung. TIA Portal V21 kann beim unveränderten SimaticML-Import mit anschließender Offline-Übersetzung zusätzliche Übersetzungen für vorhandene GRAPH-Schnittstellenkommentare erzeugen. Nutze bei einer solchen Abweichung einen unveränderten Kontrollimport desselben Bausteins. Akzeptiere beim Vergleich nur die dadurch belegten zusätzlichen Einträge mit identischem Schnittstellenpfad, identischer Sprachkennung und identischem Text. Bereits vorhandene Spracheinträge und Texte müssen erhalten bleiben. Weitere Abweichungen führen zum Abbruch.

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `GetProjectStructure` | `PIA-Custom/Server/ProjectStructureTool.cs` | PLC und vollständigen Bausteinpfad ermitteln. | implementiert |
| `GetPlcGraph` | `PIA-Custom/Server/PlcGraphTools.cs` | Konfigurierten Ablauf, Schritte, Transitionen, Zweige und permanente Operationen lesen. Details gezielt je Schritt, Transition oder Operation abrufen. | implementiert |
| `PlanPlcGraphStepFlagActions`, `ApplyPlcGraphStepFlagActions` | `PIA-Custom/Server/PlcGraphWriteTools.cs` | Unterstützte N-Aktionen in ausgewählten GRAPH-Schritten vorschauen, nach Freigabe importieren und rücklesen. Bei Verzweigungen die bestätigten Schrittnummern übergeben. Weitere Aktionsarten anhand der verfügbaren MCP-Werkzeuge prüfen. | implementiert |
| `CompilePlcSoftware` | `PIA-Custom/Server/PlcCompileTool.cs` | Geänderte PLC-Software offline übersetzen und Diagnosen melden. | implementiert |
| `GetExport`, `SaveProject` | Upstream-MCP, im PIA-Custom-Server registriert | Alle relevanten Ergebnisseiten lesen und das Projekt nach Zustimmung speichern. | registriert |

## Prüffall

- **Ausgangszustand** – Eine Testkopie enthält den GRAPH-FB `M0800_BEL_SeqHoming` mit einer Initialstufe, einer Alternativverzweigung und einem Rücksprung.
- **Lesetest** – Ermittle Reihenfolge, Übergangsbedingungen und Aktionen anhand der Verbindungen. Die Auswertung zeigt den Rücksprung und beide Alternativen.
- **Schreibtest** – Plane eine unterstützte Aktion in einem eindeutig ausgewählten Schritt und vergleiche nach dem Import den zurückgelesenen Baustein mit der Vorschau. Eine zusätzliche Änderung an einer bestehenden Transition oder Aktion muss die Übernahme verhindern.
