# Detaljert lisensgjennomgang

**Dato:** 4. oktober 2026. **Omfang:** lokal arbeidskopi av `helgaarm/fhir-preutfylling`, inkludert .NET 9-migrering, DHG-integrasjon og utviklerkommentarer. Gjennomgangen gjelder de konkrete versjonene i `packages.lock.json`, ikke fremtidige oppdateringer eller en eventuell endret publisering.

## Konklusjon og funn

Prosjektets egen kode kan fortsatt være MIT-lisensiert. De **11 låste NuGet-pakkene** oppgir **BSD-3-Clause for fire pakker** og **MIT for sju pakker**. Disse tillater kombinasjonen som brukes her når copyright, lisenstekster og påkrevde merknader beholdes. Ingen av de 11 pakkenes deklarerte hovedlisenser krever at appens egen kildekode publiseres. Dette er ikke en konklusjon om at hele .NET-distribusjonen eller utviklingsverktøy er MIT-lisensiert.

| Funn ved gjennomgang | Tiltak/status |
| --- | --- |
| Roten hadde MIT og prosjektfilen `PackageLicenseExpression=MIT`, men ingen samlet tredjepartsoversikt | Lagt til [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md), fullstendige biblioteklisenser og versjonsbundet inventar. Egen MIT-tekst og copyright er beholdt. |
| `dotnet publish` inkluderte bare prosjektets egen lisens | Prosjektfilen inkluderer nå tredjepartsmerknader, hele `LICENSES/`, denne rapporten, opprinnelse og eksempelmerknader. |
| Firely-pakkene oppga SPDX-uttrykk, men hadde ikke med en separat lisensfil i den undersøkte pakken | Hentet originale tekster fra nøyaktig repository-commit i `.nuspec`. Opphavsnavn og original ordlyd er beholdt. |
| Microsoft-pakkene leverte to forskjellige varianter av `THIRD-PARTY-NOTICES.TXT` | Begge er tatt med. Options 8.0.2 har varianten kalt 8.0.0 her; mappe-/versjonsnavn alene brukes ikke til å velge tekst. |
| Referansekode og DHG-eksempler manglet registrert rettighetsgrunnlag | Eieren bekreftet nødvendige rettigheter/tillatelser 4. oktober 2026. Se [opprinnelse](PROVENANCE.md). |
| Ingen kontroll hindret lisensoversikten i å bli utdatert | Ny lokal kontroll og CI-steg avviser endret pakkeliste, versjon, hash, metadata, merknadstekst eller manglende publiserte dokumenter. |

Dette er en teknisk lisens- og dokumentasjonsgjennomgang med kildespor. Den fastslår ikke en bestemt organisasjons avtaler, arbeidsgiverrettigheter eller rettigheter til tredjepartsinnhold som tilføyes senere.

## Grunnlaget som ble kontrollert

- `LICENSE`, prosjektfil, låsefil, gjenopprettet `project.assets.json` og `.nuspec` for samtlige pakker.
- Originale lisens- og merknadsfiler i NuGet-cachen. Firely-tekstene er hentet fra kilderevisjonene nedenfor.
- JSON-eksempler, søkeuttrykk, kildereferanser, CSS, SVG, skjermbilder, testskript, VS Code-anbefalinger og CI-verktøy.
- Eierens rettighetsbekreftelse og det allerede tilgjengelige tekstuttrekket fra DHG-veiledningen. Den undersøkte Swagger-metadataen har ikke noe lisensfelt; anonym tilgang er derfor ikke behandlet som en publiseringslisens.
- Offisielle nettsider for verktøy, lest på gjennomgangsdatoen. Det er ikke gjort nye pasientoppslag for lisensarbeidet.

## NuGet-avhengigheter og plikter

Den lesbare pakkelisten står i [tredjepartsmerknadene](../THIRD-PARTY-NOTICES.md). [Inventaret](../LICENSES/inventory.json) inneholder én rad per pakke, også transitive, med versjon, direkte/transitiv tilknytning, SPDX-uttrykk, låsefilens SHA-512, copyrightmetadata, kilderevisjon og kobling til medfølgende tekster.

| Lisens | Praktisk betydning for dette prosjektet |
| --- | --- |
| MIT | Behold opphavs- og tillatelsesteksten når programvaren eller vesentlige deler videreformidles. Tillater kommersiell bruk og endring. Ingen generell plikt til å åpne egen kode. Se [egen lisens](../LICENSE), [Microsoft-tekst](../LICENSES/Microsoft-MIT.txt) og [Newtonsoft-tekst](../LICENSES/Newtonsoft.Json-MIT.txt). |
| BSD-3-Clause | Behold copyright, vilkår og ansvarsfraskrivelse i kilde og ved binærdistribusjon. Ikke bruk rettighetshavernes navn som produktanbefaling uten tillatelse. Se [SDK](../LICENSES/Firely-SDK-BSD-3-Clause.txt) og [Metrics](../LICENSES/Firely-Metrics-BSD-3-Clause.txt). |

