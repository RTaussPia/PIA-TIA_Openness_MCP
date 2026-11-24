# PIA Industrial App Suite Library

## Geltungsbereich und Versionierung

Die folgenden Angaben gelten ab **Bausteinversion 3.14.0**. Maßgeblich ist die Version des jeweiligen Bibliotheksbausteins im Projekt.

Ändert sich der Zweck, die Einsatzebene oder die Aufrufpflicht eines Bausteins, dokumentiere die Änderung mit der ersten betroffenen Bausteinversion. Erhalte die bisherige Beschreibung mit ihrem Versionsbereich, damit auch ältere Projekte anhand der dafür gültigen Regeln geprüft werden können.

## Hauptverbindung

`FB_MqttRuntime` stellt die Hauptverbindung zur IAS her. Der FB muss **mindestens einmal im Projekt** aufgerufen werden. Typischer Einsatzort ist der allgemeine Projektbereich.

## Features

Unter `Features` liegen die Funktionen, die je nach Projekt und Station eingesetzt werden.

| Unterordner | Zweck | Einsatzebene und Einsatzregel |
| --- | --- | --- |
| `Alarming` | HMI-Meldungen und Alarme an die IAS senden. | Wenn das Projekt ProDiag nutzt, sollte ein FB mit dem Namenspräfix `FB_MqttProDiag` einmal im allgemeinen Projektbereich aufgerufen werden. |
| `Logging` | Diagnosedaten protokollieren. | Optional. Einsatz dort, wo Diagnosedaten aufgezeichnet werden sollen. |
| `MachineState` | Maschinenstatus an die IAS senden. | Pflicht für **alle Stationen**, einschließlich Transfer- und Roboterstationen. Der Aufruf erfolgt direkt oder über einen Baustein aus `Templates`. |
| `Measurement` | Prozessdaten oder Messwerte an die IAS senden. | Optional. Einsatz an der Station, an der die Daten erfasst werden. |
| `Process` | Taktzeit und Bauteilstatus an die IAS senden. | Pflicht für **bearbeitende Stationen**. Bei Robotern und Roboterstationen ist `Process` zusammen mit `Subcycle` zur Jobaufzeichnung wünschenswert. Bei reinen Transferstationen ist `Process` unüblich. Der Aufruf erfolgt direkt oder über einen Baustein aus `Templates`. |
| `Sensor` | Zeitreihendaten an die IAS senden. | Optional. Einsatz an der Station, an der die Zeitreihendaten erfasst werden. |
| `Subcycle` | Subtakte oder Subprozesszeiten an die IAS senden. Ein Subtakt erfasst die Dauer eines einzelnen Arbeitsschritts während der Bearbeitung eines Bauteils. | Einsatz an Bearbeitungsstationen für die jeweils zu erfassenden Arbeitsschritte. Bei Robotern und Roboterstationen ist `Subcycle` zusammen mit `Process` zur Jobaufzeichnung wünschenswert. |

## Weitere Bibliotheksordner

| Ordner | Zweck | Verwendung im Projekt |
| --- | --- | --- |
| `Templates` | Vorlagenbausteine für Stationen. | Eine Station kann `MachineState` und `Process` über einen Vorlagenbaustein aufrufen. Für die Verwendungsprüfung zählt auch dieser indirekte Aufruf. |
| `BaseLogic` | Interne Hilfsfunktionen der IAS-Bibliotheksbausteine. | Die Bibliotheksbausteine rufen diese Funktionen intern auf. Ein direkter Projektaufruf ist nicht erforderlich. |
| `Siemens` | Siemens-Funktionen für die Hauptverbindung. | `FB_MqttRuntime` verwendet diese Funktionen intern. Ein direkter Projektaufruf ist nicht erforderlich. |
| `UDTs` | Datentypen der IAS-Bibliothek. | Die Bausteine referenzieren diese Datentypen in ihren Deklarationen. |
