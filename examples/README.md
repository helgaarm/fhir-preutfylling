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
| [questionnaire-hapi-weight.json](questionnaire-hapi-weight.json) | To felt: nyeste ferdigstilte kroppsvekt i kg og måletid, hentet fra Observation for valgt pasient. |

## Prøv kroppsvekt fra HAPI

1. Opprett en kilde under **Konfigurasjon → Kilder** med ID `hapi`, FHIR-base `https://hapi.fhir.org/baseR4/`, søkemetode `GET` og **Vis også som enkeltkildeprofil** slått på. Behold standardene `PatientLookup.Interaction: read`, `PatientBinding.Parameter: patient`, `PatientBinding.ValueFrom: patientId` og `Capabilities.Paging: follow`. Den offentlige testserveren trenger ikke bearer-token. **Valider utkast**, og **Lagre og ta i bruk**.
2. Åpne framsiden på nytt, velg HAPI-profilen, og bruk **Last opp JSON** med `questionnaire-hapi-weight.json`.
3. Oppgi `Patient.id` til en eksisterende syntetisk testpasient på denne serveren, og klikk **Hent data og preutfyll**. En pasient-ID fra lokal demo eller DHG kan ikke brukes med mindre samme pasient faktisk finnes på HAPI.

Skjemaet søker etter `Observation?patient={{%patient.id}}&code=http://loinc.org|29463-7`. Det velger nyeste måling blant observasjoner med status `final`, `amended` eller `corrected`, `effectiveDateTime` og `valueQuantity` i UCUM `kg`, uten comparator. Andre enheter omregnes ikke. Manglende treff gir tomme felt; like nye, konkurrerende målinger gir ubesvart vekt og merknad. Pasienten må derfor ha en passende kroppsvekt-observasjon for at noe skal fylles ut. Dette er et avgrenset utviklingseksempel, ikke full støtte for alle måter kroppsvekt kan registreres på.

For automatisk profilvalg registreres skjemaet under **Konfigurasjon → Skjemaer** med URL `https://example.org/fhir/Questionnaire/hapi-weight`, versjon `1.0.0` og profil-ID `hapi`. Registreringen er nødvendig dersom **Krev registrert Questionnaire-URL og versjon** er slått på. Skjemaet inneholder ingen fast pasient-ID eller serveradresse.

Kode og enhet følger [FHIR R4 Body Weight](https://hl7.org/fhir/R4/bodyweight.html). Kildeadressen er dokumentert av [HAPI](https://hapi.fhir.org/baseR4/swagger-ui/). Skjemaet og testmålingene er laget lokalt; ingen pasientressurser er kopiert fra den offentlige serveren. Bruk kun syntetiske data på [HAPI-testserveren](https://hapi.fhir.org/).

Se [opprinnelsesoversikten](../docs/PROVENANCE.md) for eierens bekreftelse om referansemateriale og DHG-eksempler.
