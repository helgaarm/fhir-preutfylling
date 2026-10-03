using Hl7.Fhir.Model;
using QType = Hl7.Fhir.Model.Questionnaire.QuestionnaireItemType;

namespace GenericPopulation;

/// <summary>Kobler FHIR-verdier til spørsmålets svartype uten å tolke eller omregne innholdet.</summary>
public static class AnswerMapper
{
    /// <summary>
    /// Returnerer en kopi ved støttet typekombinasjon; ellers kastes answer-type.
    /// Bevarer false, tallet 0 og enheter. Quantity krever system og kode, uten comparator.
    /// Beregnede FHIRPath-systemverdier trenger en eksplisitt adapter før de kan bli FHIR-svar.
    /// </summary>
    public static DataType Map(QType? questionType, Base value)
    {
        DataType? answer = (questionType, value) switch
        {
            (QType.Boolean, FhirBoolean { Value: not null } v) => v,
            (QType.Integer, Integer { Value: not null } v) => v,
            (QType.Decimal, FhirDecimal { Value: not null } v) => v,
            (QType.Date, Date { Value: not null } v) => v,
            (QType.DateTime, FhirDateTime { Value: not null } v) => v,
            (QType.Time, Time { Value: not null } v) => v,
            (QType.String or QType.Text, FhirString { Value: not null } v) => v,
            (QType.Url, FhirUri { Value: not null } v) => v,
            (QType.Quantity, Quantity { Value: not null, Comparator: null } v)
                when !string.IsNullOrWhiteSpace(v.System) &&
                     !string.IsNullOrWhiteSpace(v.Code) => v,
            _ => null
        };
        if (answer is null)
            throw new PopulationException("answer-type", "Verdien passer ikke til spørsmålets datatype eller enhetskrav.");
        return (DataType)answer.DeepCopy();
    }
}
