# Opprinnelse og rettighetsgrunnlag

Registrert 4. oktober 2026. Denne oversikten dokumenterer hva som er undersøkt og hva eieren har bekreftet. Den overfører ikke tredjepartsrettigheter.

| Materiale | Opprinnelse og bruk | Dokumentert grunnlag |
| --- | --- | --- |
| Egen C#-, JavaScript-, HTML-, CSS- og Python-kode samt egen dokumentasjon | Dette repoet, videreutviklet fra eierens vedlagte arkitekturbeskrivelse og C#-demokode | Rotens `LICENSE`: MIT, copyright 2026 Armann Helgason. Eieren bekreftet nødvendige rettigheter/tillatelser 4. oktober 2026. |
| Opprinnelig arkitekturbeskrivelse og demokode | Referansegrunnlag nevnt i README; originalfilene inngår ikke i repoets distribusjon | Eieren svarte «ja jeg har det» på spørsmål om rettigheter eller tillatelse til publisering og videreformidling av referansekoden og DHG-eksemplene. Det er ikke registrert en separat avtale eller originalfilhash her. |
| `DHG_FHIR_API_Utviklerveiledning_v2_5 (1).docx` | Brukt til implementasjon av API-kontrakt og eksempler. Tittelsiden angir versjon 1.3, 27. september 2026 | Samme eierbekreftelse. Dette er ikke en erklæring om at den eksterne tjenesten eller hele dokumentet er MIT-lisensiert. DOCX-filen distribueres ikke. |
| DHG-testidentifikatorene | To syntetiske testpersoner fra utviklerveiledningen, brukt i konfigurasjon, requests og tester | Dekket av eierens bekreftelse for DHG-eksemplene. Behandles som testidentifikatorer; de gir ingen tilgangsrett i produksjon. |
| `examples/dhg-patient.json`, `dhg-resources.json` og Q-eksempler | Lokale syntetiske fixtures og klienteksempler laget for appen med dokumentert DHG-kontrakt | Egen eksempelstruktur under MIT, med forbehold for kodeverkene. Filene er ikke kopier av komplette kliniske API-svar. |
| Andre filer i `examples/` og `verification/actual-response-pregnancy.json` | Syntetiske demoressurser og generert QR fra lokal demo | Egen struktur under MIT; LOINC/UCUM-referanser beholder sine vilkår. |
| `verification/app-*.png` | Skjermbilder av testappen | Appens eget grensesnitt og syntetiske testdata; ingen påstand om eierskap til eventuelle viste varemerker eller kodeverk. |
| `wwwroot/favicon.svg` | Enkel bokstavbasert SVG i repoet | Ingen innbakt fontfil eller nedlastet ikonpakke identifisert. Behandles som prosjektets eget grensesnittmateriale. |
| Skrifttyper i CSS/SVG | Systemfonter angitt som navn, blant annet Inter, Segoe UI, Consolas og Arial | Ingen fontfiler, CDN-import eller `@font-face`-filer distribueres. Installasjon/distribusjon av selve fontene må vurderes separat dersom dette endres. |
| NuGet-biblioteker | Gjenopprettede pakker, med upstream-revisjon fra `.nuspec` | [Versjoner, hash og lisensbevis](../LICENSES/inventory.json). |

`output/`, `publish/`, `bin/`, `obj/` og lokale verktøymiljøer ignoreres av Git. Midlertidige uttrekk av DOCX, hentet Swagger og undersøkelsesfiler i `output/` skal ikke tas med ved manuell ZIP-pakking. Originaldokumentet ble lest som faglig referanse, ikke som instruksjoner om å endre repoets lisens.

Bekreftelsen fra eieren er et registrert rettighetsgrunnlag for prosjektarbeidet, ikke en uavhengig kontroll av arbeidsavtaler, arbeidsgiverrettigheter eller avtaler med andre eiere. Bevar underliggende tillatelser hos prosjektets eier; ikke legg konfidensielle avtaler i et offentlig repo. Nye innlimte utdrag, grafikk, kodeverk eller referansedokumenter må få egen kilde- og rettighetsregistrering.

Se [lisensgjennomgangen](LICENSE_REVIEW.md) for distribusjonskrav og forhold som må vurderes for den enkelte bruker.
