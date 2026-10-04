using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

/// <summary>
/// Lokale regresjonstester for motor, skjema-/søkegrenser og konfigurerbare HTTP-klienter.
/// Bruker syntetiske filer og falske HTTP-svar uten eksterne kall eller eget testbibliotek.
/// Kjør fra prosjektet med dotnet run -- --self-test.
/// </summary>
public static class SelfTests
{
    /// <summary>Kjører navngitte testtilfeller og returnerer prosesskode 0 ved suksess, ellers 1 for CI.</summary>
    public static async Task<int> RunAsync()
    {
        var cases = new List<(string, Func<SysTask>)>
        {
            ("Demografi, canonical-versjon, false og gjentatte svar", async () =>
            {
                var result = await Run("general");
                Check(result.Response.Questionnaire!.EndsWith("|1.0.0"), "canonical version");
                Check(Find(result.Response, "given").Answer.Count == 2, "repeated answers");
                Check(((FhirBoolean)Find(result.Response, "recordActive").Answer.Single().Value!).Value == false, "false is preserved");
                Check(((Date)Find(result.Response, "birthDate").Answer.Single().Value!).Value == "1994-04-23", "FHIR date");
            }),
            ("Nyeste måling, UCUM og uutfylt kommentarfelt", async () =>
            {
                var result = await Run("pregnancy");
                var ga = (Quantity)Find(result.Response, "gestationalAge").Answer.Single().Value!;
                Check(ga.Value == 210 && ga.Code == "d", "gestational age");
                Check(((Quantity)Find(result.Response, "systolic").Answer.Single().Value!).Value == 128, "systolic");
                Check(Find(result.Response, "comment").Answer.Count == 0, "manual field");
            }),
            ("Ingen samtykketilgang gir null kildekall", async () =>
            {
                var source = new FixtureDataSource(DemoFiles.Resources());
                var result = await new PopulationEngine(source).CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context(false));
                Check(source.SearchCount == 0, "no source calls");
                Check(Flatten(result.Response.Item).All(i => i.Answer.Count == 0), "no prepopulation");
            }),
            ("Manglende observasjoner blir ubesvarte felt", async () =>
            {
                var result = await new PopulationEngine(new FixtureDataSource([]))
                    .CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context());
                Check(Find(result.Response, "gestationalAge").Answer.Count == 0, "empty != zero");
            }),
            ("Likeverdige nyeste treff velges ikke vilkårlig", async () =>
            {
                var resources = DemoFiles.Resources();
                var copy = (Observation)resources.Single(r => r.Id == "ga-current").DeepCopy();
                copy.Id = "ga-conflict";
                ((Quantity)copy.Value!).Value = 211;
                resources.Add(copy);
                var result = await new PopulationEngine(new FixtureDataSource(resources))
                    .CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context());
                Check(Find(result.Response, "gestationalAge").Answer.Count == 0, "ambiguous answer omitted");
                Check(result.Outcome.Issue.Any(i => i.Severity == OperationOutcome.IssueSeverity.Warning), "warning");
            }),
            ("Feil datatype gir merknad, ikke automatisk konvertering", async () =>
            {
                var q = DemoFiles.Questionnaire("general");
                q.Item[0].Item.Single(i => i.LinkId == "birthDate").Type = QType.Boolean;
                var result = await new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, Context());
                Check(Find(result.Response, "birthDate").Answer.Count == 0, "no coercion");
            }),
            ("Gjentatte grupper avvises eksplisitt", async () =>
            {
                var q = DemoFiles.Questionnaire("general"); q.Item[0].Repeats = true;
                await Error("unsupported-structure", () => new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, Context()));
            }),
            ("Ukjente extensions avvises eksplisitt", async () =>
            {
                var q = DemoFiles.Questionnaire("general");
                q.Item[0].Extension.Add(new Extension("https://example.org/unsupported", new FhirBoolean(true)));
                await Error("unsupported-extension", () => new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, Context()));
            }),
            ("Absolutt URL i Q avvises", () =>
            {
                SyncError("query-policy", () => FhirSearch.Parse("https://evil.example/Observation?patient=demo-patient", "demo-patient"));
                return SysTask.CompletedTask;
            }),
            ("Et annet patient-filter avvises", () =>
            {
                SyncError("patient-context", () => FhirSearch.Parse("Observation?patient=another", "demo-patient"));
                return SysTask.CompletedTask;
            }),
            ("Feil subject i kildedata stopper preutfyllingen", async () =>
            {
                var resources = DemoFiles.Resources();
                ((Observation)resources[0]).Subject!.Reference = "Patient/another";
                await Error("patient-mismatch", () => new PopulationEngine(new FixtureDataSource(resources))
                    .CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context()));
            }),
            ("Identiske søk deles innen én forespørsel", async () =>
            {
                var q = DemoFiles.Questionnaire("pregnancy");
                var extra = (Extension)q.Extension.First(e => e.Value is Expression { Name: "gaBundle" }).DeepCopy();
                ((Expression)extra.Value!).Name = "gaAgain"; q.Extension.Add(extra);
                var source = new FixtureDataSource(DemoFiles.Resources());
                await new PopulationEngine(source).CreateAsync(q, Context());
                Check(source.SearchCount == 2, "request-scoped cache");
            }),
            ("To datakilder uten endring i motor eller Q", async () =>
            {
                var a = new FixtureDataSource(DemoFiles.Resources());
                var b = new FixtureDataSource(DemoFiles.Resources());
                var router = new RoutingFhirDataSource(new PopulationProfileOptions { Routes = [
                    new() { ResourceType = "Observation", Code = "http://loinc.org|18185-9", Source = "a" },
                    new() { ResourceType = "Observation", Code = "http://loinc.org|85354-9", Source = "b" }] },
                    new Dictionary<string, IFhirDataSource> { ["a"] = a, ["b"] = b });
                await new PopulationEngine(router).CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context());
                Check(a.SearchCount == 1 && b.SearchCount == 1, "independent sources");
            }),
            ("Nettsøk fra FHIRPath er ikke tillatt", () =>
            {
                SyncError("expression-policy", () => ExpressionEvaluator.Check("%patient.generalPractitioner.resolve()"));
                return SysTask.CompletedTask;
            }),
            ("Dupliserte variabelnavn avvises", async () =>
            {
                var q = DemoFiles.Questionnaire("pregnancy");
                q.Extension.Add((Extension)q.Extension.First(e => e.Value is Expression { Name: "gaBundle" }).DeepCopy());
                await Error("variable-name", () => new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, Context()));
            }),
            ("Neste side utenfor endepunktet avvises", async () =>
            {
                var first = new Bundle
                {
                    Type = Bundle.BundleType.Searchset,
                    Link = [new Bundle.LinkComponent { Relation = "next", Url = "https://evil.example/fhir/Observation?page=2" }]
                };
                var handler = new QueueHandler(first);
                using var client = new HttpClient(handler);
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                var query = FhirSearch.Parse("Observation?patient=demo-patient", "demo-patient");
                await Error("destination-policy", () => source.SearchAsync(query, Context(), default));
                Check(handler.Count == 1, "no request to other host");
            }),
            ("HTTP-paginering autoriserer hvert kall", async () =>
            {
                var first = new Bundle
                {
                    Type = Bundle.BundleType.Searchset,
                    Link = [new Bundle.LinkComponent { Relation = "next", Url = "https://source.example/fhir/Observation?page=2" }]
                };
                var second = new Bundle { Type = Bundle.BundleType.Searchset };
                var auth = new CountingAuthorizer();
                using var client = new HttpClient(new QueueHandler(first, second));
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), auth);
                await source.SearchAsync(FhirSearch.Parse("Observation?patient=demo-patient", "demo-patient"), Context(), default);
                Check(auth.Count == 2, "authorization per request");
            }),
            ("Patient hentes via HTTP og autoriseres", async () =>
            {
                var auth = new CountingAuthorizer();
                using var client = new HttpClient(new QueueHandler(DemoFiles.Patient()));
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), auth);
                var patient = await source.ReadPatientAsync("demo-patient", default);
                Check(patient.Id == "demo-patient" && auth.Count == 1 && source.RequestCount == 1, "patient read");
            }),
            ("Feil pasient-ID i HTTP-oppslag avvises", async () =>
            {
                var patient = DemoFiles.Patient(); patient.Id = "another";
                using var client = new HttpClient(new QueueHandler(patient));
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                await Error("patient-mismatch", () => source.ReadPatientAsync("demo-patient", default));
            }),
            ("Ugyldig Patient-ID gir ingen HTTP-kall", async () =>
            {
                var handler = new QueueHandler(DemoFiles.Patient());
                using var client = new HttpClient(handler);
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                await Error("patient-context", () => source.ReadPatientAsync("../Patient/other", default));
                Check(handler.Count == 0, "no call");
            }),
            ("HTTP 200 med OperationOutcome blir en kildefeil", async () =>
            {
                var outcome = new OperationOutcome { Issue = [new OperationOutcome.IssueComponent
                { Severity = OperationOutcome.IssueSeverity.Error, Code = OperationOutcome.IssueType.Forbidden }] };
                using var client = new HttpClient(new QueueHandler(outcome));
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                await Error("source-outcome", () => source.ReadPatientAsync("demo-patient", default));
            }),
            ("Sidehenting utenfor base-path avvises", async () =>
            {
                var page = new Bundle { Type = Bundle.BundleType.Searchset,
                    Link = [new Bundle.LinkComponent { Relation = "next", Url = "https://source.example/other/Observation?page=2" }] };
                var handler = new QueueHandler(page);
                using var client = new HttpClient(handler);
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                await Error("destination-policy", () => source.SearchAsync(FhirSearch.Parse("Observation?patient=demo-patient", "demo-patient"), Context(), default));
                Check(handler.Count == 1, "path restriction");
            }),
            ("Paginering i løkke stoppes", async () =>
            {
                var page = new Bundle { Type = Bundle.BundleType.Searchset,
                    Link = [new Bundle.LinkComponent { Relation = "next", Url = "Observation?patient=demo-patient" }] };
                var handler = new QueueHandler(page);
                using var client = new HttpClient(handler);
                var source = new HttpFhirDataSource(client, new Uri("https://source.example/fhir/"), new CountingAuthorizer());
                await Error("paging-limit", () => source.SearchAsync(FhirSearch.Parse("Observation?patient=demo-patient", "demo-patient"), Context(), default));
                Check(handler.Count == 1, "loop detection");
            }),
            ("Statisk nullverdi 0 bevares, og Q endres ikke", async () =>
            {
                var q = DemoFiles.Questionnaire("general");
                q.Item.Add(new Questionnaire.ItemComponent { LinkId = "zero", Type = QType.Integer,
                    Initial = [new Questionnaire.InitialComponent { Value = new Integer(0) }] });
                var before = new FhirJsonSerializer().SerializeToString(q);
                var result = await new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, Context());
                Check(((Integer)Find(result.Response, "zero").Answer.Single().Value!).Value == 0, "preserve zero");
                Check(before == new FhirJsonSerializer().SerializeToString(q), "immutable input");
            }),
            ("Duplisert launchContext-underfelt avvises kontrollert", () =>
            {
                var q = DemoFiles.Questionnaire("general");
                var launch = q.Extension.Single(e => e.Url == Sdc.Launch);
                launch.Extension.Add((Extension)launch.Extension.Single(e => e.Url == "name").DeepCopy());
                SyncError("launch-context", () => QuestionnaireGuard.Validate(q));
                return SysTask.CompletedTask;
            }),
            ("Lokal variabel kan ikke erstatte patient-konteksten", () =>
            {
                var q = DemoFiles.Questionnaire("general");
                q.Item[0].Extension.Add(new Extension(Sdc.Variable, new Expression
                { Name = "patient", Language = "text/fhirpath", Expression_ = "%patient" }));
                SyncError("variable-name", () => QuestionnaireGuard.Validate(q));
                return SysTask.CompletedTask;
            }),
            ("Gyldig OperationOutcome også ved suksess", async () =>
            {
                var result = await Run("general");
                Check(result.Outcome.Issue.Count > 0, "OperationOutcome.issue 1..*");
            })
        };
        cases.AddRange(FhirSourceSelfTests.Cases());
        cases.AddRange(FhirTransportSelfTests.Cases());
        cases.AddRange(RoutingSelfTests.Cases());
        cases.AddRange(QuestionnaireRoutingSelfTests.Cases());
        cases.AddRange(ConfigurationSelfTests.Cases());
        cases.AddRange(EndpointConnectivitySelfTests.Cases());
        var failed = 0;
        foreach (var (name, test) in cases)
        {
            try { await test(); Console.WriteLine("PASS: " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + " [" + ex.GetType().Name + "]"); }
        }
        Console.WriteLine($"{cases.Count - failed}/{cases.Count} bestått.");
        return failed == 0 ? 0 : 1;
    }

    // Felles hjelpere lager isolerte testkjøringer og finner svar via linkId, uavhengig av gruppenivå.
    private static PopulationContext Context(bool allowed = true) => new(DemoFiles.Patient(), allowed);
    private static Task<PopulationResult> Run(string name) =>
        new PopulationEngine(new FixtureDataSource(DemoFiles.Resources())).CreateAsync(DemoFiles.Questionnaire(name), Context());
    private static IEnumerable<QuestionnaireResponse.ItemComponent> Flatten(IEnumerable<QuestionnaireResponse.ItemComponent> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flatten(i.Item)));
    private static QuestionnaireResponse.ItemComponent Find(QuestionnaireResponse qr, string id) =>
        Flatten(qr.Item).Single(i => i.LinkId == id);
    private static void Check(bool success, string label)
    {
        if (!success) throw new InvalidOperationException(label);
    }
    private static async SysTask Error<T>(string code, Func<Task<T>> action)
    {
        try { await action(); }
        catch (PopulationException e) when (e.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
    private static void SyncError(string code, Action action)
    {
        try { action(); }
        catch (PopulationException e) when (e.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
    // Bekrefter at også neste side i et søk går gjennom autorisasjon.
    private sealed class CountingAuthorizer : IRequestAuthorizer
    {
        public int Count { get; private set; }
        public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
        { Count++; return SysTask.CompletedTask; }
    }
    // Erstatter nettverket med en kø av FHIR-sider, men lar den virkelige GET-klienten behandle svarene.
    private sealed class QueueHandler(params Resource[] pages) : HttpMessageHandler
    {
        private readonly Queue<Resource> queue = new(pages);
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++;
            return SysTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new FhirJsonSerializer().SerializeToString(queue.Dequeue()),
                    System.Text.Encoding.UTF8, "application/fhir+json")
            });
        }
    }
}
