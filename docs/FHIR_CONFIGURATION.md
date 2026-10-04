# Flere FHIR-endepunkter i samme populering

Velg **Lokal demo · flere endepunkter** i appen og bruk `demo-patient`. Profilen henter Patient én gang fra `/demo/fhir/`, svangerskapsalder fra samme base og blodtrykk fra `/demo/vitals/`. Svangerskapseksemplet gir tre HTTP-kall og verdier fra begge kildene. Ingen eksterne tjenester brukes i dette eksemplet.

## Rediger konfigurasjonen i appen

Klikk **Konfigurasjon** i toppmenyen, eller åpne `/configuration.html`.

1. **Kilder** viser FHIR-baser, GET/POST, sentralt pasientoppslag, pasientfilter og API-begrensninger. Bruk **Legg til** for et nytt endepunkt. Miljøvariabel for token er bare variabelnavnet; tokenverdien hentes aldri til nettleseren.
2. **Profiler** velger sentral pasientkilde, standardkilde, ruteregler og eventuelle koblinger per søkevariabel. Profiler som er opprettet automatisk fra en kilde, redigeres ved å åpne denne kilden. Nye profiler og kilder blir tilgjengelige i valgene allerede i utkastet.
3. **Skjemaer** kobler eksakt Questionnaire-URL og versjon til en profil. **Dupliser** lager et utgangspunkt for en ny versjon; fyll inn det tomme versjonsfeltet. Bryteren **Krev registrert Questionnaire-URL og versjon** styrer `RequireQuestionnaireBinding`.
4. **Valider utkast** kontrollerer struktur og referanser uten å lagre, aktivere eller kontakte kildene. **Lagre og ta i bruk** validerer på nytt, lagrer og aktiverer hele oppsettet samlet. **Fjern fra utkast** får først virkning ved lagring; referanser til fjernede kilder/profiler må rettes før lagring godtas.

Enkle verdier har egne felt. `PatientLookup`, `PatientBinding`, `Capabilities`, `Routes` og `QueryBindings` har JSON-felt med hjelpetekst og støtter feltene beskrevet nedenfor. **Last ned JSON** eksporterer hele utkastet, også ulagrede endringer, for sikkerhetskopi eller filbasert konfigurasjon. **Hent lagret** forkaster utkastet etter bekreftelse.

### Lagring og tilbakestilling

`FhirConfigurationStore` leser først `Fhir` fra ASP.NET-konfigurasjonen, inkludert appsettings og miljøvariabler. Finnes `.local/fhir-configuration.json`, overstyrer den **hele Fhir-delen**, også Fhir-miljøvariabler. Filen inneholder direkte `Sources`, `PopulationProfiles`, `QuestionnaireBindings` og `RequireQuestionnaireBinding`, uten et ytre `Fhir`-objekt. Andre innstillinger, som `Demo:Port`, følger fortsatt vanlig ASP.NET-konfigurasjon. Ved portbytte må lagrede lokale base-URL-er også oppdateres.

Filen ligger under appens innholdsmappe: normalt `src/GenericPopulation/.local/` ved `dotnet run --project src/GenericPopulation`, eller `.local/` i publiseringsmappen når appen startes der. Den er utenfor `wwwroot`, ignoreres av Git og tas ikke med i bygg/publisering. Appsettings endres ikke. Tokenverdier settes fortsatt i prosessens miljø før oppstart. Lagring krever skrivetilgang til denne mappen.

Nye kall bruker lagret konfigurasjon umiddelbart, mens pågående kall beholder sitt oppsett. Åpne preutfyllingssiden på nytt etter lagring. En gammel fane får en tydelig konfliktmelding i stedet for å bruke endrede endepunkter. To redigeringsfaner kan ikke overskrive samme revisjon; ved konflikt beholdes utkastet slik at det kan eksporteres før siste versjon hentes.

For å gå tilbake til appsettings/miljøvariabler: stopp appen, ta vare på den lokale filen utenfor `.local/fhir-configuration.json`, og start appen igjen. Manuell filredigering krever også omstart. En ugyldig lokal fil stopper oppstart i stedet for å velge et annet oppsett. Filendringer som skjer mens appen kjører, oppdages ved neste lagringsforsøk og gir konflikt.

