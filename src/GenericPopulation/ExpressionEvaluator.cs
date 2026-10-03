using System.Text.RegularExpressions;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Samler Firely SDKs FHIRPath-evaluering og variabelbinding på ett sted.
/// Uttrykk leser allerede innhentede ressurser; nettverksoppslag håndteres av datakilden.
/// </summary>
public sealed class ExpressionEvaluator
{
    /// <summary>
    /// Avviser uttrykk utenfor demoens profil. Q må være betrodd og validert ved publisering;
    /// disse kontrollene utgjør ikke en sandkasse for vilkårlige, fiendtlige FHIRPath-programmer.
    /// </summary>
    public static void Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || !text.StartsWith('%'))
            throw new PopulationException("expression-policy", "Uttrykket faller utenfor demoens uttrykksprofil.");
        if (Regex.IsMatch(text, @"\b(resolve|trace)\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(text, @"%(context|resource|rootResource|questionnaire|qitem)\b"))
            throw new PopulationException("expression-policy", "Uttrykket bruker en ustøttet kontekst eller funksjon.");
    }

    /// <summary>
    /// Evaluerer mot et fokusobjekt med navngitte variabler (for eksempel %patient).
    /// Resultatet er en samling: tomt betyr ingen verdi, flere elementer kan kreve et gjentatt svar.
    /// </summary>
    public Base[] Evaluate(Base focus, string text, IReadOnlyDictionary<string, Base[]> scope)
    {
        Check(text);
        var evaluation = new FhirEvaluationContext();
        foreach (var (name, values) in scope)
            evaluation.Environment[name] = values.Select(v => v.ToPocoNode()).ToArray();
        // Ingen resolver eller trace-mottaker er installert; evalueringen skal ikke hente eller logge data.
        try
        {
            return focus.Select(text, evaluation).Where(v => v is not null).Cast<Base>().ToArray();
        }
        catch (Exception e) when (e is not PopulationException)
        {
            // SDK-feilen kan inneholde uttrykk eller pasientverdier og skal ikke vises direkte.
            throw new PopulationException("expression-evaluation", "FHIRPath-uttrykket kunne ikke evalueres.");
        }
    }
}
