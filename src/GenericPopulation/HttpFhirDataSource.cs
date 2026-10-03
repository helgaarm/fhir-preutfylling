using System.Net.Http.Headers;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace GenericPopulation;

public sealed class HttpFhirDataSource(
    HttpClient client, Uri baseUri, IRequestAuthorizer authorizer) : IFhirDataSource
{
    private const int MaxPages = 20, MaxEntries = 2000, MaxBytes = 2 * 1024 * 1024;
    public int RequestCount { get; private set; }

    public async Task<Patient> ReadPatientAsync(string patientId, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(patientId, @"\A[A-Za-z0-9\-.]{1,64}\z"))
            throw new PopulationException("patient-context", "Bruk en logisk FHIR Patient-ID (1–64 bokstaver, tall, punktum eller bindestrek).");
        var context = new PopulationContext(new Patient { Id = patientId }, true);
        var resource = await GetAsync(new Uri(baseUri, "Patient/" + patientId), context, ct);
        if (resource is not Patient patient || patient.Id != patientId)
            throw new PopulationException("patient-mismatch", "Patient-oppslaget returnerte feil ressurstype eller pasient-ID.");
        return patient;
    }

    public async Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        if (!baseUri.AbsolutePath.EndsWith('/'))
            throw new PopulationException("configuration", "FHIR base URL må ende med skråstrek.");
        var merged = new Bundle { Type = Bundle.BundleType.Searchset };
        var visited = new HashSet<string>();
        Uri? next = new(baseUri, search.RelativeUrl);
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaxPages || !visited.Add(next.AbsoluteUri))
                throw new PopulationException("paging-limit", "Søket overskred sidegrensen eller inneholdt en løkke.");
            var resource = await GetAsync(next, context, ct);
            if (resource is not Bundle { Type: Bundle.BundleType.Searchset } bundle)
                throw new PopulationException("source-contract", "Forventet et searchset Bundle.");
            foreach (var entry in bundle.Entry)
            {
                if (entry.Resource is null) continue;
                if (entry.Resource is not OperationOutcome && entry.Resource.TypeName != search.ResourceType)
                    throw new PopulationException("source-contract", "Uventet ressurstype i søket.");
                FhirSearch.AssertSubject(entry.Resource, context.Patient.Id!, baseUri);
                merged.Entry.Add((Bundle.EntryComponent)entry.DeepCopy());
                if (merged.Entry.Count > MaxEntries)
                    throw new PopulationException("result-limit", "Søket overskred resultatgrensen.");
            }
            var links = bundle.Link.Where(l => l.Relation == "next").ToArray();
            if (links.Length > 1)
                throw new PopulationException("source-paging", "Tvetydig neste-side-lenke.");
            if (links.Length == 0) next = null;
            else if (string.IsNullOrWhiteSpace(links[0].Url) || !Uri.TryCreate(next, links[0].Url, out next))
                throw new PopulationException("source-paging", "Ugyldig neste-side-lenke.");
        }
        return merged;
    }

    private async Task<Resource> GetAsync(Uri uri, PopulationContext context, CancellationToken ct)
    {
        AssertDestination(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        request.Headers.TryAddWithoutValidation("Prefer", "handling=strict");
        await authorizer.AuthorizeAsync(request, context, ct);
        RequestCount++;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new PopulationException("source-http", "FHIR-kilden svarte med HTTP " + (int)response.StatusCode + ". Kontroller kilde, tilgang og pasient-ID.");
        if (response.Content.Headers.ContentType?.MediaType is not ("application/fhir+json" or "application/json"))
            throw new PopulationException("source-content", "Uventet innholdstype fra FHIR-kilden.");
        var json = await ReadBoundedAsync(response.Content, ct);
        Resource resource;
        try { resource = new FhirJsonDeserializer().Deserialize<Resource>(json); }
        catch (Exception) { throw new PopulationException("source-json", "Ugyldig FHIR JSON fra datakilden."); }
        if (resource is OperationOutcome)
            throw new PopulationException("source-outcome", "FHIR-kilden returnerte OperationOutcome i stedet for de forespurte dataene.");
        return resource;
    }

    private void AssertDestination(Uri uri)
    {
        if ((!uri.IsLoopback && uri.Scheme != "https") ||
            uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new PopulationException("destination-policy", "FHIR-kallet peker utenfor godkjent endepunkt.");
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxBytes)
            throw new PopulationException("response-size", "Datakildens svar er for stort.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (memory.Length + read > MaxBytes)
                throw new PopulationException("response-size", "Datakildens svar er for stort.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
}

public sealed class LocalDemoAuthorizer : IRequestAuthorizer
{
    public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
    {
        if (request.RequestUri is null || !request.RequestUri.IsLoopback || !context.PrepopulationAllowed)
            throw new PopulationException("demo-only", "Uautentisert demo er begrenset til loopback og syntetiske data.");
        return SysTask.CompletedTask;
    }
}
