# Check library usage

## Prompt

Prüfe im geöffneten TIA-Portal-Projekt die Verwendung von Bibliotheksbausteinen. Leite aus der Frage ab, welche Bibliothek, welche Bausteine oder Features und welche Ausgabe gemeint sind. Nutze bei PIA-Bezeichnungen das [Projektglossar](../knowledge/TiaPortalProjectConventions.md).

1. **Projekt prüfen.** Ist kein Projekt geöffnet, bitte den Benutzer, das gewünschte Projekt zu öffnen. Prüfe den Speicherzustand. Bei ungespeicherten Änderungen frage, ob das namentlich genannte Projekt gespeichert werden darf. Setze die Prüfung nach erfolgreichem Speichern fort. Ist der Speicherzustand technisch nicht feststellbar, bitte den Benutzer, den gespeicherten Stand zu bestätigen.
2. **Bibliothek und Prüfbereich bestimmen.** Wähle anhand der Frage die Projektbibliothek oder eine geöffnete globale Bibliothek. Prüfe den genannten Eintrag oder alle Einträge im gewählten Ordner einschließlich Unterordnern. Zeige bei Mehrdeutigkeit die möglichen Bibliotheken oder vollständigen Pfade. Schlage bei einem fehlenden Ordner ähnlich benannte vorhandene Ordner vor. Warte in beiden Fällen auf die Auswahl des Benutzers. Ist für eine IAS-Abfrage weder in der Projektbibliothek noch in den geöffneten globalen Bibliotheken eine IAS-Referenz verfügbar, frage nach der Referenzbibliothek oder dem maßgeblichen Bibliotheksstand.
3. **Projektobjekte zuordnen.** Prüfe alle relevanten PLCs. Ordne Bibliothekstypen über Typ-ID und Versions-ID ihren Projektinstanzen zu. Zeige gleichnamige Typen mit abweichender ID gesondert und frage, ob diese Identität separat geprüft oder übersprungen werden soll. Warte vor der separaten Prüfung auf die Entscheidung. Vergleiche bei Kopiervorlagen die Objektnamen und kennzeichne die Ergebnisse als Namenstreffer.
4. **Verwendungen ermitteln.** Ermittle die direkten Aufrufe und Referenzen der zugeordneten Projektobjekte. Für Stationsfragen erfasse zuerst alle Steuerkreise oder Module und Stationsordner. Gleiche den Bestand mit den direkten Aufrufen und den belegten Aufrufketten über Zwischenbausteine ab. Führe auch Stationen ohne Treffer auf. Ordne Verwendungen im allgemeinen Bereich der entsprechenden SPS oder dem Steuerkreis zu.
5. **Ergebnis ausgeben.** Bei einer Fundstellenliste gruppiere nach Bibliotheksbaustein und zeige je direkter Fundstelle den Bibliothekspfad, die PLC, das Projektobjekt, das referenzierende Objekt, die Verwendungsart und die verfügbare Netzwerk- oder Codeposition. Führe jeden geprüften Bibliothekseintrag auf. Unterscheide zwischen „kein zugehöriges Projektobjekt gefunden“, „Projektobjekt vorhanden, keine direkte Verwendung gefunden“ und „Prüfung unvollständig“. Benenne bei einer Lücke, welcher Bereich offen blieb.

### Typische Fragen und erwartete Ausgabe

| Frage im Chat | Erwartete Ausgabe |
| --- | --- |
| „Prüfe, welche Bausteine aus der Projektbibliothek ‚MachineState‘ im Projekt verwendet werden.“ | Alle Einträge im angegebenen Ordner und seinen Unterordnern mit ihren direkten Fundstellen oder dem jeweiligen Prüfstatus. |
| „Wo wird der Subcycle-FB aus der geöffneten Optimum-Bibliothek verwendet?“ | Die direkten Fundstellen des ausgewählten FB mit belegter Typ- und Versionszuordnung zur globalen Bibliothek. |
| „Welche IAS-Bausteine sind im Projekt verwendet?“ | Alle Steuerkreise oder Module und Stationen mit ihren verwendeten IAS-Bausteinen. Übergreifende Verwendungen auf der zugehörigen SPS- oder Steuerkreisebene aufführen. |
| „Wo sind überall die Prozessbausteine drin?“ | Alle Stationen mit dem Ist-Stand für `Process`. Bei einem Treffer den aufrufenden FB im Stationsordner angeben und bei Bedarf die Aufrufkette zeigen. |
| „Fehlen irgendwo Process- oder Subcycle-Aufrufe?“ | Alle Stationen mit je einer Spalte für `Process` und `Subcycle`. Fehlende Aufrufe anhand der Stationsart und der [Einsatzregel für die jeweilige IAS-Bausteinversion](../knowledge/PiaIndustrialAppSuiteLibrary.md) bewerten. Fehlende Pflichtaufrufe und offene Empfehlungen separat ausweisen. |

