using System.Net;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace GenericPopulation;

/// <summary>Regresjoner for kildevalg, kildeavhengig cache og samme generiske transport med ulike profiler.</summary>
internal static class RoutingSelfTests
{
    private static PopulationContext Context() => new(DemoFiles.Patient(), true);
    private static FhirSearch Query(string suffix = "") => FhirSearch.Parse("Observation?patient=demo-patient" + suffix, "demo-patient");
    private static Dictionary<string, IFhirDataSource> Sources() => new()
    { ["a"] = new FixtureDataSource([]), ["b"] = new FixtureDataSource([]), ["c"] = new FixtureDataSource([]) };
    private static FhirSourceOptions Options() => new() { Id = "generic", Name = "Generic", BaseUrl = "https://source.example/fhir/" };
    private static Bundle BundleOf(params Resource[] resources) => new()
    { Type = Bundle.BundleType.Searchset, Entry = resources.Select(r => new Bundle.EntryComponent { Resource = r }).ToList() };

    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("Ruting: uttrykk > kode/profil > ressurstype > standardkilde", () =>
        {
            var profile = new PopulationProfileOptions { DefaultSource = "a", Routes = [
                new() { ResourceType = "Observation", Source = "b" },
                new() { ResourceType = "Observation", Code = "sys|code", Source = "c" },
                new() { ResourceType = "Encounter", Profile = "urn:profile", Source = "c" }],
                QueryBindings = [new() { Questionnaire = "urn:q", Version = "1", Variable = "chosen", Source = "a" }] };
            var router = new RoutingFhirDataSource(profile, Sources());
            Check(router.SelectSource(Query()) == "b", "resource default");
            Check(router.SelectSource(Query("&code=sys|code")) == "c", "code beats resource");
            Check(router.SelectSource(Query("&code=sys|code") with { Origin = new("urn:q", "1", null, "chosen") }) == "a", "expression wins");
            Check(router.SelectSource(Query("&code=sys|code") with { Origin = new("urn:q", "2", null, "chosen") }) == "c", "version is exact");
            Check(router.SelectSource(FhirSearch.Parse("CareTeam?patient=demo-patient", "demo-patient")) == "a", "profile default");
            Check(router.SelectSource(FhirSearch.Parse("Encounter?patient=demo-patient&_profile=urn:profile", "demo-patient")) == "c", "requested profile");
            return SysTask.CompletedTask;
        }),
        ("Ruting: like presise treff avvises før kildekall", async () =>
        {
            var router = new RoutingFhirDataSource(new() { DefaultSource = "a", Routes = [
                new() { ResourceType = "Observation", Code = "s|c", Source = "b" },
                new() { ResourceType = "Observation", Profile = "urn:p", Source = "c" }] }, Sources());
            await Error("source-routing", () => router.SearchAsync(Query("&code=s|c&_profile=urn:p"), Context(), default));
            Check(router.SearchCounts.Count == 0, "no hidden fallback");
            await Error("source-routing", () => new RoutingFhirDataSource(new(), Sources()).SearchAsync(Query(), Context(), default));
        }),
        ("Ruting: like søk til ulike kilder gir forskjellige svar og separat cache", async () =>
        {
            var q = DemoFiles.Questionnaire("general");
            q.Extension.RemoveAll(e => e.Url == Sdc.Variable);
            q.Item.Clear();
            // Samme variabelnavn på tre forskjellige items. Første og tredje bruker samme kilde.
            foreach (var id in new[] { "one", "two", "three" }) q.Item.Add(new()
            {
                LinkId = id, Type = Questionnaire.QuestionnaireItemType.Integer,
                Extension = [new(Sdc.Variable, new Expression { Name = "valueBundle", Language = "application/x-fhir-query", Expression_ = "Observation?patient={{%patient.id}}" }),
                    new(Sdc.Initial, new Expression { Language = "text/fhirpath", Expression_ = "%valueBundle.entry.resource.ofType(Observation).value" })]
            });
            var a = new FixtureDataSource([new Observation { Id = "same", Subject = new("Patient/demo-patient"), Value = new Integer(10) }]);
            var b = new FixtureDataSource([new Observation { Id = "same", Subject = new("Patient/demo-patient"), Value = new Integer(20) }]);
            var router = new RoutingFhirDataSource(new() { DefaultSource = "a", QueryBindings = [new()
                { Questionnaire = q.Url!, Version = q.Version!, LinkId = "two", Variable = "valueBundle", Source = "b" }] },
                new Dictionary<string, IFhirDataSource> { ["a"] = a, ["b"] = b });
            var result = await new PopulationEngine(router).CreateAsync(q, Context());
            Check(result.Response.Item.Select(i => ((Integer)i.Answer.Single().Value!).Value).SequenceEqual(new int?[] { 10, 20, 10 }), "scope and source preserved");
            Check(a.SearchCount == 1 && b.SearchCount == 1, "same-source cache reused, cross-source cache separated");
        }),
        ("Ruting: feil i en påkrevd kilde avbryter uten reservekall", async () =>
        {
            var unused = new FixtureDataSource([]);
            var router = new RoutingFhirDataSource(new() { DefaultSource = "a", Routes = [new() { ResourceType = "Observation", Source = "b" }] },
                new Dictionary<string, IFhirDataSource> { ["a"] = unused, ["b"] = new FailingSource() });
            await Error("source-http", () => new PopulationEngine(router).CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context()));
            Check(unused.SearchCount == 0, "no fallback after error");
        }),
        ("Profiler: ukjente kilder og dupliserte koblinger avvises ved oppstart", () =>
        {
            var source = Options();
            foreach (var profile in new PopulationProfileOptions[] {
                new() { Id = "p", Name = "P", PatientSource = "missing" },
                new() { Id = "p", Name = "P", PatientSource = source.Id, OptionalSources = [source.Id] },
                new() { Id = "p", Name = "P", PatientSource = source.Id, Routes = [
                    new() { ResourceType = "Observation", Source = source.Id }, new() { ResourceType = "Observation", Source = source.Id }] },
                new() { Id = "p", Name = "P", PatientSource = source.Id, QueryBindings = [new() { Source = source.Id, Variable = "v" }] }
            }) Expect("configuration", () => profile.Validate([source]));
            Expect("configuration", () => PopulationProfileOptions.Build([source], [new() { Id = source.Id, Name = "collision", PatientSource = source.Id }]));
            return SysTask.CompletedTask;
        }),
        ("Generisk transport: POST, bearer og GET neste side uten leverandøradapter", async () =>
        {
            var first = BundleOf(); first.Link.Add(new() { Relation = "next", Url = "https://source.example/fhir/Observation?page=2" });
            using var handler = new Handler(first, BundleOf());
            using var http = new HttpClient(handler);
            var options = Options(); options.SearchMethod = "POST";
            var auth = new HeaderAuthorizer();
            var client = new HttpFhirDataSource(http, options, auth);
            await client.SearchAsync(Query("&date=ge2026-01-01&date=le2026-12-31&_sort=date"), Context(), default);
            Check(handler.Requests.Count == 2 && handler.Requests[0].Method == HttpMethod.Post && handler.Requests[1].Method == HttpMethod.Get, "standard paging");
            Check(auth.Count == 2 && handler.Requests.All(r => r.Authorized), "authorization on every method");
            Check(handler.Requests[0].Body.Contains("date=ge2026-01-01&date=le2026-12-31"), "repeated date supported by default");
        }),
        ("Generisk transport: sentral Patient brukes uten lokalt pasientoppslag", async () =>
        {
            var condition = new Condition { Id = "condition", Subject = new("https://central.example/fhir/Patient/demo-patient") };
            using var handler = new Handler(BundleOf(condition));
            using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, Options());
            var context = Context() with { PatientBaseUri = new("https://central.example/fhir/") };
            var result = await source.SearchAsync(FhirSearch.Parse("Condition?patient=demo-patient&clinical-status=active", "demo-patient"), context, default);
            Check(handler.Requests.Count == 1 && handler.Requests[0].Uri.AbsolutePath.EndsWith("/Condition"), "no Patient lookup on secondary source");
            Check(result.Entry[0].FullUrl == "https://source.example/fhir/Condition/condition", "resource origin");
        }),
        ("Generisk transport: konfigurert patient-sti og filter fungerer for annen ressurstype", async () =>
        {
            using var handler = new Handler(BundleOf(new AllergyIntolerance { Patient = new("Patient/demo-patient") }));
            using var http = new HttpClient(handler);
            var options = Options();
            options.Capabilities.Resources["AllergyIntolerance"] = new() { PatientReferencePath = "patient", SearchParameters = ["patient"] };
            await new HttpFhirDataSource(http, options).SearchAsync(FhirSearch.Parse("AllergyIntolerance?patient=demo-patient", "demo-patient"), Context(), default);
            Check(handler.Requests.Count == 1, "configured reference path");
        }),
        ("Generisk transport: API-begrensninger gjelder kun konfigurert kilde", async () =>
        {
            var options = Options(); options.Capabilities.Resources["Observation"] = new() { SearchParameters = ["patient", "date"], MaxOccurrences = new() { ["date"] = 1 } };
            using var handler = new Handler(); using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, options);
            await Error("query-policy", () => source.SearchAsync(Query("&date=ge2026-01-01&date=le2026-12-31"), Context(), default));
            await Error("query-policy", () => source.SearchAsync(Query("&_sort=date"), Context(), default));
            await Error("query-policy", () => source.SearchAsync(FhirSearch.Parse("Condition?patient=demo-patient", "demo-patient"), Context(), default));
            Check(handler.Requests.Count == 0, "unsupported queries rejected before HTTP");
        }),
        ("Generisk transport: sentral identifikator bindes fra kontekst og ikke fra Patient-ID", async () =>
        {
            using var handler = new Handler(BundleOf()); using var http = new HttpClient(handler);
            var options = Options(); options.SearchMethod = "POST"; options.PatientBinding = new() { Parameter = "patient.identifier", ValueFrom = "patientIdentifier", IdentifierSystem = "urn:test" };
            var patient = DemoFiles.Patient(); patient.Identifier.Add(new("urn:test", "0001"));
            var source = new HttpFhirDataSource(http, options);
            await source.SearchAsync(Query(), new(patient, true), default);
            Check(handler.Requests.Single().Body == "patient.identifier=urn%3Atest%7C0001", "system and value preserved");
            patient.Identifier.Add(new("urn:test", "0002"));
            await Error("patient-context", () => source.SearchAsync(Query(), new(patient, true), default));
            Check(handler.Requests.Count == 1, "ambiguous identity not sent");
        }),
        ("Generisk transport: manglende total er tillatt som standard også med POST", async () =>
        {
            var options = Options(); options.SearchMethod = "POST"; options.PatientLookup.Interaction = "search";
            using var handler = new Handler(BundleOf(DemoFiles.Patient()), BundleOf()); using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, options);
            var patient = await source.ReadPatientAsync("arbitrary-identifier", default);
            await source.SearchAsync(Query(), new(patient, true), default);
            Check(handler.Requests.Count == 2, "no provider-specific input or total restriction");
        }),
        ("Valgfri kilde: utilgjengelighet gir tomme felt og tydelig merknad", async () =>
        {
            var router = new RoutingFhirDataSource(new() { DefaultSource = "a", OptionalSources = ["b"], Routes = [
                new() { ResourceType = "Observation", Code = "http://loinc.org|85354-9", Source = "b" }] },
                new Dictionary<string, IFhirDataSource> { ["a"] = new FixtureDataSource(DemoFiles.Resources()),
                    ["b"] = new FailingSource(new PopulationException("source-http", "Sensitive upstream details", 503)) });
            var result = await new PopulationEngine(router).CreateAsync(DemoFiles.Questionnaire("pregnancy"), Context());
            var values = result.Response.Item.SelectMany(i => new[] { i }.Concat(i.Item)).ToDictionary(i => i.LinkId!);
            Check(values["gestationalAge"].Answer.Count == 1 && values["systolic"].Answer.Count == 0, "required values retained, optional empty");
            Check(result.Outcome.Issue.Any(i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Details?.Text?.Contains("'b'") == true) &&
                result.Outcome.Issue.All(i => i.Details?.Text?.Contains("Sensitive") != true), "source named without raw diagnostic");
        }),
        ("Valgfri kilde: dataintegritet, autorisasjon og samlet kansellering avbryter", async () =>
        {
            foreach (var error in new[] { new PopulationException("patient-mismatch", "Mismatch"),
                new PopulationException("source-json", "Invalid"), new PopulationException("source-http", "Denied", 403) })
            {
                var router = new RoutingFhirDataSource(new() { DefaultSource = "b", OptionalSources = ["b"] },
                    new Dictionary<string, IFhirDataSource> { ["b"] = new FailingSource(error) });
                await Error(error.Code, () => router.SearchAsync(Query(), Context(), default));
                Check(router.Issues.Count == 0, "integrity error never downgraded");
            }
            using var cts = new CancellationTokenSource(); cts.Cancel();
            var cancelled = new RoutingFhirDataSource(new() { DefaultSource = "b", OptionalSources = ["b"] },
                new Dictionary<string, IFhirDataSource> { ["b"] = new FailingSource(new OperationCanceledException()) });
            try { await cancelled.SearchAsync(Query(), Context(), cts.Token); }
            catch (OperationCanceledException) { Check(cancelled.Issues.Count == 0, "caller cancellation preserved"); return; }
            throw new InvalidOperationException("Expected operation cancellation");
        })
    ];

    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Expect(string code, Action action)
    { try { action(); } catch (PopulationException e) when (e.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
    private static async SysTask Error(string code, Func<SysTask> action)
    { try { await action(); } catch (PopulationException e) when (e.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
    private sealed class FailingSource(Exception? error = null) : IFhirDataSource
    { public Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct) => throw error ?? new PopulationException("source-http", "Synthetic failure"); }
    private sealed class HeaderAuthorizer : IRequestAuthorizer
    {
        public int Count { get; private set; }
        public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
        { Count++; request.Headers.Authorization = new("Bearer", "synthetic"); return SysTask.CompletedTask; }
    }
    private sealed record Request(HttpMethod Method, Uri Uri, string Body, bool Authorized);
    private sealed class Handler(params Resource[] resources) : HttpMessageHandler
    {
        private readonly Queue<Resource> queue = new(resources);
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization is not null));
            return new(HttpStatusCode.OK) { Content = new StringContent(new FhirJsonSerializer().SerializeToString(queue.Dequeue()), Encoding.UTF8, "application/fhir+json") };
        }
    }
}
