# DHG som kilde for preutfylling

Appen støtter den anonyme DHG–FHIR-testfasaden i vedlagte `DHG_FHIR_API_Utviklerveiledning_v2_5 (1).docx`, særlig kapittel 3, 6, 8 og 9. Dokumentets tittelside angir versjon 1.3, 27. september 2026. Forespørselskontrakten er også kontrollert mot testinstansens [Swagger](https://fhir-gravid-test.blackbay-1cf2ad3e.norwayeast.azurecontainerapps.io/swagger/v1/swagger.json).

## Lisens og opprinnelse

Repoeieren bekreftet 4. oktober 2026 nødvendige rettigheter eller tillatelser til referansekoden og DHG-eksemplene. [Opprinnelsesoversikten](PROVENANCE.md) registrerer bekreftelsen og hvilke filer den gjelder. Original DOCX, Swagger-uttrekk og rå API-svar distribueres ikke. MIT-lisensen på klientkoden gir ingen selvstendig rett til tjenestetilgang eller eksterne data.

Se [eksempeloversikten](../examples/README.md) for formålet med de medfølgende JSON-filene.

## Prøv i appen

```sh
dotnet run --project src/GenericPopulation
```

1. Åpne `http://127.0.0.1:5077`.
2. Velg **DHG · Azure Test (syntetiske testpersoner)** som FHIR-endepunkt.
3. Velg **Testperson 1** eller **Testperson 2**, henholdsvis person A og B fra veiledningen.
4. Appen velger eksemplet **DHG – svangerskap og kontakter**. Et eget innlimt skjema beholdes ved kildebytte; velg DHG-eksemplet selv hvis du vil erstatte det.
5. Trykk **Hent data og preutfyll**. Kontroller forhåndsvisning, ubesvarte felt og merknader før eventuell nedlasting.

Ingen eksterne oppslag gjøres ved oppstart, valg av kilde eller lasting av eksemplet. Bytte av kilde, testperson eller skjema fjerner forrige resultat.

| Valg | Syntetisk NIN fra veiledningen |
| --- | --- |
| Testperson 1 / A | `29760484634` |
| Testperson 2 / B | `11859699482` |

Bare disse verdiene er godkjent i standardkonfigurasjonen. Serveren håndhever samme liste som grensesnittet. Listen er en avgrensning til avtalte testpersoner, ikke en autorisasjonsløsning for klinisk bruk.

## Hva eksemplet gjør

[questionnaire-dhg.json](../examples/questionnaire-dhg.json) gjør ett Patient-oppslag og ett søk for hver av Observation, Encounter og CareTeam: **fire HTTP-kall** ved vellykket fullføring. Observasjonene hentes én gang og velges deretter av uttrykkene i skjemaet. Like søk innen samme preutfylling gjenbrukes; data mellom forespørsler eller pasienter gjenbrukes ikke.

Skjemaet viser navn fra `Patient.name.text`, eksplisitt tolkebehov, svangerskapsalder på måledato, blodtrykkskomponenter, ultralydtermin, konsultasjonsdatoer og navn på inneholdte Practitioner-ressurser i CareTeam. Det gjøres ingen nettverksbasert `resolve()` for kontaktene, og deres roller utledes ikke.

Fødselsdato forblir tom når den ikke er uttrykkelig oppgitt; den beregnes aldri fra NIN. `false` og `0` beholdes. Kildens `status: unknown` omtolkes ikke til en klinisk status. Datoer i `valueDateTime` er svarverdier, ikke automatisk måletid. Flere likeverdige treff for et enkelt svar gir tomt felt og en merknad. Testpersonene kan ha forskjellige eller manglende data. Fixtureverdiene i repoet er konstruerte og er ikke en garanti for dagens Azure-svar.

## Kjørbart API-eksempel

Med appen kjørende kan [populate-request-dhg.json](../examples/populate-request-dhg.json) sendes fra PowerShell. [requests.http](../requests.http) har også et REST Client-eksempel.

```powershell
$request = Get-Content -Raw -Encoding UTF8 ./examples/populate-request-dhg.json |
    ConvertFrom-Json
# Valgfritt: velg den andre syntetiske testpersonen.
# $request.patientIdentifier = '11859699482'
$result = Invoke-RestMethod -Method Post `
    -Uri 'http://127.0.0.1:5077/api/populate' `
    -ContentType 'application/json' `
    -Body ([System.Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Depth 100)))
