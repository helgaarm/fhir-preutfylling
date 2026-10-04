# Verifisert leveranse

## Konfigurasjon i GUI

Kontrollert 4. oktober 2026 med syntetiske data og lokale endepunkter. Konfigurasjonssiden viser og redigerer kilder, profiler og Questionnaire-versjoner. Hele utkastet valideres før atomisk lagring i `.local/fhir-configuration.json`; nye kall bruker det lagrede oppsettet, mens pågående kall beholder sin konfigurasjon.

| Kontroll | Resultat |
| --- | --- |
| Release-bygg og publisering med `--warnaserror` | Bestått, ingen advarsler |
| C#-regresjoner | 76 av 76 bestått, inkludert ugyldig konfigurasjon, isolerte utkast, lagring/omstart, samtidige lagringer, manuell filendring, skrivefeil og avbrudd |
| Opprinnelige HTTP-regresjoner | 24 av 24 bestått |
| DHG, ruting og konfigurasjons-API | 56 av 56 bestått; validering uten aktivering, lagring, referanser, Origin/JSON-grense og revisjonskonflikter inngår |
| Konfigurasjon i Edge | 9 av 9 scenarioer bestått: lasting, ugyldig JSON, oppretting av alle tre nivåer, faktisk populering fra lagret oppsett, konflikt mellom faner, sletting av referert kilde, duplisering/eksport, mobilvisning og feil ved lasting |
| Eksisterende nettleserflyt og asynkron UI-tilstand | Desktop/mobil bestått uten JavaScript-feil; 8 av 8 asynkrone regresjoner bestått |
| Visuell kontroll | Konfigurasjonssiden kontrollert ved 1440 px og 390 px; ingen vannrett overflyt |
| `git diff --check` | Bestått |

