# Lisens og opprinnelse for eksemplene

JSON-filene er syntetiske utviklingseksempler. Egen skjemastruktur og lokale testdata følger prosjektets [MIT-lisens](../LICENSE); kodeverk og standardreferanser har egne vilkår i [tredjepartsmerknadene](../THIRD-PARTY-NOTICES.md).

| Eksempel | Formål og tredjepartsinnhold |
| --- | --- |
| `patient.json`, `questionnaire-general.json` | Lokal demopasient og personopplysninger; FHIR/SDC-struktur. |
| `observations.json`, `questionnaire-pregnancy.json`, `populate-request.json` | Syntetisk svangerskapsalder og blodtrykk; LOINC-koder og UCUM-enheter. |
| `expected-response-pregnancy.json` | Forventet syntetisk QR med bevarte UCUM-enheter. |
| `dhg-patient.json`, `dhg-resources.json` | Lokale DHG-fixtures; LOINC, UCUM, én SNOMED CT-kode og HL7 `v3-ActCode`. |
| `questionnaire-dhg.json`, `populate-request-dhg.json` | Eksempel på DHG-preutfylling; filtrene refererer til LOINC og SNOMED CT. Requesten bruker et dokumentert syntetisk NIN. |

LOINC-kodene er `18185-9` (Gestational age), `85354-9` (Blood pressure panel with all children optional), `8480-6` (Systolic blood pressure) og `8462-4` (Diastolic blood pressure). UCUM-kodene er `d` og `mm[Hg]`. DHG-eksemplet refererer til SNOMED CT `738070007` for terminopplysningen. Kodeverkene er ikke endret eller gjort til egne kodeverk.

Disse små kodeutdragene er ikke en låst terminologiutgave, komplett terminologitabell eller klinisk validert profil. Norske spørsmålstekster er lokale beskrivelser. Ved bruk av nye koder, offisielle oversettelser eller eksterne spørreskjemaer må utgave, kilde og eventuelle særvilkår kontrolleres.

Ved kopiering av eksemplene til andre prosjekter må relevante merknader følge med, særlig [LOINC_short_license.txt](../LICENSES/LOINC_short_license.txt), [UCUM](../LICENSES/UCUM-NOTICE.txt) og [SNOMED CT](../LICENSES/SNOMED-CT-NOTICE.txt). SNOMED-merknaden er ikke en lisens. Se [opprinnelsesoversikten](../docs/PROVENANCE.md) for eierens bekreftelse om referansemateriale og DHG-eksempler.
