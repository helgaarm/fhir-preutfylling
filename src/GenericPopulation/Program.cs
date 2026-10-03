using System.Text.Json;
using GenericPopulation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

// Startpunkt og HTTP-vert for den lokale utviklerappen.
// Leseflyt: input -> QuestionnaireGuard -> kildeadapter/Patient -> PopulationEngine -> ny QR.
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
var sources = builder.Configuration.GetSection("Fhir:Sources").Get<List<FhirSourceOptions>>() ?? [];
if (sources.Count == 0 || sources.Select(s => s.Id).Distinct().Count() != sources.Count)
    throw new InvalidOperationException("Konfigurer Fhir:Sources med unike ID-er i appsettings.json.");
foreach (var source in sources) source.Validate();
var app = builder.Build();
// Del forbindelser, men ikke pasientkontekst eller søkecache. Ingen automatiske omdirigeringer
// eller cookies: kildesvar skal ikke kunne flytte et kall med tilgangs- eller pasientinformasjon.
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(20) };

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
        var status = e.Code == "source-id" ? 400 :
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
app.MapGet("/api/config", () => Results.Json(new
{
    sources = sources.Select(s => new
    {
        s.Id, s.Name, s.BaseUrl,
        patientInput = s.IsDhg ? "identifier" : "id",
        testPatientIdentifiers = s.IsDhg ? s.AllowedTestPatientIdentifiers : [],
        defaultExample = s.IsDhg ? "dhg" : "pregnancy"
    }),
    defaultPatientId = "demo-patient", fhirVersion = "4.0.1"
}));
app.MapGet("/api/examples/{scenario}", (string scenario) => Fhir(DemoFiles.Questionnaire(scenario)));
app.MapGet("/health", () => Results.Json(new { status = "ok" }));
// Offentlige lisensfiler fra appens distribusjon, uten pasientdata eller brukerbestemte filstier.
// Tekstformat bevarer den originale juridiske ordlyden uten å tolke den som HTML.
app.MapGet("/licenses", () => Results.Text(LicenseText(), "text/plain; charset=utf-8"));

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
// Samme behandling med to svarformater: Parameters med QR + merknader, eller bare QR.
app.MapPost("/api/populate", (Func<HttpContext, Task<IResult>>)(context => Populate(context, false)));
app.MapPost("/api/questionnaire-response", (Func<HttpContext, Task<IResult>>)(context => Populate(context, true)));

async Task<IResult> Populate(HttpContext httpContext, bool responseOnly)
{
    // Én frist for hele innhentingen, koblet til at nettleseren kan avbryte forespørselen.
    if (!httpContext.Request.HasJsonContentType())
        return Fhir(Outcome("content-type", "Send JSON med Content-Type: application/json."), 415);
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    var ct = deadline.Token;
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("questionnaire", out var qJson) || qJson.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("sourceId", out var sourceId) || sourceId.ValueKind != JsonValueKind.String)
        return Fhir(Outcome("input", "Input må ha questionnaire (Q-objekt), sourceId og pasientvalg for kilden."), 400);
    // Kilden velges fra serverens register. Avvis ukjente/dupliserte felt og blanding av
    // logisk Patient-ID og DHG-identifikator, slik at pasientvalget er entydig.
    var sourceOptions = sources.SingleOrDefault(s => s.Id == sourceId.GetString())
        ?? throw new PopulationException("source-id", "Velg en kilde som er konfigurert i appsettings.json.");
    var patientField = sourceOptions.IsDhg ? "patientIdentifier" : "patientId";
    if (!root.TryGetProperty(patientField, out var patientValue) || patientValue.ValueKind != JsonValueKind.String ||
        root.EnumerateObject().Any(p => p.Name is not ("questionnaire" or "sourceId") && p.Name != patientField) ||
        root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() != 1))
        return Fhir(Outcome("input", "Send bare questionnaire, sourceId og " + patientField + " som tekst. Dupliserte felt er ikke tillatt."), 400);
    var patientKey = patientValue.GetString()!;
    sourceOptions.ValidatePatientKey(patientKey);
    Questionnaire q;
    try { q = new FhirJsonDeserializer().Deserialize<Questionnaire>(qJson.GetRawText()); }
    catch (Exception) { throw new PopulationException("questionnaire-json", "Input må være et gyldig FHIR R4 Questionnaire."); }
    // Valider skjemaet før nettverkskall. Motoren validerer også for andre kallere, som CLI og tester.
    QuestionnaireGuard.Validate(q);
    // Adapteren lever bare i denne forespørselen; DHG lagrer NIN -> pseudonym Patient-ID her.
    IPatientFhirDataSource source = sourceOptions.IsDhg
        ? new DhgFhirDataSource(http, sourceOptions)
        : new HttpFhirDataSource(http, sourceOptions.Validate(), new ConfiguredAuthorizer(sourceOptions));
    // Utviklermodus tillater testpasienter. En klinisk vert må autentisere brukeren og avgjøre
    // tilgang før innhenting og før den oppretter PopulationContext med PrepopulationAllowed=true.
    var patient = await source.ReadPatientAsync(patientKey, ct);
    var result = await new PopulationEngine(source).CreateAsync(q, new PopulationContext(patient, true), ct);
    httpContext.Response.Headers["X-Fhir-Requests"] = source.RequestCount.ToString();
    if (responseOnly) return Fhir(result.Response);
    return Fhir(new Parameters
    {
        Parameter = [
            new Parameters.ParameterComponent { Name = "response", Resource = result.Response },
            new Parameters.ParameterComponent { Name = "issues", Resource = result.Outcome }
        ]
    });
}

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
