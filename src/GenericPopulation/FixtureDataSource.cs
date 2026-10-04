using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace GenericPopulation;

/// <summary>
/// Datakilde i minnet for syntetiske eksempler og tester uten eksterne oppslag.
/// Støtter bare patient og code, og simulerer ikke en full FHIR-søketjeneste.
/// </summary>
public sealed class FixtureDataSource(IReadOnlyList<Resource> resources) : IFhirDataSource
{
    /// <summary>Antall søk, brukt i tester som kontrollerer cache og avslått tilgang.</summary>
    public int SearchCount { get; private set; }
    /// <summary>Filtrerer testressurser og returnerer kopier, slik at originaldataene kan gjenbrukes.</summary>
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
            // Feil pasient i testdata skal oppdages, ikke skjules ved å filtrere bort ressursen.
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

/// <summary>
/// Leser syntetiske JSON-eksempler som prosjektfilen kopierer til examples ved bygg/publisering.
/// De samme filene brukes av kommandolinjedemoen, webappens eksempler og regresjonstestene.
/// </summary>
public static class DemoFiles
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "examples");
    /// <summary>Leser en kjent lokal eksempelfil; filename skal komme fra kode, ikke fra HTTP-input.</summary>
    public static T Read<T>(string filename) where T : Resource =>
        new FhirJsonDeserializer().Deserialize<T>(File.ReadAllText(Path.Combine(Folder, filename)));
    /// <summary>Kobler de tillatte scenarionavnene til skjemafiler uten å bygge filstier fra input.</summary>
    public static Questionnaire Questionnaire(string name) => name switch
    {
        "general" => Read<Questionnaire>("questionnaire-general.json"),
        "pregnancy" => Read<Questionnaire>("questionnaire-pregnancy.json"),
        "routed-v1" => Read<Questionnaire>("questionnaire-routed-v1.json"),
        "routed-v2" => Read<Questionnaire>("questionnaire-routed-v2.json"),
        "dhg" => Read<Questionnaire>("questionnaire-dhg.json"),
        _ => throw new PopulationException("scenario", "Ukjent demoscenario.")
    };
    // Vanlig demo og DHG har hvert sitt datasett med samsvarende Patient-ID og subject-referanser.
    public static Patient Patient() => Read<Patient>("patient.json");
    public static Patient DhgPatient() => Read<Patient>("dhg-patient.json");
    public static List<Resource> DhgResources() => Read<Bundle>("dhg-resources.json").Entry
        .Select(e => e.Resource).OfType<Resource>().ToList();
    public static List<Resource> Resources() => Read<Bundle>("observations.json").Entry
        .Select(e => e.Resource).OfType<Resource>().ToList();
}
