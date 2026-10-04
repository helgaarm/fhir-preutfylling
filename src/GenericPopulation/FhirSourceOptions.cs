using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace GenericPopulation;

/// <summary>Serverstyrt endepunkt og transport. Leverandørnavn styrer ikke klientens oppførsel.</summary>
public sealed class FhirSourceOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? BearerTokenEnvironmentVariable { get; set; }
    public string SearchMethod { get; set; } = "GET";
    public PatientLookupOptions PatientLookup { get; set; } = new();
    public PatientBindingOptions PatientBinding { get; set; } = new();
    public FhirCapabilities Capabilities { get; set; } = new();
    public string DefaultExample { get; set; } = "pregnancy";
    /// <summary>Slå av for endepunkter som bare skal brukes gjennom en fler-kildeprofil.</summary>
    public bool ExposeAsProfile { get; set; } = true;
    /// <summary>Valgfri offentlig liste med syntetiske testpersoner. Tom liste gir fri identifikatorinput.</summary>
    public List<string> AllowedTestPatientIdentifiers { get; set; } = [];
    public string? PatientIdentifierPattern { get; set; }

    public Uri Validate()
    {
        if (!Regex.IsMatch(Id, @"\A[A-Za-z0-9_-]+\z") ||
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !uri.AbsolutePath.EndsWith('/') || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new PopulationException("configuration", "FHIR-kilden må ha en ID og en HTTPS-base med avsluttende skråstrek (HTTP tillates lokalt).");
        if (SearchMethod is not ("GET" or "POST") || PatientLookup.Interaction is not ("read" or "search") ||
            !FhirSearch.IsParameterName(PatientLookup.Parameter) || !FhirSearch.IsParameterName(PatientBinding.Parameter) ||
            PatientBinding.ValueFrom is not ("patientId" or "inputIdentifier" or "patientIdentifier") ||
            (PatientBinding.ValueFrom == "patientIdentifier" && string.IsNullOrWhiteSpace(PatientBinding.IdentifierSystem)) ||
            Capabilities.Paging is not ("follow" or "none") || Capabilities.MaxFormBytes is < 1 or > 65536 ||
            DefaultExample is not ("pregnancy" or "general" or "dhg"))
            throw new PopulationException("configuration", "Ugyldig transport-, pasient- eller kapabilitetskonfigurasjon.");
        if (PatientIdentifierPattern is { } pattern)
        {
            try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { throw new PopulationException("configuration", "Ugyldig mønster for pasientidentifikator."); }
        }
        foreach (var (type, policy) in Capabilities.Resources)
            if (!FhirSearch.IsResourceType(type) ||
                !Regex.IsMatch(policy.PatientReferencePath, @"\A[a-zA-Z][a-zA-Z0-9]*(\.[a-zA-Z][a-zA-Z0-9]*)*\z") ||
                policy.MaxOccurrences.Any(p => !FhirSearch.IsParameterName(p.Key) || p.Value < 1) ||
                policy.SearchParameters?.Any(p => !FhirSearch.IsParameterName(p)) == true)
                throw new PopulationException("configuration", "Ugyldig ressurskonfigurasjon.");
        foreach (var identifier in AllowedTestPatientIdentifiers) ValidatePatientKey(identifier);
        return uri;
    }

    /// <summary>Validerer nøkkelen til den sentrale pasientkilden før nettverkskall.</summary>
    public void ValidatePatientKey(string key)
    {
        if (PatientLookup.Interaction == "read")
        {
            if (!FhirSearch.IsLogicalId(key))
                throw new PopulationException("patient-context", "Ugyldig logisk FHIR Patient-ID.");
            return;
        }
        var valid = !string.IsNullOrWhiteSpace(key) && key.Length <= 256 && !key.Any(char.IsControl);
        try
        {
            if (PatientIdentifierPattern is { } pattern)
                valid &= Regex.IsMatch(key, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException) { valid = false; }
        if (!valid || (AllowedTestPatientIdentifiers.Count > 0 && !AllowedTestPatientIdentifiers.Contains(key, StringComparer.Ordinal)))
            throw new PopulationException("patient-context", "Velg en gyldig pasientidentifikator som er tillatt for den sentrale kilden.");
    }
}

/// <summary>Patient leses sentralt med ID eller søkes frem med identifikator.</summary>
public sealed class PatientLookupOptions
{
    public string Interaction { get; set; } = "read";
    public string Parameter { get; set; } = "identifier";
    public bool RequireDistinctResourceId { get; set; }
}

/// <summary>Oversetter det logiske patient-filteret til endepunktets parameter og verdi.</summary>
public sealed class PatientBindingOptions
{
    public string Parameter { get; set; } = "patient";
    public string ValueFrom { get; set; } = "patientId";
    public string? IdentifierSystem { get; set; }

    public string Value(PopulationContext context)
    {
        if (ValueFrom == "patientId") return context.Patient.Id!;
        if (ValueFrom == "inputIdentifier" && !string.IsNullOrWhiteSpace(context.InputIdentifier)) return context.InputIdentifier;
        if (ValueFrom == "patientIdentifier")
        {
            var values = context.Patient.Identifier.Where(i => i.System == IdentifierSystem && !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => i.Value).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length == 1) return IdentifierSystem + "|" + values[0];
        }
        throw new PopulationException("patient-context", "Den sentrale pasienten mangler en entydig identifikator for kildens søk.");
    }
}