$qr = ($result.parameter | Where-Object name -eq 'response').resource
```

`$qr` inneholder den nye QuestionnaireResponse. Bruk `/api/questionnaire-response` med samme input for bare QR uten merknader. Feltet er `patientIdentifier` for DHG og `patientId` for vanlige FHIR-kilder. Begge felt samtidig, numerisk NIN, dupliserte inputfelt og ikke godkjente identifikatorer avvises. NIN beholdes som tekst, inkludert eventuelle ledende nuller.

## HTTP-kontrakt

| Operasjon | Forespørsel til DHG | Pasientvalg i formkropp |
| --- | --- | --- |
| Patient | `POST /fhir/Patient/_search` | `identifier` |
| Observation | `POST /fhir/Observation/_search` | `patient.identifier` |
| Encounter | `POST /fhir/Encounter/_search` | `patient.identifier` |
| CareTeam | `POST /fhir/CareTeam/_search` | `patient.identifier` |

Kroppen er `application/x-www-form-urlencoded`, med `Accept: application/fhir+json`. NIN settes ikke i URL-en. Patient-svaret må inneholde nøyaktig én ressurs med gyldig pseudonym ID. Alle senere `subject`-referanser kontrolleres mot denne ID-en. QR bruker pseudonymet; appen kopierer ikke NIN fra input til QR.

Questionnaire bruker fortsatt uttrykk som `Observation?patient={{%patient.id}}&code=http://loinc.org|85354-9`. Adapteren oversetter pasientfilteret til `patient.identifier` i formkroppen. For Observation støttes `code`, `category` og ett `date`-filter. `code` krever `system|code`. Dato krever `yyyy-MM-dd` med valgfritt `eq`, `ne`, `gt`, `lt`, `ge` eller `le`. Gjentatte datofiltre og parametere som `_sort`, `_include` og `_count` avvises. Encounter og CareTeam støtter bare pasientvalget.

Formkroppen begrenses til 4096 byte, hvert svar til 2 MiB og hvert søkeresultat til 2000 ressurser. Et tomt searchset betyr ingen treff; HTTP-feil og OperationOutcome behandles som feil, uten delvis QR. Kildens HTTP-status vises i en kontrollert feilmelding uten rå feilkropp, NIN eller kliniske opplysninger. Redirects følges ikke. Neste-side-lenker avvises fordi POST-kontrakten ikke beskriver paginering.

Oppslagene er ikke en atomisk DHG-transaksjon. Klienten har en grense på 20 sekunder per HTTP-kall, inkludert lesing av hele svarkroppen, og 60 sekunder for hele preutfyllingen, uten automatiske nye forsøk.

## Konfigurasjon og tilgang

Kilden er allerede lagt til i `src/GenericPopulation/appsettings.json`:

```json
{
  "Id": "dhg-test",
  "Name": "DHG · Azure Test (syntetiske testpersoner)",
  "Mode": "dhg-post",
  "BaseUrl": "https://fhir-gravid-test.blackbay-1cf2ad3e.norwayeast.azurecontainerapps.io/fhir/",
  "AllowedTestPatientIdentifiers": ["29760484634", "11859699482"]
}
```

Basen skal inkludere `/fhir/` og ende med skråstrek. Bare serverkonfigurasjonen velger endepunkt. Flere testpersoner må avtales med API-tilbyderen før de legges til. Nettleseren får se listen, så den skal bare inneholde godkjente syntetiske identifikatorer.

Denne modusen gjelder den **anonyme Azure-testinstansen**. Ingen Authorization-, DPoP- eller X-Patient-Context-header sendes. STS/HelseID, audience, scope, brukerflyt og eventuelle DPoP-krav for et autentisert miljø må avklares og implementeres separat. `BearerTokenEnvironmentVariable` sammen med `dhg-post` avvises ved oppstart for å unngå en feilaktig bearer-integrasjon.

## Test uten DHG-tilgang

Et rent lokalt eksempel bruker konstruerte FHIR-fixtures:

```sh
dotnet run --project src/GenericPopulation -- --demo dhg
```

Dette skriver QR og merknader til appens `output/`-mappe, normalt `src/GenericPopulation/output/` ved `dotnet run`. Det gjør ingen nettverkskall. Dataene ligger i [dhg-patient.json](../examples/dhg-patient.json) og [dhg-resources.json](../examples/dhg-resources.json).

Adapter- og HTTP-regresjoner:

```sh
dotnet build --configuration Release --warnaserror
dotnet run --project src/GenericPopulation --configuration Release --no-build -- --self-test
dotnet publish src/GenericPopulation --configuration Release --no-build --output publish
python verification/dhg-http-smoke.py --app-dir publish
```

HTTP-testen starter en lokal DHG-testdobbel og appen på egne loopback-porter, bruker bare syntetiske data og stopper prosessene etterpå. Den inngår i CI og kontakter aldri Azure eller DHG.

Med Python Playwright og en nettleser installert kan UI-testene kjøres mot samme testdobbel med `--browser`. Eksempel i PowerShell med installert Edge:

```powershell
$env:PLAYWRIGHT_CHANNEL = 'msedge'
$env:FHIR_SCREENSHOT_DIR = "$PWD/output/dhg-browser"
python verification/dhg-http-smoke.py --app-dir publish --browser
```

Utelat `PLAYWRIGHT_CHANNEL` for Playwrights egen Chromium. Testene omfatter automatisk eksempelskifte, bevaring av eget skjema, pasientbytte uten gamle svar og mobilvisning. Faktiske eksterne testresultater står i [verifikasjonsrapporten](../verification/RESULTATER.md).
