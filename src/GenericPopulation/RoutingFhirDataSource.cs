using Hl7.Fhir.Model;

namespace GenericPopulation;

// This is a SERVER-SIDE registry, never an endpoint list supplied by an end user.
// Logical data selectors may be resolved to different deployments without changing Q.
public sealed record SourceRoute(string ResourceType, string? ExactCode, IFhirDataSource Source);

public sealed class RoutingFhirDataSource(IReadOnlyList<SourceRoute> routes) : IFhirDataSource
{
    public Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        var matches = routes.Where(r => r.ResourceType == search.ResourceType &&
            (r.ExactCode is null || r.ExactCode == search.Get("code"))).ToArray();
        if (matches.Length != 1)
            throw new PopulationException("source-routing", "Søket må ha nøyaktig én godkjent datakilde.");
        return matches[0].Source.SearchAsync(search, context, ct);
    }
}