Firely SDK 6.6.0 er kontrollert mot [commit da98b807e006afa1b8ee9ae449932935fb835c23](https://github.com/FirelyTeam/firely-net-sdk/blob/da98b807e006afa1b8ee9ae449932935fb835c23/LICENSE). Metrics 1.4.0 er kontrollert mot [commit b95488943b6cfccb2b24470fb8da4407c9f03537](https://github.com/FirelyTeam/Fhir.Metrics/blob/b95488943b6cfccb2b24470fb8da4407c9f03537/LICENSE). Ingen krav om kommersiell Firely-produktlisens ble identifisert for disse konkrete SDK-pakkene. Andre Firely-produkter inngår ikke i denne vurderingen.

Copyright-år i `.nuspec` og i selve lisensfilen kan være ulike, for eksempel for Metrics og Newtonsoft.Json. Ingen årstall eller navn er «rettet» til prosjektets navn. Microsofts brede tredjepartsmerknader er beholdt som levert; dette er ikke en påstand om at alle omtalte biblioteker følger appen.

## Frontend, test- og utviklingsverktøy

Frontend har ingen npm-avhengigheter, eksternt JavaScript, innbakte fontfiler eller nedlastet ikonbibliotek. Favicon er en enkel lokal SVG. Dette gjelder de undersøkte filene, ikke eventuelle senere designressurser.

Verktøyene nedenfor brukes under utvikling/CI og distribueres ikke gjennom appens vanlige `dotnet publish`. Lisensoversikten for NuGet er derfor ikke en full SBOM for verktøy, nettlesere eller GitHub-runneren.

| Verktøy / undersøkt versjon | Lisens eller vilkår / kilde |
| --- | --- |
| .NET SDK 9.0.318; .NET/ASP.NET Core 9.0.20 i lokalt miljø | .NET har MIT og egne tredjepartsmerknader. Det er skilt mellom separat installert runtime og filer som faktisk videreformidles. [Runtime 9.0.20](https://github.com/dotnet/runtime/tree/v9.0.20). |
| Playwright Python 1.63.0 | Apache-2.0, kontrollert i installert pakkemetadata og LICENSE. Brukes til nettlesertester lokalt og i CI; ikke en appavhengighet. Ved videreformidling av testmiljøet må også Playwrights NOTICE, driver, Node og nettleserlisenser kontrolleres. [Offisielt prosjekt](https://github.com/microsoft/playwright-python). |
| greenlet 3.5.6; pyee 13.0.1; typing_extensions 4.16.0 | Henholdsvis `MIT AND PSF-2.0`, `MIT` og `PSF-2.0` i den undersøkte lokale installasjonen. Ikke låst som prosjektavhengigheter. |
| actions/checkout v7.0.1 | MIT, [fast revisjon](https://github.com/actions/checkout/blob/3d3c42e5aac5ba805825da76410c181273ba90b1/LICENSE). |
| actions/setup-dotnet v6.0.0 | MIT, [fast revisjon](https://github.com/actions/setup-dotnet/blob/a98b56852c35b8e3190ac28c8c2271da59106c68/LICENSE). |
| actions/dependency-review-action v5.0.0 | MIT, [fast revisjon](https://github.com/actions/dependency-review-action/blob/a1d282b36b6f3519aa1f3fc636f609c47dddb294/LICENSE). |
| github/codeql-action v4.38.2 | Selve Action-koden er MIT, [fast revisjon](https://github.com/github/codeql-action/blob/2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2/LICENSE). Dette omfatter ikke automatisk CodeQL-motorens bruksvilkår; se [GitHubs veiledning](https://docs.github.com/en/code-security/concepts/code-scanning/codeql/codeql-cli), særlig ved privat repo. |
| actionlint 1.7.12; Gitleaks 8.30.1 | MIT, kontrollert mot [actionlint-taggen](https://github.com/rhysd/actionlint/blob/v1.7.12/LICENSE.txt) og [Gitleaks-taggen](https://github.com/gitleaks/gitleaks/blob/v8.30.1/LICENSE). Verktøyenes egne avhengigheter følger deres distribusjoner. |
| C# Dev Kit, anbefalt i VS Code | Proprietær utvidelse med egne Community-/Visual Studio-vilkår. Gratis bruk er tilgjengelig for blant annet kvalifiserende open-source-prosjekter; organisasjoners kommersielle bruk må vurderes separat. Vanlig `dotnet`-CLI trenger ikke Dev Kit. [Microsofts FAQ](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq). |
| VS Code, Edge/Chromium, Python og øvrig lokalt verktøymiljø | Separat installerte produkter; ikke innlemmet i appens MIT-lisens. Ikke pakk inn hele utviklermiljøet som om det var appkode. |

## Hva som skal følge en distribusjon

1. Behold rotens `LICENSE`, `THIRD-PARTY-NOTICES.md` og hele `LICENSES/`, inkludert inventaret.
2. Ta med `docs/LICENSE_REVIEW.md`, `docs/PROVENANCE.md` og `examples/README.md` når appens eksempler medfølger. Prosjektfilen gjør dette for normal build/publish.
3. Ved nettbasert nedlasting: gjør tredjepartsmerknadene tilgjengelige fra nedlastingssiden. I en kjørende app er de tilgjengelige via **Lisenser** (`/licenses`).
4. Ved `--self-contained`, andre runtime-versjoner/RID-er, single-file, trimming, Native AOT eller container: gjennomgå de faktiske ekstra komponentene og deres tekster. Denne rapporten er ingen komplett lisensklarering for slike artefakter. Også lisensfiler må distribueres når applikasjonen ellers består av én binærfil.
5. Ikke inkluder `output/`, virtuelle Python-miljøer, rå API-svar, DOCX eller lokale hemmeligheter i manuelle arkiver.

Standardpubliseringen er avhengig av en separat installert ASP.NET Core-runtime. De låste NuGet-pakkene er inventarisert uavhengig av hvilke av deres assemblies som ender som løse filer; noen referanser kan løses av det delte rammeverket. Apphost og runtime-merknader er vurdert for .NET 9.0.20. En oppdatering av SDK/runtime krever kontroll av at de medfølgende tekstene fortsatt er de riktige.

## Vedlikehold og lokal kontroll

Etter en endring av NuGet-pakker skal utvikleren kontrollere original lisens, opphav og merknader for **alle endrede direkte og transitive pakker**, oppdatere låsefil og `LICENSES/inventory.json`, erstatte relevante tekster og oppdatere tabellene. Nye pakker blir ikke automatisk godkjent fordi SPDX-uttrykket er MIT eller BSD. Nye assets krever separat manuell vurdering; NuGet-kontrollen oppdager ikke slikt.

```sh
dotnet restore --locked-mode
python scripts/check-licenses.py
python verification/license-check-tests.py
dotnet build -c Release --no-restore --warnaserror
dotnet publish src/GenericPopulation -c Release --no-build -o publish
python scripts/check-licenses.py --publish-dir publish
```

Kontrollen bruker standardbiblioteket i Python og ingen nettverk. Den sammenligner også kopierte pakkemerknader med originalene i lokal NuGet-cache. Hashene for tekst beregnes etter UTF-8-dekoding uten BOM og normalisering av linjeskift til LF, slik at Git på Windows/Linux gir samme resultat. Øvrig innhold, inkludert mellomrom, beholdes.

Inventarformat 2 lagrer også gjennomgåtte sjekksummer for `LICENSE` og `THIRD-PARTY-NOTICES.md` i `reviewedDocuments`. Tomme eller avkortede dokumenter avvises selv om kilde og publish er like. Ved en tilsiktet endring av disse dokumentene må hele innholdet gjennomgås før den normaliserte SHA-256-sjekksummen oppdateres i inventaret. Endret sjekksum kan aldri godkjenne et tomt dokument.

CI kjører kontrollen etter restore og etter publish, i tillegg til regresjoner for kontrollen. Dette dokumenterer konsistens og at lisensfilene følger med; det er ikke automatisk juridisk godkjenning av ukjente rettigheter eller nedlastede verktøy.

## Utført verifikasjon

Lokal lisenskontroll og kontroll av publiserte dokumenter bestod for alle 11 NuGet-pakker. Seks regresjoner av selve kontrollen bestod. Release-bygg hadde ingen advarsler eller feil; 44 C#-tester, 24 generiske HTTP-kontroller og 24 DHG-kontroller mot lokal testdobbel bestod. Nettlesertestene bestod på desktop og mobil uten JavaScript-feil. CI-filen er kontrollert med actionlint 1.7.12. Lisensarbeidet brukte ingen eksterne FHIR-kall.

Den eksisterende MIT-lisensen er uendret. `verification/RESULTATER.md` i kildekoderepoet inneholder kontrollene og avgrensningene; den filen er ikke en del av appens publiserte lisenspakke.
