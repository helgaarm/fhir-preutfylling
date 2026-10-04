using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>Regresjoner for skjemaversjon som ytterste nivå i kildekonfigurasjonen.</summary>
internal static class QuestionnaireRoutingSelfTests
{
    private static readonly PopulationProfileOptions[] Profiles = [new() { Id = "a" }, new() { Id = "b" }];
    private static readonly QuestionnaireBindingOptions[] Bindings = [
        new() { Questionnaire = "https://example.org/q", Version = "1.0", ProfileId = "a" },
        new() { Questionnaire = "https://example.org/q", Version = "2.0", ProfileId = "b" }];
    private static Questionnaire Q(string version = "1.0", string url = "https://example.org/q") => new() { Url = url, Version = version };

    public static IEnumerable<(string, Func<SysTask>)> Cases() =>
    [
        ("Skjemaruting: samme URL kan bruke ulike profiler per eksakt versjon", () =>
        {
            var registry = new QuestionnaireProfileRegistry(Profiles, Bindings);
            Check(registry.Resolve(Q(), null).Id == "a" && registry.Resolve(Q("2.0"), null).Id == "b", "version selects profile");
            Check(registry.Resolve(Q(), "a").Id == "a", "matching explicit choice accepted");
            return SysTask.CompletedTask;
        }),
        ("Skjemaruting: ukjent versjon og overstyring gir aldri reserveprofil", () =>
        {
            var registry = new QuestionnaireProfileRegistry(Profiles, Bindings);
            foreach (var version in new[] { "3.0", "1.0.0", "1.0 " })
                Expect("questionnaire-routing", () => registry.Resolve(Q(version), "a"));
            Expect("questionnaire-routing", () => registry.Resolve(Q(), "b"));
            Expect("questionnaire-routing", () => registry.Resolve(Q("2.0"), "a"));
            return SysTask.CompletedTask;
        }),
        ("Skjemaruting: manuell testprofil gjelder bare uregistrerte skjemaer", () =>
        {
            var registry = new QuestionnaireProfileRegistry(Profiles, Bindings);
            Check(registry.Resolve(Q(url: "https://example.org/other"), "b").Id == "b", "manual test fallback");
            Expect("questionnaire-routing", () => registry.Resolve(Q(url: "https://example.org/other"), null));
            Expect("source-id", () => registry.Resolve(Q(url: "https://example.org/other"), "missing"));
            Expect("questionnaire-routing", () => registry.Resolve(Q(url: "https://example.org/Q"), null));
            var strict = new QuestionnaireProfileRegistry(Profiles, Bindings, true);
            Expect("questionnaire-routing", () => strict.Resolve(Q(url: "https://example.org/other"), "a"));
            Check(strict.Resolve(Q(), null).Id == "a", "registered versions work in strict mode");
            return SysTask.CompletedTask;
        }),
        ("Skjemaruting: duplikater, feil canonical og ukjente profiler avvises ved oppstart", () =>
        {
            Expect("configuration", () => new QuestionnaireProfileRegistry(Profiles, [Bindings[0], Bindings[0]]));
            foreach (var binding in new QuestionnaireBindingOptions[] {
                new() { Questionnaire = "https://example.org/q", Version = "1.0", ProfileId = "missing" },
                new() { Questionnaire = "relative", Version = "1.0", ProfileId = "a" },
                new() { Questionnaire = "https://example.org/q|1.0", Version = "1.0", ProfileId = "a" },
                new() { Questionnaire = "https://example.org/q", ProfileId = "a" }
            }) Expect("configuration", () => new QuestionnaireProfileRegistry(Profiles, [binding]));
            return SysTask.CompletedTask;
        })
    ];

    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Expect(string code, Action action)
    { try { action(); } catch (PopulationException e) when (e.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
}
