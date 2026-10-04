# FHIR preutfylling — .NET/C# testapp

[MIT-lisens](LICENSE) · [Tredjepartsmerknader](THIRD-PARTY-NOTICES.md) · [Lisensgjennomgang](docs/LICENSE_REVIEW.md) · [Sikkerhet og privat rapportering](SECURITY.md) · [Bidra](CONTRIBUTING.md)

En lokal fullstack-app som tar **FHIR R4 Questionnaire (Q)** som JSON, henter **Patient og relevante ressurser fra et FHIR-endepunkt**, evaluerer uttrykkene i Q og lager en **QuestionnaireResponse (QR)**. Bygger videre på den vedlagte arkitekturbeskrivelsen og C#-demokoden.

Backend er ASP.NET Core / C# på **.NET 9**, med **Firely Hl7.Fhir.R4 6.6.0**. Frontend er HTML, CSS og JavaScript som serveres av samme app. Du trenger ikke Node, npm, database eller en ekstern FHIR-server for å prøve demoen.

## Start i Visual Studio Code

1. Installer [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) versjon **9.0.300 eller nyere i 9.0-serien** og eventuelt [Visual Studio Code](https://code.visualstudio.com/) med den anbefalte **C# Dev Kit**-utvidelsen. Dev Kit har [egne bruksvilkår](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq); terminalalternativet nedenfor trenger ikke utvidelsen.
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

For DHG-fasaden er **DHG · Azure Test** allerede tilgjengelig i kildevalget. Velg kilden og en av de to godkjente syntetiske testpersonene; eksemplet **DHG – svangerskap og kontakter** lastes automatisk. Klikk **Hent data og preutfyll** for å gjøre oppslagene. Se [DHG-veiledningen](docs/DHG.md) for kontrakt, kjørbare eksempler og lokal testing uten eksterne kall.

Andre FHIR-testservere med vanlig GET-kontrakt konfigureres slik:

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

Kilder velges fra serverens konfigurasjon. Vilkårlige URL-er fra Q eller API-input godtas ikke. DHG-testkilden bruker `POST _search` med `identifier`/`patient.identifier` i formkroppen. OAuth-pålogging, tokenfornyelse, STS, HelseID, DPoP og X-Patient-Context er ikke implementert; autentisert DHG krever en avtalt tilgangskontrakt og egen adapter. Det er heller ingen automatisk kapabilitetsoppdagelse.

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

For `sourceId: "dhg-test"` brukes `patientIdentifier` (godkjent syntetisk NIN som tekst) i stedet for `patientId`. Begge feltene samtidig avvises. Se **examples/populate-request-dhg.json**. Returnert QR bruker DHGs pseudonyme Patient-ID, og appen kopierer ikke NIN fra requesten til QR.

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
| Ressurser | Patient read (DHG: POST-søk); søk på Observation, Encounter, CareTeam |
| Søkeparametere | Obligatorisk `patient`; i tillegg `code`, `category`, `date` for Observation |
| Initialverdier | `text/fhirpath` via SDC `initialExpression`, eller statisk `initial.value[x]` |
| Svar | boolean, integer, decimal, date, dateTime, time, string/text, uri, Quantity |
| Struktur | Ikke-gjentatte grupper og gjentatte spørsmål; display-items utelates fra QR |
| Quantity | Verdi, system og kode kreves; comparator støttes ikke; enhet beholdes |

Det er **ikke** støtte for choice/open-choice, Reference, Attachment, gjentatte grupper, `answer.item`, CQL, `enableWhen`, `calculatedExpression`, `itemPopulationContext` eller dynamiske valgsett. Ukjente extensions avvises. `resolve()` og `trace()` er sperret. FHIRPath-systemverdier støttes bare når de kan representeres av motorens eksplisitte svaradapter; dette er ikke full SDC-konformitet eller full profil-/terminologivalidering.

Eksemplenes kliniske koder og utvalgsregler ligger i Q, ikke i motoren. Flere likeverdige treff for et ikke-gjentatt spørsmål gir et tomt felt og en merknad. `false` og `0` beholdes. Feil pasient eller kritisk HTTP-feil stopper hele preutfyllingen. Eksisterende QR aksepteres ikke som input.

## Prosjektstruktur

```text
FhirPreutfylling.slnx          Solution for .NET 9 / VS Code
FhirPreutfylling.code-workspace
.vscode/                      F5, build-task, self-test og utvidelser
src/GenericPopulation/
  Program.cs                  Webvert, API, lokal FHIR-kilde og CLI
  PopulationEngine.cs         Q → variabler/søk → typet QR
  ExpressionEvaluator.cs      Firely FHIRPath
  QuestionnaireGuard.cs       Støtteprofil og strukturkontroller
  AnswerMapper.cs             Bevarer svarenes FHIR-datatyper
  HttpFhirDataSource.cs        Patient, søk, paginering og størrelsesgrenser
  DhgFhirDataSource.cs         DHG POST-søk og kontroll av pasientreferanser
  FhirHttpResponse.cs          Felles begrensning og validering av HTTP-svar
  FhirSourceOptions.cs        Kilderegister og bearer-tokenadapter
  FhirSearch.cs               Relativ query-binding og pasientkontroll
  RoutingFhirDataSource.cs    Gjenbrukbart register for flere kilder
  FixtureDataSource.cs        Syntetiske demoressurser
  SelfTests.cs                Opprinnelige regresjonstester uten ekstra testrammeverk
  DhgSelfTests.cs              DHG-kontrakt, pasientisolering og feiltilfeller
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

DHG-kontrakten testes med en lokal syntetisk server. Testen starter og stopper appen selv på egne porter og gjør ingen eksterne kall:

```sh
dotnet publish src/GenericPopulation -c Release -o publish
python verification/dhg-http-smoke.py --app-dir publish
```

Nettlesertester (krever Python og Playwright; kjøres også i CI mot en lokal testkilde):

```sh
python -m pip install playwright
python -m playwright install chromium
python verification/browser-smoke.py
python verification/browser-state-tests.py
```

CLI med fixtures direkte, uten HTTP:

```sh
dotnet run --project src/GenericPopulation -- --demo pregnancy
dotnet run --project src/GenericPopulation -- --demo general
dotnet run --project src/GenericPopulation -- --demo pregnancy --no-consent
dotnet run --project src/GenericPopulation -- --demo dhg
```

CLI skriver syntetisk QR og Outcome til `output/`. `--no-consent` er et historisk demoflagg fra referansekoden som simulerer avslag på preutfylling; det er ikke en faktisk samtykkemekanisme.

```sh
dotnet publish src/GenericPopulation -c Release -o publish
cd publish
dotnet GenericPopulation.dll
```

Publisert app kjøres fra `publish` slik at appsettings og wwwroot blir funnet. .NET 9 ASP.NET Core Runtime kreves. Porten endres med `Demo:Port`; oppdater også demokildens `BaseUrl` og eventuell `applicationUrl` i launchSettings.

## Avgrensning og verifikasjon

Dette er en lokal utviklerapp for **syntetiske/testdata og betrodde Q-definisjoner**. Den har ingen brukerinnlogging eller klinisk tilgangskontroll; Patient-ID er testkontekst. Den lytter på loopback, blokkerer kryssopprinnelses-POST, har ingen CORS-åpning, bruker `no-store`, logger ikke FHIR-data, følger ikke HTTP-redirects og begrenser kildekall til konfigurerte baser. Nettleseren bruker ikke localStorage. FHIRPath-kontrollene er ikke en sandbox for ondsinnede programmer; HTTP-tidsavbrudd avbryter heller ikke et allerede kjørende synkront FHIRPath-uttrykk.

Grenser: 256 KiB API-request, 240 KiB opplastet Q, 200 items, 12 gruppenivåer, 4 000 tegn per FHIRPath, 20 sider per søk, 2 000 ressurser per søk og 2 MiB per kilderespons. HTTP-kall har 20 sekunders tidsgrense inkludert lesing av hele svarkroppen, og en operasjon har 60 sekunders kanselleringsfrist.

Bygg, selvtester, HTTP- og nettleserflyt er kontrollert. DHG Test er også verifisert med én preutfylling for hver av de to dokumenterte syntetiske testpersonene. Se **verification/RESULTATER.md**. Autentisert klinisk FHIR-tilgang er ikke verifisert; tilkobling til andre servere og deres tilgangsmodell må testes separat. Demoen er ikke produksjonsklar og skriver ikke tilbake til FHIR-kilden.

## Lisens og sikkerhet i det offentlige repoet

Prosjektets egen kode og dokumentasjon er utgitt under [MIT-lisensen](LICENSE), copyright 2026 Armann Helgason. Behold lisens- og copyrightteksten ved videreformidling. De låste NuGet-pakkene er under MIT/BSD-3-Clause og beholder sine egne merknader.

[Tredjepartsmerknadene](THIRD-PARTY-NOTICES.md), [lisensgjennomgangen](docs/LICENSE_REVIEW.md) og [opprinnelsesoversikten](docs/PROVENANCE.md) beskriver biblioteklisensene og eierens rettighetsbekreftelse. Originale lisenstekster ligger i [LICENSES/](LICENSES/); de følger normal build/publish og kan leses via **Lisenser** i appens bunntekst. Behold disse filene når appen pakkes videre.

Kontroller lisensoversikten med `python scripts/check-licenses.py` etter restore, og med `python scripts/check-licenses.py --publish-dir publish` etter publisering. CI kjører begge kontrollene. Ved nye pakker eller versjoner må lisensoversikten oppdateres etter manuell vurdering.

Repoet har konfigurasjon for CI, CodeQL, Gitleaks, Dependency Review og ukentlige Dependabot-oppdateringer. NuGet-avhengigheter er låst i `packages.lock.json`. CI bruker `dotnet restore --locked-mode` og avviser audit-advarsler også for transitive pakker. Actions bruker faste commit-ID-er og minimale tokenrettigheter.

Les [SECURITY.md](SECURITY.md) for privat rapportering. [Sikkerhetsoppsett](docs/GITHUB_SECURITY.md) og [verifisert status](docs/SECURITY_SETUP_STATUS.md) beskriver hvilke GitHub-innstillinger som faktisk er aktivert, og hvilke som eventuelt krever eierens administrasjonstilgang.
