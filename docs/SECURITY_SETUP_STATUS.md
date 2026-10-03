# Verifisert sikkerhetsstatus

Kontrollert 3. oktober 2026. Dette er et øyeblikksbilde for oppsettet i [PR #1](https://github.com/helgaarm/fhir-preutfylling/pull/1). Senere endringer og nye sårbarheter krever ny vurdering.

## Implementert og kontrollert

| Tiltak | Verifikasjon |
| --- | --- |
| MIT | LICENSE og prosjektmetadata; identisk LICENSE kopieres til publisert app |
| NuGet-låsing og audit | Seks direkte/transitive pakker; `restore --locked-mode` mot NuGet.org uten audit-advarsler |
| Bygg/test | Release-bygg uten advarsler/feil; 27/27 C#-tester og 22/22 HTTP-kontroller bestått |
| GitHub CI | [Build and test, Secret scan og Workflow lint bestått](https://github.com/helgaarm/fhir-preutfylling/actions/runs/37152623898) |
| CodeQL | [C# og JavaScript-analyser fullført](https://github.com/helgaarm/fhir-preutfylling/actions/runs/37152623904) |
| Hemmeligheter | Gitleaks over Git-historikken uten funn; funn ville vært redigert i loggene |
| Workflows | Faste 40-tegns Action-commits, tidsgrenser, lesetoken i CI og kun nødvendig CodeQL-skrivetilgang |
| Forsyningskjede | Eksplisitt NuGet.org-kilde, låsefil, SHA-256-verifiserte Gitleaks/actionlint-binarier |
| Vedlikehold | Dependabot-konfigurasjon for NuGet/Actions, CODEOWNERS, SECURITY.md og PR-/bidragsveiledning |

CodeQL-resultatopplasting er verifisert gjennom vellykkede workflow-kjøringer. Den tilkoblede appen mangler tilgang til API-et for å liste sikkerhetsvarsler (403); rapporten hevder derfor ikke at antall åpne CodeQL-funn er null. Se Security-fanen som repo-eier.

## Ikke aktivert eller ikke verifisert av tilkoblingen

| GitHub-funksjon | Faktisk observert status / gjenstående arbeid |
| --- | --- |
| Dependency Graph / Dependency Review | Dependency Review returnerte «not supported … ensure Dependency graph is enabled». Jobben er derfor eksplisitt `skipped` til eieren aktiverer grafen og `DEPENDENCY_REVIEW_ENABLED=true`. NuGet-auditen i CI er aktiv uavhengig av dette. |
| Private vulnerability reporting | API viste `enabled: false`. Aktivering returnerte 403. SECURITY.md har en reservevei for å etablere privat kontakt. |
| Dependabot alerts og security updates | Aktiveringskall returnerte 403. Må aktiveres/bekreftes av eieren; ukentlig versjonsoppdatering i dependabot.yml er en separat mekanisme. |
| Secret scanning og push protection | Aktiveringskall returnerte 403. Tilkoblingen kan ikke bekrefte de effektive repo-innstillingene. Gitleaks erstatter ikke push protection. |
| Actions-standardrettigheter og fork-godkjenning | Administrative lese-/endringskall returnerte 403. De nye workflowene setter selv begrensede rettigheter. Repoets standard må kontrolleres av eieren. |
| `main`-vern | `main` ble observert med `protected: false`; ingen rulesets ble listet. Administrasjon kreves for å håndheve PR, vellykkede sjekker, vern mot force-push/sletting og avklarte diskusjoner. |
| Automatisk sletting av flettede grener | Repoet viste `delete_branch_on_merge: false`; endringskallet returnerte 403. |

Disse 403-svarene kommer fra GitHub-appens rettigheter, ikke fra manglende godkjenning av selve oppgaven. Ingen av innstillingene over påstås aktivert av en dokumentasjonsfil eller workflow.

## Fullfør administrasjonsoppsettet

Eieren kan bruke [repoets innstillinger](https://github.com/helgaarm/fhir-preutfylling/settings) og følge [GITHUB_SECURITY.md](GITHUB_SECURITY.md), eller kjøre dette med egen `gh`-innlogging som har administrasjonstilgang:

```sh
python scripts/configure-github-security.py --apply
```

Scriptet venter med grenbeskyttelse til fem påkrevde sjekker er grønne på gjeldende `main`. Deretter kan innstillingene inspiseres med samme kommando uten `--apply`. Dependency Graph og aktiveringsvariabelen for Dependency Review må også slås på som beskrevet i veiledningen.
