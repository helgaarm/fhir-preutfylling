using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Kobler hele Questionnaire-versjonen til en serverstyrt populeringsprofil. Dette nivået
/// velger endepunktene før sentral Patient hentes; profilens ruteregler velger kilde per søk.
/// </summary>
public sealed class QuestionnaireBindingOptions
{
    public string Questionnaire { get; set; } = "";
    public string Version { get; set; } = "";
    public string ProfileId { get; set; } = "";
}

/// <summary>Eksakt URL-/versjonsoppslag uten nyeste-versjon-fallback eller overstyring fra nettleseren.</summary>
public sealed class QuestionnaireProfileRegistry
{
    private readonly IReadOnlyList<PopulationProfileOptions> profiles;
    private readonly IReadOnlyList<QuestionnaireBindingOptions> bindings;
    private readonly bool requireBinding;

    public QuestionnaireProfileRegistry(IReadOnlyList<PopulationProfileOptions> profiles,
        IReadOnlyList<QuestionnaireBindingOptions> bindings, bool requireBinding = false)
    {
        this.profiles = profiles;
        this.bindings = bindings;
        this.requireBinding = requireBinding;
        if (bindings.Any(b => !Uri.TryCreate(b.Questionnaire, UriKind.Absolute, out _) ||
                b.Questionnaire.Contains('|') || b.Questionnaire.Contains('#') ||
                string.IsNullOrWhiteSpace(b.Version) || b.Version != b.Version.Trim() ||
                !profiles.Any(p => p.Id == b.ProfileId)) ||
            bindings.GroupBy(b => (b.Questionnaire, b.Version)).Any(g => g.Count() != 1))
            throw new PopulationException("configuration", "Skjemakoblinger krever unik Questionnaire-URL og versjon samt en kjent ProfileId.");
    }

    /// <summary>
    /// Registrerte skjemaer krever en kjent versjon og kan ikke bruke en annen profil. Valgfri
    /// utviklerfallback gjelder bare helt uregistrerte URL-er og krever eksplisitt profilvalg.
    /// </summary>
    public PopulationProfileOptions Resolve(Questionnaire questionnaire, string? requestedProfileId)
    {
        var known = bindings.Where(b => b.Questionnaire == questionnaire.Url).ToArray();
        var binding = known.SingleOrDefault(b => b.Version == questionnaire.Version);
        if (binding is not null)
        {
            if (requestedProfileId is not null && requestedProfileId != binding.ProfileId)
                throw new PopulationException("questionnaire-routing", "Valgt profil stemmer ikke med endepunktene konfigurert for denne Questionnaire-versjonen.");
            return profiles.Single(p => p.Id == binding.ProfileId);
        }
        if (known.Length > 0 || requireBinding)
            throw new PopulationException("questionnaire-routing", "Questionnaire-URL og eksakt versjon må være registrert før preutfylling.");
        if (requestedProfileId is null)
            throw new PopulationException("questionnaire-routing", "Skjemaet mangler kildekonfigurasjon. Registrer Questionnaire-versjonen eller velg en profil for testskjemaet.");
        return profiles.SingleOrDefault(p => p.Id == requestedProfileId)
            ?? throw new PopulationException("source-id", "Velg en profil som er konfigurert i appsettings.json.");
    }
}
