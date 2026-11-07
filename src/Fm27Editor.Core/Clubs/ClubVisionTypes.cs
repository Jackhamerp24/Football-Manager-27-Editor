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
        [150] = "Play attacking football",
        [155] = "Play entertaining football",
    };

    /// <summary>What the UI shows for a code whose name was never read off the game screen.</summary>
    public const string UnnamedLabel = "Other objective";

    /// <summary>Name shown by the game for this type code, or null when the code has not been observed in its UI.</summary>
    public static string? Name(byte typeCode) => Names.GetValueOrDefault(typeCode);

    /// <summary>
    /// Text for the UI. Never contains the numeric code: an unmapped code reads "Other objective" so the interface
    /// cannot leak internal enum values such as 182 or 184.
    /// </summary>
    public static string DisplayName(byte typeCode) => Name(typeCode) ?? UnnamedLabel;

    /// <summary>
    /// Whether the record's <c>Target</c> is an age, a contract length in years, or a competition stage/position —
    /// measured on the same rows: 104/105 carried 23 (age), 81/82 carried 32 and 30 (age), 84 carried 4 (years),
    /// 2/10/33 carried 4, 3, 6, 2, 1 (stage or league position).
    /// </summary>
    public static ClubVisionTargetKind TargetKind(byte typeCode) => typeCode switch
    {
        104 or 105 or 81 or 82 => ClubVisionTargetKind.Age,
        84 => ClubVisionTargetKind.ContractYears,
        1 or 2 or 5 or 10 or 33 => ClubVisionTargetKind.CompetitionStage,
        _ => ClubVisionTargetKind.Unknown,
    };
}

public enum ClubVisionTargetKind { Unknown, Age, ContractYears, CompetitionStage }
