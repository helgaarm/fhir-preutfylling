using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Adapter for den anonyme POST-kontrakten i DHG Test. Oversetter motorens søk til skjemafelter
/// i /_search og holder syntetisk NIN adskilt fra pasientens pseudonyme FHIR-ID.
/// Har pasienttilstand og må opprettes på nytt for hver preutfylling. STS/HelseID er ikke implementert.
/// </summary>
public sealed class DhgFhirDataSource : IPatientFhirDataSource
{
    private readonly HttpClient client;
    private readonly FhirSourceOptions options;
    private readonly Uri baseUri;
    // identifier brukes bare i utgående POST-kropp; patientId brukes i Q, QR og subject-kontroll.
    private string? identifier, patientId;
    /// <inheritdoc />
    public int RequestCount { get; private set; }

    /// <summary>Kontrollerer DHG-konfigurasjonen. HttpClient leveres av verten med omdirigeringer avslått.</summary>
    public DhgFhirDataSource(HttpClient client, FhirSourceOptions options)
    {
        this.client = client;
        this.options = options;
        baseUri = options.Validate();
        if (!options.IsDhg)
            throw new PopulationException("configuration", "DHG-klienten krever kildemodus dhg-post.");
    }

    /// <summary>
    /// Slår opp et godkjent syntetisk NIN via Patient/_search og etablerer pasienten for videre søk.
    /// Forventer nøyaktig én Patient med en annen ressurs-ID enn NIN.
    /// </summary>
    public async Task<Patient> ReadPatientAsync(string patientKey, CancellationToken ct)
    {
        // Et mislykket pasientbytte må aldri etterlate forrige pasient som aktiv kontekst.
        identifier = patientId = null;
        options.ValidatePatientKey(patientKey);
        var bundle = await PostAsync("Patient", [new("identifier", patientKey)], ct);
        if (bundle.Entry.Count == 0)
            throw new PopulationException("source-patient-not-found", "DHG returnerte ingen Patient for valgt testperson.");
        if (bundle.Entry.Count != 1 || bundle.Entry[0].Resource is not Patient patient ||
            patient.Id is null || !Regex.IsMatch(patient.Id, @"\A[A-Za-z0-9\-.]{1,64}\z") || patient.Id == patientKey)
            throw new PopulationException("source-contract", "DHG må returnere nøyaktig én Patient med gyldig pseudonym ressurs-ID.");
        identifier = patientKey;
        patientId = patient.Id;
        return patient;
    }

    /// <summary>
    /// Erstatter søkets logiske patient-filter med patient.identifier i POST-kroppen.
    /// Alle returnerte ressurser må referere til pasienten fra ReadPatientAsync.
    /// </summary>
    public async Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        if (!context.PrepopulationAllowed)
            throw new PopulationException("authorization", "Preutfylling er ikke tillatt i denne konteksten.");
        if (identifier is null || patientId is null || context.Patient.Id != patientId)
            throw new PopulationException("patient-context", "DHG-søk krever pasienten fra samme innhenting.");
        // Kontroller også kallere som har opprettet FhirSearch direkte uten Parse/FromTemplate.
        search = FhirSearch.Parse(search.RelativeUrl, patientId);
        ValidateFilters(search);
        var form = new List<KeyValuePair<string, string>> { new("patient.identifier", identifier) };
        form.AddRange(search.Parameters.Where(p => p.Key != "patient"));
        var bundle = await PostAsync(search.ResourceType, form, ct);
        foreach (var entry in bundle.Entry)
        {
            if (entry.Resource is null || entry.Resource.TypeName != search.ResourceType)
                throw new PopulationException("source-contract", "Uventet ressurstype fra DHG-søket.");
            FhirSearch.AssertSubject(entry.Resource, patientId, baseUri);
        }
        return bundle;
    }

    // DHG støtter færre filtervarianter enn vanlig FHIR: én verdi per filter, code som system|code
    // og en gyldig kalenderdato med valgfritt sammenligningsprefiks. Avvis før nettverkskallet.
    private static void ValidateFilters(FhirSearch search)
    {
        if (search.Parameters.GroupBy(p => p.Key).Any(g => g.Count() > 1))
            throw new PopulationException("query-policy", "DHG støtter bare én verdi per filter, også date.");
        if (search.Get("code") is { } code && !IsToken(code))
            throw new PopulationException("query-policy", "DHG code må ha formatet system|code.");
        if (search.Get("category") is { } category && category.Contains('|') &&
            (!IsToken(category) || !category.StartsWith("http://terminology.hl7.org/CodeSystem/observation-category|", StringComparison.Ordinal)))
            throw new PopulationException("query-policy", "DHG category må være en kategorikode eller et token fra observation-category.");
        if (search.Get("date") is { } date)
        {
            var match = Regex.Match(date, @"\A(?:eq|ne|gt|lt|ge|le)?([0-9]{4}-[0-9]{2}-[0-9]{2})\z");
            if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new PopulationException("query-policy", "DHG date krever en gyldig dato (yyyy-MM-dd), eventuelt med eq, ne, gt, lt, ge eller le.");
        }
    }

    private static bool IsToken(string value) => value.Split('|') is [var system, var code] &&
        !string.IsNullOrWhiteSpace(system) && !string.IsNullOrWhiteSpace(code);

    // Felles transport for Patient, Observation, Encounter og CareTeam. Krever et komplett
    // searchset; usikre eller delvise svar skal ikke gi en tilsynelatende ferdig QR.
    private async Task<Bundle> PostAsync(string resourceType, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Bare kontrollerte ressursnavn når denne metoden. NIN legges aldri i URL-en.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, resourceType + "/_search"));
        request.Content = new FormUrlEncodedContent(form);
        if ((await request.Content.ReadAsByteArrayAsync(ct)).Length > 4096)
            throw new PopulationException("query-policy", "DHG-søkekroppen kan ikke være større enn 4096 byte.");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        RequestCount++;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var resource = await FhirHttpResponse.ReadAsync(response, ct);
        if (resource is not Bundle { Type: Bundle.BundleType.Searchset } bundle)
            throw new PopulationException("source-contract", "Forventet et searchset Bundle fra DHG.");
        if (bundle.Entry.Count > 2000)
            throw new PopulationException("result-limit", "DHG-søket overskred resultatgrensen.");
        if (bundle.Entry.Any(e => e.Resource is OperationOutcome))
            throw new PopulationException("source-outcome", "DHG returnerte OperationOutcome i søket. Ingen delvis QR blir returnert.");
        // Den dokumenterte POST-kontrakten har ingen mekanisme for neste side.
        // next-lenker eller avvikende total avvises fremfor å miste data i stillhet.
        if (bundle.Link.Any(l => l.Relation == "next"))
            throw new PopulationException("source-paging", "DHG returnerte en neste-side-lenke som ikke støttes av POST-kontrakten.");
        if (bundle.Total is null || bundle.Total != bundle.Entry.Count)
            throw new PopulationException("source-contract", "DHG-svarets total stemmer ikke med antall returnerte ressurser.");
        return bundle;
    }
}
