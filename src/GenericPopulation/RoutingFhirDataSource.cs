using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>Serverstyrt plan for én populering. PatientSource er den eneste kilden som henter Patient.</summary>
public sealed class PopulationProfileOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PatientSource { get; set; } = "";
    public string? DefaultSource { get; set; }
    public string DefaultExample { get; set; } = "pregnancy";
    public List<ResourceRouteOptions> Routes { get; set; } = [];
    public List<QueryBindingOptions> QueryBindings { get; set; } = [];
    /// <summary>Bare disse kildene kan utelates ved tilgjengelighetsfeil. Sentral Patient er alltid påkrevd.</summary>
    public List<string> OptionalSources { get; set; } = [];

    public IEnumerable<string> SourceIds() => new[] { PatientSource, DefaultSource }.OfType<string>()
        .Concat(Routes.Select(r => r.Source)).Concat(QueryBindings.Select(b => b.Source)).Distinct(StringComparer.Ordinal);

    public void Validate(IReadOnlyCollection<FhirSourceOptions> sources)
    {
        var known = sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (!System.Text.RegularExpressions.Regex.IsMatch(Id, @"\A[A-Za-z0-9_-]+\z") || string.IsNullOrWhiteSpace(Name) ||
            DefaultExample is not ("pregnancy" or "general" or "dhg") || SourceIds().Any(id => !known.Contains(id)) ||
            OptionalSources.Any(id => id == PatientSource || !SourceIds().Contains(id, StringComparer.Ordinal)))
            throw new PopulationException("configuration", "Populeringsprofilen må ha navn, ID og kjente datakilder.");
        foreach (var route in Routes)
            if (!FhirSearch.IsResourceType(route.ResourceType) || route.Code is "" || route.Profile is "")
                throw new PopulationException("configuration", "Ugyldig ressursrute i populeringsprofilen.");
        foreach (var binding in QueryBindings)
            if (!Uri.TryCreate(binding.Questionnaire, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(binding.Version) ||
                string.IsNullOrWhiteSpace(binding.Variable) || binding.LinkId is "")
                throw new PopulationException("configuration", "Søkekoblingen krever Questionnaire-URL, versjon og variabelnavn. Utelat LinkId for rotvariabler.");
        if (Routes.GroupBy(r => (r.ResourceType, r.Code, r.Profile)).Any(g => g.Count() > 1) ||
            QueryBindings.GroupBy(b => (b.Questionnaire, b.Version, b.LinkId, b.Variable)).Any(g => g.Count() > 1))
            throw new PopulationException("configuration", "Populeringsprofilen inneholder dupliserte ruteregler.");
    }

    /// <summary>Bevarer enkeltkilde-API-et: hver kilde får en implisitt profil med seg selv som standard.</summary>
    public static List<PopulationProfileOptions> Build(List<FhirSourceOptions> sources, List<PopulationProfileOptions> configured)
    {
        var profiles = sources.Where(s => s.ExposeAsProfile).Select(s => new PopulationProfileOptions
        { Id = s.Id, Name = s.Name, PatientSource = s.Id, DefaultSource = s.Id, DefaultExample = s.DefaultExample }).Concat(configured).ToList();
        if (profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
            throw new PopulationException("configuration", "Kilder og populeringsprofiler må ha unike ID-er.");
        foreach (var profile in profiles) profile.Validate(sources);
        return profiles;
    }
}

/// <summary>Eksakt samsvar med søkets code og/eller _profile. Ingen kodeoversettelse eller profilvalidering.</summary>
public sealed class ResourceRouteOptions
{
    public string ResourceType { get; set; } = "";
    public string? Code { get; set; }
    public string? Profile { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>Kobler en bestemt standard variable-extension til en kilde uten å endre FHIR-skjemaet.</summary>
public sealed class QueryBindingOptions
{
    public string Questionnaire { get; set; } = "";
    public string Version { get; set; } = "";
    public string? LinkId { get; set; }
    public string Variable { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Matches(QueryOrigin? origin) => origin is not null && origin.Questionnaire == Questionnaire &&
        origin.Version == Version && origin.LinkId == LinkId && origin.Variable == Variable;
}

/// <summary>
/// Ruter hvert søk til én kilde. Kilder er påkrevde som standard; det finnes ingen skjult reservekilde.
/// Opprett en ny ruter per populering, slik at merknader og tellere ikke deles mellom kjøringer.
/// </summary>
public sealed class RoutingFhirDataSource(PopulationProfileOptions profile,
    IReadOnlyDictionary<string, IFhirDataSource> sources) : IFhirDataSource
{
    /// <summary>Kun kilde-ID og antall søk, uten pasientverdier eller søkestrenger.</summary>
    public Dictionary<string, int> SearchCounts { get; } = new(StringComparer.Ordinal);
    private readonly List<OperationOutcome.IssueComponent> issues = [];
    public IReadOnlyList<OperationOutcome.IssueComponent> Issues => issues;

    public string CacheKey(FhirSearch search, PopulationContext context) =>
        SelectSource(search) + "\n" + context.Patient.Id + "\n" + search.RelativeUrl;

    public string SelectSource(FhirSearch search)
    {
        var bindings = profile.QueryBindings.Where(b => b.Matches(search.Origin)).ToArray();
        if (bindings.Length > 1) throw RoutingError();
        if (bindings.Length == 1) return Known(bindings[0].Source);
        var matches = profile.Routes.Where(r => r.ResourceType == search.ResourceType &&
            (r.Code is null || search.Parameters.Any(p => p.Key == "code" && p.Value == r.Code)) &&
            (r.Profile is null || search.Parameters.Any(p => p.Key == "_profile" && p.Value == r.Profile)))
            .Select(r => (Route: r, Specificity: (r.Code is null ? 0 : 1) + (r.Profile is null ? 0 : 1))).ToArray();
        if (matches.Length == 0) return profile.DefaultSource is { } fallback ? Known(fallback) : throw RoutingError();
        var best = matches.Where(m => m.Specificity == matches.Max(x => x.Specificity)).ToArray();
        return best.Length == 1 ? Known(best[0].Route.Source) : throw RoutingError();
    }

    public async Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        var id = SelectSource(search);
        SearchCounts[id] = SearchCounts.GetValueOrDefault(id) + 1;
        try { return await sources[id].SearchAsync(search, context, ct); }
        catch (Exception e) when (!ct.IsCancellationRequested && profile.OptionalSources.Contains(id, StringComparer.Ordinal) &&
            (e is HttpRequestException or OperationCanceledException ||
             e is PopulationException { Code: "source-http", HttpStatus: 408 or 429 or >= 500 }))
        {
            // Ikke gjenbruk delvise sider. Pasientfeil, ugyldige data og avvist autorisasjon
            // omfattes aldri av regelen. Brukerens avbrudd og samlet frist avbryter alltid.
            issues.Add(new()
            {
                Severity = OperationOutcome.IssueSeverity.Warning, Code = OperationOutcome.IssueType.Processing,
                Details = new CodeableConcept { Text = "Valgfri kilde '" + id + "' var utilgjengelig. Data fra dette søket mangler; kontroller ubesvarte felt." }
            });
            return new Bundle { Type = Bundle.BundleType.Searchset };
        }
    }

    private string Known(string id) => sources.ContainsKey(id) ? id : throw RoutingError();
    private static PopulationException RoutingError() => new("source-routing", "Søket må ha nøyaktig én mest presis regel med en konfigurert datakilde.");
}
