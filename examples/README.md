# Lisens og opprinnelse for eksemplene

JSON-filene er syntetiske utviklingseksempler. Egen skjemastruktur og lokale testdata følger prosjektets [MIT-lisens](../LICENSE).

| Eksempel | Formål |
| --- | --- |
| `patient.json`, `questionnaire-general.json` | Lokal demopasient og personopplysninger; FHIR/SDC-struktur. |
| `observations.json`, `questionnaire-pregnancy.json`, `populate-request.json` | Syntetisk svangerskapsalder og blodtrykk. |
| `populate-request-multi-source.json` | Samme syntetiske svangerskapsskjema med sentral Patient og to endepunkter via profilen `demo-multi`. |
| `questionnaire-routed-v1.json`, `questionnaire-routed-v2.json`, `populate-request-questionnaire.json` | Samme canonical med to skjemaversjoner som er koblet til ulike kildeprofiler. Requesten utelater profilvalg. |
| `expected-response-pregnancy.json` | Forventet syntetisk QR med bevarte måleenheter. |
| `dhg-patient.json`, `dhg-resources.json` | Lokale DHG-fixtures med pasient, observasjoner, konsultasjon og behandlingsteam. |
| `questionnaire-dhg.json`, `populate-request-dhg.json` | Eksempel på DHG-preutfylling. Requesten bruker et dokumentert syntetisk NIN. |

Se [opprinnelsesoversikten](../docs/PROVENANCE.md) for eierens bekreftelse om referansemateriale og DHG-eksempler.
