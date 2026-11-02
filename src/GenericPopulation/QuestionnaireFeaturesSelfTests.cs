using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

public static class QuestionnaireFeaturesSelfTests
{
    static Coding Yes() => new("urn:test:yes-no", "1", "Ja");
    static Questionnaire Form(params Questionnaire.ItemComponent[] items)
    {
        var q = DemoFiles.Questionnaire("general"); q.Item = items.ToList(); return q;
    }
    static Questionnaire.ItemComponent Question(string id, QType type, DataType? value = null) => new()
    { LinkId = id, Type = type, Initial = value is null ? [] : [new() { Value = value }] };
    static Questionnaire.EnableWhenComponent When(string id, Questionnaire.QuestionnaireItemOperator op, DataType value) => new()
    { Question = id, Operator = op, Answer = value };
    static Task<PopulationResult> Run(Questionnaire q) => new PopulationEngine(new FixtureDataSource([])).CreateAsync(q, new PopulationContext(DemoFiles.Patient(), true));
    static void Check(bool value) { if (!value) throw new InvalidOperationException("Questionnaire feature check failed"); }
    static IEnumerable<QuestionnaireResponse.ItemComponent> Flat(IEnumerable<QuestionnaireResponse.ItemComponent> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flat(i.Item)).Concat(i.Answer.SelectMany(a => Flat(a.Item))));
    public static IEnumerable<(string, Func<SysTask>)> Cases()
    {
        yield return ("Originale kodede valg og underspørsmål bevares i answer.item", async () =>
        {
            var parent = Question("parent", QType.Choice, Yes());
            parent.AnswerOption = [new() { Value = Yes() }];
            var child = Question("child", QType.String, new FhirString("metadata"));
            child.EnableWhen = [When("parent", Questionnaire.QuestionnaireItemOperator.Equal, new Coding("urn:test:yes-no", "1"))];
            parent.Item = [child];
            var q = Form(parent); var before = new FhirJsonSerializer().SerializeToString(q);
            var result = await Run(q);
            Check(result.Response.Item[0].Item.Count == 0 && result.Response.Item[0].Answer[0].Item.Single().LinkId == "child");
            Check(before == new FhirJsonSerializer().SerializeToString(q));
        });
        yield return ("Kodet flervalg bevarer begge svar og metadata under hvert svar", async () =>
        {
            var item = Question("multi", QType.Choice); item.Repeats = true;
            item.AnswerOption = [new() { Value = new Coding("urn:test", "a", "A") }, new() { Value = new Coding("urn:test", "b", "B") }];
            item.Extension = [new(Sdc.Initial, new Expression { Language = "text/fhirpath", Expression_ = "%qitem.answerOption.value.ofType(Coding)" })];
            item.Item = [Question("metadata", QType.String, new FhirString("synthetic"))];
            var followUp = Question("follow-up", QType.String, new FhirString("shown"));
            followUp.EnableWhen = [When("multi", Questionnaire.QuestionnaireItemOperator.Equal, new Coding("urn:test", "b"))];
            var q = Form(item, followUp);
            var result = await Run(q); var response = result.Response.Item[0];
            Check(response.Answer.Select(a => ((Coding)a.Value!).Code).SequenceEqual(["a", "b"]));
            Check(response.Item.Count == 0 && response.Answer.All(a => a.Item.Single().LinkId == "metadata"));
            Check(result.Response.Item.Any(i => i.LinkId == "follow-up"));
            item.Repeats = false;
            var single = await Run(q);
            Check(single.Response.Item.Single().Answer.Count == 0);
            Check(single.Outcome.Issue.Any(i => i.Diagnostics == "linkId=multi"));
        });
        yield return ("Inneholdt verdiliste bevares og ukjente svarkoder avvises", async () =>
        {
            var item = Question("choice", QType.Choice, Yes()); item.AnswerValueSet = "#yes-no";
            var q = Form(item);
            q.Contained = [new ValueSet { Id = "yes-no", Status = PublicationStatus.Draft,
                Compose = new() { Include = [new() { System = "urn:test:yes-no", Concept = [new() { Code = "1", Display = "Ja" }] }] } }];
            Check((await Run(q)).Response.Item.Single().Answer.Count == 1);
            item.Initial[0].Value = new Coding("urn:test:yes-no", "unknown");
            var invalid = await Run(q); Check(invalid.Response.Item.Single().Answer.Count == 0);
            Check(invalid.Outcome.Issue.Any(i => i.Severity == OperationOutcome.IssueSeverity.Warning));
            item.AnswerValueSet = "https://example.org/external";
            try { await Run(q); throw new InvalidOperationException(); }
            catch (PopulationException e) when (e.Code == "answer-valueset") { }
        });
        yield return ("Visningsvilkår håndterer fremoverreferanse, manglende svar og deaktivert forelder", async () =>
        {
            var branch = Question("branch", QType.Group);
            branch.EnableWhen = [When("switch", Questionnaire.QuestionnaireItemOperator.Equal, new FhirBoolean(true))];
            branch.Item = [Question("inside", QType.String, new FhirString("value"))];
            var switched = Question("switch", QType.Boolean, new FhirBoolean(false));
            var noAnswer = Question("no-answer", QType.String);
            var unequal = Question("unequal", QType.String, new FhirString("must not appear"));
            unequal.EnableWhen = [When("no-answer", Questionnaire.QuestionnaireItemOperator.NotEqual, new FhirString("x"))];
            var q = Form(branch, switched, noAnswer, unequal);
            Check(Flat((await Run(q)).Response.Item).All(i => i.LinkId is not ("branch" or "inside" or "unequal")));
            switched.Initial[0].Value = new FhirBoolean(true);
            Check(Flat((await Run(q)).Response.Item).Any(i => i.LinkId == "inside"));
        });
        yield return ("Visningsvilkår any, all og exists bevarer false som svar", async () =>
        {
            var a = Question("a", QType.Boolean, new FhirBoolean(false));
            var target = Question("target", QType.String, new FhirString("shown"));
            target.EnableWhen = [When("a", Questionnaire.QuestionnaireItemOperator.Exists, new FhirBoolean(true)), When("a", Questionnaire.QuestionnaireItemOperator.Equal, new FhirBoolean(true))];
            target.EnableBehavior = Questionnaire.EnableWhenBehavior.Any; var q = Form(a, target);
            Check((await Run(q)).Response.Item.Count == 2);
            target.EnableBehavior = Questionnaire.EnableWhenBehavior.All; Check((await Run(q)).Response.Item.Count == 1);
        });
        yield return ("Sirkulære visningsvilkår gir kontrollert feil", async () =>
        {
            var a = Question("a", QType.Boolean, new FhirBoolean(true)); var b = Question("b", QType.Boolean, new FhirBoolean(true));
            a.EnableWhen = [When("b", Questionnaire.QuestionnaireItemOperator.Equal, new FhirBoolean(true))];
            b.EnableWhen = [When("a", Questionnaire.QuestionnaireItemOperator.Equal, new FhirBoolean(true))];
            try { await Run(Form(a, b)); throw new InvalidOperationException(); }
            catch (PopulationException e) when (e.Code == "enablewhen-cycle") { }
        });
        yield return ("SDC-kontekst gir originale Coding-svar og typede beregnede datoer/heltall", async () =>
        {
            var choice = Question("choice", QType.Choice); choice.AnswerOption = [new() { Value = Yes() }];
            choice.Extension = [new(Sdc.Initial, new Expression { Language = "text/fhirpath", Expression_ = "%qitem.answerOption.value.ofType(Coding).where(%context.answer.empty())" })];
            var date = Question("date", QType.Date); date.Extension = [new(Sdc.Initial, new Expression { Language = "text/fhirpath", Expression_ = "%patient.iif(true, @2026-10-01, {})" })];
            var integer = Question("integer", QType.Integer); integer.Extension = [new(Sdc.Initial, new Expression { Language = "text/fhirpath", Expression_ = "%patient.iif(true, 100, {})" })];
            var result = await Run(Form(choice, date, integer));
            Check(result.Response.Item[0].Answer[0].Value is Coding { Code: "1" });
            Check(result.Response.Item[1].Answer[0].Value is Date { Value: "2026-10-01" });
            Check(result.Response.Item[2].Answer[0].Value is Integer { Value: 100 });
        });
        yield return ("Helsenorge-regler endres ikke eller utføres stille", async () =>
        {
            var item = Question("legacy", QType.String);
            item.Extension = [new("http://ehelse.no/fhir/StructureDefinition/sdf-fhirpath", new FhirString("Practitioner.name"))];
            var q = Form(item); var result = await Run(q);
            Check(result.Response.Item.Single().Answer.Count == 0 && item.Extension.Count == 1);
            Check(result.Outcome.Issue.Any(i => i.Severity == OperationOutcome.IssueSeverity.Warning && i.Details?.Text?.Contains("Helsenorge") == true));
        });
        yield return ("Større skjema har eksplisitt øvre grense", () =>
        {
            var q = Form(Enumerable.Range(0, 500).Select(i => Question("q" + i, QType.String)).ToArray());
            QuestionnaireGuard.Validate(q); q.Item.Add(Question("too-many", QType.String));
            try { QuestionnaireGuard.Validate(q); throw new InvalidOperationException(); }
            catch (PopulationException e) when (e.Code == "linkid") { }
            return SysTask.CompletedTask;
        });
    }
}
