using System.Text.RegularExpressions;
using Hl7.Fhir.Model;

namespace GenericPopulation;

public sealed record FhirSearch(string ResourceType,
    IReadOnlyList<KeyValuePair<string, string>> Parameters)
{
    private static readonly HashSet<string> Resources =
        ["Observation", "Encounter", "CareTeam"];
    private static readonly HashSet<string> AllowedParameters =
        ["patient", "code", "category", "date"];

    public string? Get(string name) => Parameters
        .Where(p => p.Key == name).Select(p => p.Value).SingleOrDefault();

    public string RelativeUrl => ResourceType + "?" + string.Join("&", Parameters.Select(p =>
        Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    // Deliberately restricted x-fhir-query template support: only {{%patient.id}}.
    // A production SDC implementation can add a bounded template evaluator.
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
        // Multiple date bounds are legal. All other duplicate parameters are rejected.
        if (pairs.GroupBy(p => p.Key).Any(g => g.Count() > 1 && g.Key != "date"))
            throw new PopulationException("query-policy", "Tvetydige søkeparametre.");
        if (pairs.Count(p => p.Key == "patient") != 1 ||
            pairs.Single(p => p.Key == "patient").Value != expectedPatientId)
            throw new PopulationException("patient-context", "Pasientfilteret stemmer ikke med konteksten.");
        if (parts[0] != "Observation" && pairs.Any(p => p.Key != "patient"))
            throw new PopulationException("query-policy", "Søkeparameter støttes ikke for ressursen.");
        return new(parts[0], pairs);
    }

    // Defence in depth: this is NOT a replacement for source-side authorization.
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
