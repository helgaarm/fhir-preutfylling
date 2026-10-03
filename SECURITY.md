# Sikkerhet

## Rapportering av sårbarheter

Ikke legg sårbarhetsdetaljer, tokens, pasientdata eller andre hemmeligheter i offentlige issues, PR-er eller workflow-logger.

Bruk [privat sikkerhetsrapportering på GitHub](https://github.com/helgaarm/fhir-preutfylling/security/advisories/new) når funksjonen er aktivert. Hvis rapporteringsskjemaet er utilgjengelig, kontakt vedlikeholderen via en privat kontaktkanal oppgitt på [GitHub-profilen](https://github.com/helgaarm). Hvis ingen slik kanal er oppgitt, opprett en issue som **bare ber om en privat kontaktkanal**, uten tekniske detaljer eller eksempeldata.

En privat rapport bør beskrive berørt versjon/commit, konsekvens og reproduksjon med syntetiske data. Ved lekkasje av et token må det tilbakekalles eller roteres hos utstederen; sletting fra siste commit er ikke tilstrekkelig.

## Støttede versjoner

Sikkerhetsrettelser vedlikeholdes på `main`. Historiske commits, grener og lokale ZIP-kopier oppdateres ikke automatisk. Prosjektet har foreløpig ingen stabil produksjonsrelease eller garantert responstid.

## Bruksgrense

Dette er en lokal testapp for betrodde Questionnaire-definisjoner og syntetiske data. Den er ikke en klinisk tjeneste eller en sandbox for vilkårlige FHIRPath-uttrykk. Ikke eksponer den offentlig eller bruk virkelige pasientdata uten en separat implementasjon og gjennomgang av autentisering, autorisasjon, kliniske regler og drift.

- Behold loopback-binding, begrensede FHIR-baser, avvisning av redirects og pasientkontroll.
- Oppbevar tokens i miljøvariabler eller en egnet hemmelighetstjeneste. Ikke legg dem i Q, Git, issues, skjermbilder eller `launch.json`.
- Del aldri ekte Patient-/Observation-ressurser i feilrapporter. Alle sjekkede eksempler skal være syntetiske.
- Publisert kildekode og historikk skal behandles som permanent offentlig tilgjengelig.

## Kontroller i repoet

- CI bygger med låste NuGet-avhengigheter, avviser audit-advarsler og kjører C#- og HTTP-testene.
- CodeQL analyserer C# og JavaScript. Gitleaks skanner Git-historikken; funn redigeres bort fra loggene.
- Dependency Review er klargjort for PR-er, men krever at eieren aktiverer Dependency Graph og setter `DEPENDENCY_REVIEW_ENABLED=true`; inntil da er jobben synlig hoppet over. NuGet-auditen i CI kjører uavhengig av dette. Dependabot-konfigurasjonen foreslår ukentlige NuGet- og Actions-oppdateringer når GitHub-funksjonen er tilgjengelig; de flettes ikke automatisk.
- Actions er låst til hele commit-ID-er. CI har lesetilgang; CodeQL får bare den ekstra skrivetilgangen det trenger til sikkerhetsresultater. Fork-PR-er kjører uten repo-hemmeligheter og bruker ikke `pull_request_target`.

Workflowene erstatter ikke aktivering av GitHubs egne innstillinger. Status og oppsett for varsler, push-beskyttelse, privat rapportering og grenvern beskrives i [docs/GITHUB_SECURITY.md](docs/GITHUB_SECURITY.md). Ingen skanner garanterer at alle sårbarheter eller personopplysninger blir funnet.
