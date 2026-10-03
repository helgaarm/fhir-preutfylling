using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Serverdefinert kobling fra ressurstype og valgfri eksakt code-verdi til en datakilde.
/// Null ExactCode matcher alle koder for typen. Rutene skal ikke komme fra sluttbrukeren.
/// </summary>
public sealed record SourceRoute(string ResourceType, string? ExactCode, IFhirDataSource Source);

/// <summary>
/// Valgfritt utvidelsespunkt for å hente ulike ressurser fra ulike kilder uten å endre Q.
/// Brukes i tester; webverten velger i dag én kilde per forespørsel direkte i Program.cs.
/// </summary>
public sealed class RoutingFhirDataSource(IReadOnlyList<SourceRoute> routes) : IFhirDataSource
{
    /// <summary>
    /// Krever nøyaktig én match. En generell og en spesifikk rute som begge matcher gir feil;
    /// rekkefølgen i registeret gir ingen prioritet eller automatisk reservekilde.
    /// </summary>
    public Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context, CancellationToken ct)
    {
        var matches = routes.Where(r => r.ResourceType == search.ResourceType &&
            (r.ExactCode is null || r.ExactCode == search.Get("code"))).ToArray();
        if (matches.Length != 1)
            throw new PopulationException("source-routing", "Søket må ha nøyaktig én godkjent datakilde.");
        return matches[0].Source.SearchAsync(search, context, ct);
    }
}
