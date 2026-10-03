# Tredjepartsmerknader

Kontrollert 4. oktober 2026 for den låste `net9.0`-avhengighetsgrafen. Prosjektets egen kode er under [MIT](LICENSE). Dette endrer ikke rettighetene til bibliotekene, kodeverkene eller varemerkene nedenfor. Se [detaljert lisensgjennomgang](docs/LICENSE_REVIEW.md) og [opprinnelse](docs/PROVENANCE.md).

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

## LOINC

This material contains content from LOINC (http://loinc.org). LOINC is copyright © Regenstrief Institute, Inc. and the Logical Observation Identifiers Names and Codes (LOINC) Committee and is available at no cost under the license at http://loinc.org/license. LOINC® is a registered United States trademark of Regenstrief Institute, Inc.

Den påkrevde merknaden følger også som [LOINC_short_license.txt](LICENSES/LOINC_short_license.txt). Behold den sammen med produkter som videreformidler disse eksemplene. Koder og offisielle navn brukt her:

| LOINC-kode | Long Common Name | Kilde |
| --- | --- | --- |
| 18185-9 | Gestational age | https://loinc.org/18185-9 |
| 85354-9 | Blood pressure panel with all children optional | https://loinc.org/85354-9 |
| 8480-6 | Systolic blood pressure | https://loinc.org/8480-6 |
| 8462-4 | Diastolic blood pressure | https://loinc.org/8462-4 |

Ingen full LOINC-tabell, spørreskjemainstrumenter eller terminologiserver distribueres. Norske feltetiketter er appens forklaringer, ikke en offisiell norsk LOINC-utgave. Vilkårene for kodeverket er https://loinc.org/license og gjelder separat fra MIT.

## UCUM

Enhetskodene `d` og `mm[Hg]` bruker systemet `http://unitsofmeasure.org`. UCUM er copyright 1999–2024 Regenstrief Institute, Inc., under UCUM License 1.1. Se [UCUM-merknaden](LICENSES/UCUM-NOTICE.txt) og https://ucum.org/license for vilkår og ansvarsfraskrivelse. Appen endrer ikke kodene eller definisjonene og distribuerer ingen selvstendig UCUM-tabell.

## SNOMED CT

DHG-eksemplet refererer til `http://snomed.info/sct|738070007`. SNOMED CT tilhører SNOMED International og omfattes ikke av prosjektets MIT-lisens. [SNOMED CT-merknaden](LICENSES/SNOMED-CT-NOTICE.txt) gir kilder og avgrensning, men er ingen lisensavtale eller underlisens. Egen bruk og videreformidling må ha relevant lisensgrunnlag: https://www.snomed.org/get-snomed.

## HL7 FHIR, SDC og øvrige navn

FHIR R4-spesifikasjonen er publisert under CC0: https://hl7.org/fhir/R4/license.html. Appen bruker også SDC-extension-URL-er og HL7-koder, blant annet `v3-ActCode|AMB`; det følger ikke med komplette spesifikasjoner eller HL7-kodeverk. Tredjepartsterminologi får ikke CC0-status bare fordi den omtales i en FHIR-spesifikasjon. Se også https://terminology.hl7.org/license.html.

HL7®, FHIR®, LOINC® og SNOMED CT® tilhører sine respektive rettighetshavere. Ingen godkjenning, sertifisering eller tilslutning fra HL7, Firely, Regenstrief, SNOMED International eller DHG-tilbyderen er underforstått.

## DHG og distribusjon

Repoeieren bekreftet 4. oktober 2026 nødvendige rettigheter eller tillatelser til referansekoden og DHG-eksemplene. Dette omfatter ikke automatisk tredjepartenes kodeverk eller rett til å bruke den eksterne API-tjenesten. Original DOCX, Swagger-uttrekk, API-dumper og tilgangsopplysninger inngår ikke i distribusjonen.

Ved distribusjon av appen skal `LICENSE`, denne filen og hele `LICENSES/` følge med. I appen finnes de samme merknadene og lisenstekstene via **Lisenser** (`/licenses`). Videre distributører må også gjøre LOINC-merknaden tilgjengelig ved sin nedlasting eller i tjenestens lisensvilkår.
