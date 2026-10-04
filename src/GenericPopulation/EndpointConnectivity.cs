using System.Net;

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

/// <summary>Én anonym HEAD til konfigurert base. Ingen pasientdata, token, redirects, retry eller lesing av svarkropp.</summary>
internal sealed class EndpointConnectivity(HttpClient client, TimeSpan? timeout = null)
{
    public async Task<EndpointStatus> CheckAsync(FhirSourceOptions source, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Head, source.Validate());
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var status = (int)response.StatusCode;
            return status switch
            {
                >= 200 and < 300 => Result("ok", "Svarer", $"HTTP {status}. Endepunktet svarer. Tilgang til FHIR-data er ikke testet.", status),
                401 or 403 => Result("warning", "Krever tilgang", $"HTTP {status}. Serveren svarer, men krever tilgang. Kontrollen sender ikke token eller pasientdata.", status),
                404 or 405 => Result("warning", "Svarer · ikke bekreftet", $"HTTP {status}. Serveren kan nås, men adressen finnes ikke eller støtter ikke HEAD-kontrollen. FHIR-funksjonen er ikke bekreftet.", status),
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

    private static EndpointStatus Result(string state, string label, string detail, int? status = null) =>
        new(state, label, detail, status, DateTimeOffset.UtcNow);
}
