using Hl7.Fhir.Model;

namespace GenericPopulation;

/// <summary>
/// Pasienten og vertens beslutning om preutfylling for én kjøring.
/// Verten må etablere tilgang før konteksten opprettes; den skal aldri bindes direkte fra HTTP-input.
/// </summary>
public sealed record PopulationContext(Patient Patient, bool PrepopulationAllowed);

/// <summary>En ny QuestionnaireResponse med separate merknader om mangler og tvetydige svar.</summary>
public sealed record PopulationResult(QuestionnaireResponse Response, OperationOutcome Outcome);

/// <summary>
/// Datakildegrensen som gjør motoren uavhengig av HTTP, DHG og lokale testdata.
/// Implementasjonen må kontrollere ressurstype og pasienttilhørighet før data returneres.
/// </summary>
public interface IFhirDataSource
{
    /// <summary>Utfører et pasientavgrenset søk og returnerer et searchset Bundle.</summary>
    Task<Bundle> SearchAsync(FhirSearch search, PopulationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// En datakilde som også kan hente startpasienten. Opprett én instans per preutfyllingsforespørsel
/// slik at pasientkontekst og kallteller ikke deles mellom kjøringer.
/// </summary>
public interface IPatientFhirDataSource : IFhirDataSource
{
    /// <summary>Antall HTTP-kall forsøkt i denne kjøringen, inkludert pasientoppslag og sider.</summary>
    int RequestCount { get; }

    /// <summary>Henter Patient med logisk FHIR-ID eller syntetisk NIN, avhengig av kildemodus.</summary>
    Task<Patient> ReadPatientAsync(string patientKey, CancellationToken cancellationToken);
}

/// <summary>
/// Tilgangspunkt for autorisasjon av hvert utgående GET-kall, også neste side i et søk.
/// Produksjonsintegrasjoner må håndtere riktig audience/scope og eventuelt nytt DPoP-bevis per kall.
/// </summary>
public interface IRequestAuthorizer
{
    /// <summary>Kontrollerer tilgang og setter nødvendige headere før forespørselen sendes.</summary>
    SysTask AuthorizeAsync(HttpRequestMessage request, PopulationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Forventet feil som verten kan vise som OperationOutcome. Meldingen må være trygg å vise
/// og skal ikke inneholde pasientverdier, token eller rå feilsvar fra datakilden.
/// </summary>
public sealed class PopulationException(string code, string safeMessage)
    : Exception(safeMessage)
{
    public string Code { get; } = code;
}

/// <summary>Felles extension-URL-er for variabler og SDC (Structured Data Capture).</summary>
public static class Sdc
{
    public const string Variable = "http://hl7.org/fhir/StructureDefinition/variable";
    public const string Initial =
        "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-initialExpression";
    public const string Launch =
        "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-launchContext";
}