/// <summary>Valgfrie API-begrensninger. Tom Resources betyr ingen ressurs-/parameterbegrensning fra API-et.</summary>
public sealed class FhirCapabilities
{
    public string Paging { get; set; } = "follow";
    public bool RequireTotal { get; set; }
    public bool RejectOutcomeEntries { get; set; }
    public int MaxFormBytes { get; set; } = 65536;
    public Dictionary<string, ResourceSearchPolicy> Resources { get; set; } = new(StringComparer.Ordinal);

    public ResourceSearchPolicy Policy(string type)
    {
        if (Resources.TryGetValue(type, out var policy)) return policy;
        if (Resources.Count == 0) return new();
        throw new PopulationException("query-policy", "Ressurstypen er ikke aktivert for valgt endepunkt.");
    }
}

/// <summary>Begrensninger gjelder parameterne etter pasientbinding. Enkle elementstier brukes til pasientkontroll.</summary>
public sealed class ResourceSearchPolicy
{
    public List<string>? SearchParameters { get; set; }
    public Dictionary<string, int> MaxOccurrences { get; set; } = new(StringComparer.Ordinal);
    public List<string> TokenParameters { get; set; } = [];
    public List<string> DateParameters { get; set; } = [];
    public Dictionary<string, string> TokenSystems { get; set; } = new(StringComparer.Ordinal);
    public string PatientReferencePath { get; set; } = "subject";

    public void Validate(IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        foreach (var group in parameters.GroupBy(p => p.Key))
        {
            if (SearchParameters is not null && !SearchParameters.Contains(group.Key, StringComparer.Ordinal) ||
                MaxOccurrences.TryGetValue(group.Key, out var max) && group.Count() > max)
                throw new PopulationException("query-policy", "Søket overskrider endepunktets konfigurerte parameterstøtte.");
            foreach (var (_, value) in group)
            {
                if (TokenParameters.Contains(group.Key) && !IsToken(value) ||
                    TokenSystems.TryGetValue(group.Key, out var system) && value.Contains('|') && (!IsToken(value) || !value.StartsWith(system + "|", StringComparison.Ordinal)))
                    throw new PopulationException("query-policy", "Søkefilteret krever et gyldig system|code-token for dette endepunktet.");
                if (DateParameters.Contains(group.Key))
                {
                    var match = Regex.Match(value, @"\A(?:eq|ne|gt|lt|ge|le)?([0-9]{4}-[0-9]{2}-[0-9]{2})\z");
                    if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        throw new PopulationException("query-policy", "Endepunktet krever en gyldig kalenderdato med valgfritt sammenligningsprefiks.");
                }
            }
        }
    }
    private static bool IsToken(string value) => value.Split('|') is [var system, var code] &&
        !string.IsNullOrWhiteSpace(system) && !string.IsNullOrWhiteSpace(code);
}

/// <summary>Legger eventuelt på et ferdig bearer-token per kall. Anskaffelse og fornyelse tilhører verten.</summary>
public sealed class ConfiguredAuthorizer(FhirSourceOptions options) : IRequestAuthorizer
{
    public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.PrepopulationAllowed)
            throw new PopulationException("authorization", "Preutfylling er ikke tillatt i denne konteksten.");
        if (options.BearerTokenEnvironmentVariable is { Length: > 0 } variable)
        {
            var token = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
                throw new PopulationException("configuration", "FHIR-kildens token mangler eller har ugyldig format. Kontroller miljøvariabelen på serveren.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return SysTask.CompletedTask;
    }
}