### API for konfigurasjon

| Endepunkt | Bruk |
| --- | --- |
| `GET /api/configuration` | Returnerer `revision`, relativ lagringssti `storage` og full `configuration` med feltnavn som i appsettings. |
| `POST /api/configuration/validate` | Tar `{ "revision": "...", "configuration": { ... } }`. Kontrollerer utkastet uten endringer. |
| `POST /api/configuration` | Samme input; returnerer lagret konfigurasjon og ny revisjon. |

POST krever `application/json` og følger samme loopback-, Host-, Origin- og størrelsesgrense som appen. Ukjente/dupliserte felt, ugyldige datatyper og brutte referanser avvises. Konflikt gir HTTP 409; skrivefeil gir kontrollert HTTP 500 uten å endre aktivt oppsett. Ingen FHIR-kall gjøres under validering eller lagring. Dette er administrasjon av den lokale utviklerappen, med samme lokale tilgang som resten av appen.

`GET /api/config` inkluderer også `revision`. Preutfyllingssiden sender denne som `configurationRevision` til `/api/populate`; en foreldet revisjon avvises med HTTP 409 før pasientoppslag. Feltet er valgfritt for eksisterende API-klienter. Når det utelates brukes aktiv konfigurasjon ved starten av kallet.

## Ansvarsdeling

`QuestionnaireProfileRegistry` velger populeringsprofil fra Questionnaire-URL og eksakt versjon før pasientoppslaget. `PopulationEngine` evaluerer Questionnaire og bygger QR. Den kjenner ikke DHG, endepunkter eller API-begrensninger. `RoutingFhirDataSource` velger kilde for hvert søk innen den valgte profilen. Alle HTTP-kilder bruker `HttpFhirDataSource`; GET/POST og begrensninger kommer fra `Fhir:Sources`.

Pasienten kommer fra profilens **sentrale pasientkilde**. Det gjøres ingen separate Patient-oppslag eller ID-mapping hos de kliniske kildene. De må bruke den sentrale pasientidentiteten, eventuelt med et konfigurert identifikatorfilter. Returnerte pasientreferanser kontrolleres fortsatt. Relative referanser, absolutte referanser under klinisk base og absolutte referanser under sentral pasientbase godtas når Patient-ID stemmer.

## Kildekonfigurasjon per Questionnaire og versjon

`Fhir:QuestionnaireBindings` er det ytterste konfigurasjonsnivået. Hver kombinasjon av `Questionnaire.url` og `Questionnaire.version` peker på én populeringsprofil. Profilen bestemmer sentral pasientkilde, endepunkter, ruteregler og valgfrie kilder. Flere skjemaer eller versjoner kan gjenbruke samme profil.

```json
"QuestionnaireBindings": [
  {
    "Questionnaire": "https://example.org/fhir/Questionnaire/routed-pregnancy-demo",
    "Version": "1.0.0",
    "ProfileId": "demo"
  },
  {
    "Questionnaire": "https://example.org/fhir/Questionnaire/routed-pregnancy-demo",
    "Version": "2.0.0",
    "ProfileId": "demo-multi"
  }
],
"RequireQuestionnaireBinding": false
```

Dette ligger allerede under `Fhir` i appsettings. Velg **Skjemastyrte kilder · versjon 1** eller **versjon 2** i eksempelvelgeren. Samme Questionnaire-URL bruker da henholdsvis én og to kilder. Nettleseren velger og låser profilen fra skjemaet, også ved filopplasting eller innliming. Skjemainnholdet og versjonen endres ikke av profilvalget.

