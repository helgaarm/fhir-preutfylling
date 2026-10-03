using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace GenericPopulation;

/// <summary>
/// Felles lesing av HTTP-svar for GET- og DHG-klientene: begrenser størrelse, kontrollerer
/// innholdstype og deserialiserer FHIR. Rå kildefeil eksponeres ikke for brukeren.
/// </summary>
internal static class FhirHttpResponse
{
    private const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Leser inntil 2 MiB FHIR JSON; transportfeil og OperationOutcome avbryter innhentingen.</summary>
    public static async Task<Resource> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // Feilsvar fra kilden kan inneholde pasientdata eller tilgangsinformasjon.
        if (!response.IsSuccessStatusCode)
            throw new PopulationException("source-http", "FHIR-kilden svarte med HTTP " + (int)response.StatusCode + ". Kontroller kilde, tilgang og valgt testperson.");
        if (response.Content.Headers.ContentType?.MediaType is not ("application/fhir+json" or "application/json"))
            throw new PopulationException("source-content", "Uventet innholdstype fra FHIR-kilden.");
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new PopulationException("response-size", "Datakildens svar er for stort.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        // Håndhev også grensen under lesing: Content-Length kan mangle eller være misvisende.
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (memory.Length + read > MaxBytes)
                throw new PopulationException("response-size", "Datakildens svar er for stort.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        Resource resource;
        try { resource = new FhirJsonDeserializer().Deserialize<Resource>(System.Text.Encoding.UTF8.GetString(memory.ToArray())); }
        catch (Exception) { throw new PopulationException("source-json", "Ugyldig FHIR JSON fra datakilden."); }
        if (resource is OperationOutcome)
            throw new PopulationException("source-outcome", "FHIR-kilden returnerte OperationOutcome i stedet for de forespurte dataene.");
        return resource;
    }
}
