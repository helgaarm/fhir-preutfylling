using System.Text.Json;
using GenericPopulation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

// Startpunkt og HTTP-vert for den lokale utviklerappen.
// Leseflyt: input -> QuestionnaireGuard -> sentral Patient -> ruter -> FHIR-klienter -> ny QR.
// Motoren tolker skjemaet; datakildene håndterer transport; wwwroot viser resultat og merknader.
// appsettings.json velger kilder, og examples/ viser hvordan Q uttrykker databehovet.

// Kommandolinjemodusene bruker syntetiske data uten å starte webserver eller kalle DHG Test.
if (args.Contains("--self-test"))
{
    Environment.ExitCode = await SelfTests.RunAsync();
    return;
}
if (args.Contains("--demo"))
{
    var name = args.Contains("dhg") ? "dhg" : args.Contains("general") ? "general" : "pregnancy";
    var result = await new PopulationEngine(new FixtureDataSource(name == "dhg" ? DemoFiles.DhgResources() : DemoFiles.Resources()))
        .CreateAsync(DemoFiles.Questionnaire(name), new PopulationContext(name == "dhg" ? DemoFiles.DhgPatient() : DemoFiles.Patient(), !args.Contains("--no-consent")));
    Directory.CreateDirectory("output");
    await File.WriteAllTextAsync("output/questionnaire-response.json", Json(result.Response));
    await File.WriteAllTextAsync("output/population-outcome.json", Json(result.Outcome));
    Console.WriteLine("Syntetisk demo: QR og merknader er skrevet til output/.");
    return;
}

// Oppstart: valider serverstyrte kilder før appen tar imot forespørsler.
var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Demo:Port", 5077);
// Loopback-begrensningen gjelder utviklerappen; klinisk drift krever en egen tilgangsgrense.
var address = $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(address);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
// Unngå standardlogging av kliniske data, søkestrenger og token i denne testverten.
builder.Logging.ClearProviders();
if (builder.Configuration.GetSection("Fhir:Sources").GetChildren().Any(s => s["Mode"] is not null))
    throw new InvalidOperationException("Fhir:Sources:Mode er erstattet. Konfigurer SearchMethod, PatientLookup, PatientBinding og Capabilities eksplisitt.");
var configurationStore = new FhirConfigurationStore(builder.Environment.ContentRootPath,
    builder.Configuration.GetSection("Fhir").Get<FhirConfiguration>() ?? new());
// Ikke la en bakgrunnsprosess arve utviklingsmiljøets sperreproxy og fremstå som klar for eksterne kilder.
try { NetworkStartupGuard.Validate(configurationStore.Current.Configuration.Sources, HttpClient.DefaultProxy); }
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
    return;
}
var app = builder.Build();
// Del forbindelser, men ikke pasientkontekst eller søkecache. Ingen automatiske omdirigeringer
// eller cookies: kildesvar skal ikke kunne flytte et kall med tilgangs- eller pasientinformasjon.
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(20) };
var connectivity = new EndpointConnectivity(http);

// Felles HTTP-grense: unngå caching, begrens nettleseropprinnelse og oversett feil til
// kontrollerte OperationOutcome-svar. Rå unntak og feilkropper fra kilden skal ikke sendes videre.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    try
    {
        if (context.Request.Host.Host is not ("127.0.0.1" or "localhost"))
        { await Error(context, 403, "host", "Testappen er bare tilgjengelig via localhost eller 127.0.0.1."); return; }
        if (context.Request.Method == "POST")
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site" ||
                (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}"))
            { await Error(context, 403, "origin", "Forespørselen må komme fra testappen på samme adresse."); return; }
        }
        await next();
    }
    catch (PopulationException e)
    {
        var status = e.Code == "configuration-conflict" ? 409 : e.Code == "configuration-storage" ? 500 : e.Code == "source-id" ? 400 :
            e.Code.StartsWith("source-") || e.Code is "patient-mismatch" or "paging-limit" or "result-limit" or "response-size" or "destination-policy" ? 502 : 422;
        await Error(context, status, e.Code, e.Message);
    }
    catch (JsonException) { await Error(context, 400, "json", "Input er ikke gyldig JSON."); }
    catch (BadHttpRequestException e) { await Error(context, e.StatusCode, "request", "Forespørselen er ugyldig eller større enn 256 KiB."); }
    catch (HttpRequestException) { await Error(context, 502, "connection", "Kan ikke koble til FHIR-kilden. Kontroller URL, nettverk og sertifikat."); }
    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    { await Error(context, 504, "timeout", "FHIR-hentingen overskred tidsgrensen. Ingen QR ble returnert."); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception)
    { await Error(context, 500, "internal", "En intern feil stoppet preutfyllingen. Ingen QR ble returnert."); }
});