- URL og versjon sammenlignes eksakt som tekst, med skille mellom store og små bokstaver. Ingen «nyeste versjon», versjonsintervall eller fallback til en annen registrert versjon brukes.
- Når en URL er registrert, avvises en ukjent versjon selv om klienten sender `profileId` eller eldre `sourceId`.
- Et eksplisitt profilvalg må stemme med koblingen. Det kan ikke overstyre endepunktene for en registrert skjemaversjon.
- Duplikate URL-/versjonskoblinger og henvisning til ukjente profiler avvises ved oppstart.
- `RequireQuestionnaireBinding: false` beholder manuelt profilvalg for helt uregistrerte testskjemaer. De må sende `profileId` eller `sourceId`. Sett `true` for å kreve registrert URL/versjon for alle preutfyllinger.

Bindingen evalueres på serveren før sentral Patient hentes og før kliniske kildekall. Den gjelder begge API-endepunktene. Rekkefølgen blir dermed **Questionnaire + versjon → populeringsprofil → søkets ruteregel → endepunkt**. `QueryBindings` nedenfor er et eget, mer detaljert nivå inne i profilen og brukes bare til å rute bestemte søkevariabler.

## Populeringsprofiler

Konfigurer `Fhir:PopulationProfiles` ved siden av `Fhir:Sources` i [appsettings.json](../src/GenericPopulation/appsettings.json). `Source` og `PatientSource` peker på en kildes `Id`; de inneholder aldri URL-er.

```json
{
  "Id": "demo-multi",
  "Name": "Lokal demo · flere endepunkter",
  "PatientSource": "demo",
  "DefaultSource": "demo",
  "DefaultExample": "pregnancy",
  "Routes": [
    { "ResourceType": "Observation", "Source": "demo" },
    { "ResourceType": "Observation", "Code": "http://loinc.org|85354-9", "Source": "demo-vitals" }
  ]
}
```

Reglene velges i denne rekkefølgen:

1. `QueryBindings`: en bestemt søkevariabel i en bestemt Questionnaire-versjon og scope.
2. `Routes`: ressurstype med eksakt `Code` og/eller `Profile`. `Profile` matcher parameteren `_profile` i søket, ikke `meta.profile` i et senere svar. En regel med både kode og profil er mer presis enn en regel med bare én av dem.
3. En regel med bare `ResourceType`.
4. `DefaultSource`, dersom den er konfigurert.

To regler med samme høyeste presisjon gir feil, også om begge peker på samme kilde. Rekkefølgen i JSON avgjør aldri resultatet. Dupliserte regler og ukjente kilder avvises ved oppstart; overlapp som avhenger av søket avvises før det kliniske kildekallet. Uten treff eller standardkilde stoppes populeringen.

### Knytt en bestemt extension til en kilde

Standard `variable`-extension brukes av mange søk. Derfor identifiseres den konkrete forekomsten med Questionnaire-URL, versjon, valgfri `LinkId` og variabelnavn. Ingen egen extension-URL eller leverandøradapter er nødvendig. Eksempel i profilen:

```json
"QueryBindings": [
  {
    "Questionnaire": "https://example.org/fhir/Questionnaire/pregnancy-demo",
    "Version": "1.0.0",
    "Variable": "bpBundle",
    "Source": "demo-vitals"
  }
]
```

Utelatt `LinkId` betyr rotvariabel; legg til `"LinkId": "measurements"` dersom variabelen er definert på akkurat dette itemet. Koblingen gjelder bare `application/x-fhir-query` i en standard variable-extension. FHIRPath/initialExpression evaluerer allerede innhentede data og gjør ikke egne nettverkskall. Skjemaendringer kan kreve oppdatering av versjonen i koblingen.

Hver kilde får som standard også en enkeltkildeprofil med samme ID. Sett `ExposeAsProfile: false` på kilder som bare leverer kliniske data og ikke skal vises som selvstendige profiler. Profil-ID-er må være unike. UI-et laster disse profilene fra `/api/config`; det inneholder ingen leverandørvalg i koden.

## Generisk transport og begrensninger

