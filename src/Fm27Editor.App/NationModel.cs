using System.Globalization;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

/// <summary>Nation screen labels, groups (FMRTE 27's Information / National Teams order) and input ranges.</summary>
public static class NationLabels
{
    // Development: England 1 Developed, Vietnam 2 Developing (FMRTE); other codes stay raw.
    static readonly Dictionary<long, string> Development = new() { [1] = "Developed", [2] = "Developing" };

    public static string DevelopmentName(long code) => Development.TryGetValue(code, out var s) ? s : $"Level {code}";
    public static string ImportanceName(long code) => code switch { 1 => "Very Important", 2 => "Important", 3 => "Unimportant", 4 => "No One Cares", _ => $"Level {code}" };
    public static string GainNationalityTypeName(long code) => code switch { 1 => "Continuous", 2 => "Accumulative", _ => $"Level {code}" };

    public static readonly (string Title, (NationField Field, string Label)[] Items)[] Groups =
    [
        ("Overview", [
            (NationField.Development, "Development"), (NationField.EconomicFactor, "Economic Factor"),
            (NationField.YearsToGainNationality, "Years To Gain Nationality"), (NationField.GainNationalityType, "Gain Nationality Type"),
            (NationField.DoesNotAllowDualNationality, "Does Not Allow Dual Nationality")]),
        ("National Team", [
            (NationField.Importance, "Importance"), (NationField.AllowMultipleAssistantManagers, "Allow Multiple Assistant Managers"),
            (NationField.FaFinancialPower, "FA Financial Power"), (NationField.FifaPosition, "FIFA Position"),
            (NationField.FifaRankingPoints, "FIFA Ranking Points"), (NationField.YearAffiliatedToFifa, "Year Affiliated to FIFA"),
            (NationField.YearAffiliatedToFederation, "Year Affiliated to Federation"), (NationField.YearAssociationFormed, "Year Association Formed"),
            (NationField.ForeignManagerLikelihood, "Foreign Manager Likelihood"), (NationField.FaPatience, "FA Patience"),
            (NationField.YouthRating, "Youth Rating"), (NationField.MaxYouthAge, "Max. Youth Age")]),
        ("Attendance", [
            (NationField.AverageAttendance, "Average"), (NationField.MinimumAttendance, "Minimum"), (NationField.MaximumAttendance, "Maximum")]),
        ("Women's National Team", [
            (NationField.WomenFaFinancialPower, "FA Financial Power"), (NationField.WomenFifaPosition, "FIFA Position"),
            (NationField.WomenFifaRankingPoints, "FIFA Ranking Points"), (NationField.WomenYearAffiliatedToFifa, "Year Affiliated to FIFA"),
            (NationField.WomenYearAffiliatedToFederation, "Year Affiliated to Federation"), (NationField.WomenYearAssociationFormed, "Year Association Formed"),
            (NationField.WomenForeignManagerLikelihood, "Foreign Manager Likelihood"), (NationField.WomenFaPatience, "FA Patience"),
            (NationField.WomenYouthRating, "Youth Rating"), (NationField.WomenMaxYouthAge, "Max. Youth Age")]),
        ("Women's Attendance", [
            (NationField.WomenAverageAttendance, "Average"), (NationField.WomenMinimumAttendance, "Minimum"), (NationField.WomenMaximumAttendance, "Maximum")]),
    ];

    public static (long Min, long Max) Range(NationField f) => Enum.Parse<NationField>(f.ToString().StartsWith("Women", StringComparison.Ordinal) ? f.ToString()[5..] : f.ToString()) switch
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

    /// <summary>Parses every pending change; the first invalid one is returned as an error and nothing is applied.</summary>
    public static bool TryParse(IReadOnlyDictionary<NationField, string> changes, out Dictionary<NationField, long> values, out string? error)
    {
        values = [];
        error = null;
        foreach (var (f, text) in changes)
        {
            var (min, max) = Range(f);
            if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) || v < min || v > max)
            {
                error = $"{f}: enter a whole number {min}–{max}";
                values = [];
                return false;
            }
            values[f] = v;
        }
        return true;
    }
}
