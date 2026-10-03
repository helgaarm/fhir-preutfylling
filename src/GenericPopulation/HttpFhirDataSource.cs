using System.Net.Http.Headers;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Adapter for FHIR-kilder med GET-oppslag og GET-søk. Samler sider til ett Bundle og
/// kontrollerer destinasjon, pasient og resultatgrenser før motoren får dataene.
/// Verten deler HttpClient, men oppretter en ny adapter per preutfylling og slår av omdirigeringer.
/// </summary>
public sealed class HttpFhirDataSource(
    HttpClient client, Uri baseUri, IRequestAuthorizer authorizer) : IPatientFhirDataSource
{
    private const int MaxPages = 20, MaxEntries = 2000;
    /// <inheritdoc />
    public int RequestCount { get; private set; }

    /// <summary>Leser Patient/{id} og kontrollerer at svaret har den forespurte logiske ID-en.</summary>
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

    /// <summary>Henter alle tillatte sider fra et validert søk; feil avbryter uten delresultat.</summary>
    public async Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        if (!baseUri.AbsolutePath.EndsWith('/'))
            throw new PopulationException("configuration", "FHIR base URL må ende med skråstrek.");
        var merged = new Bundle { Type = Bundle.BundleType.Searchset };
        // En feilaktig next-lenke må verken gi en uendelig løkke eller ubegrenset minnebruk.
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

    // Kontroller destinasjonen før autorisasjon legges på. Dette gjelder også next-lenker fra kilden,
    // slik at et token ikke sendes videre til en annen vert eller en annen del av samme server.
    private async Task<Resource> GetAsync(Uri uri, PopulationContext context, CancellationToken ct)
    {
        AssertDestination(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        request.Headers.TryAddWithoutValidation("Prefer", "handling=strict");
        await authorizer.AuthorizeAsync(request, context, ct);
        RequestCount++;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return await FhirHttpResponse.ReadAsync(response, ct);
    }

    // Samme protokoll, vert, port og basesti som den serverkonfigurerte FHIR-kilden.
    private void AssertDestination(Uri uri)
    {
        if ((!uri.IsLoopback && uri.Scheme != "https") ||
            uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new PopulationException("destination-policy", "FHIR-kallet peker utenfor godkjent endepunkt.");
    }

}

/// <summary>Autorisasjonsadapter for lokale syntetiske tester; tillater bare loopback.</summary>
public sealed class LocalDemoAuthorizer : IRequestAuthorizer
{
    /// <inheritdoc />
    public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
    {
        if (request.RequestUri is null || !request.RequestUri.IsLoopback || !context.PrepopulationAllowed)
            throw new PopulationException("demo-only", "Uautentisert demo er begrenset til loopback og syntetiske data.");
        return SysTask.CompletedTask;
    }
}