app.UseDefaultFiles();
app.UseStaticFiles();
// Eksponer bare feltene grensesnittet trenger. Token og token-miljøvariabel sendes ikke til klienten.
app.MapGet("/api/config", () =>
{
    var snapshot = configurationStore.Current;
    var sources = snapshot.Configuration.Sources;
    return Results.Json(new
    {
    revision = snapshot.Revision,
    questionnaireBindings = snapshot.Configuration.QuestionnaireBindings,
    requireQuestionnaireBinding = snapshot.Configuration.RequireQuestionnaireBinding,
    profiles = snapshot.Profiles.Select(p =>
    {
        var patientSource = sources.Single(s => s.Id == p.PatientSource);
        return new
        {
            p.Id, p.Name, p.PatientSource, p.DefaultExample,
            patientInput = patientSource.PatientLookup.Interaction == "search" ? "identifier" : "id",
            testPatientIdentifiers = patientSource.AllowedTestPatientIdentifiers,
            endpoints = p.SourceIds().Select(id => { var s = sources.Single(s => s.Id == id); return new { s.Id, s.Name, s.BaseUrl }; })
        };
    }),
    // Kompatibilitet for eksisterende API-klienter som viser enkeltkilder.
    sources = sources.Where(s => s.ExposeAsProfile).Select(s => new
    {
        s.Id, s.Name, s.BaseUrl,
        patientInput = s.PatientLookup.Interaction == "search" ? "identifier" : "id",
        testPatientIdentifiers = s.AllowedTestPatientIdentifiers,
        s.DefaultExample
    }),
    defaultPatientId = "demo-patient", fhirVersion = "4.0.1"
    });
});
// Den lokale konfigurasjonssiden får kun innstillinger, aldri verdier fra token-miljøvariabler.
app.MapGet("/api/configuration", () => ConfigurationResult(configurationStore.Current));
app.MapPost("/api/configuration/validate", (Func<HttpContext, Task<IResult>>)(context => EditConfiguration(context, false)));
app.MapPost("/api/configuration", (Func<HttpContext, Task<IResult>>)(context => EditConfiguration(context, true)));
app.MapPost("/api/sources/{sourceId}/status", async (string sourceId, HttpContext context) =>
{
    if (!context.Request.HasJsonContentType())
        return Fhir(Outcome("content-type", "Send JSON med Content-Type: application/json."), 415);
    using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
        !root.TryGetProperty("configurationRevision", out var revision) || revision.ValueKind != JsonValueKind.String)
        return Fhir(Outcome("input", "Send bare configurationRevision fra den lastede konfigurasjonen."), 400);
    var snapshot = configurationStore.Current;
    if (revision.GetString() != snapshot.Revision)
        return Fhir(Outcome("configuration-conflict", "Konfigurasjonen er endret. Last siden på nytt før du tester tilkoblingen."), 409);
    var source = snapshot.Configuration.Sources.SingleOrDefault(s => s.Id == sourceId);
    if (source is null) return Fhir(Outcome("source-id", "Velg en lagret FHIR-kilde."), 404);
    var result = await connectivity.CheckAsync(source, context.RequestAborted);
    if (configurationStore.Current.Revision != snapshot.Revision)
        return Fhir(Outcome("configuration-conflict", "Konfigurasjonen ble endret under kontrollen. Last siden på nytt."), 409);
    return Results.Json(result);
});
app.MapGet("/api/examples/{scenario}", (string scenario) => Fhir(DemoFiles.Questionnaire(scenario)));
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
// Offentlige lisensfiler fra appens distribusjon, uten pasientdata eller brukerbestemte filstier.
// Tekstformat bevarer den originale juridiske ordlyden uten å tolke den som HTML.
app.MapGet("/licenses", () => Results.Text(LicenseText(), "text/plain; charset=utf-8"));
app.MapMethods("/demo/fhir/", ["HEAD"], () => Results.NoContent());
app.MapMethods("/demo/vitals/", ["HEAD"], () => Results.NoContent());

// Lokal syntetisk FHIR-kilde: demoen går gjennom samme GET-klient og HTTP-kontroller som eksterne kilder.
app.MapGet("/demo/fhir/Patient/{id}", (string id) => id == "demo-patient"
    ? Fhir(DemoFiles.Patient())
    : Fhir(Outcome("not-found", "Fant ikke syntetisk pasient. Bruk demo-patient."), 404));
