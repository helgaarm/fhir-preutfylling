using Hl7.Fhir.Model;

namespace GenericPopulation;

// The host creates this AFTER authorization. Never bind it directly from a public request.
public sealed record PopulationContext(Patient Patient, bool PrepopulationAllowed);
public sealed record PopulationResult(QuestionnaireResponse Response, OperationOutcome Outcome);

public interface IFhirDataSource
{
    Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context,
        CancellationToken cancellationToken);
}

// Called for EVERY HTTP request, including pagination. A production implementation
// obtains the correct audience/scope and creates a new DPoP proof when required.
public interface IRequestAuthorizer
{
    SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context,
        CancellationToken cancellationToken);
}

public sealed class PopulationException(string code, string safeMessage)
    : Exception(safeMessage)
{
    public string Code { get; } = code;
}

public static class Sdc
{
    public const string Variable = "http://hl7.org/fhir/StructureDefinition/variable";
    public const string Initial =
        "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-initialExpression";
    public const string Launch =
        "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-launchContext";
}
