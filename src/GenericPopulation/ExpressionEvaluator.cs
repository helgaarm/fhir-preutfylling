using System.Text.RegularExpressions;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;

namespace GenericPopulation;

public sealed class ExpressionEvaluator
{
    // Only trusted, publication-validated Questionnaires are accepted.
    // These guards are not a sandbox for hostile arbitrary FHIRPath programs.
    public static void Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || !text.StartsWith('%'))
            throw new PopulationException("expression-policy", "Uttrykket faller utenfor demoens uttrykksprofil.");
        if (Regex.IsMatch(text, @"\b(resolve|trace)\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(text, @"%(context|resource|rootResource|questionnaire|qitem)\b"))
            throw new PopulationException("expression-policy", "Uttrykket bruker en ustøttet kontekst eller funksjon.");
    }

    public Base[] Evaluate(Base focus, string text, IReadOnlyDictionary<string, Base[]> scope)
    {
        Check(text);
        var evaluation = new FhirEvaluationContext();
        foreach (var (name, values) in scope)
            evaluation.Environment[name] = values.Select(v => v.ToPocoNode()).ToArray();
        // No resolver, trace sink or outbound network is installed in FHIRPath.
        try
        {
            return focus.Select(text, evaluation).Where(v => v is not null).Cast<Base>().ToArray();
        }
        catch (Exception e) when (e is not PopulationException)
        {
            // Never include raw SDK exceptions, expressions or patient data in the public error.
            throw new PopulationException("expression-evaluation", "FHIRPath-uttrykket kunne ikke evalueres.");
        }
    }
}
