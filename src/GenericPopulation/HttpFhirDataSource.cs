using System.Net.Http.Headers;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Felles FHIR-klient for alle endepunkter. GET/POST, pasientfilter og API-begrensninger kommer
/// fra konfigurasjonen. Opprett én klient per kilde per populering; del bare HttpClient.
/// </summary>
public sealed class HttpFhirDataSource : IPatientFhirDataSource
{
    private const int MaxPages = 20, MaxEntries = 2000;
    private readonly HttpClient client;
    private readonly FhirSourceOptions options;
    private readonly Uri baseUri;
    private readonly IRequestAuthorizer authorizer;
    // Brukes bare når denne instansen selv er sentral pasientkilde. Andre kilder får konteksten fra verten.
    private string? lookupIdentifier, lookupPatientId;
    public int RequestCount { get; private set; }

    public HttpFhirDataSource(HttpClient client, FhirSourceOptions options, IRequestAuthorizer? authorizer = null)
    {
        this.client = client;
        this.options = options;
        baseUri = options.Validate();
        this.authorizer = authorizer ?? new ConfiguredAuthorizer(options);
    }

    /// <summary>Standard FHIR-transport for eksisterende programmatisk bruk.</summary>
    public HttpFhirDataSource(HttpClient client, Uri baseUri, IRequestAuthorizer authorizer)
        : this(client, new FhirSourceOptions { Id = "fhir", BaseUrl = baseUri.AbsoluteUri }, authorizer) { }

    public async Task<Patient> ReadPatientAsync(string patientKey, CancellationToken ct)
    {
        lookupIdentifier = lookupPatientId = null;
        options.ValidatePatientKey(patientKey);
        var context = new PopulationContext(new Patient { Id = options.PatientLookup.Interaction == "read" ? patientKey : null }, true);
        Patient patient;
        if (options.PatientLookup.Interaction == "read")
        {
            var resource = await SendAsync(new Uri(baseUri, "Patient/" + patientKey), null, context, ct);
            if (resource is not Patient found || found.Id != patientKey)
                throw new PopulationException("patient-mismatch", "Patient-oppslaget returnerte feil ressurstype eller pasient-ID.");
            patient = found;
        }
        else
        {
            var bundle = await SearchPagesAsync("Patient", [new(options.PatientLookup.Parameter, patientKey)], context, null, ct);
            if (bundle.Entry.Count == 0)
                throw new PopulationException("source-patient-not-found", "Den sentrale kilden returnerte ingen Patient.");
            if (bundle.Entry.Count != 1 || bundle.Entry[0].Resource is not Patient found || !FhirSearch.IsLogicalId(found.Id) ||
                options.PatientLookup.RequireDistinctResourceId && found.Id == patientKey)
                throw new PopulationException("source-contract", "Den sentrale kilden må returnere nøyaktig én Patient med gyldig ressurs-ID.");
            patient = found;
            lookupIdentifier = patientKey;
            lookupPatientId = patient.Id;
        }
        return patient;
    }

