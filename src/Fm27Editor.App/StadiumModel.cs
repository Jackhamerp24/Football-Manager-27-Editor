using System.Globalization;
using Fm27Editor.Core.Stadiums;

namespace Fm27Editor.App;

/// <summary>Stadium screen labels and input rules. Enum names are only the codes read back from FMRTE; others show raw.</summary>
public static class StadiumLabels
{
    /// <summary>FMRTE's Used By National Team list; 0, 1, 2 and 6 were written through FMRTE and read back in RAM, 3, 4, 5 follow its order.</summary>
    static readonly Dictionary<long, string> NationalUse = new()
    {
        [0] = "No Matches", [1] = "All Matches", [2] = "Major Matches", [3] = "Medium Matches", [4] = "Minor Matches",
        [5] = "Major Matches And Some Medium Matches", [6] = "Minor Matches and Some Medium Matches",
    };

    /// <summary>The check boxes in FMRTE's order with their labels.</summary>
    public static readonly (StadiumFlag Flag, string Label)[] Flags =
    [
        (StadiumFlag.IsTrainingGround, "Is Training Ground"), (StadiumFlag.NotUsedInContinentalFinal, "Not Used In Continental Final"),
        (StadiumFlag.Extinct, "Extinct"), (StadiumFlag.IsCovered, "Is Covered"), (StadiumFlag.RetractableRoof, "Retractable Roof"),
        (StadiumFlag.HasUndersoilHeating, "Has Undersoil Heating"), (StadiumFlag.DigitalBoards, "Digital Advertising Boards"),
    ];

    public static bool FlagOn(Fm27Editor.Core.Stadiums.StadiumRecord s, StadiumOffsets offsets, StadiumFlag flag) =>
        offsets.AltFlagBits.TryGetValue(flag, out byte alt) ? (s.AltFlags & alt) != 0 : (s.Flags & offsets.FlagBits[flag]) != 0;

    public static readonly Dictionary<StadiumField, Dictionary<long, string>> Choices = new()
    {
        [StadiumField.Environment] = new() { [3] = "Standard", [4] = "High End", [5] = "State of the art" },
        [StadiumField.OwnerType] = new() { [1] = "Club", [2] = "Council", [3] = "Private" },
        [StadiumField.PitchDeteriorationRate] = new() { [1] = "Fast", [2] = "Medium", [3] = "Slow" },
        [StadiumField.PitchType] = new()
        {
            [1] = "Grass", [2] = "Synthetic (New Type - Soft)", [3] = "Synthetic (New Type - Hard)", [4] = "Gravel/Clay/Sand",
            [5] = "Gravel", [6] = "Clay", [7] = "Sand", [8] = "Grass/Synthetic Mix",
        },
        [StadiumField.UsedByNationalTeam] = NationalUse,
        [StadiumField.UsedByNationalU21Team] = NationalUse,
        [StadiumField.UsedByNationalU19Team] = NationalUse,
    };

    public static string Choice(StadiumField f, long code) =>
        Choices.TryGetValue(f, out var m) && m.TryGetValue(code, out var s) ? s : $"Code {code}";

    /// <summary>FMRTE's stand order: bottom, bottom-left, left, top-left, top, top-right, right, bottom-right.</summary>
    public static readonly string[] Stands = ["Bottom", "Bottom Left", "Left", "Top Left", "Top", "Top Right", "Right", "Bottom Right"];

    public static readonly (string Title, (StadiumField Field, string Label)[] Items)[] Groups =
    [
        ("Stadium", [
            (StadiumField.Environment, "Environment"), (StadiumField.OwnerType, "Owner Type"), (StadiumField.Decay, "Decay"),
            (StadiumField.BuildDate, "Build Date"), (StadiumField.RebuildDate, "Re-Build Date")]),
        ("Capacity", [
            (StadiumField.Capacity, "Capacity"), (StadiumField.SeatingCapacity, "Seating Capacity"),
            (StadiumField.AllSeaterCapacity, "Capacity For All-Seater Competitions"), (StadiumField.ExpansionCapacity, "Expansion Capacity"),
            (StadiumField.UsedCapacity, "Used Capacity"), (StadiumField.UsedByNationalTeam, "Used By National Team"),
            (StadiumField.UsedByNationalU21Team, "Used By National U21 Team"), (StadiumField.UsedByNationalU19Team, "Used By National U19 Team")]),
        ("Pitch", [
            (StadiumField.PitchType, "Pitch Type"), (StadiumField.PitchCondition, "Pitch Condition"), (StadiumField.PitchDeteriorationRate, "Pitch Deterioration Rate"),
            (StadiumField.PitchRecoveryRate, "Pitch Recovery Rate"), (StadiumField.PitchLastRelaid, "Date Pitch Last Relaid"),
            (StadiumField.PitchRelayRequired, "Pitch Relay Required Date"), (StadiumField.PitchLength, "Pitch Length (m)"),
            (StadiumField.PitchWidth, "Pitch Width (m)"), (StadiumField.MinPitchLength, "Min. Length (m)"), (StadiumField.MinPitchWidth, "Min. Width (m)"),
            (StadiumField.MaxPitchLength, "Max. Length (m)"), (StadiumField.MaxPitchWidth, "Max. Width (m)")]),
    ];

    public static bool IsDate(StadiumField f) => f is StadiumField.BuildDate or StadiumField.RebuildDate or StadiumField.PitchLastRelaid or StadiumField.PitchRelayRequired;

    /// <summary>yyyymmdd (0 = none) shown in the chosen date layout, empty for none.</summary>
    public static string Format(StadiumField f, long v) =>
        IsDate(f) ? v == 0 ? "" : StadiumDay(v) : v.ToString(CultureInfo.InvariantCulture);

    static string StadiumDay(long v) =>
        v / 10000 is >= 1 and <= 9999 && v / 100 % 100 is >= 1 and <= 12 && v % 100 is >= 1 and <= 31 && v % 100 <= DateTime.DaysInMonth((int)(v / 10000), (int)(v / 100 % 100))
            ? DateSetting.Format(new DateOnly((int)(v / 10000), (int)(v / 100 % 100), (int)(v % 100)))
            : $"{v / 10000:D4}-{v / 100 % 100:D2}-{v % 100:D2}";

    static (long Min, long Max) Range(StadiumField f) => f switch
    {
        StadiumField.Decay => (1, 20),
        StadiumField.PitchRecoveryRate => (1, 20),
        StadiumField.PitchCondition => (0, 200),
        StadiumField.PitchLength or StadiumField.MinPitchLength or StadiumField.MaxPitchLength => (80, 130),
        StadiumField.PitchWidth or StadiumField.MinPitchWidth or StadiumField.MaxPitchWidth => (40, 100),
        _ => (0, 200_000),
    };

    /// <summary>Parses one pending value; dates take the chosen date layout (empty = none).</summary>
    public static bool TryParse(StadiumField f, string text, out long value, out string? error)
    {
        error = null;
        text = text.Trim();
        if (IsDate(f))
        {
            if (text.Length == 0) { value = 0; return true; }
            if (DateSetting.TryParse(text, out var d) && d.Year is > 1900 and <= 2300)
            { value = d.Year * 10000L + d.Month * 100 + d.Day; return true; }
            value = 0; error = $"{f}: enter a date as {DateSetting.Hint}"; return false;
        }
        var (min, max) = Range(f);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max) return true;
        error = $"{f}: enter a whole number {min}–{max}";
        return false;
    }
}
