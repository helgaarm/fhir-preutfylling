using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace GenericPopulation;

// Only a test double: not a general FHIR search server.
public sealed class FixtureDataSource(IReadOnlyList<Resource> resources) : IFhirDataSource
{
    public int SearchCount { get; private set; }
    public Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SearchCount++;
        if (search.Parameters.Any(p => p.Key is not ("patient" or "code")))
            throw new PopulationException("fixture-search", "Testdatakilden støtter bare patient og code.");
        var bundle = new Bundle { Type = Bundle.BundleType.Searchset };
        foreach (var resource in resources.Where(r => r.TypeName == search.ResourceType))
        {
            if (search.Get("code") is { } code)
            {
                var token = code.Split('|', 2);
                if (token.Length != 2 || resource is not Observation observation ||
                    !observation.Code.Coding.Any(c => c.System == token[0] && c.Code == token[1])) continue;
            }
            // Fixtures intentionally fail on a wrong patient rather than hiding a broken test.
            FhirSearch.AssertSubject(resource, context.Patient.Id!);
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                Resource = (Resource)resource.DeepCopy(),
                Search = new Bundle.SearchComponent { Mode = Bundle.SearchEntryMode.Match }
            });
        }
        bundle.Total = bundle.Entry.Count;
        return SysTask.FromResult(bundle);
    }
}

public static class DemoFiles
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "examples");
    public static T Read<T>(string filename) where T : Resource =>
        new FhirJsonDeserializer().Deserialize<T>(File.ReadAllText(Path.Combine(Folder, filename)));
    public static Questionnaire Questionnaire(string name) => name switch
    {
        "general" => Read<Questionnaire>("questionnaire-general.json"),
        "pregnancy" => Read<Questionnaire>("questionnaire-pregnancy.json"),
        _ => throw new PopulationException("scenario", "Ukjent demoscenario.")
    };
    public static Patient Patient() => Read<Patient>("patient.json");
    public static List<Resource> Resources() => Read<Bundle>("observations.json").Entry
        .Select(e => e.Resource).OfType<Resource>().ToList();
}
