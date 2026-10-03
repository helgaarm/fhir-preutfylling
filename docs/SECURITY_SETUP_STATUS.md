# Verifisert sikkerhetsstatus

Kontrollert 3. oktober 2026 mot GitHubs API med eierens lokale `gh`-innlogging. Innloggingen har administrasjonstilgang til **helgaarm/fhir-preutfylling**. Repoet er fortsatt offentlig. Dette er et øyeblikksbilde, ikke en garanti mot sårbarheter.

## Aktivert og lest tilbake fra GitHub

| Tiltak | Verifisert status |
| --- | --- |
| Dependabot-varsler og Dependency Graph | Aktivert. Varsel-API-et og Dependency Graphs sammenlignings-API svarer uten tilgangsfeil. |
| Dependabot security updates | `enabled: true`, `paused: false` |
| Dependency Review | Repository-variabelen `DEPENDENCY_REVIEW_ENABLED` er `true`. Workflowen vil kjøre ved nye PR-hendelser. |
| Privat sårbarhetsrapportering | `enabled: true`; [privat rapporteringsskjema](https://github.com/helgaarm/fhir-preutfylling/security/advisories/new) |
| Secret scanning | `enabled` |
| Push protection | `enabled` |
| Actions-standardrettigheter | `default_workflow_permissions: read`, `can_approve_pull_request_reviews: false` |
| Workflowkjøringer fra eksterne fork-bidragsytere | `approval_policy: all_external_contributors` |
| Automatisk sletting av flettede grener | `delete_branch_on_merge: true` |
| Grenvern for `main` | Aktivert og håndhevet også for administratorer. Se kravene nedenfor. |

Før denne kontrollen var Dependabot-varsler, sikkerhetsoppdateringer og privat rapportering avslått, `main` var ubeskyttet, og Dependency Review-variabelen manglet. Secret scanning, push protection og begrensede Actions-standardrettigheter var allerede aktive. Den tidligere GitHub-appens 403-feil hindret ikke eierens lokale innlogging i å fullføre oppsettet.

## Grenvern

`main` krever pull request, oppdatert gren og disse vellykkede kontrollene, bundet til GitHub Actions (app-ID `15368`):

- `Build and test`
- `Secret scan`
- `Workflow lint`
- `CodeQL (csharp)`
- `CodeQL (javascript-typescript)`

Force-push og sletting er blokkert. Lineær historikk og avklarte PR-diskusjoner kreves. Administratorer omfattes av reglene.

Det kreves **0 uavhengige godkjenninger**, slik at en eneeier kan flette egne PR-er etter grønne kontroller. Minst én uavhengig godkjenning og kodeeiergodkjenning bør kreves når en ekstra vedlikeholder er tilgjengelig. Dependency Review er foreløpig ikke en sjette påkrevd statuskontroll; kontroller først en vellykket PR-kjøring etter aktivering.

## Gjennomførte kontroller

Gjeldende `main` ved kontrollen var [`b2b5cf115144e90ef7ffc8a3b8915ac94bf751e1`](https://github.com/helgaarm/fhir-preutfylling/commit/b2b5cf115144e90ef7ffc8a3b8915ac94bf751e1). Alle fem påkrevde sjekker var fullført med `success` før grenvernet ble aktivert:

- [CI: Build and test, Secret scan og Workflow lint](https://github.com/helgaarm/fhir-preutfylling/actions/runs/37153010033)
- [CodeQL: C# og JavaScript](https://github.com/helgaarm/fhir-preutfylling/actions/runs/37153010052)

API-oppslag etter aktivering viste **0 åpne CodeQL-varsler, 0 åpne Dependabot-varsler og 0 åpne secret-scanning-varsler**. Nyaktiverte analyser kan gi senere funn; ingen hemmelighetsverdier ble skrevet ut under kontrollen.

Lokalt bestod `dotnet restore --locked-mode --force` med audit av direkte og transitive NuGet-pakker uten audit-advarsler. Prosjektet er lokalt endret til .NET 9; disse endringene og dokumentasjonsoppdateringene er ikke publisert til GitHub i denne kontrollen. Bygg- og regresjonsresultatene for .NET 9 står i [verification/RESULTATER.md](../verification/RESULTATER.md).

## Gjenstående verifikasjon og vedlikehold

- Kontroller Dependency Review ved neste PR-hendelse. Aktivering av variabelen starter ikke gamle, hoppede kjøringer på nytt.
- Vurder nye sikkerhetsvarsler og Dependabot-PR-er fortløpende. Oppdateringer flettes ikke automatisk.
- Gitleaks og actionlint er binærnedlastinger med fast versjon og SHA-256; Dependabot oppdaterer ikke disse URL-ene og sjekksummene.
- CodeQL bruker advanced workflow. Default setup er `not-configured` og skal ikke aktiveres samtidig.

Lesende kontroll av innstillingene:

```sh
python scripts/configure-github-security.py
gh api repos/helgaarm/fhir-preutfylling/actions/variables/DEPENDENCY_REVIEW_ENABLED
```

Scriptets `OK` viser at API-kallet lyktes. Vurder også verdiene i svaret; for eksempel er `enabled: false` ikke en aktivert kontroll. Se [oppsettsveiledningen](GITHUB_SECURITY.md) for administrasjonskommandoer og forutsetninger.
