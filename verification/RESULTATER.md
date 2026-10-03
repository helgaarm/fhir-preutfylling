# Verifisert leveranse

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
