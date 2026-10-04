using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GenericPopulation;

/// <summary>Avvis en kjent sperreproxy før en webvert med eksterne kilder startes. Endrer aldri nettverkspolicy.</summary>
internal static class NetworkStartupGuard
{
    public const string BlockedProxyMessage = "Appen kan ikke starte med eksterne FHIR-kilder fordi den har arvet en lokal sperreproxy på port 9. " +
        "Start appen fra en vanlig terminal eller en godkjent prosess med nettverkstilgang. Proxyinnstillingene er ikke endret.";

    public static void Validate(IEnumerable<FhirSourceOptions> sources, IWebProxy proxy)
    {
        foreach (var source in sources)
        {
            var destination = source.Validate();
            if (!destination.IsLoopback && !proxy.IsBypassed(destination) &&
                proxy.GetProxy(destination) is { IsLoopback: true, Port: 9 })
                throw new InvalidOperationException(BlockedProxyMessage);
        }
    }
}

public sealed record EndpointStatus(string State, string Label, string Detail, int? HttpStatus, DateTimeOffset CheckedAt);

/// <summary>Én anonym GET til basens metadata-endepunkt. Leser kun avgrenset servermetadata, aldri pasientdata.</summary>
internal sealed class EndpointConnectivity(HttpClient client, TimeSpan? timeout = null)
{
    private const int MaxMetadataBytes = 256 * 1024;

    public async Task<EndpointStatus> CheckAsync(FhirSourceOptions source, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(source.Validate(), "metadata"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return await ReadMetadataAsync(response, deadline.Token);
            return status switch
            {
                401 or 403 => Result("warning", "Krever tilgang", $"HTTP {status}. Serveren svarer, men krever tilgang. Kontrollen sender ikke token eller pasientdata.", status),
                404 or 405 => Result("warning", "Metadata ikke tilgjengelig", $"HTTP {status}. Serveren svarer, men tilbyr ikke FHIR-metadata på denne adressen. Kontroller FHIR-base URL. Tilgang til pasientdata er ikke testet.", status),
                >= 500 => Result("error", "Serverfeil", $"HTTP {status}. Serveren svarer med en feil. Prøv igjen senere.", status),
                >= 300 and < 400 => Result("warning", "Omdirigering", $"HTTP {status}. Serveren henviser videre. Kontroller baseadressen; kontrollen følger ikke omdirigeringer.", status),
                _ => Result("warning", "Svarer · avvist", $"HTTP {status}. Serveren svarer, men avviser kontrollen. Kontroller adressen og serverens tilgangskrav.", status)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result("error", "Tidsavbrudd", "Ingen respons innen tidsfristen. Kontroller nettverk, VPN og serverstatus.");
        }
        catch (HttpRequestException error)
        {
            var detail = error.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "Fant ikke serveradressen (DNS). Kontroller FHIR-base URL og nettverk.",
                HttpRequestError.SecureConnectionError => "Kunne ikke opprette en sikker forbindelse. Kontroller serverens TLS-sertifikat.",
                HttpRequestError.ProxyTunnelError => "Proxyforbindelsen ble avvist. Kontroller nettverk og proxyinnstillinger.",
                _ => "Kunne ikke koble til endepunktet. Kontroller adresse, nettverk, VPN og proxyinnstillinger."
            };
            return Result("error", "Kan ikke nås", detail);
        }
    }

    private static async Task<EndpointStatus> ReadMetadataAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (response.Content.Headers.ContentType?.MediaType is not ("application/fhir+json" or "application/json"))
            return Result("warning", "Uventet svar", "Serveren svarer, men returnerer ikke FHIR-metadata som JSON. Kontroller FHIR-base URL.", status);
        if (response.Content.Headers.ContentLength > MaxMetadataBytes)
            return MetadataTooLarge(status);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk.AsMemory(), ct)) != 0)
        {
            if (buffer.Length + count > MaxMetadataBytes) return MetadataTooLarge(status);
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        ct.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("resourceType", out var resourceType) ||
                resourceType.ValueKind != JsonValueKind.String || resourceType.GetString() != "CapabilityStatement")
                return Result("warning", "Uventet metadata", "Serveren returnerer JSON, men ikke et FHIR CapabilityStatement. Kontroller FHIR-base URL.", status);
            if (!root.TryGetProperty("fhirVersion", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "4.0.1")
                return Result("warning", "FHIR-versjon ikke bekreftet", "Servermetadata bekrefter ikke FHIR R4 (4.0.1), som denne appen bruker.", status);
            return Result("ok", "FHIR svarer", $"HTTP {status}. FHIR R4-metadata er bekreftet. Tilgang til pasientdata er ikke testet.", status);
        }
        catch (JsonException)
        {
            return Result("warning", "Ugyldig metadata", "Serveren svarer, men metadata er ikke gyldig JSON. Kontroller FHIR-base URL.", status);
        }
    }

    private static EndpointStatus MetadataTooLarge(int status) =>
        Result("warning", "Metadata for store", "Serveren svarer, men metadata overstiger kontrollens grense på 256 KiB.", status);

    private static EndpointStatus Result(string state, string label, string detail, int? status = null) =>
        new(state, label, detail, status, DateTimeOffset.UtcNow);
}
