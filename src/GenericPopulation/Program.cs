using System.Text.Json;
using System.Text.RegularExpressions;
using GenericPopulation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

if (args.Contains("--self-test"))
{
    Environment.ExitCode = await SelfTests.RunAsync();
    return;
}
if (args.Contains("--demo"))
{
    var name = args.Contains("general") ? "general" : "pregnancy";
    var result = await new PopulationEngine(new FixtureDataSource(DemoFiles.Resources()))
        .CreateAsync(DemoFiles.Questionnaire(name), new PopulationContext(DemoFiles.Patient(), !args.Contains("--no-consent")));
    Directory.CreateDirectory("output");
    await File.WriteAllTextAsync("output/questionnaire-response.json", Json(result.Response));
    await File.WriteAllTextAsync("output/population-outcome.json", Json(result.Outcome));
    Console.WriteLine("Syntetisk demo: QR og merknader er skrevet til output/.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Demo:Port", 5077);
// This is a local developer workbench, not a public clinical service.
var address = $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(address);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
builder.Logging.ClearProviders(); // Do not log clinical payloads, query strings or tokens.
var sources = builder.Configuration.GetSection("Fhir:Sources").Get<List<FhirSourceOptions>>() ?? [];
if (sources.Count == 0 || sources.Select(s => s.Id).Distinct().Count() != sources.Count)
    throw new InvalidOperationException("Konfigurer Fhir:Sources med unike ID-er i appsettings.json.");
foreach (var source in sources) source.Validate();
var app = builder.Build();
// Reuse connections; expression scope and search cache remain request-local.
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(20) };

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
app.MapGet("/api/config", () => Results.Json(new
{
    sources = sources.Select(s => new { s.Id, s.Name, s.BaseUrl }),
    defaultPatientId = "demo-patient", fhirVersion = "4.0.1"
}));
app.MapGet("/api/examples/{scenario}", (string scenario) => Fhir(DemoFiles.Questionnaire(scenario)));
app.MapGet("/health", () => Results.Json(new { status = "ok" }));

// Called through HTTP by the same client that accesses external FHIR sources.
app.MapGet("/demo/fhir/Patient/{id}", (string id) => id == "demo-patient"
    ? Fhir(DemoFiles.Patient())
    : Fhir(Outcome("not-found", "Fant ikke syntetisk pasient. Bruk demo-patient."), 404));
app.MapGet("/demo/fhir/{resourceType}", async (string resourceType, HttpRequest request, CancellationToken ct) =>
{
    var context = new PopulationContext(DemoFiles.Patient(), true);
    var search = FhirSearch.Parse(resourceType + request.QueryString.Value, context.Patient.Id!);
    return Fhir(await new FixtureDataSource(DemoFiles.Resources()).SearchAsync(search, context, ct));
});
app.MapPost("/api/populate", (Func<HttpContext, Task<IResult>>)(context => Populate(context, false)));
app.MapPost("/api/questionnaire-response", (Func<HttpContext, Task<IResult>>)(context => Populate(context, true)));

async Task<IResult> Populate(HttpContext httpContext, bool responseOnly)
{
    if (!httpContext.Request.HasJsonContentType())
        return Fhir(Outcome("content-type", "Send JSON med Content-Type: application/json."), 415);
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    var ct = deadline.Token;
    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("questionnaire", out var qJson) || qJson.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("sourceId", out var sourceId) || sourceId.ValueKind != JsonValueKind.String ||
        !root.TryGetProperty("patientId", out var patientId) || patientId.ValueKind != JsonValueKind.String)
        return Fhir(Outcome("input", "Input må ha questionnaire (Q-objekt), sourceId og patientId."), 400);
    if (root.EnumerateObject().Any(p => p.Name is not ("questionnaire" or "sourceId" or "patientId")))
        return Fhir(Outcome("input", "Ukjent inputfelt. En eksisterende QR, tilgangsflagg eller kilde-URL kan ikke sendes inn."), 400);
    var sourceOptions = sources.SingleOrDefault(s => s.Id == sourceId.GetString())
        ?? throw new PopulationException("source-id", "Velg en kilde som er konfigurert i appsettings.json.");
    var id = patientId.GetString()!;
    if (!Regex.IsMatch(id, @"\A[A-Za-z0-9\-.]{1,64}\z"))
        throw new PopulationException("patient-context", "Ugyldig logisk FHIR Patient-ID.");
    Questionnaire q;
    try { q = new FhirJsonDeserializer().Deserialize<Questionnaire>(qJson.GetRawText()); }
    catch (Exception) { throw new PopulationException("questionnaire-json", "Input må være et gyldig FHIR R4 Questionnaire."); }
    QuestionnaireGuard.Validate(q);
    var source = new HttpFhirDataSource(http, sourceOptions.Validate(), new ConfiguredAuthorizer(sourceOptions));
    // Local developer mode authorizes test patients. Replace this boundary with actual
    // authentication and authorization before reusing the engine in a clinical service.
    var patient = await source.ReadPatientAsync(id, ct);
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

static string Json(Resource resource) => new FhirJsonSerializer().SerializeToString(resource, pretty: true);
static IResult Fhir(Resource resource, int status = 200) => Results.Text(Json(resource), "application/fhir+json", statusCode: status);
static OperationOutcome Outcome(string code, string message) => new()
{
    Issue = [new OperationOutcome.IssueComponent
    {
        Severity = OperationOutcome.IssueSeverity.Error, Code = OperationOutcome.IssueType.Processing,
        Details = new CodeableConcept { Text = message }, Diagnostics = code
    }]
};
static async SysTask Error(HttpContext context, int status, string code, string message)
{
    if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) return;
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/fhir+json";
    await context.Response.WriteAsync(Json(Outcome(code, message)));
}