app.MapGet("/demo/fhir/{resourceType}", async (string resourceType, HttpRequest request, CancellationToken ct) =>
{
    var context = new PopulationContext(DemoFiles.Patient(), true);
    var search = FhirSearch.Parse(resourceType + request.QueryString.Value, context.Patient.Id!);
    return Fhir(await new FixtureDataSource(DemoFiles.Resources()).SearchAsync(search, context, ct));
});
// Et separat syntetisk endepunkt gjør fler-kildeflyten kjørbar uten eksterne tjenester.
app.MapGet("/demo/vitals/Observation", async (HttpRequest request, CancellationToken ct) =>
{
    var context = new PopulationContext(DemoFiles.Patient(), true);
    var search = FhirSearch.Parse("Observation" + request.QueryString.Value, context.Patient.Id!);
    var resources = DemoFiles.Resources().OfType<Observation>()
        .Where(o => o.Code.Coding.Any(c => c.System == "http://loinc.org" && c.Code == "85354-9")).Cast<Resource>().ToList();
    return Fhir(await new FixtureDataSource(resources).SearchAsync(search, context, ct));
});
// Samme behandling med to svarformater: Parameters med QR + merknader, eller bare QR.
app.MapPost("/api/populate", (Func<HttpContext, Task<IResult>>)(context => Populate(context, false)));
app.MapPost("/api/questionnaire-response", (Func<HttpContext, Task<IResult>>)(context => Populate(context, true)));

async Task<IResult> Populate(HttpContext httpContext, bool responseOnly)
{
    // Stabilt oppsett gjennom hele operasjonen, også hvis en annen fane lagrer konfigurasjon.
    var snapshot = configurationStore.Current;
    var sources = snapshot.Configuration.Sources;
    // Én frist for hele innhentingen, koblet til at nettleseren kan avbryte forespørselen.
    if (!httpContext.Request.HasJsonContentType())
        return Fhir(Outcome("content-type", "Send JSON med Content-Type: application/json."), 415);
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    var ct = deadline.Token;
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("questionnaire", out var qJson) || qJson.ValueKind != JsonValueKind.Object)
        return Fhir(Outcome("input", "Input må ha questionnaire (Q-objekt) og pasientvalg."), 400);
    // Valider innpakningen før oppslag. Registrerte skjemaer trenger ikke profilvalg i input.
    if (root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() != 1) ||
        root.EnumerateObject().Any(p => p.Name is not ("questionnaire" or "profileId" or "sourceId" or "patientId" or "patientIdentifier" or "configurationRevision")) ||
        root.TryGetProperty("profileId", out _) && root.TryGetProperty("sourceId", out _))
        return Fhir(Outcome("input", "Send questionnaire, pasientvalg og eventuelt ett profilvalg. Ukjente og dupliserte felt er ikke tillatt."), 400);
    if (root.TryGetProperty("configurationRevision", out var revision))
    {
        if (revision.ValueKind != JsonValueKind.String)
            return Fhir(Outcome("input", "configurationRevision må være tekst."), 400);
        if (revision.GetString() != snapshot.Revision)
            throw new PopulationException("configuration-conflict", "Konfigurasjonen er endret. Last siden på nytt for å bruke gjeldende endepunkter.");
    }
    string? requestedProfileId = null;
    var selector = root.TryGetProperty("profileId", out _) ? "profileId" : "sourceId";
    if (root.TryGetProperty(selector, out var selection))
    {
        if (selection.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(selection.GetString()))
            return Fhir(Outcome("input", "Profilvalg må være en ikke-tom tekstverdi."), 400);
        requestedProfileId = selection.GetString();
        if (selector == "sourceId" && !sources.Any(s => s.ExposeAsProfile && s.Id == requestedProfileId))
            throw new PopulationException("source-id", "Velg en konfigurert enkeltkilde eller bruk profileId.");
    }
    Questionnaire q;
    try { q = new FhirJsonDeserializer().Deserialize<Questionnaire>(qJson.GetRawText()); }
    catch (Exception) { throw new PopulationException("questionnaire-json", "Input må være et gyldig FHIR R4 Questionnaire."); }
    QuestionnaireGuard.Validate(q);
    // Skjemaversjonen bestemmer profilen før pasientfelt eller endepunkt velges.
    var profile = snapshot.Registry.Resolve(q, requestedProfileId);
    var sourceOptions = sources.Single(s => s.Id == profile.PatientSource);
    var patientField = sourceOptions.PatientLookup.Interaction == "search" ? "patientIdentifier" : "patientId";
    if (!root.TryGetProperty(patientField, out var patientValue) || patientValue.ValueKind != JsonValueKind.String ||
        root.EnumerateObject().Any(p => p.Name != "questionnaire" && p.Name != "configurationRevision" && p.Name != selector && p.Name != patientField))
        return Fhir(Outcome("input", "Pasientvalget må være " + patientField + " som tekst. Ikke kombiner pasientfeltene."), 400);
    var patientKey = patientValue.GetString()!;
    sourceOptions.ValidatePatientKey(patientKey);
    // Bare klienter i valgt profil opprettes. Én sentral Patient leses; øvrige kilder deler konteksten.
    var clients = profile.SourceIds().ToDictionary(id => id,
        id => new HttpFhirDataSource(http, sources.Single(s => s.Id == id)), StringComparer.Ordinal);
    var router = new RoutingFhirDataSource(profile, clients.ToDictionary(p => p.Key, p => (IFhirDataSource)p.Value));
    // Utviklermodus tillater testpasienter. En klinisk vert må autentisere brukeren og avgjøre
    // tilgang før innhenting og før den oppretter PopulationContext med PrepopulationAllowed=true.
    var patient = await clients[profile.PatientSource].ReadPatientAsync(patientKey, ct);
    var populationContext = new PopulationContext(patient, true,
        patientField == "patientIdentifier" ? patientKey : null, sourceOptions.Validate());
    var result = await new PopulationEngine(router).CreateAsync(q, populationContext, ct);
    httpContext.Response.Headers["X-Fhir-Requests"] = clients.Values.Sum(s => s.RequestCount).ToString();
    httpContext.Response.Headers["X-Population-Profile"] = profile.Id;
    if (responseOnly) return Fhir(result.Response);
    return Fhir(new Parameters
    {
        Parameter = [
            new Parameters.ParameterComponent { Name = "response", Resource = result.Response },
            new Parameters.ParameterComponent { Name = "issues", Resource = result.Outcome },
            // Kildesporing uten pasientverdier. QR holdes fri for transportmetadata.
            .. clients.Where(p => p.Value.RequestCount > 0).Select(p => new Parameters.ParameterComponent
            {
                Name = "source", Part = [
                    new() { Name = "id", Value = new FhirString(p.Key) },
                    new() { Name = "requests", Value = new Integer(p.Value.RequestCount) },
                    new() { Name = "searches", Value = new Integer(router.SearchCounts.GetValueOrDefault(p.Key)) }
                ]
            })
        ]
    });
}

