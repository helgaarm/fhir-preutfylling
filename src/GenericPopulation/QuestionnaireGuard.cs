using System.Text.RegularExpressions;
using Hl7.Fhir.Model;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

/// <summary>
/// Avgrenser hvilke skjemaegenskaper motoren kan håndtere. Dette er appens støttede delmengde
/// av FHIR/SDC, ikke en full FHIR-validator; ustøttede funksjoner avvises fremfor å ignoreres.
/// </summary>
public static class QuestionnaireGuard
{
    private static readonly HashSet<QType> Supported =
    [QType.Group, QType.Display, QType.Boolean, QType.Integer, QType.Decimal,
     QType.Date, QType.DateTime, QType.Time, QType.String, QType.Text, QType.Url, QType.Quantity, QType.Choice, QType.OpenChoice];

    /// <summary>Kontrollerer versjon, pasientkontekst, struktur og uttrykk før Q behandles.</summary>
    public static void Validate(Questionnaire q)
    {
        if (string.IsNullOrWhiteSpace(q.Url) || string.IsNullOrWhiteSpace(q.Version))
            throw new PopulationException("questionnaire-version", "Q må ha canonical URL og eksplisitt versjon.");
        if (q.Status != PublicationStatus.Active || q.ModifierExtension.Count != 0)
            throw new PopulationException("questionnaire-profile", "Q er ikke aktiv eller har ustøttede modifierExtensions.");
        CheckExtensions(q.Extension, true);
        var launches = q.Extension.Where(e => e.Url == Sdc.Launch).ToArray();
        if (launches.Length != 1 ||
            launches[0].Extension.Count(e => e.Url == "name") != 1 ||
            launches[0].Extension.Count(e => e.Url == "type") != 1 ||
            launches[0].Extension.SingleOrDefault(e => e.Url == "name")?.Value is not Coding { Code: "patient" } ||
            launches[0].Extension.SingleOrDefault(e => e.Url == "type")?.Value is not Code { Value: "Patient" })
            throw new PopulationException("launch-context", "Demoen krever én launchContext med navn patient og type Patient.");
        var ids = new HashSet<string>();
        Visit(q.Item, ids, 0);
        foreach (var item in QuestionnaireFeatures.Flat(q.Item))
        {
            if (item.AnswerValueSet != null) _ = QuestionnaireFeatures.Options(q, item);
            if (item.EnableWhen.Count > 1 && item.EnableBehavior is null)
                throw new PopulationException("enablewhen", "Flere visningsvilkår krever enableBehavior.");
            foreach (var condition in item.EnableWhen)
                if (condition.Question is null || !ids.Contains(condition.Question) || condition.Question == item.LinkId ||
                    condition.Operator is not (Questionnaire.QuestionnaireItemOperator.Equal or Questionnaire.QuestionnaireItemOperator.NotEqual or Questionnaire.QuestionnaireItemOperator.Exists) ||
                    condition.Answer is null || condition.Operator == Questionnaire.QuestionnaireItemOperator.Exists && condition.Answer is not FhirBoolean { Value: not null })
                    throw new PopulationException("enablewhen", "Ustøttet eller ugyldig visningsvilkår.");
        }
    }

    // Felles ID-sett for hele treet sikrer entydig kobling til QR; grenser på dybde og antall
    // beskytter rekursjonen og gjør arbeidsmengden per skjema begrenset.
    private static void Visit(IEnumerable<Questionnaire.ItemComponent> items, HashSet<string> ids, int depth)
    {
        if (depth > 12) throw new PopulationException("questionnaire-size", "For dypt Q-hierarki.");
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.LinkId) || !ids.Add(item.LinkId) || ids.Count > 500)
                throw new PopulationException("linkid", "Q har ugyldige eller dupliserte linkId-er.");
            if (item.Type is null || !Supported.Contains(item.Type.Value) ||
                item.ModifierExtension.Count != 0 || item.AnswerOption.Count != 0 && item.AnswerValueSet is not null)
                throw new PopulationException("unsupported-feature", "Q bruker funksjoner utenfor demoens støttede profil.");
            CheckExtensions(item.Extension, false);
            var initial = item.Extension.Where(e => e.Url == Sdc.Initial).ToArray();
            if (initial.Length > 1 || (initial.Length != 0 && item.Initial.Count != 0) ||
                ((item.Type == QType.Group || item.Type == QType.Display) &&
                 (initial.Length != 0 || item.Initial.Count != 0)))
                throw new PopulationException("initial-conflict", "Ugyldig kombinasjon av initialverdier.");
            Visit(item.Item, ids, depth + 1);
        }
    }

    // Roten kan etablere launchContext og variabler; spørsmål kan også ha initialExpression.
    // Variabelnavn er lokale for nivået, men vertens reserverte kontekst kan ikke overskrives.
    private static void CheckExtensions(IEnumerable<Extension> extensions, bool root)
    {
        var localNames = new HashSet<string>();
        foreach (var ext in extensions)
        {
            if (root && ext.Url == Sdc.Launch) continue;
            if (ext.Url != null && QuestionnaireFeatures.PreservedExtensions.Contains(ext.Url)) continue;
            if (ext.Url != Sdc.Variable && (root || ext.Url != Sdc.Initial))
                throw new PopulationException("unsupported-extension", "Q inneholder en extension demoen ikke støtter.");
            if (ext.Value is not Expression expression || string.IsNullOrWhiteSpace(expression.Expression_) ||
                expression.Reference is not null)
                throw new PopulationException("expression-profile", "Det kreves et inline Expression-uttrykk.");
            if (ext.Url == Sdc.Variable)
            {
                if (string.IsNullOrWhiteSpace(expression.Name) ||
                    !Regex.IsMatch(expression.Name, @"\A[A-Za-z][A-Za-z0-9_]*\z") ||
                    !localNames.Add(expression.Name) ||
                    new[] { "context", "resource", "rootResource", "questionnaire", "qitem", "ucum" }.Contains(expression.Name) ||
                    expression.Name == "patient")
                    throw new PopulationException("variable-name", "Variabelnavnet er ugyldig, reservert eller duplisert.");
                if (expression.Language is not ("text/fhirpath" or "application/x-fhir-query"))
                    throw new PopulationException("expression-language", "Ustøttet uttrykksspråk.");
            }
            else if (expression.Language != "text/fhirpath" || expression.Name is not null)
                throw new PopulationException("initial-profile", "initialExpression må være navnløs FHIRPath i demoen.");
            if (expression.Language == "text/fhirpath")
                ExpressionEvaluator.Check(expression.Expression_);
        }
    }
}
