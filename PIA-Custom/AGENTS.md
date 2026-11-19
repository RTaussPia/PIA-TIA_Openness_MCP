# Entwicklungsregeln für PIA-Custom

## 1. Jede Fähigkeit bekommt einen versionierten Prompt

Lege für jede neue Fähigkeit oder eigenständig nutzbare Funktion eine Datei unter `PIA-Custom/capabilities/` an. Verwende einen aussagekräftigen englischen Namen wie `ComparePlcBlocks.md` und die [Vorlage](capabilities/_template.md). Hilfsfunktionen dokumentierst du bei der Fähigkeit, die sie verwendet.

Schreibe den Prompt als eigenständig verständlichen Auftrag. Benenne die Eingaben, das gewünschte Verhalten und das überprüfbare Ergebnis. Bei Schreibzugriffen muss erkennbar sein, was sich in TIA ändern darf.

Checke den Prompt in Git ein. Wenn sich das Verhalten oder die benötigten Funktionen ändern, aktualisiere die Fähigkeitsdatei im selben Änderungssatz wie die Implementierung.

## 2. Jede Fähigkeit nennt ihre benötigten Funktionen

Führe unter **Benötigte Funktionen** die konkreten Aufrufe auf, von denen die Fähigkeit abhängt. Verwende für Upstream-MCP-Tools ihren registrierten Toolnamen und für eigene Wrapper oder Hilfsfunktionen den Funktionsnamen und den relativen Quellpfad. Gib zu jedem Eintrag an, wofür die Fähigkeit ihn braucht. Bei bedingten Aufrufen gehört die Auslösebedingung dazu.

Kennzeichne geplante Funktionen als `geplant`, bis sie implementiert sind. Prüfe vorhandene MCP-Toolnamen über `tools/list` im Vollprofil oder über `FindTools`. Kommt eine Fähigkeit ohne weitere Funktionsaufrufe aus, vermerke das ausdrücklich.
