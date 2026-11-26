# Entwicklungsregeln für PIA-Custom

## 1. Jede Fähigkeit bekommt einen versionierten Prompt

Lege für jede neue Fähigkeit oder eigenständig nutzbare Funktion eine Datei unter `PIA-Custom/capabilities/` an. Verwende einen aussagekräftigen englischen Namen wie `ComparePlcBlocks.md` und die [Vorlage](capabilities/_template.md). Hilfsfunktionen dokumentierst du bei der Fähigkeit, die sie verwendet.

Schreibe den Prompt als eigenständig verständlichen Auftrag. Benenne die Eingaben, das gewünschte Verhalten und das überprüfbare Ergebnis. Bei Schreibzugriffen muss erkennbar sein, was sich in TIA ändern darf.

Checke den Prompt in Git ein. Wenn sich das Verhalten oder die benötigten Funktionen ändern, aktualisiere die Fähigkeitsdatei im selben Änderungssatz wie die Implementierung.

## 2. Jede Fähigkeit nennt ihre benötigten Funktionen

Führe unter **Benötigte Funktionen** die konkreten Aufrufe auf, von denen die Fähigkeit abhängt. Verwende für Upstream-MCP-Tools ihren registrierten Toolnamen und für eigene Wrapper oder Hilfsfunktionen den Funktionsnamen und den relativen Quellpfad. Gib zu jedem Eintrag an, wofür die Fähigkeit ihn braucht. Bei bedingten Aufrufen gehört die Auslösebedingung dazu.

Kennzeichne geplante Funktionen als `geplant`, bis sie implementiert sind. Prüfe vorhandene MCP-Toolnamen über `tools/list` im Vollprofil oder über `FindTools`. Kommt eine Fähigkeit ohne weitere Funktionsaufrufe aus, vermerke das ausdrücklich.

## 3. Vorschauen für TIA-Programmänderungen

- Schlage für jedes neue Netzwerk einen aussagekräftigen Titel und für jede
  neu angelegte Variable oder Schnittstellendeklaration einen Kommentar vor.
  Zeige die Texte in der Änderungsvorschau, übernimm sie mit der freigegebenen
  Logik und prüfe sie beim Rücklesen.
- Liste unbeschaltete Eingänge und Eingänge mit bewusst leeren Werten in
  der Vorschau einzeln mit Netzwerk, Baustein und Pin auf. Kennzeichne,
  dass sie manuell geprüft und bei Bedarf nachbeschaltet werden müssen.
  Weise darauf hin, wenn die Schnittstelle nicht vollständig geprüft wurde.
- Übernimm bei bestehenden TIA-Texten die Sprachzuordnungen aus dem Export.
  Verwende für neue Netzwerktitel und Kommentare die Sprache der relevanten
  vorhandenen Texte, wenn sie eindeutig ist. Verwende sonst die aktive
  Bearbeitungssprache des Projekts. Zeige die gewählte Sprache in der Vorschau
  und prüfe sie beim Rücklesen.
- Zeige Bausteintypen in der Vorschau wie in der TIA-Bausteinschnittstelle.
  Erzeuge die Schreibweise für den SIMATIC-SD-Import anhand des eindeutig
  aufgelösten Bausteintyps. Akzeptiere beim Rücklesen abweichende
  Schreibweisen nur bei nachgewiesener Typgleichheit. Melde weitere
  Abweichungen mit ihrer Fundstelle und brich die Übernahme ab.
- Prüfe vor dem Anlegen von Variablen in Bausteinschnittstellen,
  Datenbausteinen oder PLC-Variablentabellen, ob es für vergleichbare
  Variablen ein Namensschema gibt. Prüfe zuerst ähnliche Variablen in
  derselben Station und bei Bedarf gleichartige Bausteinaufrufe in
  anderen Stationen. Übernimm ein eindeutig belegtes Schema einschließlich
  Präfixen und Suffixen. Zeige den vorgeschlagenen Namen und die Referenz
  in der Änderungsvorschau.
- Frage bei widersprüchlichen Schemata, welches Schema gelten soll und
  ob es im gesamten TIA-Projekt für diese Art von Variablen gelten soll.
  Bei bestätigter projektweiter Gültigkeit zeige den vorgesehenen Pfad
  und den genauen Wortlaut für eine lokale Markdown-Datei des Projekts.
  Speichere die Regel dort nach ausdrücklicher Freigabe.
- Übernimm beim Anlegen einer statischen Variable die Einstellungen für
  Erreichbarkeit aus HMI / OPC UA / Web API, Schreibbarkeit aus HMI /
  OPC UA / Web API, Sichtbarkeit im HMI Engineering und Einstellwert vom
  direkt vorhergehenden Eintrag in derselben Schnittstelle.
  Ohne vorhergehenden Eintrag setze alle vier Eigenschaften auf „Nein“.
  Zeige die vier Werte und ihre Herkunft in der Vorschau und prüfe sie
  nach dem Import durch Rücklesen. Halte bei unlesbaren Eigenschaften
  des vorhergehenden Eintrags vor dem Import an.

## 4. Temporäre PLC-Bausteine

Wenn eine PIA-Fähigkeit temporäre Bausteine im TIA-Projekt anlegt, gelten die
[gemeinsamen Regeln für temporäre PLC-Bausteine](knowledge/TemporaryPlcBlockRules.md).
Setze Ordner, Sperr-FB und Bereinigung für diese Fähigkeiten über denselben
MCP-Mechanismus um.
