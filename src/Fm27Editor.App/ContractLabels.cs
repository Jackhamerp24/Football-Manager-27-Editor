using System.Globalization;
using System.Text.RegularExpressions;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>Display names for contract data, as FM and FMRTE show them.</summary>
public static class ContractLabels
{
    /// <summary>Bonus/clause type codes measured on FM26/FM27 (see docs/handover-app.md). Unknown codes show as numbers.</summary>
    public static string Term(short type) => Terms.TryGetValue(type, out var name) ? name : $"Clause {type}";

    /// <summary>
    /// Contract bonus/clause codes. 11, 13, 22, 32, 34, 38, 39 are measured on FM26/FM27 contracts; the rest
    /// follow the same numbering (FMRTE 26's list), which matched every measured code.
    /// </summary>
    public static IReadOnlyDictionary<short, string> Terms { get; } = new Dictionary<short, string>
    {
        [0] = "Minimum Fee Release Clause",
        [1] = "Relegation Release Clause",
        [2] = "Non Promotion Release Clause",
        [3] = "Yearly Wage Rise",
        [4] = "Promotion Wage Rise",
        [5] = "Relegation Wage Drop",
        [6] = "Non Playing Job Offer Release Clause",
        [7] = "Release At Any Time",
        [10] = "Seasonal Landmark Goal Bonus",
        [11] = "One-Year Extension After League Games (Final Season)",
        [12] = "Match Highest Earner",
        [13] = "Wage After Reaching Club League Games",
        [14] = "Top Division Promotion Wage Rise",
        [15] = "Top Division Relegation Wage Drop",
        [19] = "Wage After Reaching International Caps",
        [21] = "Percentage Of Gate Receipts",
        [22] = "Optional Contract Extension By Club",
        [29] = "Contract Extension After Promotion",
        [30] = "Injury Release Clause",
        [32] = "Appearance Fee",
        [33] = "Goal Bonus",
        [34] = "Clean Sheet Bonus",
        [37] = "International Cap Bonus",
        [38] = "Unused Substitute Fee",
        [39] = "Assist Bonus",
        [40] = "Seasonal Landmark Assist Bonus",
        [41] = "Seasonal Landmark Goal And Assist Bonus",
    };

    /// <summary>What the parameter of a term counts, or null when the term has none.</summary>
    public static string? TermParameter(short type) => type switch
    {
        11 or 13 => "Games",
        10 => "Goals",
        40 => "Assists",
        41 => "Goals + Assists",
        19 => "Caps",
        22 => "Years",
        3 or 4 or 5 or 14 or 15 => "%",
        _ => null,
    };

    /// <summary>Terms whose value is not money (the game stores -1).</summary>
    public static bool TermHasValue(short type) => type is not (11 or 22 or 12 or 29 or 7 or 3 or 4 or 5 or 14 or 15);

    public static string Spaced(string name) => Regex.Replace(name, "(?<!^)([A-Z])", " $1");

    public static string Of(PlayingTime value) => value switch
    {
        PlayingTime.Unset => "Any",
        PlayingTime.SquadPlayer2 => "Squad Player",
        PlayingTime.FringePlayer2 => "Fringe Player",
        PlayingTime.BTeamRegular => "B Team Regular",
        PlayingTime.FirstChoiceGoalkeeper => "First-Choice Goalkeeper",
        PlayingTime.CupGoalkeeper2 => "Cup Goalkeeper",
        _ => Spaced(value.ToString()),
    };

    public static string Of(ContractType value) => value switch
    {
        ContractType.NonContract => "Non Contract",
        _ => Spaced(value.ToString()) + " Contract",
    };

    public static string Of(TransferClauseKind kind) => kind switch
    {
        TransferClauseKind.SellOn => "Sell-On",
        _ => Spaced(kind.ToString()),
    };

    /// <summary>
    /// A clause's name; an appearance-fee record that counts league goals is a goals fee (FM: "Goals Money"). A counter the
    /// editor has not identified keeps the appearance-fee name with its code.
    /// </summary>
    public static string Of(TransferClause c) => c.Kind != TransferClauseKind.AppearanceFee ? Of(c.Kind) : c.Condition switch
    {
        null or TransferClauseCondition.LeagueAppearances => "Appearance Fee",
        TransferClauseCondition.LeagueGoals => "Goals Fee",
        { } other => $"Appearance Fee (type {other})",
    };

    /// <summary>What an appearance-fee record counts: "apps", "goals", or "" when not identified.</summary>
    public static string CountUnit(TransferClause c) => c.Condition switch
    {
        null or TransferClauseCondition.LeagueAppearances => "apps",
        TransferClauseCondition.LeagueGoals => "goals",
        _ => "",
    };

    /// <summary>Amount column of a transfer clause; a sell-on is a percentage, not a sum.</summary>
    public static string Amount(TransferClause c) =>
        c.Kind == TransferClauseKind.SellOn ? $"{c.Percentage}%" : c.Amount?.ToString("N0", CultureInfo.InvariantCulture) ?? "";

    public static string Detail(TransferClause c) => c.Kind switch
    {
        TransferClauseKind.SellOn when c.SignedFor is { } f => $"signed for {f.ToString("N0", CultureInfo.InvariantCulture)}",
        TransferClauseKind.AppearanceFee => $"{c.AppearancesSoFar} / {c.Appearances} {CountUnit(c)}".TrimEnd(),
        TransferClauseKind.CompetitionBonus => string.Join("  ", new[]
        {
            Competition(c),
            c.Ends is { } d ? $"until {DateSetting.Format(d)}" : "",
        }.Where(s => s.Length > 0)),
        _ => "",
    };

    /// <summary>Competition name; a competition not in the loaded divisions (a cup) shows its index.</summary>
    public static string Competition(TransferClause c) =>
        c.Competition?.Name ?? c.Competition?.ShortName ?? (c.CompetitionIndex is { } i ? $"Competition {i}" : "");
}
