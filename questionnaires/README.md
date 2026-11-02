# Kartlegging før første konsultasjon

Last opp [forstekonsultasjon-gravid.json](forstekonsultasjon-gravid.json) i testappen og velg Gravid-testkilden. Filen inneholder 220 elementer og 30 Gravid-preutfyllingsregler, uten pasientdata eller API-svar.

Utgangspunktet er brukerens vedlagte `cfe1fda3-2529-4e60-8d54-748ef77b21f5-v2.2.1.json`, med tittel «Kartlegging før første svangerskapskontroll» og utgiver Hdir. Originalens SHA-256 er `be309095849d2e794d5920bc9cb79ea9e9e0487b4d59ac1ca40b71da37393fa1`. Dette er en tilpasset testkopi; den er ikke en ny offisiell utgave fra utgiveren. Originalens rettighets- og metadataopplysninger gjelder fortsatt; prosjektets MIT-lisens gir ingen nye rettigheter til originalskjemaet.

All opprinnelig metadata, rekkefølge, struktur, spørsmålstekst, svaralternativer, verdilister og visningsvilkår er bevart, med tre uttrykkelig avtalte rettelser: `repeats: true` er lagt til på avkrysningsspørsmål som manglet egenskapen:

| Spørsmål | linkId |
| --- | --- |
| Har du hatt noe av følgende under tidligere svangerskap? | `242dc246-8ac8-4d55-8467-40845a932e68` |
| Har du hofteleddsdysplasi eller noen annen arvelig sykdom? | `c5e33601-28ec-43fd-8440-1679ce307ee5` |
| Har du, eller har du tidligere hatt noe av dette? | `5628da84-f498-41c4-8d7b-c3c6d20361f6` |

Preutfyllingen legger til pasientkontekst, variabler, initialExpression og en inneholdt Parameters-ressurs med originalens svarkoder. Skjemaversjon, ID og canonical er beholdt som ønsket for sammenligning. Last ikke testkopien inn over en offisiell versjon på en skjemaserver.

API-verdier oversettes til originalens datatyper og svarkoder. Manglende eller tvetydige kildedata gir tomme felt. Nåværende stillingsprosent brukes ikke som dokumentasjon på arbeid de siste seks månedene. Flervalg kan gi flere separate Coding-svar.

Testappen kjører SDC-reglene, men ikke originalens Helsenorge-spesifikke preutfylling, beregninger eller ekstraksjon. Disse definisjonene er bevart og gir en merknad. Navn, fødselsnummer og folkeregistrert adresse/postnummer/poststed har originalens Helsenorge-regler; de fem Gravid-reservereglene fra den tidligere forenklede testkopien er ikke med. Appen oppretter en ny QR og tar ikke et allerede utfylt QR som input.

Filen er identisk med den lokalt verifiserte flervalgsversjonen, SHA-256 `9224778423d17d48d8398f1438d0e04b0880f95efa9a05b14415d33209f82298`. Ved leveransen ble den sammenlignet strukturelt mot originalen, og alle forskjeller var begrenset til de nevnte tilleggene og tre flervalgsrettelser. Verifisering med lokale data omfattet 23 flervalgskontroller, 16 HTTP-kontroller og opplasting/forhåndsvisning. API-snapshots og personspesifikke verifikasjonsfiler lagres ikke i repoet.
