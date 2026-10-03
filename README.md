# FHIR preutfylling — .NET/C# testapp

En lokal fullstack-app som tar **FHIR R4 Questionnaire (Q)** som JSON, henter **Patient og relevante ressurser fra et FHIR-endepunkt**, evaluerer uttrykkene i Q og lager en **QuestionnaireResponse (QR)**. Bygger videre på den vedlagte arkitekturbeskrivelsen og C#-demokoden.

Backend er ASP.NET Core / C# på **.NET 10**, med **Firely Hl7.Fhir.R4 6.6.0**. Frontend er HTML, CSS og JavaScript som serveres av samme app. Du trenger ikke Node, npm, database eller en ekstern FHIR-server for å prøve demoen.

## Start i Visual Studio Code

1. Installer [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [Visual Studio Code](https://code.visualstudio.com/) og den anbefalte **C# Dev Kit**-utvidelsen.
2. Pakk ut prosjektet og åpne **FhirPreutfylling.code-workspace**, eller åpne prosjektmappen med `code .`.
3. Trykk **F5** og velg **FHIR preutfylling · web**. Prosjektet bygges og nettleseren åpnes på **http://127.0.0.1:5077**. Første bygg trenger tilgang til NuGet.
4. Behold kilden **Lokal demo** og Patient-ID **demo-patient**. Klikk **Hent data og preutfyll**.
5. Se forhåndsvisningen, QR JSON og merknader. **Last ned QR** gir en ren FHIR QuestionnaireResponse. **QR + merknader** gir en FHIR Parameters-konvolutt.

Terminalalternativ fra prosjektroten:

```sh
dotnet restore
dotnet build --no-restore
dotnet run --project src/GenericPopulation
```

Stopp med Ctrl+C. VS Code har også en `self-test`-task og en egen feilsøkingskonfigurasjon for selvtestene. Sett gjerne et breakpoint i `PopulationEngine.CreateAsync` eller `HttpFhirDataSource.ReadPatientAsync`.

## Prøv hele flyten

Nettleseren laster svangerskapseksemplet som standard. Du kan bytte til personopplysninger, lime inn en egen Q eller laste opp en `.json`-fil. **Egen Q må følge støtteprofilen nedenfor**, inkludert uttrykk som beskriver databehovet; motoren utleder ikke datakoblinger fra spørsmålets tekst.

Den lokale FHIR-kilden eksponerer:

- `GET /demo/fhir/Patient/demo-patient`
- `GET /demo/fhir/Observation?patient=demo-patient&code=http%3A%2F%2Floinc.org%7C18185-9`
- `GET /demo/fhir/Observation?patient=demo-patient&code=http%3A%2F%2Floinc.org%7C85354-9`

Webflyten gjør faktiske HTTP-kall også mot demoen. Svangerskapseksemplet gir **3 kall**: Patient og to Observation-søk. Det gir fødselsdato, svangerskapsalder **210 dager på måledatoen**, blodtrykk **128/76 mmHg** og måletid. Kommentarfeltet blir ubesvart. Personopplysninger gir ett Patient-kall, to fornavn, etternavn, fødselsdato og et eksplisitt `false`.

QR har versjonert `questionnaire`, `subject`, ny `id`, `authored`, korrekt `item`/`answer.value[x]` og status **in-progress**. Preutfylling er et forslag som skal kontrolleres, og gjør ikke besvarelsen automatisk `completed`. Nettleseren viser resultatet uten å lagre det; den er ikke en full skjemautfyller.

## Bruk din FHIR-testserver

Legg til en kilde i `src/GenericPopulation/appsettings.json` og start appen på nytt:

```json
{
  "Demo": { "Port": 5077 },
  "Fhir": {
    "Sources": [
      {
        "Id": "demo",
        "Name": "Lokal demo · syntetiske data",
        "BaseUrl": "http://127.0.0.1:5077/demo/fhir/"
      },
      {
        "Id": "testserver",
        "Name": "Min FHIR R4-testserver",
        "BaseUrl": "https://fhir.example.no/r4/",
        "BearerTokenEnvironmentVariable": "FHIR_TEST_TOKEN"
      }
    ]
  }
}
```

Erstatt eksempeladressen med en faktisk FHIR-base, **med avsluttende skråstrek**. HTTPS kreves, bortsett fra lokale loopback-adresser. Velg kilden i nettleseren og angi en eksisterende logisk `Patient.id`. Serveren må støtte `GET Patient/{id}` og søkene i Q. Absolutte subject-referanser må bruke samme FHIR-base; ellers brukes `Patient/{id}`.

Hvis kilden trenger bearer-token, sett miljøvariabelen `FHIR_TEST_TOKEN` før oppstart. Eksempel i PowerShell:

```powershell
$env:FHIR_TEST_TOKEN = Read-Host 'Token til FHIR-testserver' -MaskInput
dotnet run --project src/GenericPopulation
```

`-MaskInput` krever PowerShell 7.1+. Ved F5 må miljøvariabelen være tilgjengelig for VS Code-prosessen; start eventuelt VS Code fra terminalen der variabelen er satt. Token legges på hvert HTTP-kall, også sidehenting. Utelat `BearerTokenEnvironmentVariable` for kilder uten autentisering. Ikke legg token i Q, frontend, kildekode eller `launch.json`.

Kilder velges fra serverens konfigurasjon. Vilkårlige URL-er fra Q eller API-input godtas ikke. Det er ikke implementert OAuth-pålogging, tokenfornyelse, HelseID, DPoP, X-Patient-Context eller DHG-spesifikk `POST _search`. Slike kontrakter må implementeres i verten/autorisasjonsadapteren for aktuell kilde. Det er heller ingen automatisk kapabilitetsoppdagelse.

## API

### Q inn, QR og merknader ut

`POST /api/populate`, `Content-Type: application/json`:

```json
{
  "sourceId": "demo",
  "patientId": "demo-patient",
  "questionnaire": {
    "resourceType": "Questionnaire",
    "...": "bruk en komplett Q fra examples/"
  }
}
```

Dette er et illustrert utdrag. En komplett kjørbar request ligger i **examples/populate-request.json**.

Svaret er `application/fhir+json` med `resourceType: Parameters` og parameterne:

- `response`: QuestionnaireResponse.
- `issues`: OperationOutcome med informasjon og eventuelle merknader.

`X-Fhir-Requests` viser antall kildekall i operasjonen. Kritiske kildefeil returnerer OperationOutcome uten en delvis QR. HTTP 400/415 brukes for ugyldig API-input, 422 for ustøttet Q/kontekst, 502 for kildefeil, 504 for tidsavbrudd og 413 for for stor request.

### Bare QR som respons

`POST /api/questionnaire-response` bruker samme input og returnerer en ren QuestionnaireResponse. **Bruk `/api/populate` når merknader skal vises eller behandles**; direkteendepunktet utelater merknadene og kan ha ubesvarte felt ved tvetydige/feiltypede kildeverdier.

Eksempel, med appen kjørende og terminalen i prosjektroten:

```sh
curl -X POST http://127.0.0.1:5077/api/populate \
  -H 'Content-Type: application/json' \
  --data-binary @examples/populate-request.json
```

PowerShell:

```powershell
$r = Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:5077/api/questionnaire-response' `
  -ContentType 'application/json' -InFile './examples/populate-request.json'
$r | ConvertTo-Json -Depth 100
```

`requests.http` kan brukes med VS Code REST Client-utvidelsen (valgfri). Dette er en **egen testkontrakt**, ikke en full implementasjon av standardoperasjonen `Questionnaire/$populate`.

## Støtteprofil for Q

| Område | Støttet |
| --- | --- |
| FHIR-versjon | R4 / 4.0.1 |
| Q-metadata | `status: active`, canonical `url`, eksplisitt `version`, unike `linkId` |
| Kontekst | Én SDC `launchContext`: `patient`, type `Patient` |
| Variabler | Standard `variable`, på root/item, evaluert i angitt rekkefølge med nedarvet scope |
| FHIR-søk | `application/x-fhir-query`; relative søk; kun `{{%patient.id}}` som malbinding |
| Ressurser | Patient read; søk på Observation, Encounter, CareTeam |
| Søkeparametere | Obligatorisk `patient`; i tillegg `code`, `category`, `date` for Observation |
| Initialverdier | `text/fhirpath` via SDC `initialExpression`, eller statisk `initial.value[x]` |
| Svar | boolean, integer, decimal, date, dateTime, time, string/text, uri, Quantity |
| Struktur | Ikke-gjentatte grupper og gjentatte spørsmål; display-items utelates fra QR |
| Quantity | Verdi, system og kode kreves; comparator støttes ikke; enhet beholdes |

Det er **ikke** støtte for choice/open-choice, Reference, Attachment, gjentatte grupper, `answer.item`, CQL, `enableWhen`, `calculatedExpression`, `itemPopulationContext` eller dynamiske valgsett. Ukjente extensions avvises. `resolve()` og `trace()` er sperret. FHIRPath-systemverdier støttes bare når de kan representeres av motorens eksplisitte svaradapter; dette er ikke full SDC-konformitet eller full profil-/terminologivalidering.

Eksemplenes kliniske koder og utvalgsregler ligger i Q, ikke i motoren. Flere likeverdige treff for et ikke-gjentatt spørsmål gir et tomt felt og en merknad. `false` og `0` beholdes. Feil pasient eller kritisk HTTP-feil stopper hele preutfyllingen. Eksisterende QR aksepteres ikke som input.

## Prosjektstruktur

```text
FhirPreutfylling.slnx          Solution for .NET 10 / VS Code
FhirPreutfylling.code-workspace
.vscode/                      F5, build-task, self-test og utvidelser
src/GenericPopulation/
  Program.cs                  Webvert, API, lokal FHIR-kilde og CLI
  PopulationEngine.cs         Q → variabler/søk → typet QR
  ExpressionEvaluator.cs      Firely FHIRPath
  QuestionnaireGuard.cs       Støtteprofil og strukturkontroller
  AnswerMapper.cs             Bevarer svarenes FHIR-datatyper
  HttpFhirDataSource.cs        Patient, søk, paginering og størrelsesgrenser
  FhirSourceOptions.cs        Kilderegister og bearer-tokenadapter
  FhirSearch.cs               Relativ query-binding og pasientkontroll
  RoutingFhirDataSource.cs    Gjenbrukbart register for flere kilder
  FixtureDataSource.cs        Syntetiske demoressurser
  SelfTests.cs                27 selvtester uten ekstra testrammeverk
  wwwroot/                    Norsk, responsivt webgrensesnitt
examples/                     Q-er, Patient, Observations og eksempelrequest
verification/                 HTTP-/nettlesertester og testresultater
```

Webappen velger én kilde per operasjon. Den medfølgende `RoutingFhirDataSource` kan gjenbrukes ved senere integrasjon med flere kilder innen samme operasjon.

## Test og publiser lokalt

```sh
dotnet build
dotnet run --project src/GenericPopulation -- --self-test
```

Med appen kjørende, i en annen terminal:

```sh
python verification/http-smoke.py
```

Valgfri nettlesertest (Python og Playwright kreves bare til denne testen):

```sh
python -m pip install playwright
python -m playwright install chromium
python verification/browser-smoke.py
```

CLI med fixtures direkte, uten HTTP:

```sh
dotnet run --project src/GenericPopulation -- --demo pregnancy
dotnet run --project src/GenericPopulation -- --demo general
dotnet run --project src/GenericPopulation -- --demo pregnancy --no-consent
```

CLI skriver syntetisk QR og Outcome til `output/`. `--no-consent` er et historisk demoflagg fra referansekoden som simulerer avslag på preutfylling; det er ikke en faktisk samtykkemekanisme.

```sh
dotnet publish src/GenericPopulation -c Release -o publish
cd publish
dotnet GenericPopulation.dll
```

Publisert app kjøres fra `publish` slik at appsettings og wwwroot blir funnet. .NET 10 ASP.NET Core Runtime kreves. Porten endres med `Demo:Port`; oppdater også demokildens `BaseUrl` og eventuell `applicationUrl` i launchSettings.

## Avgrensning og verifikasjon

Dette er en lokal utviklerapp for **syntetiske/testdata og betrodde Q-definisjoner**. Den har ingen brukerinnlogging eller klinisk tilgangskontroll; Patient-ID er testkontekst. Den lytter på loopback, blokkerer kryssopprinnelses-POST, har ingen CORS-åpning, bruker `no-store`, logger ikke FHIR-data, følger ikke HTTP-redirects og begrenser kildekall til konfigurerte baser. Nettleseren bruker ikke localStorage. FHIRPath-kontrollene er ikke en sandbox for ondsinnede programmer; HTTP-tidsavbrudd avbryter heller ikke et allerede kjørende synkront FHIRPath-uttrykk.

Grenser: 256 KiB API-request, 240 KiB opplastet Q, 200 items, 12 gruppenivåer, 4 000 tegn per FHIRPath, 20 sider per søk, 2 000 ressurser per søk og 2 MiB per kilderespons. HTTP-kall har 20 sekunders tidsgrense, og en operasjon har 60 sekunders kanselleringsfrist.

Bygg, selvtester, HTTP- og nettleserflyt er kjørt i leveransemiljøet. Se **verification/RESULTATER.md**. Ingen faktisk ekstern klinisk FHIR-kilde var oppgitt; tilkobling til din server og dens tilgangsmodell må verifiseres der. Demoen er ikke produksjonsklar og skriver ikke tilbake til FHIR-kilden.
