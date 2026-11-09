namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Type codes of club-vision / board-objective records, read off the game's own "Club Culture" screen for Manchester
/// United on 7/11/2026 by aligning its 22 visible rows with the 25 records in RAM (type, importance and dates all
/// matched row by row). Codes not listed here stay unnamed on purpose: they were never seen in the game UI.
/// </summary>
public static class ClubVisionTypes
{
    static readonly Dictionary<byte, string> Names = new()
    {
        [1] = "Win the Competition",
        [2] = "Qualify For Competition",
        [5] = "Challenge for the Competition title",
        [10] = "Reach the latter stages of the Competition",
        [33] = "Be competitive in the competition",
        [44] = "Work within wage budget",
        [45] = "Sign players to sell for a profit",
        [60] = "Become the most reputable team in Nation",
        [61] = "Become the most reputable team in continent",
        [62] = "Become the most reputable team in the world",
        [64] = "Grow the club's reputation",
        [81] = "Maximum one-year contracts for players over the age of X",
        [82] = "Maximum two-year contracts for players over the age of X",
        [84] = "Minimum X-year contracts for first-team players",
        [104] = "Sign players under the age of X for the first team",
        [105] = "Sign players under the age of X for the future",
        [120] = "Develop players using the club's youth system",
        [121] = "Develop the best youth system in the country",
        [122] = "Develop division best youth system",
        [9] = "Challenge for the Competition",
        [42] = "Increase commercial revenue",
        [103] = "Sign high-reputation players",
        [107] = "Don't Sign Players Over X Age",
        [153] = "Play direct football",
        [184] = "Finish above club in the league",
        [101] = "Sign Players Based In Nation",
        [150] = "Play attacking football",
        [152] = "Play possession football",
        [155] = "Play entertaining football",
        [157] = "Play high-tempo pressing football",
    };

    /// <summary>All decoded club vision objective types mapped to their game UI names.</summary>
    public static IReadOnlyDictionary<byte, string> All => Names;

    /// <summary>Name shown by the game for this type code, or null when the code has not been observed in its UI.</summary>
    public static string? Name(byte typeCode) => Names.GetValueOrDefault(typeCode);

    /// <summary>Whether the game text for this code was actually observed. Unknown codes must not be shown at all.</summary>
    public static bool IsKnown(byte typeCode) => Names.ContainsKey(typeCode);

    /// <summary>
    /// Text for the UI. Never contains the numeric code and never invents a name: a code that was not read off the game
    /// screen comes back empty, so callers skip that row (FMRTE hides those rows too — it lists 22–23 of the 25 rows we read).
    /// </summary>
    public static string DisplayName(byte typeCode) => Name(typeCode) ?? string.Empty;

    /// <summary>
    /// Text for a row that points at another club (type 184): the game reads "Finish above &lt;club&gt; in the league",
    /// with the club taken from the row's club item, not from <c>Target</c> (that stays -1).
    /// </summary>
    public static string DisplayName(byte typeCode, string? clubName)
    {
        if (typeCode == 184 && !string.IsNullOrWhiteSpace(clubName)) return $"Finish above {clubName} in the league";
        return DisplayName(typeCode);
    }

    /// <summary>
    /// Whether the record's <c>Target</c> is an age, a contract length in years, or a competition stage/position —
    /// measured on the same rows: 104/105 carried 23 (age), 81/82 carried 32 and 30 (age), 84 carried 4 (years),
    /// 2/10/33 carried 4, 3, 6, 2, 1 (stage or league position). 184 carries -1 and names a club instead.
    /// </summary>
    public static ClubVisionTargetKind TargetKind(byte typeCode) => typeCode switch
    {
        104 or 105 or 81 or 82 or 107 => ClubVisionTargetKind.Age,
        84 => ClubVisionTargetKind.ContractYears,
        1 or 2 or 5 or 9 or 10 or 33 => ClubVisionTargetKind.CompetitionStage,
        184 => ClubVisionTargetKind.Club,
        _ => ClubVisionTargetKind.Unknown,
    };
}

public enum ClubVisionTargetKind { Unknown, Age, ContractYears, CompetitionStage, Club }
