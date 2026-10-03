using System.Net;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace GenericPopulation;

/// <summary>
/// Tester DHG-adapterens POST-kontrakt, pasientisolasjon og feilgrenser med syntetiske HTTP-svar.
/// Kjøres som del av SelfTests; ingen forespørsler når det eksterne DHG-endepunktet.
/// </summary>
internal static class DhgSelfTests
{
    private const string TestIdentifier = "29760484634";
    private static FhirSourceOptions Options() => new()
    {
        Id = "dhg-test", Mode = "dhg-post", BaseUrl = "https://dhg.example/fhir/",
        AllowedTestPatientIdentifiers = [TestIdentifier, "11859699482", "00000000001"]
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

    /// <summary>Testtilfeller for både full Q-til-QR-flyt og avvisning av utrygge eller ufullstendige svar.</summary>
    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("DHG: full Q-flyt med fire POST-kall og pseudonym QR", async () =>
        {
            var resources = DemoFiles.DhgResources();
            var q = DemoFiles.Questionnaire("dhg");
            // Like spørringer i samme kjøring skal treffe motorens cache, ikke lese DHG på nytt.
            q.Extension.Add(new Extension(Sdc.Variable, new Expression
            { Name = "sameObservations", Language = "application/x-fhir-query", Expression_ = "Observation?patient={{%patient.id}}" }));
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())),
                Reply(BundleOf(resources.OfType<Observation>().ToArray())),
                Reply(BundleOf(resources.OfType<Encounter>().ToArray())),
                Reply(BundleOf(resources.OfType<CareTeam>().ToArray())));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            var result = await new PopulationEngine(source).CreateAsync(q, new(patient, true));
            Check(source.RequestCount == 4 && handler.Requests.Count == 4, "four requests including cache reuse");
            var items = Flatten(result.Response.Item).ToDictionary(i => i.LinkId!);
            Check(((FhirString)items["nameText"].Answer.Single().Value!).Value == "Syntetisk DHG-eksempel", "name.text");
            Check(((FhirBoolean)items["interpreterRequired"].Answer.Single().Value!).Value == false, "preserve false");
            Check(items["birthDate"].Answer.Count == 0, "no inferred birth date");
            Check(((Quantity)items["gestationalAge"].Answer.Single().Value!).Value == 210, "unknown status remains usable");
            Check(((Quantity)items["systolic"].Answer.Single().Value!).Value == 128, "blood pressure component");
            Check(items["consultationDates"].Answer.Count == 1 && items["careTeamContacts"].Answer.Count == 1, "Encounter and contained contacts");
            Check(result.Response.Subject?.Reference == "Patient/" + patient.Id, "pseudonym subject");
            Check(!new FhirJsonSerializer().SerializeToString(result.Response).Contains(TestIdentifier), "no NIN in QR");
            foreach (var request in handler.Requests)
            {
                Check(request.Method == HttpMethod.Post && request.Uri.Query.Length == 0, "POST without query");
                Check(request.ContentType == "application/x-www-form-urlencoded", "form content");
                Check(request.Accept == "application/fhir+json" && !request.HasCredentials, "anonymous contract");
                var form = QueryHelpers.ParseQuery(request.Body);
                var expectedKey = request.Uri.AbsolutePath == "/fhir/Patient/_search" ? "identifier" : "patient.identifier";
                Check(form.Count == 1 && form[expectedKey] == TestIdentifier, "correct patient selection field");
            }
        }),
        ("DHG: form-enkoding bevarer tokenfiltre og ledende nuller", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())), Reply(BundleOf()));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync("00000000001", default);
            var code = "urn:example:code|a+b&c";
            await source.SearchAsync(Search(patient, "&code=" + Uri.EscapeDataString(code) + "&category=vital-signs&date=ge2026-09-01"), new(patient, true), default);
            var form = QueryHelpers.ParseQuery(handler.Requests[1].Body);
            Check(form["patient.identifier"] == "00000000001" && form["code"] == code && form["date"] == "ge2026-09-01", "form roundtrip");
            Check(!form.ContainsKey("patient"), "logical ID not sent as search identity");
        }),
        ("DHG: ugyldig eller ikke godkjent NIN stoppes før nettverkskall", async () =>
        {
            using var handler = new Handler();
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            foreach (var id in new[] { "", "demo-patient", "12345678901", "29760484634\n", "２９７６０４８４６３４", "urn:nin|29760484634" })
                await Error("patient-context", () => source.ReadPatientAsync(id, default));
            Check(handler.Requests.Count == 0, "no requests");
        }),
        ("DHG: manglende eller tvetydig Patient gir ikke QR", async () =>
        {
            using var handler = new Handler(Reply(BundleOf()), Reply(BundleOf(DemoFiles.DhgPatient(), DemoFiles.DhgPatient())));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            await Error("source-patient-not-found", () => source.ReadPatientAsync(TestIdentifier, default));
            await Error("source-contract", () => source.ReadPatientAsync(TestIdentifier, default));
        }),
        ("DHG: Patient må ha gyldig pseudonym ID", async () =>
        {
            foreach (var id in new string?[] { null, "../other", TestIdentifier })
            {
                var patient = DemoFiles.DhgPatient(); patient.Id = id;
                using var http = new HttpClient(new Handler(Reply(BundleOf(patient))));
                await Error(id == "../other" ? "source-json" : "source-contract", () => new DhgFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
            }
        }),
        ("DHG: feil pasient stoppes for alle støttede ressurser", async () =>
        {
            var fixtures = DemoFiles.DhgResources();
            var observation = fixtures.OfType<Observation>().First(); observation.Subject = new("Patient/other");
            var encounter = fixtures.OfType<Encounter>().First(); encounter.Subject = new("Patient/other");
            var careTeam = fixtures.OfType<CareTeam>().First(); careTeam.Subject = new("Patient/other");
            Resource[] wrong = [observation, encounter, careTeam];
            foreach (var resource in wrong)
            {
                using var http = new HttpClient(new Handler(Reply(BundleOf(DemoFiles.DhgPatient())), Reply(BundleOf(resource))));
                var source = new DhgFhirDataSource(http, Options());
                var patient = await source.ReadPatientAsync(TestIdentifier, default);
                await Error("patient-mismatch", () => source.SearchAsync(Search(patient, resource: resource.TypeName), new(patient, true), default));
            }
        }),
        ("DHG: ukjent ressurstype i søkesvar avvises", async () =>
        {
            using var http = new HttpClient(new Handler(Reply(BundleOf(DemoFiles.DhgPatient())), Reply(BundleOf(DemoFiles.DhgResources().OfType<Encounter>().First()))));
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            await Error("source-contract", () => source.SearchAsync(Search(patient), new(patient, true), default));
        }),
        ("DHG: ingen gjenbruk av kontekst etter mislykket pasientbytte", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())), Reply(BundleOf()));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            await Error("source-patient-not-found", () => source.ReadPatientAsync("11859699482", default));
            await Error("patient-context", () => source.SearchAsync(Search(patient), new(patient, true), default));
            Check(handler.Requests.Count == 2, "no stale search");
        }),
        ("DHG: manglende eller avvist kontekst gir ingen søk", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = DemoFiles.DhgPatient();
            await Error("patient-context", () => source.SearchAsync(Search(patient), new(patient, true), default));
            await source.ReadPatientAsync(TestIdentifier, default);
            await Error("authorization", () => source.SearchAsync(Search(patient), new(patient, false), default));
            var other = new Patient { Id = "other" };
            await Error("patient-context", () => source.SearchAsync(Search(other), new(other, true), default));
            Check(handler.Requests.Count == 1, "only patient lookup");
        }),
        ("DHG: ugyldige og gjentatte filtre avvises før POST", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            foreach (var suffix in new[] { "&date=ge2026-01-01&date=le2026-12-31", "&date=2026-02-30", "&date=sa2026-09-01", "&code=85354-9", "&code=a|b|c", "&category=https://wrong.example|survey" })
                await Error("query-policy", () => source.SearchAsync(Search(patient, suffix), new(patient, true), default));
            // Forged objects must not bypass the query parser or become arbitrary routes.
            await Error("query-policy", () => source.SearchAsync(new("../Patient", [new("patient", patient.Id!)]), new(patient, true), default));
            Check(handler.Requests.Count == 1, "no invalid search");
        }),
        ("DHG: 4096-byte-grense kontrolleres etter form-enkoding", async () =>
        {
            using var handler = new Handler(Reply(BundleOf(DemoFiles.DhgPatient())));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            var patient = await source.ReadPatientAsync(TestIdentifier, default);
            await Error("query-policy", () => source.SearchAsync(Search(patient, "&category=" + new string('x', 4060)), new(patient, true), default));
            Check(handler.Requests.Count == 1, "oversize form not sent");
        }),
        ("DHG: feilstatus eksponerer ikke kildens rå feilmelding", async () =>
        {
            foreach (var status in new[] { 400, 401, 403, 404, 429, 503, 302 })
            {
                using var http = new HttpClient(new Handler(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive-upstream-diagnostic") }));
                var error = await Error("source-http", () => new DhgFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
                Check(error.Message.Contains(status.ToString()) && !error.Message.Contains("sensitive"), "safe status only");
            }
        }),
        ("DHG: OperationOutcome og ugyldige HTTP 200-svar avvises", async () =>
        {
            var outcome = new OperationOutcome { Issue = [new() { Severity = OperationOutcome.IssueSeverity.Error, Code = OperationOutcome.IssueType.Processing }] };
            (Resource, string)[] cases = [(outcome, "source-outcome"), (BundleOf(outcome), "source-outcome"),
                (DemoFiles.DhgPatient(), "source-contract"), (new Bundle { Type = Bundle.BundleType.Collection }, "source-contract")];
            foreach (var (resource, code) in cases)
            {
                using var http = new HttpClient(new Handler(Reply(resource)));
                await Error(code, () => new DhgFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
            }
        }),
        ("DHG: delvis Bundle og neste-side-lenke følges ikke", async () =>
        {
            var page = BundleOf(DemoFiles.DhgPatient());
            page.Link.Add(new() { Relation = "next", Url = "https://untrusted.example/Patient" });
            var partial = BundleOf(DemoFiles.DhgPatient()); partial.Total = 2;
            using var handler = new Handler(Reply(page), Reply(partial));
            using var http = new HttpClient(handler);
            var source = new DhgFhirDataSource(http, Options());
            await Error("source-paging", () => source.ReadPatientAsync(TestIdentifier, default));
            await Error("source-contract", () => source.ReadPatientAsync(TestIdentifier, default));
            Check(handler.Requests.Count == 2, "no paging calls");
        }),
        ("DHG: HTML, ugyldig JSON og for store svar avvises", async () =>
        {
            (string, string, string)[] cases = [("<html>upstream</html>", "text/html", "source-content"),
                ("{broken", "application/fhir+json", "source-json"),
                (new string('x', 2 * 1024 * 1024 + 1), "application/fhir+json", "response-size")];
            foreach (var (body, contentType, code) in cases)
            {
                using var http = new HttpClient(new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) }));
                await Error(code, () => new DhgFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, default));
            }
        }),
        ("DHG: kansellering stopper før nettverkskall", async () =>
        {
            using var handler = new Handler();
            using var http = new HttpClient(handler);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            try { await new DhgFhirDataSource(http, Options()).ReadPatientAsync(TestIdentifier, cts.Token); }
            catch (OperationCanceledException) { Check(handler.Requests.Count == 0, "no requests"); return; }
            throw new InvalidOperationException("expected cancellation");
        }),
        ("DHG: feil kildemodus og manglende testavgrensning avvises", () =>
        {
            var invalid = new[] { Options(), Options(), Options() };
            invalid[0].Mode = "unsupported";
            invalid[1].AllowedTestPatientIdentifiers.Clear();
            invalid[2].BearerTokenEnvironmentVariable = "SHOULD_NOT_BE_USED";
            foreach (var options in invalid)
            {
                try { options.Validate(); }
                catch (PopulationException e) when (e.Code == "configuration") { continue; }
                throw new InvalidOperationException("invalid configuration accepted");
            }
            return SysTask.CompletedTask;
        })
    ];

    private static IEnumerable<QuestionnaireResponse.ItemComponent> Flatten(IEnumerable<QuestionnaireResponse.ItemComponent> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flatten(i.Item)));
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static async Task<PopulationException> Error(string code, Func<SysTask> action)
    {
        try { await action(); }
        catch (PopulationException e) when (e.Code == code) { return e; }
        throw new InvalidOperationException("Expected " + code);
    }
    // Registrer formen på kallene for å kontrollere at NIN bare sendes i kroppen og at testmodus
    // ikke legger på tilgangsheadere. Handler returnerer planlagte svar i stedet for å bruke nettverk.
    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? ContentType, string Accept, bool HasCredentials);
    private sealed class Handler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<RecordedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method, request.RequestUri!, await request.Content!.ReadAsStringAsync(ct),
                request.Content.Headers.ContentType?.MediaType, string.Join(",", request.Headers.Accept),
                request.Headers.Authorization is not null || request.Headers.Contains("DPoP") || request.Headers.Contains("X-Patient-Context")));
            return queue.Dequeue();
        }
    }
}
