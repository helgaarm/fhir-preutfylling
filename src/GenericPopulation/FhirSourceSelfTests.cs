using System.Net;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace GenericPopulation;

/// <summary>
/// Tester generiske konfigurasjonsvalg, pasientisolasjon og feilgrenser med syntetiske HTTP-svar.
/// Ingen leverandørkonfigurasjon, eksempelskjemaer eller eksterne tjenester brukes.
/// </summary>
internal static class FhirSourceSelfTests
{
    private const string TestIdentifier = "TEST-001";
    // Minimalt testoppsett. Hvert testtilfelle setter bare begrensningene det skal verifisere.
    private static FhirSourceOptions Options() => new()
    {
        Id = "test-source", Name = "Syntetisk kilde", BaseUrl = "https://source.example/fhir/",
        SearchMethod = "POST", PatientLookup = new() { Interaction = "search" },
        PatientBinding = new() { Parameter = "subject.identifier", ValueFrom = "inputIdentifier" }
    };
    private static Patient Patient() => new() { Id = "synthetic-patient", Active = true };
    private static Encounter Encounter(string patientId = "synthetic-patient") => new()
    {
        Id = "synthetic-encounter", Status = Hl7.Fhir.Model.Encounter.EncounterStatus.Finished,
        Class = new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "AMB"),
        Subject = new("Patient/" + patientId)
    };
    private static Bundle BundleOf(params Resource[] resources) => new()
    {
        Type = Bundle.BundleType.Searchset, Total = resources.Length,
        Entry = resources.Select(r => new Bundle.EntryComponent { Resource = r }).ToList()
    };
    private static HttpResponseMessage Reply(Resource resource) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new FhirJsonSerializer().SerializeToString(resource), Encoding.UTF8, "application/fhir+json")
    };
    private static FhirSearch Search(Patient patient, string suffix = "", string resource = "Observation") =>
        FhirSearch.Parse(resource + "?patient=" + patient.Id + suffix, patient.Id!);

    /// <summary>Testtilfeller for konfigurert transport og avvisning av utrygge eller ufullstendige svar.</summary>
    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("FHIR-kilde: GET og POST bruker konfigurerte pasientparametere", async () =>
        {
            foreach (var method in new[] { "GET", "POST" })
            {
                var options = Options(); options.SearchMethod = method;
                options.PatientLookup.Parameter = "custom-identifier";
                using var handler = new Handler(Reply(BundleOf(Patient())), Reply(BundleOf()));
                using var http = new HttpClient(handler);
                var source = new HttpFhirDataSource(http, options);
                var patient = await source.ReadPatientAsync(TestIdentifier, default);
                await source.SearchAsync(Search(patient), new(patient, true), default);
                Check(handler.Requests.Count == 2, "one patient lookup and one clinical search");
                foreach (var (request, index) in handler.Requests.Select((r, i) => (r, i)))
                {
                    var type = index == 0 ? "Patient" : "Observation";
                    Check(request.Method.Method == method && request.Uri.AbsolutePath ==
                        $"/fhir/{type}" + (method == "POST" ? "/_search" : ""), "configured transport");
                    Check(request.Accept == "application/fhir+json" && !request.HasCredentials, "anonymous source");
                    Check(method == "POST" ? request.Uri.Query.Length == 0 && request.ContentType == "application/x-www-form-urlencoded"
                        : request.Body.Length == 0 && request.ContentType is null, "method-specific encoding");
                    var parameters = QueryHelpers.ParseQuery(method == "POST" ? request.Body : request.Uri.Query);
                    Check(parameters.Count == 1 && parameters[index == 0 ? "custom-identifier" : "subject.identifier"] == TestIdentifier,
                        "configured patient lookup and binding");
                }
            }
        }),
        ("FHIR-kilde: form-enkoding bevarer tokenfiltre og ledende nuller", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(Patient())), Reply(BundleOf()));
            using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync("00000000001", default);
            var code = "urn:example:code|a+b&c";
            await source.SearchAsync(Search(patient, "&code=" + Uri.EscapeDataString(code) + "&category=vital-signs&date=ge2026-09-01"), new(patient, true), default);
            var form = QueryHelpers.ParseQuery(handler.Requests[1].Body);
            Check(form["subject.identifier"] == "00000000001" && form["code"] == code && form["date"] == "ge2026-09-01", "form roundtrip");
            Check(!form.ContainsKey("patient"), "logical ID not sent as search identity");
        }),
        ("FHIR-kilde: konfigurert mønster og tillatte identifikatorer håndheves før nettverkskall", async () =>
        {
            using var handler = new Handler();
            using var http = new HttpClient(handler);
            var options = Options();
            options.PatientIdentifierPattern = @"\ATEST-[0-9]{3}\z";
            options.AllowedTestPatientIdentifiers = [TestIdentifier];
            var source = new HttpFhirDataSource(http, options);
            foreach (var id in new[] { "", "not-an-identifier", "TEST-002", "TEST-001\n", "TEST-００１", "urn:test|TEST-001" })
                await Error("patient-context", () => source.ReadPatientAsync(id, default));
            Check(handler.Requests.Count == 0, "no requests");
        }),
        ("FHIR-kilde: manglende eller tvetydig Patient gir ikke QR", async () =>
        {
            using var handler = new Handler(Reply(BundleOf()), Reply(BundleOf(Patient(), Patient())));
            using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, Options());
            await Error("source-patient-not-found", () => source.ReadPatientAsync(TestIdentifier, default));
            await Error("source-contract", () => source.ReadPatientAsync(TestIdentifier, default));
        }),
        ("FHIR-kilde: gyldig ressurs-ID og valgfritt krav om separat identifikator", async () =>
        {
            foreach (var id in new string?[] { null, "../other", TestIdentifier })
            {
                var patient = Patient(); patient.Id = id;
                using var http = new HttpClient(new Handler(Reply(BundleOf(patient))));
                var options = Options(); options.PatientLookup.RequireDistinctResourceId = true;
                await Error(id == "../other" ? "source-json" : "source-contract", () => new HttpFhirDataSource(http, options).ReadPatientAsync(TestIdentifier, default));
            }
            using var ordinary = new HttpClient(new Handler(Reply(BundleOf(new Patient { Id = TestIdentifier }))));
            Check((await new HttpFhirDataSource(ordinary, Options()).ReadPatientAsync(TestIdentifier, default)).Id == TestIdentifier,
                "equal ID is allowed when the configured restriction is off");
        }),
        ("FHIR-kilde: feil pasient stoppes for alle støttede ressurser", async () =>
        {
            Resource[] wrong = [
                new Observation { Id = "observation", Status = ObservationStatus.Final,
                    Code = new CodeableConcept("urn:test", "measurement"), Subject = new("Patient/other") },
                Encounter("other"),
                new CareTeam { Id = "team", Status = CareTeam.CareTeamStatus.Active, Subject = new("Patient/other") }
            ];
            foreach (var resource in wrong)
            {
                using var http = new HttpClient(new Handler(Reply(BundleOf(Patient())), Reply(BundleOf(resource))));
                var source = new HttpFhirDataSource(http, Options());
                var patient = await source.ReadPatientAsync(TestIdentifier, default);
                await Error("patient-mismatch", () => source.SearchAsync(Search(patient, resource: resource.TypeName), new(patient, true), default));
            }
        }),
        ("FHIR-kilde: ukjent ressurstype i søkesvar avvises", async () =>
        {
            using var http = new HttpClient(new Handler(Reply(BundleOf(Patient())), Reply(BundleOf(Encounter()))));
            var source = new HttpFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            await Error("source-contract", () => source.SearchAsync(Search(patient), new(patient, true), default));
        }),
        ("FHIR-kilde: ingen gjenbruk av kontekst etter mislykket pasientbytte", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(Patient())), Reply(BundleOf()));
            using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            await Error("source-patient-not-found", () => source.ReadPatientAsync("TEST-002", default));
            await Error("patient-context", () => source.SearchAsync(Search(patient), new(patient, true), default));
            Check(handler.Requests.Count == 2, "no stale search");
        }),
        ("FHIR-kilde: manglende eller avvist kontekst gir ingen søk", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(Patient())));
            using var http = new HttpClient(handler);
            var source = new HttpFhirDataSource(http, Options());
            var patient = Patient();
            await Error("patient-context", () => source.SearchAsync(Search(patient), new(patient, true), default));
            await source.ReadPatientAsync(TestIdentifier, default);
            await Error("authorization", () => source.SearchAsync(Search(patient), new(patient, false), default));
            var other = new Patient { Id = "other" };
            await Error("patient-context", () => source.SearchAsync(Search(other), new(other, true), default));
            Check(handler.Requests.Count == 1, "only patient lookup");
        }),
        ("FHIR-kilde: konfigurerte ressurs-, dato- og tokenbegrensninger håndheves", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(Patient())));
            using var http = new HttpClient(handler);
            var options = Options();
            options.Capabilities.Resources["Observation"] = new()
            {
                SearchParameters = ["subject.identifier", "code", "category", "date"],
                MaxOccurrences = new() { ["date"] = 1 },
                TokenParameters = ["code"], DateParameters = ["date"],
                TokenSystems = new() { ["category"] = "urn:test:category" }
            };
            var source = new HttpFhirDataSource(http, options);
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            foreach (var suffix in new[] { "&date=ge2026-01-01&date=le2026-12-31", "&date=2026-02-30", "&date=sa2026-09-01", "&code=measurement", "&code=a|b|c", "&category=urn:wrong|survey", "&unsupported=value" })
                await Error("query-policy", () => source.SearchAsync(Search(patient, suffix), new(patient, true), default));
            await Error("query-policy", () => source.SearchAsync(Search(patient, resource: "Procedure"), new(patient, true), default));
            // Forged objects must not bypass the query parser or become arbitrary routes.
            await Error("query-policy", () => source.SearchAsync(new("../Patient", [new("patient", patient.Id!)]), new(patient, true), default));
            Check(handler.Requests.Count == 1, "no invalid search");
        }),
        ("FHIR-kilde: konfigurert størrelsesgrense gjelder etter form-enkoding", async () =>
        {
            foreach (var limit in new[] { 128, 512 })
            {
                using var handler = new Handler(Reply(BundleOf(Patient())));
                using var http = new HttpClient(handler);
                var options = Options(); options.Capabilities.MaxFormBytes = limit;
                var source = new HttpFhirDataSource(http, options);
                var patient = await source.ReadPatientAsync(TestIdentifier, default);
                var value = new string('æ', limit / 3);
                await Error("query-policy", () => source.SearchAsync(Search(patient, "&category=" + Uri.EscapeDataString(value)), new(patient, true), default));
                Check(handler.Requests.Count == 1, "oversize encoded form not sent");
            }
        }),
        ("FHIR-kilde: feilstatus eksponerer ikke kildens rå feilmelding", async () =>
        {
            foreach (var status in new[] { 400, 401, 403, 404, 429, 503, 302 })
            {
                using var http = new HttpClient(new Handler(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive-upstream-diagnostic") }));
                var error = await Error("source-http", () => new HttpFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
                Check(error.Message.Contains(status.ToString()) && !error.Message.Contains("sensitive"), "safe status only");
            }
        }),
        ("FHIR-kilde: OperationOutcome og ugyldige HTTP 200-svar avvises", async () =>
        {
            var outcome = new OperationOutcome { Issue = [new() { Severity = OperationOutcome.IssueSeverity.Error, Code = OperationOutcome.IssueType.Processing }] };
            (Resource, string)[] cases = [(outcome, "source-outcome"), (BundleOf(outcome), "source-outcome"),
                (Patient(), "source-contract"), (new Bundle { Type = Bundle.BundleType.Collection }, "source-contract")];
            foreach (var (resource, code) in cases)
            {
                using var http = new HttpClient(new Handler(Reply(resource)));
                await Error(code, () => new HttpFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
            }
        }),
        ("FHIR-kilde: konfigurert Paging none avviser neste side og ufullstendige svar", async () =>
        {
            var page = BundleOf(Patient());
            page.Link.Add(new() { Relation = "next", Url = "https://untrusted.example/Patient" });
            var partial = BundleOf(Patient()); partial.Total = 2;
            using var handler = new Handler(Reply(page), Reply(partial));
            using var http = new HttpClient(handler);
            var options = Options(); options.Capabilities.Paging = "none"; options.Capabilities.RequireTotal = true;
            var source = new HttpFhirDataSource(http, options);
            await Error("source-paging", () => source.ReadPatientAsync(TestIdentifier, default));
            await Error("source-contract", () => source.ReadPatientAsync(TestIdentifier, default));
            Check(handler.Requests.Count == 2, "no paging calls");
        }),
        ("FHIR-kilde: HTML, ugyldig JSON og for store svar avvises", async () =>
        {
            (string, string, string)[] cases = [("<html>upstream</html>", "text/html", "source-content"),
                ("{broken", "application/fhir+json", "source-json"),
                (new string('x', 2 * 1024 * 1024 + 1), "application/fhir+json", "response-size")];
            foreach (var (body, contentType, code) in cases)
            {
                using var http = new HttpClient(new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) }));
                await Error(code, () => new HttpFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
            }
        }),
        ("FHIR-kilde: kansellering stopper før nettverkskall", async () =>
        {
            using var handler = new Handler();
            using var http = new HttpClient(handler);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            try { await new HttpFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, cts.Token); }
            catch (OperationCanceledException) { Check(handler.Requests.Count == 0, "no requests"); return; }
            throw new InvalidOperationException("expected cancellation");
        }),
        ("FHIR-kilde: ugyldig transport og kapabilitetskonfigurasjon avvises", () =>
        {
            var invalid = new[] { Options(), Options(), Options() };
            invalid[0].SearchMethod = "unsupported";
            invalid[1].Capabilities.Paging = "unsupported";
            invalid[2].PatientBinding.ValueFrom = "unsupported";
            foreach (var options in invalid)
            {
                try { options.Validate(); }
                catch (PopulationException e) when (e.Code == "configuration") { continue; }
                throw new InvalidOperationException("invalid configuration accepted");
            }
            return SysTask.CompletedTask;
        })
    ];

    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static async Task<PopulationException> Error(string code, Func<SysTask> action)
    {
        try { await action(); }
        catch (PopulationException e) when (e.Code == code) { return e; }
        throw new InvalidOperationException("Expected " + code);
    }
    // Registrer transporten. Handler returnerer planlagte svar i stedet for å bruke nettverk.
    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? ContentType, string Accept, bool HasCredentials);
    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<RecordedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Content?.Headers.ContentType?.MediaType, string.Join(",", request.Headers.Accept),
                request.Headers.Authorization is not null || request.Headers.Contains("DPoP") || request.Headers.Contains("X-Patient-Context")));
            return queue.Dequeue();
        }
    }
}
