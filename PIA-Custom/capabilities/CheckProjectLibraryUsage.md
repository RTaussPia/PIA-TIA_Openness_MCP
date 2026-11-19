# Check project library usage

**Beispiel im Chat** – „Prüfe, welche Bausteine aus der Projektbibliothek ‚ValveBlocks‘ im Projekt verwendet werden.“

## Prompt

Prüfe im aktuell geöffneten TIA-Portal-Projekt, welche Bausteine aus dem Projektbibliotheksordner „{Ordnername oder Ordnerpfad}“ verwendet werden. Zeige für jeden Bibliotheksbaustein, wo und wie er im Projekt direkt aufgerufen oder referenziert wird.

1. **Projekt prüfen.** Ermittle das geöffnete Projekt. Ist keines geöffnet, bitte mich, das gewünschte Projekt zu öffnen. Prüfe den Speicherzustand. Bei ungespeicherten Änderungen frage mich, ob du das namentlich genannte Projekt speichern darfst. Setze die Prüfung nach erfolgreichem Speichern fort. Ist der Speicherzustand technisch nicht feststellbar, bitte mich zu bestätigen, dass der aktuelle Stand gespeichert ist.
2. **Ordner bestimmen.** Suche den angegebenen Ordner in der Projektbibliothek. Ist der Name mehrdeutig, zeige die vollständigen Pfade der Treffer. Fehlt der Ordner, schlage vorhandene Ordner mit ähnlichem Namen vor. Warte in beiden Fällen auf meine Auswahl.
3. **Direkte Verwendung ermitteln.** Erfasse alle Bausteine im gewählten Ordner einschließlich seiner Unterordner. Prüfe über alle PLCs des Projekts, welche Projektbausteine dem jeweiligen Bibliotheksbaustein zugeordnet werden können. Ermittle für jede belegte Zuordnung die direkten Aufrufe und Referenzen. Gleiche Namen allein gelten nicht als Beleg.
4. **Ergebnis als Tabelle ausgeben.** Gruppiere nach Bibliotheksbaustein und verwende eine Zeile je direkter Fundstelle. Zeige Bibliothekspfad und Baustein, PLC und zugeordneten Projektbaustein, aufrufendes oder referenzierendes Objekt, Verwendungsart sowie Netzwerk oder Codeposition, soweit verfügbar.
5. **Jeden Bibliotheksbaustein aufführen.** Gibt es keine belegte Fundstelle, unterscheide zwischen *kein zugehöriger Projektbaustein gefunden*, *Projektbaustein vorhanden, keine direkte Verwendung gefunden* und *Prüfung unvollständig*. Gib im letzten Fall konkret an, welche Zuordnung, PLC oder Referenz nicht geprüft werden konnte.

## Benötigte Funktionen

| Tool oder benötigter Schritt | Herkunft | Zweck und Aufrufbedingung | Stand |
| --- | --- | --- | --- |
| `GetState`, `ListPortalProcessProjects`, `AttachToOpenProject` | Upstream-MCP | Aktive Sitzung prüfen. Bei Bedarf ein bereits geöffnetes Projekt identifizieren und anbinden. | Registriert |
| Speicherzustand ermitteln | Zu prüfen | Vor der Auswertung feststellen, ob Änderungen ausstehen. | Toolzuordnung offen |
| `SaveProject` | Upstream-MCP | Nach ausdrücklicher Zustimmung speichern und das Ergebnis prüfen. | Registriert |
| Projektbibliotheksordner und Bausteine auflisten | Zu prüfen | Pfade für die Ordnerauswahl ermitteln und den ausgewählten Teilbaum vollständig erfassen. | Toolzuordnung offen |
| `GetDevices`, `GetSoftwareTree`, `GetSoftwareUnits` | Upstream-MCP | PLCs und Projektbausteine erfassen, gegebenenfalls einschließlich Software Units. | Registriert |
| Bibliothekszugehörigkeit nachweisen | Zu prüfen | Einen Projektbaustein eindeutig dem Bibliotheksbaustein zuordnen. | Toolzuordnung offen |
| `GetCrossReferences`, `DescribeBlockLogic` | Upstream-MCP | Direkte Referenzen suchen und bei Bedarf die Fundstelle im aufrufenden Baustein näher bestimmen. | Registriert, Abdeckung im Prüffall offen |

## Prüffall

- **Ausgangszustand** – Gespeichertes Testprojekt mit einem Bibliotheksordner `ValveBlocks`. Ein Projektbaustein ist nachweislich dem enthaltenen `FB_Valve` zugeordnet. `FB_Station` ruft ihn in den Netzwerken 2 und 5 auf. `OB1` ruft nur `FB_Station` auf. Für einen zweiten Baustein im Unterordner existiert kein zugehöriger Projektbaustein.
- **Eingabe** – Beispielaufruf oben mit `ValveBlocks`.
- **Erwartetes Ergebnis** – Zwei Zeilen für die direkten Aufrufe in `FB_Station`. Der indirekte Aufruf über `OB1` erscheint nicht als Fundstelle. Der Baustein im Unterordner erscheint mit dem Ergebnis „kein zugehöriger Projektbaustein gefunden“, sofern die Zuordnungen vollständig geprüft werden konnten.
- **Prüfstand** – Toolnamen im V21-Vollprofil bestätigt. Funktionstest an einem TIA-Projekt und Entscheidung über mögliche Erweiterungen stehen aus.
