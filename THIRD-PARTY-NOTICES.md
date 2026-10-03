# Tredjepartsmerknader

Kontrollert 4. oktober 2026 for den låste `net9.0`-avhengighetsgrafen. Prosjektets egen kode er under [MIT](LICENSE). Bibliotekene beholder sine originale lisens- og opphavsmerknader. Se [detaljert lisensgjennomgang](docs/LICENSE_REVIEW.md) og [opprinnelse](docs/PROVENANCE.md).

## Biblioteker

Versjoner, pakkehash, opphav og kilderevisjoner finnes i [LICENSES/inventory.json](LICENSES/inventory.json). Originale lisens- og merknadstekster følger i `LICENSES/`.

| Komponent | Versjon | Lisens | Medfølgende tekst |
| --- | --- | --- | --- |
| Hl7.Fhir.R4, Hl7.Fhir.Base, Hl7.Fhir.Conformance | 6.6.0 | BSD-3-Clause | [Firely SDK](LICENSES/Firely-SDK-BSD-3-Clause.txt) |
| Fhir.Metrics | 1.4.0 | BSD-3-Clause | [Firely Metrics](LICENSES/Firely-Metrics-BSD-3-Clause.txt) |
| Microsoft.Extensions.Caching.Abstractions | 8.0.0 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader A](LICENSES/Microsoft-Extensions-NOTICES-8.0.0.txt) |
| Microsoft.Extensions.Caching.Memory | 8.0.1 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader B](LICENSES/Microsoft-Extensions-NOTICES-8.0.1.txt) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader B](LICENSES/Microsoft-Extensions-NOTICES-8.0.1.txt) |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader B](LICENSES/Microsoft-Extensions-NOTICES-8.0.1.txt) |
| Microsoft.Extensions.Options | 8.0.2 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader A](LICENSES/Microsoft-Extensions-NOTICES-8.0.0.txt) |
| Microsoft.Extensions.Primitives | 8.0.0 | MIT | [Microsoft](LICENSES/Microsoft-MIT.txt), [merknader A](LICENSES/Microsoft-Extensions-NOTICES-8.0.0.txt) |
| Newtonsoft.Json | 13.0.4 | MIT | [Newtonsoft.Json](LICENSES/Newtonsoft.Json-MIT.txt) |

Firelys lisens nevner HL7, Firely, Microsoft Open Technologies og bidragsytere. Metrics' originale lisens oppgir Furore Health Informatics og inneholder bokstavelig `{organization}`. Tekstene er beholdt uendret; pakkenes nyere copyrightmetadata er bevart separat i oversikten.

Microsofts to merknadsfiler er ikke like. De er brede oppstrømsoversikter og er tatt med fra pakkene i sin helhet. De betyr ikke at alle omtalte komponenter faktisk inngår i denne appen. Den samme MIT-teksten og [runtime-merknader for .NET 9.0.20](LICENSES/Dotnet-Runtime-NOTICES-9.0.20.txt) er inkludert for standard .NET-publisering og apphost. Se avgrensningen for andre runtime-versjoner og selvstendige distribusjoner i gjennomgangen.

## DHG og distribusjon

Repoeieren bekreftet 4. oktober 2026 nødvendige rettigheter eller tillatelser til referansekoden og DHG-eksemplene. Original DOCX, Swagger-uttrekk, API-dumper og tilgangsopplysninger inngår ikke i distribusjonen.

Ved distribusjon av appen skal `LICENSE`, denne filen og hele `LICENSES/` følge med. I appen finnes de samme merknadene og lisenstekstene via **Lisenser** (`/licenses`).