Verwende für Stationsübersichten eine Tabelle mit einer Zeile je Station. Die erste Spalte heißt **Station**. Danach folgt je eine Spalte pro abgefragter Funktion in der Reihenfolge der Frage. Zeige den Ist-Stand jeder Funktion als Ampel. Falls aufrufende FBs angegeben werden, stehen sie in der letzten Spalte **Aufrufender FB**.

Sind Steuerkreise oder Module vorhanden, gruppiere die Stationen nach der im Projekt ermittelten Hierarchie. Verwende die übergeordneten Ebenen als Überschrift über der jeweiligen Tabelle, zum Beispiel **Steuerkreis CC1 → Modul 2**. So bleibt **Station** die erste Spalte. Bei einer Textausgabe zeige dieselbe Hierarchie als eingerückte Liste. Kennzeichne Stationen mit unklarer Zuordnung entsprechend.

Verwende in Stationsübersichten 🟢 für eine belegte Verwendung, 🔴 für eine vollständig geprüfte Station ohne Verwendung und ⚪ bei unvollständiger Prüfung oder unklarer Zuordnung. Die Ampel zeigt den Ist-Stand. Ob ein fehlender Aufruf eine Abweichung ist, ergibt sich aus der Einsatzregel.

## Benötigte Funktionen

| Aufruf | Herkunft / Quellpfad | Zweck und Bedingung | Status |
| --- | --- | --- | --- |
| `CheckLibraryUsage` | `PIA-Custom/Server/LibraryUsageTool.cs` | `scope` wählt `project` oder `global`. `library` identifiziert die globale Bibliothek. Genau eines von `folder` und `entry` wählt den Prüfbereich. `includeReferences` steuert die Detailabfrage (Standardwert `true`). | implementiert |
| `GetProjectStructure` | `PIA-Custom/Server/ProjectStructureTool.cs` | PLCs, Software Units und Programmordner mit vollständigen Pfaden liefern. Der Client ordnet die Ordner anhand des PIA-Projektwissens Steuerkreisen, Modulen und Stationen zu. | implementiert |
| `GetCrossReferences`, `DescribeBlockLogic` | Upstream-MCP, im PIA-Custom-Server registriert | Aufrufketten über konkrete Bausteinpfade verfolgen und bei Bedarf die Aufrufstelle im Baustein prüfen. | registriert |
| `GetProjectKnowledge` | `PIA-Custom/Server/ProjectKnowledgeTool.cs` | Die mitgelieferte Fähigkeitsbeschreibung, das Projektglossar oder die IAS-Bibliotheksbeschreibung für den Chat-Client bereitstellen. | implementiert |
| `FindControllers`, `ReadReferences` | `PIA-Custom/Server/LibraryUsage.cs` | PLCs und direkte Verwendungsstellen ermitteln. | implementiert |
| `MasterCopyUsage.Read` | `PIA-Custom/Server/MasterCopyUsage.cs` | PLC-Blöcke und Datentypen für Kopiervorlagen über ihre Namen suchen. | implementiert |
| `ListPortalProcessProjects`, `AttachToOpenProject`, `SaveProject`, `GetExport` | Upstream-MCP | Projekt bei Bedarf auswählen, nur nach Zustimmung speichern, große Ergebnisse bis zum Ende lesen. | registriert |

## Aufruf und Ergebnis

Das C#-Projekt `PIA-Custom/Server/PiaTiaMcpServer.csproj` in Visual Studio mit .NET-Desktopentwicklung öffnen. Voraussetzungen sind .NET Framework 4.8, der V21-Laufzeitordner dieses Repos und installierte TIA Portal V21 Openness-Komponenten. Mit **Debug / F5** startet der stdio-Server. Alternativ den Server als Debug bauen und aus dem MCP-Client starten. Zum Debuggen eines Clientaufrufs den Visual-Studio-Debugger an dessen laufenden `PiaTiaMcpServer.exe`-Prozess anhängen.