async Task<IResult> EditConfiguration(HttpContext context, bool save)
{
    if (!context.Request.HasJsonContentType())
        return Fhir(Outcome("content-type", "Send konfigurasjon med Content-Type: application/json."), 415);
    using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var root = document.RootElement;
    FhirConfigurationStore.ValidateJson(root);
    if (root.ValueKind != JsonValueKind.Object ||
        root.EnumerateObject().Any(p => p.Name is not ("revision" or "configuration")) ||
        !root.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.String ||
        !root.TryGetProperty("configuration", out var configuration) || configuration.ValueKind != JsonValueKind.Object)
        return Fhir(Outcome("input", "Send revision og configuration fra konfigurasjonsvisningen."), 400);
    if (save) return ConfigurationResult(await configurationStore.SaveAsync(configuration, revision.GetString()!, context.RequestAborted));
    if (revision.GetString() != configurationStore.Current.Revision)
        throw new PopulationException("configuration-conflict", "Konfigurasjonen er endret. Hent siste versjon før du validerer eller lagrer.");
    var candidate = FhirConfigurationStore.Compile(configuration);
    return Results.Json(new { valid = true, sources = candidate.Configuration.Sources.Count, profiles = candidate.Profiles.Count });
}

static IResult ConfigurationResult(FhirConfigurationSnapshot snapshot) => Results.Json(new
{
    revision = snapshot.Revision, storage = FhirConfigurationStore.RelativeFile, configuration = snapshot.Configuration
}, FhirConfigurationStore.JsonOptions);

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine($"FHIR preutfylling: {address}"));
await app.RunAsync();

// Bruk FHIR-serialisering for ressurser, slik at value[x], datoer og øvrig R4-struktur bevares.
static string Json(Resource resource) => new FhirJsonSerializer().SerializeToString(resource, pretty: true);
static string LicenseText()
{
    var folder = AppContext.BaseDirectory;
    var files = new[] { Path.Combine(folder, "LICENSE"), Path.Combine(folder, "THIRD-PARTY-NOTICES.md") }
        .Concat(Directory.EnumerateFiles(Path.Combine(folder, "LICENSES"), "*.txt").Order(StringComparer.Ordinal));
    return string.Join("\n\n", files.Select(file => Path.GetFileName(file) + "\n\n" + File.ReadAllText(file)));
}
static IResult Fhir(Resource resource, int status = 200) => Results.Text(Json(resource), "application/fhir+json", statusCode: status);
static OperationOutcome Outcome(string code, string message) => new()
{
    Issue = [new OperationOutcome.IssueComponent
    {
        Severity = OperationOutcome.IssueSeverity.Error, Code = OperationOutcome.IssueType.Processing,
        Details = new CodeableConcept { Text = message }, Diagnostics = code
    }]
};
// Skriv bare et feilsvar hvis forbindelsen fortsatt er åpen og svaret ikke allerede har startet.
static async SysTask Error(HttpContext context, int status, string code, string message)
{
    if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) return;
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/fhir+json";
    await context.Response.WriteAsync(Json(Outcome(code, message)));
}