HTTP- og nettlesertestene bruker en midlertidig kopi av den publiserte appen, slik at konfigurasjonslagring ikke endrer utviklerens innstillinger. Ingen eksterne tjenester ble kontaktet. Oppsett og lagringsprioritet er dokumentert i [konfigurasjonsveiledningen](../docs/FHIR_CONFIGURATION.md#rediger-konfigurasjonen-i-appen).

## Endepunkter per Questionnaire og versjon

Kontrollert 4. oktober 2026 med lokale, syntetiske kilder. `Fhir:QuestionnaireBindings` kobler eksakt URL/versjon til en populeringsprofil før Patient-oppslag. Nettleseren følger koblingen ved eksempellasting, opplasting og innliming; et registrert skjema kan ikke overstyres med en annen profil.

| Kontroll | Resultat |
| --- | --- |
| Release-bygg med `--warnaserror` og publisering | Bestått |
| C#-regresjoner | 69 av 69 bestått, inkludert versjonsvalg, overstyring, utviklerfallback, påkrevd registrering og konfigurasjonsfeil |
| Opprinnelige HTTP-regresjoner | 24 av 24 bestått |
| DHG/fler-kilde/skjemaversjon gjennom HTTP | 40 av 40 bestått; ukjent versjon og motstridende profil stoppes før sentral Patient hentes |
| Påkrevd skjemaregistrering gjennom publisert HTTP-app | Uregistrert skjema gir 422; registrert versjon gir 200 med riktig profil |
| Edge desktop/mobil | Bestått; versjonsbytte velger riktig profil, profilvalg låses, ukjent versjon stoppes og opplastet Q styrer profilen |
| Asynkrone UI-regresjoner | 8 av 8 bestått, inkludert registrert Q limt inn mens konfigurasjonen lastes |
| `git diff --check` | Bestått |

Eksemplene `questionnaire-routed-v1.json` og `questionnaire-routed-v2.json` har samme canonical og ulike profiler. `populate-request-questionnaire.json` fungerer uten `profileId`. Ingen eksterne tjenester ble kontaktet i testene.

## Generisk FHIR-transport og flere endepunkter

Kontrollert 4. oktober 2026 med syntetiske data og lokale tjenester. Motoren bruker nå en konfigurerbar ruter; alle endepunkter bruker samme GET/POST-klient. Patient hentes én gang fra profilens sentrale kilde. DHG-adapteren er fjernet og begrensningene ligger i appsettings.

| Kontroll | Resultat |
| --- | --- |
| Release-bygg med `--warnaserror`, publisering og `git diff --check` | Bestått |
| C#-regresjoner | 65 av 65 bestått, inkludert prioritet, tvetydige ruter, versjon/scope, cache mellom kilder, generisk POST/paginering, sentral Patient og valgfrie kilder |
| Opprinnelige HTTP-regresjoner | 24 av 24 bestått |
| DHG og fler-endepunkt mot lokale kilder | 30 av 30 bestått; sentral Patient og to kliniske søk ga tre HTTP-kall fordelt 2 + 1 på de valgte endepunktene |
| Edge desktop/mobil og brukerflyt | Bestått uten JavaScript-feil; omfatter profilbytte, kilder i samme QR, filopplasting, DHG-testliste og nedlasting |
| Asynkrone UI-regresjoner | 7 av 7 bestått |
| Lisensinventar og publiserte dokumenter | Alle 11 NuGet-pakker kontrollert |

Ingen eksterne DHG-tjenester ble kontaktet i denne verifikasjonen. [Konfigurasjonsveiledningen](../docs/FHIR_CONFIGURATION.md) beskriver ruteregler, API-begrensninger, migrering og den kjørbare lokale fler-kildedemoen. Kildestatistikken er ikke full klinisk Provenance per svarfelt. Sammenslåing av samme søk fra flere kilder er ikke implementert.

## Rettelser etter fullstack-gjennomgang: P2

Kontrollert 4. oktober 2026 med syntetiske data og lokale tjenester. De fire P2-funnene er rettet:

- Filinnlesing låser handlingene før asynkron lesing starter og fjerner gammelt resultat. Sene API-svar og feil forkastes hvis input er endret.
- Innlimt Questionnaire beholdes under oppstart, og handlingene aktiveres etter konfigurasjonslasting også uten eksempellasting. Konfigurasjonsfeil holder preutfylling avslått.
- En koblet frist dekker både HTTP-headere og hele svarkroppen i GET- og DHG-klienten. Operasjonens samlede kansellering beholdes.
- `LICENSE` og `THIRD-PARTY-NOTICES.md` kontrolleres mot gjennomgåtte sjekksummer i inventarformat 2. Tomme og avkortede dokumenter avvises også når publish inneholder samme skadede tekst.

| Kontroll | Resultat |
| --- | --- |
| Release-bygg og publisering med `--warnaserror` | Bestått |
| C#-regresjoner, inkludert frist, kansellering under lesing og frigjøring av strømmer for GET/POST | 52 av 52 bestått |
| Generiske HTTP-regresjoner / DHG mot lokal testdobbel | 24 av 24 / 24 av 24 bestått |
| Lisenskontrollens regresjoner | 10 av 10 bestått; omfatter tom/avkortet tekst i kilde og publish, manglende integritetsgrunnlag og normaliserte linjeskift |
| Lisensinventar og publiserte dokumenter | Alle 11 NuGet-pakker kontrollert |
| Eksisterende nettleserflyt i Edge, desktop og mobil | Bestått uten JavaScript-feil |
| Nye asynkrone nettleserregresjoner | 7 av 7 bestått: tidlig innliming, konfigurasjonsfeil, utsatt fil-lesing, feil/rett forsøk og foreldede svar/feil |
| Opprinnelig timeout-reproduksjon mot publisert app | HTTP 504 / OperationOutcome etter 20,37 sekunder; ingen QR |
| Python-/JavaScript-syntaks, actionlint og `git diff --check` | Bestått |

Nettlesersuitene kjøres også i CI via `dhg-http-smoke.py --browser` med Chromium og lokal testkilde. P3-funnene om gruppedybde og DHG-størrelsestestens grensedata er ikke omfattet av denne rettelsen.

## Lisensgjennomgang

Kontrollert 4. oktober 2026. [Gjennomgangen](../docs/LICENSE_REVIEW.md) dokumenterer prosjektets og bibliotekenes lisensgrunnlag. Eiers bekreftelse er registrert i [opprinnelsesoversikten](../docs/PROVENANCE.md).

| Kontroll | Resultat |
| --- | --- |
| Låsefil, restore-metadata og originale lisensmerknader | 11 av 11 NuGet-pakker kontrollert |
| Lisenskontrollens regresjoner, inkludert ny pakke, versjon, endret lisens, avkortet tekst og manglende publish-fil | 6 av 6 bestått |
| Release-bygg med `--warnaserror` og publisering | Bestått, 0 advarsler / 0 feil |
| Lisensdokumentasjon i publisert app | Alle forventede filer med riktig innhold |
| C#-regresjoner | 44 av 44 bestått |
| Generiske HTTP-regresjoner, inkludert `/licenses` og komplette lisenstekster | 24 av 24 bestått |
| DHG HTTP-regresjoner mot lokal testdobbel | 24 av 24 bestått; ingen eksterne FHIR-kall |
| Nettleser, Microsoft Edge via Playwright, desktop og mobil | Bestått; ingen JavaScript-feil eller horisontal overflyt |
| actionlint 1.7.12, Python-syntaks, lokale dokumentlenker og `git diff --check` | Bestått |

Testene bruker syntetiske data og lokale tjenester. Denne verifikasjonen kontrollerer filer, metadata og funksjon.

## DHG-integrasjon

Kontrollert 3. oktober 2026 på Windows med .NET SDK **9.0.318** og målrammeverk **net9.0**. Forespørselskontrakten er kontrollert mot den vedlagte DHG-veiledningen og Azure-testinstansens Swagger.

| Kontroll | Resultat |
| --- | --- |
| Debug- og Release-bygg med `--warnaserror` og lokal publisering | Bestått, 0 advarsler / 0 feil |
| C#-selvtester, inkludert DHG-adapter og sikkerhetsgrenser | **44 av 44 bestått** |
| Eksisterende HTTP-regresjoner mot publisert app | **22 av 22 bestått** |
| DHG HTTP-kontrakt mot lokal syntetisk testdobbel | **24 av 24 bestått** |
| Nettleser, Playwright med installert Microsoft Edge | Bestått for eksisterende flyt og DHG-valg, pasientbytte og nedlasting; ingen JavaScript-feil |
| Desktop 1440 px og mobil 390 px | Kontrollert visuelt; ingen horisontal side-overflyt |
| JavaScript- og Python-syntakskontroll | Bestått |
| Faktisk DHG Test, dokumentert syntetisk person A | HTTP 200, fire FHIR-kall, seks av elleve felt preutfylt |
| Faktisk DHG Test, dokumentert syntetisk person B | HTTP 200, fire FHIR-kall, to av elleve felt preutfylt |

De eksterne kontrollene gjorde én preutfylling per testperson, totalt åtte lesende POST-søk. Begge QR-ene brukte pseudonym Patient-referanse, uten at input-NIN ble kopiert til resultatet. Svarene ble ikke lagret. Antall svar er et øyeblikksbilde av testdataene.

`dhg-http-smoke.py` kjører mot en lokal testdobbel i CI og kontakter ingen eksterne tjenester. Den kontrollerer pasientisolering, tomme treff, formkontrakt, inndataavvisning, feilstatus uten delvis QR, redirects og fravær av pasientdata i applogg. `--browser` kjører også UI-testene mot samme testdobbel når Playwright er installert. DHG-skjermbildene i `output/dhg-browser/` er fra konstruerte lokale fixtures, ikke fra Azure-svar.

Autentisert STS/HelseID/DPoP, reelle pasientdata og klinisk bruk er ikke verifisert. Integrasjonen gjelder veiledningens anonyme DHG-testmodus. Se [DHG.md](../docs/DHG.md) for bruk og avgrensninger.

## Tidligere overgang til .NET 9

Kontrollert 3. oktober 2026 i Windows-miljø med .NET SDK **9.0.318**, målrammeverk **net9.0**, ASP.NET Core Runtime **9.0.20** og **Hl7.Fhir.R4 6.6.0**.

Låsefilen ble regenerert for .NET 9. Restore med `--locked-mode`, Debug- og Release-bygg med `--warnaserror` og lokal Release-publisering bestod uten advarsler eller feil. **27 av 27 C#-selvtester** og **22 av 22 HTTP-regresjonstester mot publisert app** bestod med syntetiske data. JSON-/prosjekt-XML og `git diff --check` bestod også. HTTP-kontroll bekreftet at nettsiden viser .NET 9.

Nettlesertesten ble ikke kjørt på nytt: ingen nettlesertilkobling var tilgjengelig i økten, og Python Playwright var ikke installert. Skjermbildene og responsartefakten nedenfor er beholdt fra den tidligere kontrollen.

## Tidligere kontroll med .NET 10

Kontrollert 3. oktober 2026 i Linux-miljø med .NET SDK **10.0.401**, målrammeverk **net10.0** og **Hl7.Fhir.R4 6.6.0**.

| Kontroll | Resultat |
| --- | --- |
| NuGet restore | Bestått |
| Debug build | Bestått, 0 advarsler / 0 feil |
| C#-selvtester | **27 av 27 bestått** |
| HTTP-integrasjon, utviklingskjøring | **22 av 22 bestått** |
| Release publish | Bestått |
| HTTP-integrasjon mot publisert app | **22 av 22 bestått** |
| Chromium / Playwright | Bestått, ingen JavaScript-feil |
| Desktop 1440 px og mobil 390 px | Kontrollert, ingen horisontal side-overflyt |
| JavaScript syntakskontroll | Bestått |

## Dekning

Selvtestene omfatter de 18 opprinnelige testene fra vedlegget og 9 tillegg: Patient via HTTP, autorisasjon ved read, ID-avvik, ugyldig ID uten kildekall, OperationOutcome som kildefeil, sidehenting utenfor base-path, løkkedeteksjon, bevart 0 og uendret Q, duplisert launchContext-underfelt og reservert patient-variabel. Enkelte tilfeller kontrollerer flere av disse egenskapene samtidig.

HTTP-testene kontrollerer typede verdier, manglende svar, versjon/status, ny QR per request, direkte QR-respons, Parameters-konvolutt, request-telling, false, gjentatte fornavn, inputvalidering, ukjent kilde, ukjent pasient, feil innholdstype, størrelsesgrense og cross-origin-beskyttelse. De er kjørt både mot utviklingskjøring på port 5077 og publisert app på port 5078.

Nettlesertesten kontrollerer begge eksempler, JSON-filopplasting, forhåndsvisning, JSON/merknad-faner, piltastnavigasjon, Ctrl+Enter, faktisk nedlasting av QR, ugyldig JSON, ukjent pasient, tømming av foreldet resultat, mobilvisning og fravær av JavaScript-feil.

## Artefakter

- `actual-response-pregnancy.json`: QR produsert av den publiserte appens HTTP-flyt, med runtime-generert ID og authored. Kun syntetiske data.
- `app-desktop.png`: faktisk skjermbilde etter preutfylling.
- `app-mobile.png`: faktisk skjermbilde ved 390 px bredde.
- `http-smoke.py`: kjørbar HTTP-regresjonstest; kun Python standardbibliotek.
- `browser-smoke.py`: kjørbar nettlesertest; krever Playwright og Chromium.

## Rettelser og utvidelser fra referansedemoen

Firely 6.6.0 bruker `FhirJsonDeserializer.Deserialize<T>()` og `FhirJsonSerializer.SerializeToString(..., pretty: true)`. Referansens `SerializerSettings`-konstruktør kompilerte ikke mot denne versjonen. Parser-/serializer-bruken og nullable-kontrollene er oppdatert.

Webverten tar nå Q som faktisk input og henter Patient via HTTP. Appen har kildekonfigurasjon, bearer-tokenadapter via miljøvariabel, håndterte HTTP-/JSON-feil, nytt norsk webgrensesnitt, eksport og VS Code-oppsett. Patient-konteksten kan ikke skygges av en lokal variabel, og dupliserte launchContext-underfelt avvises kontrollert.

Dette er funksjonell verifikasjon av testappen. Full FHIR-/SDC-profilvalidering, ekstern klinisk FHIR-integrasjon, HelseID, produksjonsautorisasjon, klinisk godkjenning og belastningstesting er ikke utført. Ingen faktisk ekstern FHIR-kilde var oppgitt.
