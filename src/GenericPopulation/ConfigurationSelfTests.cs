using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenericPopulation;

/// <summary>Konfigurasjonslagring testes i egne tempmapper, uten nettverk eller endring av appens oppsett.</summary>
internal static class ConfigurationSelfTests
{
    private static FhirConfiguration Initial() => new()
    {
        Sources = [new() { Id = "central", Name = "Sentral kilde", BaseUrl = "https://example.org/fhir/" }],
        QuestionnaireBindings = [new() { Questionnaire = "urn:test:questionnaire", Version = "1", ProfileId = "central" }]
    };
    private static JsonElement Json(FhirConfiguration value) => JsonSerializer.SerializeToElement(value, FhirConfigurationStore.JsonOptions);
    private static FhirConfiguration Changed() { var value = Initial(); value.Sources[0].Name = "Endret kilde"; return value; }

    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("Konfigurasjon: validering isolerer utkast og endrer ikke aktivt oppsett", () =>
        {
            using var folder = new TestFolder();
            var initial = Initial();
            var store = new FhirConfigurationStore(folder.Path, initial);
            var old = store.Current;
            initial.Sources[0].Name = "Endret utenfra";
            var candidate = FhirConfigurationStore.Compile(Json(Changed()));
            Check(old.Configuration.Sources[0].Name == "Sentral kilde" && candidate.Configuration.Sources[0].Name == "Endret kilde", "detached draft");
            Check(ReferenceEquals(old, store.Current) && !File.Exists(folder.File), "validation is read-only");
            return SysTask.CompletedTask;
        }),
        ("Konfigurasjon: ugyldige typer, null, duplikater og brutte referanser avvises", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            var old = store.Current;
            var mutations = new Action<JsonNode>[]
            {
                n => n["Sources"] = null,
                n => n["Sources"]!.AsArray().Add((JsonNode?)null),
                n => n["Sources"]![0]!["PatientLookup"] = null,
                n => n["Sources"]![0]!["Capabilities"]!["Resources"]!["Observation"] = null,
                n => n["Sources"]![0]!["SearchMethod"] = "DELETE",
                n => n["Sources"]![0]!["BaseUrl"] = "http://external.example/fhir/",
                n => n["Sources"]![0]!["Name"] = "",
                n => n["Sources"]![0]!["UnknownSetting"] = true,
                n => n["QuestionnaireBindings"]![0]!["ProfileId"] = "missing",
                n => n["QuestionnaireBindings"]!.AsArray().Add(n["QuestionnaireBindings"]![0]!.DeepClone())
            };
            foreach (var mutate in mutations)
            {
                var draft = JsonNode.Parse(old.Json)!; mutate(draft);
                await Expect("configuration", () => store.SaveAsync(JsonSerializer.SerializeToElement(draft), old.Revision, default));
            }
            using var duplicate = JsonDocument.Parse("{\"Sources\":[],\"Sources\":[]}");
            await Expect("configuration", () => store.SaveAsync(duplicate.RootElement, old.Revision, default));
            Check(ReferenceEquals(old, store.Current) && !File.Exists(folder.File), "all failures retain active settings and disk");
        }),
        ("Konfigurasjon: lagring aktiverer nytt oppsett og beholdes etter omstart", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            var old = store.Current;
            var saved = await store.SaveAsync(Json(Changed()), old.Revision, default);
            Check(saved.Revision != old.Revision && ReferenceEquals(saved, store.Current), "new active revision");
            Check(old.Configuration.Sources[0].Name == "Sentral kilde", "in-flight snapshot stays unchanged");
            var restarted = new FhirConfigurationStore(folder.Path, Initial());
            Check(restarted.Current.Configuration.Sources[0].Name == "Endret kilde", "persisted settings override defaults");
            Check(Directory.GetFiles(System.IO.Path.GetDirectoryName(folder.File)!).Length == 1, "no temporary files left");
        }),
        ("Konfigurasjon: samtidige faner kan ikke overskrive samme revisjon", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            var revision = store.Current.Revision;
            async Task<string> Attempt()
            {
                try { await store.SaveAsync(Json(Changed()), revision, default); return "saved"; }
                catch (PopulationException e) { return e.Code; }
            }
            var results = await Task.WhenAll(Attempt(), Attempt());
            Check(results.Count(r => r == "saved") == 1 && results.Count(r => r == "configuration-conflict") == 1, "exactly one writer succeeds");
            var savedJson = File.ReadAllText(folder.File);
            await Expect("configuration-conflict", () => store.SaveAsync(Json(Initial()), revision, default));
            Check(File.ReadAllText(folder.File) == savedJson, "stale writer cannot change file");
        }),
        ("Konfigurasjon: manuell filendring gir konflikt uten overskriving", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            await store.SaveAsync(Json(Initial()), store.Current.Revision, default);
            var old = store.Current;
            var external = JsonSerializer.Serialize(Changed(), FhirConfigurationStore.JsonOptions);
            File.WriteAllText(folder.File, external);
            await Expect("configuration-conflict", () => store.SaveAsync(Json(Initial()), old.Revision, default));
            Check(File.ReadAllText(folder.File) == external && ReferenceEquals(old, store.Current), "manual change preserved");
        }),
        ("Konfigurasjon: skrivefeil beholder aktivt oppsett", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            var old = store.Current;
            File.WriteAllText(System.IO.Path.Combine(folder.Path, ".local"), "blocks directory creation");
            await Expect("configuration-storage", () => store.SaveAsync(Json(Changed()), old.Revision, default));
            Check(ReferenceEquals(old, store.Current), "write failure does not activate draft");
        }),
        ("Konfigurasjon: avbrutt lagring endrer verken disk eller aktivt oppsett", async () =>
        {
            using var folder = new TestFolder();
            var store = new FhirConfigurationStore(folder.Path, Initial());
            var old = store.Current;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await store.SaveAsync(Json(Changed()), old.Revision, cancelled.Token); throw new InvalidOperationException("Expected cancellation"); }
            catch (OperationCanceledException) { }
            Check(ReferenceEquals(old, store.Current) && !File.Exists(folder.File), "cancelled save is inert");
        })
    ];

    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static async SysTask Expect(string code, Func<SysTask> action)
    { try { await action(); } catch (PopulationException e) when (e.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }

    private sealed class TestFolder : IDisposable
    {
        private readonly string tempRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string Path { get; }
        public string File => System.IO.Path.Combine(Path, FhirConfigurationStore.RelativeFile);
        public TestFolder()
        {
            Path = System.IO.Path.Combine(tempRoot, "fhir-config-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            // Slett bare denne testens egen mappe, aldri en sti hentet fra konfigurasjon eller input.
            if (!System.IO.Path.GetFullPath(Path).StartsWith(System.IO.Path.Combine(tempRoot, "fhir-config-test-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test cleanup path");
            Directory.Delete(Path, recursive: true);
        }
    }
}