- **MCP-Transport:** stdio. Programm nach dem Debug-Build: `PIA-Custom/Server/bin/Debug/net48/PiaTiaMcpServer.exe`. Keine Startargumente erforderlich.
- **Beispielargumente:** `{"scope":"project","folder":"Types/+++ PIA LineController LIB +++/PIA LineController/Parameter"}`.
- **Rückgabe:** Bei `status=report` enthält `entries` die Anzahl der Bibliothekseinträge. `results` enthält pro Eintrag die Identität, Projektinstanzen und Hinweise. Bei `requiresDecision` die Abweichungen unter `identityConflicts` anzeigen und die Entscheidung des Benutzers abwarten. Verwendungszeilen liefern PLC, Projektobjekt, Version, referenzierendes Objekt, Verwendungsart und TIA-Ortsangabe. `complete=false` erfordert die Auswertung von `issues` und unvollständigen Zeilen.
- **Rückfragen:** `openProjectRequired`, `selectProject`, `attachRequired`, `saveApprovalRequired`, `confirmSavedState`, `selectFolder` und `folderNotFound` erfordern die entsprechende Auswahl oder Bestätigung aus dem Prompt. `invalidFolder` meldet eine ungültige Eingabe. `incomplete` meldet einen technischen Abbruch. Bei Kopiervorlagen ohne Instanzverknüpfung bleibt die Herkunft offen.
- **Automatisierte Prüfungen** – `dotnet run --project PIA-Custom/tests/UsageTests.csproj` prüft die Auswertungsregeln. Nach dem Debug-Build prüft `python PIA-Custom/tests/test_usage_protocol.py` die MCP-Registrierung und Eingabevalidierung. Mit einem zusätzlichen Ordnerpfad als Argument prüft dasselbe Skript das gespeicherte, geöffnete Projekt samt Seitenausgabe.

## Prüffall

- **Ausgangszustand** – Gespeichertes Testprojekt mit einem Bibliotheksordner `MachineState`. Ein Projektbaustein ist nachweislich dem enthaltenen `FB_Valve` zugeordnet. `FB_Station` ruft ihn in den Netzwerken 2 und 5 auf. `OB1` ruft nur `FB_Station` auf. Für einen zweiten Baustein im Unterordner existiert kein zugehöriger Projektbaustein.
- **Eingabe** – Beispielaufruf oben mit `MachineState`.
- **Erwartetes Ergebnis** – Zwei Zeilen für die direkten Aufrufe in `FB_Station`. Der indirekte Aufruf über `OB1` erscheint nicht als Fundstelle. Der Baustein im Unterordner erscheint mit dem Ergebnis „kein zugehöriger Projektbaustein gefunden“, sofern die Zuordnungen vollständig geprüft werden konnten.
- **Live-Test am 23.11.2026** – Der direkte MCP-Aufruf lieferte im gespeicherten V21-Testprojekt für den ausgewählten Parameter-Ordner 11 zugeordnete Bibliothekseinträge und 75 direkte Verwendungsstellen. Alle Antwortseiten wurden gelesen. Die beiden direkten Aufrufe desselben FB in den Netzwerken 6 und 18 wurden bestätigt.

Ein umbenannter Baustein mit passender Typ- und Versions-ID wird gefunden. Ein gleichnamiger Typ mit abweichender Typ-ID oder Versions-ID erscheint als Hinweis mit Rückfrage. Seine Verwendungsstellen zählen erst nach einer separaten, ausdrücklich beauftragten Prüfung zum Bericht über diesen anderen Typ. Eine Kopiervorlage liefert Namenstreffer. Diese Regeln gelten in beiden Bibliotheksarten.

### Frühere Prüfung des Upstream-Vollprofils

Die 238 Tools des getesteten V21-Vollprofils konnten ein geöffnetes Projekt anbinden sowie nach Freigabe speichern und den Wechsel von `IsModified` auf `false` bestätigen. `GetObjectProperty` und `ListObjectChildren` erreichten die oberste Bibliotheksebene. Der Versuch, die Unterordner über Namen, Index oder `Find(String name)` zu lesen, scheiterte an der Adressierung der Ordnersammlung. `GetProjectTree` scheiterte in dieser Installation beim Laden von WinCC Unified. Für PLC-Stichproben funktionierten `GetDevices`, `GetBlocks`, `GetTypes` und `GetCrossReferences`. Für einen FB lieferte die ungefilterte Referenzabfrage 307 Einträge, für einen UDT 17. Die Erweiterung verwendet die Typversion und das konkrete Projektobjekt für die Zuordnung.
