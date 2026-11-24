# PIA-Begriffe und Konventionen für TIA Portal

## Module, Steuerkreise und Stationen

Module und Steuerkreise bilden die Ebene unterhalb einer Montagelinie.

| Begriff | Typische Kennzeichnung | Bedeutung |
| --- | --- | --- |
| Steuerkreis | `ST` (deutsch), `CC` (englisch) | `CC` steht für *Control Circuit*. Mehrere Steuerkreise werden häufig nummeriert, etwa `CC1`, `CC2`. |
| Station | z. B. `ST100`, `ST200` | Die Zahl bezeichnet die Station. `ST` kann je nach Projektkontext auch für einen Steuerkreis stehen. |
| Stationsart | `M`, `H`, `A` | Manche Projekte kennzeichnen Stationen mit einem Präfix: `M` für manuell, `H` für halbautomatisch, `A` für vollautomatisch. |

## Typische Ordnerstruktur im TIA-Projekt

| Ebene | Bereich | Geltungsbereich oder Inhalt |
| --- | --- | --- |
| Erste Ebene | Bibliotheken | Bibliotheksbausteine |
| Erste Ebene | Allgemeiner Bereich | Gilt für die gesamte SPS oder für mehrere Steuerkreise |
| Erste Ebene | Stationsordner | Gehört zu einer einzelnen Station |
| Unterhalb eines Stationsordners | Baugruppenordner | Optionale Unterteilung der Station nach Baugruppen |

## PIA IndustrialAppSuite

Weitere Bezeichnungen und Abkürzungen für die **PIA IndustrialAppSuite** sind `piaIAS`, `IAS`, `Optimum`, `piaOptimum` und `AppSuite`.
