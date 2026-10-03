# Bidra

Bruk en egen gren og en pull request mot `main`. Beskriv hva som er endret, hvorfor og hvordan det er testet. Hold endringen avgrenset, og bruk kun syntetiske data.

Kjør fra prosjektroten med .NET 9 SDK (9.0.300 eller nyere i 9.0-serien):

```sh
dotnet restore --locked-mode
python scripts/check-licenses.py
python verification/license-check-tests.py
dotnet build --no-restore --warnaserror
dotnet run --project src/GenericPopulation --no-build -- --self-test
```

Start appen og kjør `python verification/http-smoke.py` når HTTP- eller preutfyllingsflyten berøres. Kjør også nettlesertesten ved endringer i webgrensesnittet. Se README for oppsett.

Når en NuGet-versjon endres, kjør `dotnet restore --force-evaluate`, kontroller `packages.lock.json`, og ta med både manifest og låsefil i PR-en. Gjennomgå lisens, copyright og merknader for endrede direkte og transitive pakker. Oppdater `LICENSES/inventory.json`, de relevante lisenstekstene og `THIRD-PARTY-NOTICES.md`; se [lisensgjennomgangen](docs/LICENSE_REVIEW.md). Løs audit-varsler; ikke legg til undertrykking for å gjøre CI grønn uten en dokumentert vurdering.

Nye Actions skal bruke hele 40-tegns commit-ID-er med versjonskommentar. Workflows skal bruke minst mulig tokenrettigheter, ikke interpolere PR-titler eller annen ubetrodd tekst direkte i shell, og ikke kjøre fork-kode med hemmeligheter eller `pull_request_target`.

Ved mistanke om en sårbarhet: følg [SECURITY.md](SECURITY.md), ikke opprett en offentlig feilrapport med detaljer. Les [MIT-lisensen](LICENSE); bidrag leveres under samme lisens, og tredjepartskode må ha kompatible vilkår og beholde påkrevde merknader.

Bidra bare med materiale du har rett til å publisere under de oppgitte vilkårene, også når arbeidsgiver eller andre eier rettigheter. Dokumenter kilde og tillatelse for importert kode, tekst, grafikk og eksempler i [opprinnelsesoversikten](docs/PROVENANCE.md). Ikke merk tredjepartskodeverk som MIT: LOINC, UCUM, SNOMED CT og eksterne spørreskjemaer krever separat vurdering. Ingen CLA eller overføring av eierskap er innført med denne dokumentasjonen.