| Innstilling på kilden | Standard / funksjon |
| --- | --- |
| `SearchMethod` | `GET`; `POST` sender URL-kodet skjema til `[type]/_search` |
| `PatientLookup.Interaction` | `read` henter `Patient/{id}`; `search` søker med `PatientLookup.Parameter` (standard `identifier`) |
| `PatientLookup.RequireDistinctResourceId` | `false`; kan kreve ressurs-ID forskjellig fra inputidentifikatoren |
| `PatientBinding.Parameter` | `patient`; kan for eksempel være `patient.identifier` eller `subject` |
| `PatientBinding.ValueFrom` | `patientId`; alternativt `inputIdentifier` fra sentralt søkeoppslag eller `patientIdentifier` fra sentral Patient |
| `PatientBinding.IdentifierSystem` | Påkrevd ved `patientIdentifier`; nøyaktig én ulik verdi må finnes for systemet, og `system\|value` sendes |
| `BearerTokenEnvironmentVariable` | Valgfritt miljøvariabelnavn; samme autorisasjonspunkt brukes for GET, POST og neste side |
| `Capabilities.Paging` | `follow`; `none` avviser neste-lenker. Neste-lenker følges med GET, også etter POST-søk |
| `Capabilities.RequireTotal` | `false`; aktiveres bare dersom kildens avtalte kontrakt krever total |
| `Capabilities.RejectOutcomeEntries` | `false`; feil/fatal avvises alltid, ellers håndterer motoren merknader |
| `Capabilities.MaxFormBytes` | 65536; kan senkes, eksempelvis til 4096 |
| `Capabilities.Resources` | Tomt objekt betyr ingen API-spesifikk liste over typer/parametere. Et ikke-tomt objekt tillater bare oppførte ressurstyper for kliniske søk |

For hver ressurs kan `SearchParameters` angi tillatte parametere **etter** pasientbinding; utelat feltet for ingen API-spesifikk parameterliste. `MaxOccurrences` begrenser gjentakelser. `TokenParameters` krever `system|code`; `TokenSystems` kan begrense systemet i en parameter når et system er oppgitt. `DateParameters` begrenser formatet til kalenderdato med `eq/ne/gt/lt/ge/le`. Dette er valgfrie, konfigurerte begrensninger, ikke motorens tolkning av FHIR.

`PatientReferencePath` er en enkel FHIR-elementsti, med standard `subject`. Sett eksempelvis `patient` for `AllergyIntolerance`. En ny pasientrelatert R4-ressurstype trenger dermed ingen ny C#-adapter. Oppslag av sentral Patient følger `PatientLookup`, mens `Capabilities.Resources` gjelder kliniske søk.

