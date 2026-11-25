# Temporary PLC blocks

Diese Regel gilt für alle PIA-Fähigkeiten, wenn sie temporäre PLC-Bausteine im TIA-Projekt anlegen.

1. Lege temporäre Bausteine offline im Programmbausteinordner `--- PIA Temp ---` der betroffenen PLC an. Kopiere für eine Gegenprüfung nur die dafür benötigten Bausteine.
2. Lege beim Erstellen des Ordners den Sperr-FB `FB_DeleteTempFolder` an. Sein erstes Netzwerk enthält absichtlich ungültigen Programmcode. Prüfe vor dem ersten temporären Baustein durch eine vollständige Offline-Übersetzung der PLC-Software, dass der Sperr-FB einen Übersetzungsfehler verursacht. Prüfe vor jedem weiteren Einfügen, dass der Sperr-FB vorhanden ist und weiterhin den geprüften Inhalt hat. Brich ab, wenn die Sperre nicht nachweisbar ist.
3. Entferne nach der Gegenprüfung und bei einem Abbruch alle für den Auftrag angelegten temporären Bausteine. Entferne den Sperr-FB, wenn er für den Auftrag angelegt wurde und keine temporären Bausteine mehr vorhanden sind. Entferne den Ordner, wenn er für den Auftrag angelegt wurde und leer ist.
4. Melde verbleibende Objekte, wenn die Bereinigung fehlschlägt, und halte die Bearbeitung an. Übersetze den endgültigen Projektstand offline nach erfolgreicher Bereinigung. Speichere nur nach ausdrücklicher Zustimmung.
