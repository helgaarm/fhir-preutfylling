using Hl7.Fhir.Model;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

/// <summary>Explicitly recognized metadata and answer/branch semantics for prefill previews.</summary>
public static class QuestionnaireFeatures
{
    // These definitions remain in the submitted Q. Presentation, Helsenorge execution and
    // extraction are not performed by this preview engine; the result reports that limit.
    public static readonly HashSet<string> PreservedExtensions = new(StringComparer.Ordinal)
    {
        "http://helsenorge.no/fhir/StructureDefinition/sdf-sidebar",
        "http://helsenorge.no/fhir/StructureDefinition/sdf-information-message",
        "http://helsenorge.no/fhir/StructureDefintion/sdf-itemControl-visibility",
        "http://helsenorge.no/fhir/StructureDefinition/sdf-presentationbuttons",
        "http://helsenorge.no/fhir/StructureDefinition/sdf-save-capabilities",
        "http://helsenorge.no/fhir/StructureDefinition/sdf-sublabel",
        "http://helsenorge.no/fhir/StructureDefinition/sdf-guidanceaction",
        "http://ehelse.no/fhir/StructureDefinition/sdf-fhirpath",
        "http://ehelse.no/fhir/StructureDefinition/sdf-calculatedExpression",
        "http://ehelse.no/fhir/StructureDefinition/validationtext",
        "http://ehelse.no/fhir/StructureDefinition/sdf-maxvalue",
        "http://ehelse.no/fhir/StructureDefinition/sdf-minvalue",
        "http://ehelse.no/fhir/StructureDefinition/repeatstext",
        "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-itemExtractionContext",
        "http://hl7.org/fhir/StructureDefinition/questionnaire-hidden",
        "http://hl7.org/fhir/StructureDefinition/questionnaire-itemControl",
        "http://hl7.org/fhir/StructureDefinition/regex",
        "http://hl7.org/fhir/StructureDefinition/minValue",
        "http://hl7.org/fhir/StructureDefinition/maxValue",
        "http://hl7.org/fhir/StructureDefinition/questionnaire-minOccurs",
        "http://hl7.org/fhir/StructureDefinition/questionnaire-maxOccurs",
        "http://hl7.org/fhir/StructureDefinition/minLength",
        "http://hl7.org/fhir/StructureDefinition/questionnaire-unit",
        "http://hl7.org/fhir/StructureDefinition/maxDecimalPlaces"
    };

    public static IEnumerable<Questionnaire.ItemComponent> Flat(IEnumerable<Questionnaire.ItemComponent> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flat(i.Item)));

    public static Coding[] Options(Questionnaire q, Questionnaire.ItemComponent item)
    {
        if (item.AnswerValueSet is null) return item.AnswerOption.Select(a => a.Value).OfType<Coding>().ToArray();
        if (!item.AnswerValueSet.StartsWith('#'))
            throw new PopulationException("answer-valueset", "Testmotoren støtter bare inneholdte, eksplisitte verdilister; ingen eksterne terminologioppslag.");
        var sets = q.Contained.OfType<ValueSet>().Where(v => v.Id == item.AnswerValueSet[1..]).ToArray();
        if (sets.Length != 1 || sets[0].Compose is not { } compose || compose.Exclude.Count > 0 ||
            compose.Include.Any(i => string.IsNullOrWhiteSpace(i.System) || i.Filter.Count > 0 || i.ValueSet.Any() || i.Concept.Count == 0))
            throw new PopulationException("answer-valueset", "Verdilisten må finnes én gang i Q og ha eksplisitte koder uten filtre.");
        return compose.Include.SelectMany(i => i.Concept.Select(c => new Coding(i.System, c.Code, c.Display) { Version = i.Version })).ToArray();
    }

    public static bool IsAllowed(Questionnaire q, Questionnaire.ItemComponent item, DataType value)
    {
        if (item.Type is not (QType.Choice or QType.OpenChoice)) return true;
        if (item.Type == QType.OpenChoice && value is FhirString) return true;
        if (value is not Coding code) return false;
        var options = Options(q, item);
        return options.Length == 0 && item.AnswerValueSet is null && item.AnswerOption.Count == 0 ||
            options.Any(c => c.System == code.System && c.Code == code.Code && (c.Version is null || c.Version == code.Version));
    }

    /// <summary>Build the QR from evaluated proposals, respecting enabled ancestors and answer nesting.</summary>
    public static List<QuestionnaireResponse.ItemComponent> BuildResponse(Questionnaire q,
        IReadOnlyDictionary<string, QuestionnaireResponse.ItemComponent> proposals)
    {
        var definitions = Flat(q.Item).ToDictionary(i => i.LinkId!);
        var parents = new Dictionary<string, string>();
        void Index(IEnumerable<Questionnaire.ItemComponent> items, string? parent)
        {
            foreach (var item in items) { if (parent != null) parents[item.LinkId!] = parent; Index(item.Item, item.LinkId); }
        }
        Index(q.Item, null);
        var enabled = new Dictionary<string, bool>();
        var evaluating = new HashSet<string>();
        bool Enabled(string id)
        {
            if (enabled.TryGetValue(id, out var known)) return known;
            if (!evaluating.Add(id)) throw new PopulationException("enablewhen-cycle", "Sirkulære visningsvilkår kan ikke evalueres.");
            var item = definitions[id];
            var parentEnabled = !parents.TryGetValue(id, out var parent) || Enabled(parent) &&
                (definitions[parent].Type is QType.Group or QType.Display || proposals[parent].Answer.Count > 0);
            bool Matches(Questionnaire.EnableWhenComponent condition)
            {
                var reference = condition.Question!;
                var answers = Enabled(reference) && proposals.TryGetValue(reference, out var p)
                    ? p.Answer.Select(a => a.Value!).ToArray() : [];
                if (condition.Operator == Questionnaire.QuestionnaireItemOperator.Exists)
                    return (answers.Length > 0) == ((FhirBoolean)condition.Answer!).Value;
                if (answers.Length == 0) return false;
                bool Same(DataType actual) => actual is Coding a && condition.Answer is Coding b
                    ? a.System == b.System && a.Code == b.Code : actual.IsExactly(condition.Answer);
                return condition.Operator == Questionnaire.QuestionnaireItemOperator.Equal
                    ? answers.Any(Same) : answers.All(a => !Same(a));
            }
            var value = parentEnabled && (item.EnableWhen.Count == 0 ||
                (item.EnableBehavior == Questionnaire.EnableWhenBehavior.Any ? item.EnableWhen.Any(Matches) : item.EnableWhen.All(Matches)));
            evaluating.Remove(id); enabled[id] = value; return value;
        }
        List<QuestionnaireResponse.ItemComponent> Build(IEnumerable<Questionnaire.ItemComponent> items)
        {
            var output = new List<QuestionnaireResponse.ItemComponent>();
            foreach (var definition in items)
            {
                if (definition.Type == QType.Display || !Enabled(definition.LinkId!)) continue;
                var item = (QuestionnaireResponse.ItemComponent)proposals[definition.LinkId!].DeepCopy();
                if (definition.Type == QType.Group) item.Item = Build(definition.Item);
                else
                {
                    item.Item = [];
                    foreach (var answer in item.Answer) answer.Item = Build(definition.Item);
                }
                output.Add(item);
            }
            return output;
        }
        return Build(q.Item);
    }
}
