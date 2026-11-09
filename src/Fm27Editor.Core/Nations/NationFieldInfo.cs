namespace Fm27Editor.Core.Nations;

/// <summary>
/// Accepted input range for every nation field. The women's fields share the range of the matching men's field.
/// Ranges are the ones the nation screen already enforces; they are editor limits, not claims about the game's own limits.
/// </summary>
public static class NationFieldInfo
{
    public static (long Min, long Max) Range(NationField field)
    {
        var text = field.ToString();
        var men = text.StartsWith("Women", StringComparison.Ordinal) ? Enum.Parse<NationField>(text[5..]) : field;
        return men switch
        {
            NationField.EconomicFactor or NationField.FaFinancialPower or NationField.FaPatience or NationField.ForeignManagerLikelihood => (1, 20),
            NationField.YearsToGainNationality => (0, 30),
            NationField.Development or NationField.GainNationalityType => (1, 2),
            NationField.Importance => (1, 4),
            NationField.AllowMultipleAssistantManagers or NationField.DoesNotAllowDualNationality => (0, 1),
            NationField.FifaPosition => (1, 255),
            NationField.FifaRankingPoints => (0, 5000),
            NationField.YearAffiliatedToFifa or NationField.YearAffiliatedToFederation or NationField.YearAssociationFormed => (1800, 2100),
            NationField.YouthRating => (0, 200),
            NationField.MaxYouthAge => (15, 30),
            _ => (0, 250_000),
        };
    }
}
