# Sikkerhetsoppsett for offentlig GitHub-repo

Repoet skal være offentlig. MIT-lisensen gjelder prosjektets egen kode og dokumentasjon; avhengigheter beholder sine egne lisenser.

## Kontroller som følger kildekoden

| Tiltak | Implementasjon |
| --- | --- |
| Reproduserbare avhengigheter | `packages.lock.json`, `dotnet restore --locked-mode` i CI |
| Sårbare avhengigheter | NuGet audit av alle direkte/transitive pakker; audit-advarsler feiler restore |
| Tester | Release-bygg, C#-selvtester, HTTP-tester mot publisert app og DHG-kontrakttester med lokal syntetisk kilde |
| Endring av avhengigheter | Dependency Review aktivert for nye PR-kjøringer; forutsetter Dependency Graph og `DEPENDENCY_REVIEW_ENABLED=true` |
| Oppdateringer | Dependabot for NuGet og GitHub Actions hver uke, uten automatisk fletting |
| Kodeanalyse | CodeQL med `security-extended` for C# og JavaScript, også ukentlig |
| Hemmeligheter | Gitleaks over Git-historikken med redigerte funn i logger |
| Workflow-validering | actionlint i CI |
| Leverandørkjede | Actions er låst til hele commit-ID-er; CLI-nedlastinger har fast versjon og kontrollert SHA-256 |
| Minimale rettigheter | `contents: read`; bare CodeQL-jobben har `security-events: write`; ingen vedvarende checkout-credentials |
| Rapportering/eierskap | SECURITY.md, CODEOWNERS, PR-mal og lenke fra issue-oppsettet |

Gitleaks 8.30.1 og actionlint 1.7.12 er låst som binærnedlastinger. Dependabot oppdaterer Action-referansene, men **ikke disse to URL-ene/sjekksummene**. Vedlikeholder må kontrollere nye versjoner og oppdatere versjon og sjekksum sammen i `.github/workflows/ci.yml`.

CodeQL-funn må vurderes i Security-fanen; en vellykket analyse betyr at skanningen kjørte, ikke at koden er sårbarhetsfri. Secrets-skanning stopper ikke allerede lekkede tokens fra å virke: tilbakekall dem straks.

## Innstillinger som krever GitHub-administrasjon

Disse innstillingene ligger hos GitHub og aktiveres ikke av dokumentasjonen eller workflowfilene alene:

1. **Settings → Advanced Security / Code security**: aktiver Dependency Graph, Dependabot alerts, Dependabot security updates, secret scanning og push protection.
2. **Security → Advisories**: aktiver private vulnerability reporting. SECURITY.md beskriver en reservevei dersom skjemaet ikke er tilgjengelig.
3. **Settings → Actions → General**: velg lesetilgang som standard for `GITHUB_TOKEN`, ikke la Actions godkjenne PR-er, og krev godkjenning av workflowkjøring fra alle eksterne fork-bidragsytere.
4. **Settings → Branches / Rules**: beskytt `main` med PR-krav, oppdatert gren og vellykkede `Build and test`, `Secret scan`, `Workflow lint`, `CodeQL (csharp)` og `CodeQL (javascript-typescript)`. Blokker force-push/sletting, krev avklarte diskusjoner og lineær historikk. Regelen skal også gjelde administratorer.
5. Slett ferdig flettede arbeidsgrener automatisk. Behold repoet offentlig.

For en repo-eier uten andre vedlikeholdere brukes PR-krav med **0 påkrevde eksterne godkjenninger**, slik at eieren kan flette egne PR-er etter grønne kontroller. CODEOWNERS identifiserer fortsatt eieren. Når en ekstra vedlikeholder er lagt til, aktiver minst én uavhengig godkjenning, kodeeiergodkjenning og godkjenning etter siste push.

Oppsettet bruker **advanced CodeQL workflow**. Ikke aktiver CodeQL default setup samtidig; GitHub kan da avvise resultatopplasting fra den egendefinerte workflowen.

Dependency Graph og repository-variabelen `DEPENDENCY_REVIEW_ENABLED=true` ble aktivert og kontrollert 3. oktober 2026. Dependency Review kjøres ved neste PR-hendelse; tidligere kjøringer som er **skipped**, er ikke gjennomførte kontroller. NuGet-auditen i `Build and test` er aktiv hele tiden og trenger ikke GitHubs Dependency Graph. Se [statusrapporten](SECURITY_SETUP_STATUS.md) for skillet mellom aktiverte innstillinger og gjennomførte analyser.

Ved nytt oppsett kan variabelen settes med egen `gh`-innlogging etter at Dependency Graph er aktivert:

```sh
gh variable set DEPENDENCY_REVIEW_ENABLED --body true --repo helgaarm/fhir-preutfylling
```

Oppsettscriptets aktivering av Dependabot-varsler aktiverer også Dependency Graph, slik [GitHubs API-dokumentasjon](https://docs.github.com/en/rest/repos/repos#enable-vulnerability-alerts) beskriver. Kontroller at grafens API er tilgjengelig før variabelen settes:

```sh
gh api repos/helgaarm/fhir-preutfylling/dependency-graph/compare/main...main
```

Et vellykket svar med `[]` bekrefter at API-et er tilgjengelig for innloggingen. Sammenligning av samme commit kontrollerer ikke om avhengighetene har sårbarheter. Kontroller første Dependency Review-kjøring i en PR før den eventuelt legges til som en sjette påkrevd statuskontroll.

## Gjennomfør oppsettet

Scriptet bruker Python 3 og `gh` autentisert som en bruker/app med administrasjonsrettigheter til **helgaarm/fhir-preutfylling**. Det endrer ingen andre repoer. Standardkjøring er lesende:

```sh
python scripts/configure-github-security.py
```

Aktiver innstillingene etter at workflowene er flettet og kontrollene på `main` er grønne:

```sh
python scripts/configure-github-security.py --apply
```

Når en annen vedlikeholder kan godkjenne eierens PR-er:

```sh
python scripts/configure-github-security.py --apply --require-review
```

Scriptet rapporterer hver avvist operasjon og avslutter med feilstatus hvis noe gjenstår. GitHub `403 Resource not accessible by integration` betyr at tilkoblingen mangler nødvendig API-tilgang, selv om brukeren eier repoet. Det løses i GitHub-appens rettigheter eller ved å kjøre med eierens egen `gh`-innlogging. Ikke send tokens i chat, issues eller PR-er.

Scriptet håndhever ikke statuskontroller før de har kjørt vellykket på gjeldende `main`; dette hindrer at en ny, feilkonfigurert workflow låser repoet.

## Kontroll etter oppsett

- Åpne Actions og kontroller at CI og CodeQL har kjørt på gjeldende `main`.
- Kontroller at Security-fanen gjenkjenner LICENSE og SECURITY.md, at privat rapportering er aktiv, og at alerts/push protection er slått på.
- Kjør scriptet uten `--apply` og kontroller at lesing bekrefter innstillingene.
- Ikke test push protection med et ekte token. Bruk kun GitHubs dokumenterte testverdier hvis en slik test ønskes.

Se [statusrapporten](SECURITY_SETUP_STATUS.md) for hva som faktisk ble verifisert ved dette oppsettet. Statusen er et øyeblikksbilde og må kontrolleres på nytt etter senere innstillingsendringer.
