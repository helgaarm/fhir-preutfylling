using System.Net.Http.Headers;
using Hl7.Fhir.Model;

namespace GenericPopulation;

// URLs and credential bindings belong to server configuration, never the Questionnaire.
public sealed class FhirSourceOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? BearerTokenEnvironmentVariable { get; set; }

    public Uri Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !uri.AbsolutePath.EndsWith('/') || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new PopulationException("configuration", "FHIR-kilden må ha en ID og en HTTPS-base med avsluttende skråstrek (HTTP tillates lokalt).");
        return uri;
    }
}

public sealed class ConfiguredAuthorizer(FhirSourceOptions options) : IRequestAuthorizer
{
    public SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.PrepopulationAllowed)
            throw new PopulationException("authorization", "Preutfylling er ikke tillatt i denne konteksten.");
        if (options.BearerTokenEnvironmentVariable is { Length: > 0 } variable)
        {
            var token = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
                throw new PopulationException("configuration", "FHIR-kildens token mangler eller har ugyldig format. Kontroller miljøvariabelen på serveren.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return SysTask.CompletedTask;
    }
}