    public async Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        if (!context.PrepopulationAllowed)
            throw new PopulationException("authorization", "Preutfylling er ikke tillatt i denne konteksten.");
        _ = FhirSearch.Parse(search.RelativeUrl, context.Patient.Id!);
        if (context.InputIdentifier is null && context.Patient.Id == lookupPatientId)
            context = context with { InputIdentifier = lookupIdentifier };
        var policy = options.Capabilities.Policy(search.ResourceType);
        var parameters = search.Parameters.Select(p => p.Key == "patient"
            ? new KeyValuePair<string, string>(options.PatientBinding.Parameter, options.PatientBinding.Value(context)) : p).ToList();
        // Et annet filter må ikke kollidere med den serverstyrte pasientbindingen.
        if (parameters.Count(p => p.Key == options.PatientBinding.Parameter) != 1)
            throw new PopulationException("patient-context", "Tvetydig pasientfilter etter kildekonfigurasjon.");
        policy.Validate(parameters);
        return await SearchPagesAsync(search.ResourceType, parameters, context, policy, ct);
    }

    private async Task<Bundle> SearchPagesAsync(string type, List<KeyValuePair<string, string>> parameters,
        PopulationContext context, ResourceSearchPolicy? policy, CancellationToken ct)
    {
        var merged = new Bundle { Type = Bundle.BundleType.Searchset };
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var post = options.SearchMethod == "POST";
        Uri? next = new(baseUri, post ? type + "/_search" : new FhirSearch(type, parameters).RelativeUrl);
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaxPages || !visited.Add(next.AbsoluteUri))
                throw new PopulationException("paging-limit", "Søket overskred sidegrensen eller inneholdt en løkke.");
            var resource = await SendAsync(next, page == 0 && post ? parameters : null, context, ct);
            if (resource is not Bundle { Type: Bundle.BundleType.Searchset } bundle)
                throw new PopulationException("source-contract", "Forventet et searchset Bundle.");
            foreach (var entry in bundle.Entry)
            {
                if (entry.Resource is null || entry.Search?.Mode == Bundle.SearchEntryMode.Include)
                    throw new PopulationException("source-contract", "Søket returnerte en ugyldig eller inkludert ressurs.");
                if (entry.Resource is OperationOutcome outcome)
                {
                    if (policy is null || options.Capabilities.RejectOutcomeEntries || outcome.Issue.Any(i =>
                        i.Severity is OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal))
                        throw new PopulationException("source-outcome", "Datakilden meldte feil under søket. Ingen delvis QR blir returnert.");
                }
                else
                {
                    if (entry.Resource.TypeName != type)
                        throw new PopulationException("source-contract", "Uventet ressurstype i søket.");
                    if (policy is not null)
                        FhirSearch.AssertSubject(entry.Resource, context.Patient.Id!, baseUri, policy.PatientReferencePath, context.PatientBaseUri);
                }
                var copy = (Bundle.EntryComponent)entry.DeepCopy();
                // FullUrl bevarer ressursens opprinnelse selv når sider fra kilden samles.
                if (copy.FullUrl is null && FhirSearch.IsLogicalId(copy.Resource!.Id))
                    copy.FullUrl = new Uri(baseUri, copy.Resource.TypeName + "/" + copy.Resource.Id).AbsoluteUri;
                merged.Entry.Add(copy);
                if (merged.Entry.Count > MaxEntries)
                    throw new PopulationException("result-limit", "Søket overskred resultatgrensen.");
            }
            var links = bundle.Link.Where(l => l.Relation == "next").ToArray();
            if (links.Length > 1 || options.Capabilities.Paging == "none" && links.Length > 0)
                throw new PopulationException("source-paging", "Neste side er tvetydig eller støttes ikke av endepunktets konfigurasjon.");
            if (options.Capabilities.RequireTotal && bundle.Total is null ||
                options.Capabilities.Paging == "none" && bundle.Total is { } total && total != bundle.Entry.Count(e => e.Resource is not OperationOutcome))
                throw new PopulationException("source-contract", "Søket mangler konfigurert total eller returnerte et ufullstendig resultat.");
            if (links.Length == 0) next = null;
            else if (string.IsNullOrWhiteSpace(links[0].Url) || !Uri.TryCreate(next, links[0].Url, out next))
                throw new PopulationException("source-paging", "Ugyldig neste-side-lenke.");
        }
        merged.Total = merged.Entry.Count(e => e.Resource is not OperationOutcome);
        return merged;
    }

    // Autorisasjon skjer først etter destinasjonskontroll, også ved paginering. Neste-lenker er GET.
    private async Task<Resource> SendAsync(Uri uri, List<KeyValuePair<string, string>>? form,
        PopulationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if ((!uri.IsLoopback && uri.Scheme != "https") ||
            uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new PopulationException("destination-policy", "FHIR-kallet peker utenfor godkjent endepunkt.");
        using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, uri);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
            if ((await request.Content.ReadAsByteArrayAsync(ct)).Length > options.Capabilities.MaxFormBytes)
                throw new PopulationException("query-policy", "Søkekroppen overskrider endepunktets konfigurerte grense.");
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        request.Headers.TryAddWithoutValidation("Prefer", "handling=strict");
        await authorizer.AuthorizeAsync(request, context, ct);
        RequestCount++;
        return await FhirHttpResponse.SendAsync(client, request, ct);
    }
}

/// <summary>Autorisasjon for lokale syntetiske tester; tillater bare loopback.</summary>
public sealed class LocalDemoAuthorizer : IRequestAuthorizer
{
    public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RequestUri is null || !request.RequestUri.IsLoopback || !context.PrepopulationAllowed)
            throw new PopulationException("demo-only", "Uautentisert demo er begrenset til loopback og syntetiske data.");
        return SysTask.CompletedTask;
    }
}