Vanlig FHIR tillater søk via [GET og POST](https://hl7.org/fhir/R4/http.html#search). [Bundle.total er valgfritt](https://hl7.org/fhir/R4/bundle-definitions.html#Bundle.total) og omfatter matchende ressurser over alle sider. Klienten krever ikke total som standard og sammenligner den ikke med hver side ved paginering. Med `Paging: none` avvises et oppgitt total som viser at resultatet er ufullstendig.

Felles klientgrenser gjelder uavhengig av API-profil: relative søk med nøyaktig ett `patient`-filter, godkjent destinasjon, pasientreferanser, maksimalt 20 sider / 2000 entries, 2 MiB per svar og tidsfrister. `_include`, `_revinclude`, `_has`, `_filter`, `_elements` og `_summary` faller utenfor klientens avgrensede søkekontrakt. Andre parametere og gjentakelser sendes til kilden med `Prefer: handling=strict`, med mindre konfigurasjonen begrenser dem. Dette er ikke en full FHIR-server eller automatisk CapabilityStatement-oppdagelse.

## DHG som konfigurasjon

DHG-eksemplet bruker samme klient med `SearchMethod: POST`, `PatientLookup.Interaction: search`, `PatientBinding.Parameter: patient.identifier` og `ValueFrom: inputIdentifier`. Kildens øvrige begrensninger, testliste og inputmønster ligger i appsettings. Se [DHG-veiledningen](DHG.md).

Ved en annen sentral pasientkilde kan DHG bruke `ValueFrom: patientIdentifier` og et avtalt `IdentifierSystem` fra den sentrale Patient. Konfigurasjonen og kildenes avtale om Patient-ID må stemme; klienten gjør ingen identitetsmatching.

Migrering fra tidligere konfigurasjon: `Mode: dhg-post` er fjernet. Oppstart avvises dersom `Mode` fortsatt finnes i en kilde, slik at gammel konfigurasjon ikke stilletiende bruker GET. Bruk de eksplisitte transportfeltene i gjeldende appsettings. Autentisering avgjøres av tilgangsavtalen. En bearer-header alene implementerer ikke STS/HelseID/DPoP.

## API, cache og feil

For et registrert skjema kan profilvalget utelates. [populate-request-questionnaire.json](../examples/populate-request-questionnaire.json) er et komplett eksempel som velger `demo-multi` fra Questionnaire-versjon `2.0.0`:

```sh
curl -X POST http://127.0.0.1:5077/api/populate -H 'Content-Type: application/json' --data-binary @examples/populate-request-questionnaire.json
```

For manuelt profilvalg på et uregistrert testskjema, send [populate-request-multi-source.json](../examples/populate-request-multi-source.json) eller bruk følgende innpakning med et komplett Questionnaire:

```json
{
  "profileId": "demo-multi",
  "patientId": "demo-patient",
  "questionnaire": { "resourceType": "Questionnaire" }
}
```

Utdragets Questionnaire er ikke komplett; bruk eksempelfilen for et kjørbart kall. Bruk `patientIdentifier` dersom den **sentrale** kilden bruker Patient search. `sourceId` støttes fortsatt for enkeltkildeklienter; `sourceId` og `profileId` kan ikke kombineres. Registrerte skjemaer trenger ingen av dem, men dersom feltet sendes må det stemme med bindingen. Ruter, endepunkt-URL-er og tilgangsdata kan ikke sendes fra nettleseren. `X-Population-Profile` viser profilen serveren faktisk brukte.

Parameters-svaret inneholder `response`, `issues` og en `source`-parameter per brukt kilde, med `id`, `requests` og `searches`. `X-Fhir-Requests` summerer HTTP-kall, inkludert det ene sentrale Patient-oppslaget og eventuell paginering. Ingen pasientverdier eller rå søkestrenger legges i kildestatistikken. Bundle-entryens FullUrl bevares eller fylles fra kildebasen ved manglende FullUrl. Statistikken viser innhentingskilder, ikke full klinisk Provenance per svarfelt.

Cache lever kun i én populering og skiller kilde-ID, sentral Patient-ID og søk. Identiske søk til samme kilde gjenbrukes også fra forskjellige variabler. Identiske søk til forskjellige kilder blandes aldri.

Alle valgte kilder er **påkrevd som standard**. En kildefeil avbryter da hele populeringen uten delvis QR. Profilen kan angi `"OptionalSources": ["demo-vitals"]` for kilder som får utelates ved forbindelsesfeil, individuell tidsfrist eller HTTP 408/429/5xx. Hele det mislykkede søkeresultatet forkastes, og QR returneres med en tydelig OperationOutcome-advarsel som navngir den manglende kilden. Bruk `/api/populate` slik at advarselen følger svaret.

Sentral pasientkilde kan aldri være valgfri. Pasientavvik, ugyldige data, autorisasjonsfeil, øvrige HTTP 4xx, brukeravbrudd og samlet tidsfrist stopper alltid populeringen. Det utføres ingen automatiske nye forsøk, reservekilder eller søk mot alle endepunkter. Ett søk rutes til én kilde; sammenslåing av samme søk fra flere kilder og konfliktløsning er ikke implementert.

## Lokal verifikasjon

```sh
dotnet build --configuration Release --no-restore --warnaserror
dotnet run --project src/GenericPopulation --configuration Release --no-build -- --self-test
dotnet publish src/GenericPopulation --configuration Release --no-build --output publish
python verification/dhg-http-smoke.py --app-dir publish --browser
```

Browser-testene krever Python Playwright og en installert nettleser. Testene bruker syntetiske svar og lokale servere. De kontrollerer blant annet rutepresisjon, scope/version i QueryBindings, cache på tvers av kilder, kildefeil, nye ressurstyper, POST/paginering og hele fler-endepunktflyten gjennom appens API og nettleser. De samme regresjonene kjøres i CI.
