using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Serverens konfigurasjon av én kilde fra Fhir:Sources i appsettings.json eller miljøvariabler.
/// Nettleseren velger bare Id; Questionnaire får ikke bestemme endepunkt eller tilgangsinformasjon.
/// </summary>
public sealed class FhirSourceOptions
{
    /// <summary>Unik nøkkel som klienten sender som sourceId.</summary>
    public string Id { get; set; } = "";
    /// <summary>Visningsnavn i kildevelgeren.</summary>
    public string Name { get; set; } = "";
    /// <summary>FHIR-base med avsluttende skråstrek, slik at relative ressursstier blir riktige.</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Navnet på serverens token-miljøvariabel for GET-kilder; aldri selve tokenet.</summary>
    public string? BearerTokenEnvironmentVariable { get; set; }
    /// <summary>fhir velger vanlig GET; dhg-post velger den anonyme DHG-testkontrakten.</summary>
    public string Mode { get; set; } = "fhir";
    /// <summary>Eksplisitt tillatte syntetiske NIN i DHG Test. Disse vises i nettleserens testpersonliste.</summary>
    public List<string> AllowedTestPatientIdentifiers { get; set; } = [];
    /// <summary>Felles modussjekk for verten, inputvalideringen og klientvalget.</summary>
    public bool IsDhg => Mode == "dhg-post";

    /// <summary>Avviser ugyldig konfigurasjon ved oppstart og returnerer den godkjente baseadressen.</summary>
    public Uri Validate()
    {
        if (Mode is not ("fhir" or "dhg-post"))
            throw new PopulationException("configuration", "Ukjent FHIR-kildemodus. Bruk fhir eller dhg-post.");
        if (string.IsNullOrWhiteSpace(Id) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !uri.AbsolutePath.EndsWith('/') || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new PopulationException("configuration", "FHIR-kilden må ha en ID og en HTTPS-base med avsluttende skråstrek (HTTP tillates lokalt).");
        if (IsDhg && (BearerTokenEnvironmentVariable is not null ||
            AllowedTestPatientIdentifiers.Count == 0 ||
            AllowedTestPatientIdentifiers.Any(id => !IsTestIdentifier(id))))
            throw new PopulationException("configuration", "DHG-testmodus krever en liste med godkjente syntetiske NIN og bruker ikke bearer-token. Autentisert DHG krever en egen tilgangsintegrasjon.");
        return uri;
    }

    /// <summary>
    /// Kontrollerer kildeavhengig pasientnøkkel før innhenting: logisk FHIR-ID eller et tillatt
    /// syntetisk NIN som tekst. NIN-sjekken er en testliste og formatkontroll, ikke fødselsnummervalidering.
    /// </summary>
    public void ValidatePatientKey(string key)
    {
        if (IsDhg)
        {
            if (!IsTestIdentifier(key) || !AllowedTestPatientIdentifiers.Contains(key, StringComparer.Ordinal))
                throw new PopulationException("patient-context", "Velg et godkjent syntetisk NIN med elleve ASCII-sifre fra DHG-testkildens pasientliste.");
        }
        else if (!Regex.IsMatch(key, @"\A[A-Za-z0-9\-.]{1,64}\z"))
            throw new PopulationException("patient-context", "Ugyldig logisk FHIR Patient-ID.");
    }

    private static bool IsTestIdentifier(string? value) =>
        value is not null && Regex.IsMatch(value, @"\A[0-9]{11}\z");
}

/// <summary>
/// Enkel autorisasjonsadapter for GET-kilder som eventuelt leser et ferdig bearer-token fra miljøet.
/// Anskaffer eller fornyer ikke token og implementerer ikke OAuth-, STS- eller HelseID-flyt.
/// </summary>
public sealed class ConfiguredAuthorizer(FhirSourceOptions options) : IRequestAuthorizer
{
    /// <inheritdoc />
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
