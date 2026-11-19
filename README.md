# PIA TIA Openness MCP

Arbeitsbasis für eigene MCP-Funktionen rund um Siemens TIA Portal. Der Fork basiert auf dem [TIA Portal Openness MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP). Für den übernommenen Code gilt die [MIT-Lizenz](#mit-license).

## Lokal starten

Unter Windows werden TIA Portal mit Openness, .NET Framework 4.8 und die lokale Windows-Gruppe `Siemens TIA Openness` benötigt. Die passende Version lässt sich mit `./tia.cmd doctor` (V21) oder `./tia-v20.cmd doctor` (V20) prüfen. V21 liegt unter `runtime/v21/`. Für V20 ist die passende Engine aus den [Upstream-Releases](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP/releases) erforderlich.

Für eine manuelle stdio-Konfiguration im MCP-Client den absoluten Pfad zur `TiaMcpServer.exe` und die Argumente `--tia-major-version 21` angeben. Die verfügbaren Tools meldet `tools/list`. Im Standardprofil finden `FindTools` und `CallTool` zusätzliche Tools.

## Eigene MCP-Funktionen

Eigene MCP-Tools, Wrapper und die zugehörigen Tests gehören nach [PIA-Custom/](PIA-Custom/). Wrapper können vorhandene TIA-Tools über die öffentliche MCP-Schnittstelle aufrufen. So lassen sich Erweiterungen entwickeln und der Upstream-Server separat aktualisieren.

Prompts und benötigte Funktionen werden pro Fähigkeit dokumentiert. Für neue Fähigkeiten gelten die [Entwicklungsregeln](PIA-Custom/AGENTS.md).

## Upstream-Updates

`origin` zeigt auf den Fork und `upstream` auf das Original. In einem neuen Klon lässt sich der Upstream-Remote mit `git remote add upstream https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP.git` ergänzen.

1. `git fetch upstream` lädt den neuen Stand.
2. `git log --oneline HEAD..upstream/master` zeigt die neuen Upstream-Commits.
3. Sobald die lokalen Änderungen committet sind, `git merge upstream/master` auf dem Arbeitsbranch ausführen. Bei Konflikten in Paketdateien die Bereinigung prüfen.
4. `./tia.cmd version`, `python scripts/Check-LiteProfile.py` und `python scripts/Check-DeadToolReferences.py` ausführen.

`templates/` dient der Pfadauflösung von `__BUNDLE__` in eigenen CLI-Spezifikationen. Der Server meldet seine Werkzeugliste über `tools/list`.
