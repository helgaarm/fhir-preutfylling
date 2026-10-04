using System.Text.RegularExpressions;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>Identifiserer en konkret variable-extension uten å legge endepunkter inn i Questionnaire.</summary>
public sealed record QueryOrigin(string Questionnaire, string Version, string? LinkId, string Variable);

/// <summary>Relativt FHIR-søk. Parseren håndhever syntaks og pasientavgrensning; API-begrensninger ligger i kildekonfigurasjonen.</summary>
public sealed record FhirSearch(string ResourceType, IReadOnlyList<KeyValuePair<string, string>> Parameters)
{
    public QueryOrigin? Origin { get; init; }
    public string? Get(string name) => Parameters.Where(p => p.Key == name).Select(p => p.Value).SingleOrDefault();
    public string RelativeUrl => ResourceType + "?" + string.Join("&", Parameters.Select(p =>
        Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    public static bool IsLogicalId(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9\-.]{1,64}\z");
    public static bool IsResourceType(string value) => Enum.TryParse<Hl7.Fhir.Model.ResourceType>(value, out var type) &&
        Enum.IsDefined(type) && type.ToString() == value;
    public static bool IsParameterName(string value) => Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_.:-]*\z");

    public static FhirSearch FromTemplate(string template, PopulationContext context)
    {
        if (!IsLogicalId(context.Patient.Id))
            throw new PopulationException("patient-context", "Ugyldig logisk pasient-ID.");
        var expanded = template.Replace("{{%patient.id}}", Uri.EscapeDataString(context.Patient.Id!), StringComparison.Ordinal);
        if (expanded.Contains('{') || expanded.Contains('}'))
            throw new PopulationException("query-template", "Ustøttet søkemal.");
        return Parse(expanded, context.Patient.Id!);
    }

    public static FhirSearch Parse(string relative, string expectedPatientId)
    {
        if (relative.Length > 4096 || relative.Contains('#') || relative.Contains('\\'))
            throw new PopulationException("query-policy", "Ulovlig relativt FHIR-søk.");
        var parts = relative.Split('?', 2);
        if (parts.Length != 2 || !IsResourceType(parts[0]))
            throw new PopulationException("query-policy", "Forventet en FHIR R4-ressurstype i et relativt søk.");
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var part in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                throw new PopulationException("query-policy", "Ugyldig søkeparameter.");
            var key = Uri.UnescapeDataString(kv[0].Replace('+', ' '));
            var value = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            // Inkluderte/omvendte ressurser og projeksjoner passer ikke kontrakten: alle treff må
            // ha forventet type og en kontrollerbar pasientreferanse. Dette er klientens sikkerhetsgrense.
            if (!IsParameterName(key) || string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) ||
                key.Split(':')[0] is "_include" or "_revinclude" or "_has" or "_filter" or "_elements" or "_summary" ||
                key.StartsWith("patient:", StringComparison.Ordinal) || key.StartsWith("patient.", StringComparison.Ordinal))
                throw new PopulationException("query-policy", "Søkeparameteren faller utenfor den pasientavgrensede søkekontrakten.");
            pairs.Add(new(key, value));
        }
        if (!IsLogicalId(expectedPatientId) || pairs.Count(p => p.Key == "patient") != 1 ||
            pairs.Single(p => p.Key == "patient").Value != expectedPatientId)
            throw new PopulationException("patient-context", "Pasientfilteret stemmer ikke med konteksten.");
        return new(parts[0], pairs);
    }

    /// <summary>Kontrollerer valgt pasient i alle treff. Sentral og lokal base kan ha samme avtalte pasient-ID.</summary>
    public static void AssertSubject(Resource resource, string id, Uri? baseUri = null,
        string referencePath = "subject", Uri? patientBaseUri = null)
    {
        if (resource is OperationOutcome) return;
        var values = resource.Select(referencePath).ToArray();
        var relative = "Patient/" + id;
        if (values.Length == 0 || values.Any(v => v is not ResourceReference r ||
            !(r.Reference == relative || baseUri is not null && r.Reference == new Uri(baseUri, relative).AbsoluteUri ||
              patientBaseUri is not null && r.Reference == new Uri(patientBaseUri, relative).AbsoluteUri)))
            throw new PopulationException("patient-mismatch", "Datakilden returnerte en annen eller manglende pasientkontekst.");
    }
}
