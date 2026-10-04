using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenericPopulation;

/// <summary>Hele Fhir-delen som kan redigeres. Tokenverdier inngår aldri i konfigurasjonen.</summary>
public sealed class FhirConfiguration
{
    public List<FhirSourceOptions> Sources { get; set; } = [];
    public List<PopulationProfileOptions> PopulationProfiles { get; set; } = [];
    public List<QuestionnaireBindingOptions> QuestionnaireBindings { get; set; } = [];
    public bool RequireQuestionnaireBinding { get; set; }
}

/// <summary>En ferdig validert konfigurasjon. Hver populering beholder samme instans gjennom hele kjøringen.</summary>
public sealed record FhirConfigurationSnapshot(string Revision, FhirConfiguration Configuration,
    IReadOnlyList<PopulationProfileOptions> Profiles, QuestionnaireProfileRegistry Registry, string Json);

/// <summary>
/// Validerer utkast før lagring, avviser samtidige overskrivinger og bytter aktiv konfigurasjon
/// først etter vellykket atomisk filbytte. Filen ligger utenfor wwwroot og publiseres ikke.
/// </summary>
public sealed class FhirConfigurationStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public const string RelativeFile = ".local/fhir-configuration.json";
    private readonly string filePath;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private FhirConfigurationSnapshot current;
    private string? persistedJson;
    public FhirConfigurationSnapshot Current => Volatile.Read(ref current);

    public FhirConfigurationStore(string contentRoot, FhirConfiguration initial)
    {
        filePath = Path.Combine(contentRoot, RelativeFile);
        if (File.Exists(filePath))
        {
            persistedJson = File.ReadAllText(filePath, Encoding.UTF8);
            using var document = JsonDocument.Parse(persistedJson);
            current = Compile(document.RootElement);
        }
        else current = Compile(JsonSerializer.SerializeToElement(initial, JsonOptions));
    }

    /// <summary>Streng JSON og samme kryssvalidering ved oppstart, kontroll av utkast og lagring.</summary>
    public static FhirConfigurationSnapshot Compile(JsonElement json)
    {
        ValidateJson(json);
        FhirConfiguration data;
        try { data = json.Deserialize<FhirConfiguration>(JsonOptions) ?? throw new JsonException(); }
        catch (JsonException)
        { throw new PopulationException("configuration", "Konfigurasjonen har ukjente felt, manglende objekter eller feil datatyper. Bruk feltnavnene fra konfigurasjonsvisningen."); }
        try
        {
            if (data.Sources.Count == 0 || data.Sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != data.Sources.Count)
                throw new PopulationException("configuration", "Konfigurasjonen må ha minst én kilde, med unike kilde-ID-er.");
            foreach (var source in data.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Name))
                    throw new PopulationException("configuration", "Alle kilder må ha et visningsnavn.");
                source.Validate();
            }
            var profiles = PopulationProfileOptions.Build(data.Sources, data.PopulationProfiles);
            if (profiles.Count == 0)
                throw new PopulationException("configuration", "Konfigurasjonen må ha minst én populeringsprofil.");
            var registry = new QuestionnaireProfileRegistry(profiles, data.QuestionnaireBindings, data.RequireQuestionnaireBinding);
            return new(Guid.NewGuid().ToString("N"), data, profiles, registry, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (Exception e) when (e is ArgumentException or NullReferenceException)
        { throw new PopulationException("configuration", "Konfigurasjonen inneholder en manglende eller ugyldig verdi."); }
    }

    /// <summary>Avviser tvetydige JSON-objekter og null-elementer, også inne i ruteregler og lister.</summary>
    public static void ValidateJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new PopulationException("configuration", "Dupliserte JSON-felt er ikke tillatt i konfigurasjonen.");
                ValidateJson(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null)
                    throw new PopulationException("configuration", "Konfigurasjonslister kan ikke inneholde null-elementer.");
                ValidateJson(item);
            }
    }

    public async Task<FhirConfigurationSnapshot> SaveAsync(JsonElement configuration, string revision, CancellationToken ct)
    {
        var candidate = Compile(configuration);
        await writeLock.WaitAsync(ct);
        string? temporary = null;
        try
        {
            if (revision != Current.Revision)
                throw Conflict();
            // Også manuell redigering av lagringsfilen skal oppdages, ikke overskrives i stillhet.
            var disk = File.Exists(filePath) ? await File.ReadAllTextAsync(filePath, ct) : null;
            if (disk != persistedJson) throw Conflict();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, candidate.Json, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, overwrite: true);
            // Ingen await mellom filbytte og aktivering: avbrudd kan ikke gi halvveis oppdatering.
            persistedJson = candidate.Json;
            Volatile.Write(ref current, candidate);
            return candidate;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new PopulationException("configuration-storage", "Konfigurasjonen kunne ikke lagres. Forrige oppsett er fortsatt aktivt. Kontroller skrivetilgang til appens .local-mappe."); }
        finally
        {
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            writeLock.Release();
        }
    }

    private static PopulationException Conflict() => new("configuration-conflict",
        "Konfigurasjonen er endret siden den ble lastet. Hent siste versjon før du lagrer. Ved manuell filendring må appen startes på nytt.");
}
