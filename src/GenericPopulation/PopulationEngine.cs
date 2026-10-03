using Hl7.Fhir.Model;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

public sealed class PopulationEngine(IFhirDataSource source)
{
    private readonly ExpressionEvaluator evaluator = new();

    // Creates a NEW QR only. An existing/partly completed QR is deliberately not an input.
    public async Task<PopulationResult> CreateAsync(Questionnaire q, PopulationContext context,
        CancellationToken cancellationToken = default)
    {
        QuestionnaireGuard.Validate(q);
        if (string.IsNullOrWhiteSpace(context.Patient.Id))
            throw new PopulationException("patient-context", "Pasientkontekst mangler.");
        var qr = new QuestionnaireResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            Questionnaire = q.Url + "|" + q.Version,
            Status = QuestionnaireResponse.QuestionnaireResponseStatus.InProgress,
            Subject = new ResourceReference("Patient/" + context.Patient.Id),
            Authored = DateTimeOffset.UtcNow.ToString("o")
        };
        var outcome = new OperationOutcome();
        if (!context.PrepopulationAllowed)
        {
            qr.Item = Skeleton(q.Item);
            Warn(outcome, null, "Preutfylling ble ikke utført. Skjemaet kan fylles ut manuelt.");
            return new(qr, outcome);
        }
        var scope = new Dictionary<string, Base[]>
        {
            ["patient"] = [(Patient)context.Patient.DeepCopy()]
        };
        // Request-local only: never reuse this cache across patients, tenants or authorization contexts.
        var cache = new Dictionary<string, Bundle>(StringComparer.Ordinal);
        scope = await BindAsync(q.Extension, qr, scope, context, cache, outcome, cancellationToken);
        qr.Item = await PopulateItemsAsync(q.Item, scope, context, cache, outcome, cancellationToken);
        if (outcome.Issue.Count == 0)
            outcome.Issue.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Information,
                Code = OperationOutcome.IssueType.Informational,
                Details = new CodeableConcept { Text = "Preutfyllingen er gjennomført. Verdiene må kontrolleres av utfylleren." }
            });
        return new(qr, outcome);
    }

    private async Task<Dictionary<string, Base[]>> BindAsync(IEnumerable<Extension> extensions,
        Base focus, Dictionary<string, Base[]> parent, PopulationContext context,
        Dictionary<string, Bundle> cache, OperationOutcome outcome, CancellationToken ct)
    {
        var scope = new Dictionary<string, Base[]>(parent);
        // Extension order matters: a variable may use variables declared before it.
        foreach (var ext in extensions.Where(e => e.Url == Sdc.Variable))
        {
            ct.ThrowIfCancellationRequested();
            var expression = (Expression)ext.Value!;
            Base[] values;
            if (expression.Language == "application/x-fhir-query")
            {
                var query = FhirSearch.FromTemplate(expression.Expression_!, context);
                if (!cache.TryGetValue(query.RelativeUrl, out var bundle))
                {
                    bundle = await source.SearchAsync(query, context, ct);
                    if (bundle.Type != Bundle.BundleType.Searchset)
                        throw new PopulationException("source-contract", "Datakilden returnerte ikke et searchset Bundle.");
                    foreach (var entry in bundle.Entry)
                    {
                        if (entry.Resource is OperationOutcome sourceOutcome)
                        {
                            if (sourceOutcome.Issue.Any(i => i.Severity is
                                OperationOutcome.IssueSeverity.Error or OperationOutcome.IssueSeverity.Fatal))
                                throw new PopulationException("source-outcome", "Datakilden meldte feil under søket.");
                            if (sourceOutcome.Issue.Count > 0)
                                Warn(outcome, null, "Datakilden returnerte en merknad; resultatet må kontrolleres.");
                        }
                    }
                    cache[query.RelativeUrl] = bundle;
                }
                // x-fhir-query binds a Bundle, not just entry.resource.
                values = [bundle];
            }
            else values = evaluator.Evaluate(focus, expression.Expression_!, scope);
            scope[expression.Name!] = values;
        }
        return scope;
    }

    private async Task<List<QuestionnaireResponse.ItemComponent>> PopulateItemsAsync(
        IEnumerable<Questionnaire.ItemComponent> questions, Dictionary<string, Base[]> parent,
        PopulationContext context, Dictionary<string, Bundle> cache,
        OperationOutcome outcome, CancellationToken ct)
    {
        var items = new List<QuestionnaireResponse.ItemComponent>();
        foreach (var question in questions)
        {
            ct.ThrowIfCancellationRequested();
            if (question.Type == QType.Display) continue;
            var item = new QuestionnaireResponse.ItemComponent
            {
                LinkId = question.LinkId, Text = question.Text, Definition = question.Definition
            };
            var scope = await BindAsync(question.Extension, item, parent, context, cache, outcome, ct);
            if (question.Type == QType.Group)
            {
                item.Item = await PopulateItemsAsync(question.Item, scope, context, cache, outcome, ct);
            }
            else
            {
                var expression = question.Extension.SingleOrDefault(e => e.Url == Sdc.Initial)?.Value as Expression;
                var values = expression is null
                    ? question.Initial.Select(i => (Base)i.Value!).ToArray()
                    : evaluator.Evaluate(item, expression.Expression_!, scope);
                if (question.Repeats != true && values.Length > 1)
                    Warn(outcome, question.LinkId, "Flere likeverdige treff. Feltet er ikke preutfylt.");
                else
                {
                    try
                    {
                        // Materialize first: a conversion failure must not leave half an answer collection.
                        var mapped = values.Select(v => AnswerMapper.Map(question.Type, v)).ToArray();
                        item.Answer = mapped.Select(v =>
                            new QuestionnaireResponse.AnswerComponent { Value = v }).ToList();
                    }
                    catch (PopulationException e) when (e.Code == "answer-type")
                    {
                        Warn(outcome, question.LinkId, "Kildeverdien har feil datatype eller enhet. Feltet er ikke preutfylt.");
                    }
                }
            }
            items.Add(item);
        }
        return items;
    }

    private static List<QuestionnaireResponse.ItemComponent> Skeleton(
        IEnumerable<Questionnaire.ItemComponent> questions) => questions
        .Where(q => q.Type != QType.Display)
        .Select(q => new QuestionnaireResponse.ItemComponent
        {
            LinkId = q.LinkId, Text = q.Text, Definition = q.Definition,
            Item = q.Type == QType.Group ? Skeleton(q.Item) : []
        }).ToList();

    private static void Warn(OperationOutcome outcome, string? linkId, string message) =>
        outcome.Issue.Add(new OperationOutcome.IssueComponent
        {
            Severity = OperationOutcome.IssueSeverity.Warning,
            Code = OperationOutcome.IssueType.Processing,
            Details = new CodeableConcept { Text = message },
            // linkId is form metadata, not an FHIRPath issue.expression.
            Diagnostics = linkId is null ? null : "linkId=" + linkId
        });
}
