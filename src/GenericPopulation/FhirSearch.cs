using System.Text.RegularExpressions;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Beskriver et relativt, pasientavgrenset søk uavhengig av transport og serveradresse.
/// Bruk Parse eller FromTemplate for å kontrollere søket før det sendes til en datakilde.
/// </summary>
public sealed record FhirSearch(string ResourceType,
    IReadOnlyList<KeyValuePair<string, string>> Parameters)
{
    private static readonly HashSet<string> Resources =
        ["Observation", "Encounter", "CareTeam"];
    private static readonly HashSet<string> AllowedParameters =
        ["patient", "code", "category", "date"];

    /// <summary>Leser en enkeltverdi; bruk Parameters ved gjentatte date-grenser i vanlig FHIR.</summary>
    public string? Get(string name) => Parameters
        .Where(p => p.Key == name).Select(p => p.Value).SingleOrDefault();

    /// <summary>URL-koder søket for GET og for motorens cache. DHG oversetter det til POST-felter.</summary>
    public string RelativeUrl => ResourceType + "?" + string.Join("&", Parameters.Select(p =>
        Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    /// <summary>
    /// Setter inn pasientens logiske ressurs-ID i {{%patient.id}} og validerer resultatet.
    /// Andre maluttrykk støttes ikke; NIN skal ikke settes inn i skjemaets søkemal.
    /// </summary>
    public static FhirSearch FromTemplate(string template, PopulationContext context)
    {
        var id = context.Patient.Id;
        if (string.IsNullOrWhiteSpace(id) ||
            !Regex.IsMatch(id, @"\A[A-Za-z0-9\-.]{1,64}\z"))
            throw new PopulationException("patient-context", "Ugyldig logisk pasient-ID.");
        var expanded = template.Replace("{{%patient.id}}", Uri.EscapeDataString(id),
            StringComparison.Ordinal);
        if (expanded.Contains('{') || expanded.Contains('}'))
            throw new PopulationException("query-template", "Ustøttet søkemal.");
        return Parse(expanded, id);
    }

    /// <summary>
    /// Tillater bare kjente ressurser og parametre med nøyaktig riktig pasientfilter.
    /// Absolutte URL-er og utvidelser som _include faller utenfor denne søkeprofilen.
    /// </summary>
    public static FhirSearch Parse(string relative, string expectedPatientId)
    {
        if (relative.Length > 4096 || relative.Contains('#') || relative.Contains('\\'))
            throw new PopulationException("query-policy", "Ulovlig relativt FHIR-søk.");
        var parts = relative.Split('?', 2);
        if (parts.Length != 2 || !Resources.Contains(parts[0]))
            throw new PopulationException("query-policy", "Ressurstypen er ikke tillatt.");
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var part in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                throw new PopulationException("query-policy", "Ugyldig søkeparameter.");
            var key = Uri.UnescapeDataString(kv[0].Replace('+', ' '));
            var value = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            if (!AllowedParameters.Contains(key) || string.IsNullOrWhiteSpace(value) ||
                value.Any(char.IsControl))
                throw new PopulationException("query-policy", "Søkeparameteren er ikke tillatt.");
            pairs.Add(new(key, value));
        }
        // Vanlig FHIR kan bruke flere date-grenser; DHG-klienten har en strengere egen kontroll.
        if (pairs.GroupBy(p => p.Key).Any(g => g.Count() > 1 && g.Key != "date"))
            throw new PopulationException("query-policy", "Tvetydige søkeparametre.");
        if (pairs.Count(p => p.Key == "patient") != 1 ||
            pairs.Single(p => p.Key == "patient").Value != expectedPatientId)
            throw new PopulationException("patient-context", "Pasientfilteret stemmer ikke med konteksten.");
        if (parts[0] != "Observation" && pairs.Any(p => p.Key != "patient"))
            throw new PopulationException("query-policy", "Søkeparameter støttes ikke for ressursen.");
        return new(parts[0], pairs);
    }

    /// <summary>
    /// Kontrollerer subject-referansen i returnerte kliniske ressurser, også når kilden svarer 200.
    /// Tillater relativ referanse eller absolutt referanse på godkjent base. Kilden må fortsatt
    /// håndheve autorisasjon; OperationOutcome behandles separat av klienten eller motoren.
    /// </summary>
    public static void AssertSubject(Resource resource, string id, Uri? baseUri = null)
    {
        if (resource is OperationOutcome) return;
        string? reference = resource switch
        {
            Observation o => o.Subject?.Reference,
            Encounter e => e.Subject?.Reference,
            CareTeam c => c.Subject?.Reference,
            _ => throw new PopulationException("source-contract", "Uventet ressurstype fra datakilden.")
        };
        var relative = "Patient/" + id;
        if (reference == relative) return;
        if (baseUri is not null && reference == new Uri(baseUri, relative).AbsoluteUri) return;
        throw new PopulationException("patient-mismatch", "Datakilden returnerte en annen pasientkontekst.");
    }
}
