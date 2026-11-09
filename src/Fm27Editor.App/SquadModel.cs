using System.Globalization;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed record SquadPlayerRow(
    PlayerRecord Player,
    int? SquadNumber,
    string InfFlags,
    string FormattedPositions,
    long? Condition,
    long? AskingPrice,
    string AvgRating = "—",
    string InfTooltip = "")
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string DisplayNumber => SquadNumber is > 0 ? SquadNumber.Value.ToString(Inv) : "";

    public string DisplayAge => Player.Age?.ToString(Inv) ?? "";
    public string DisplayCa => Player.CurrentAbility.ToString(Inv);
    public string DisplayPa => Player.PotentialAbility.ToString(Inv);
    public string DisplayCondition => Condition is { } c and >= 0 ? c.ToString(Inv) : "";
    public string DisplayAskingPrice => AskingPrice is { } p and >= 0 ? p.ToString("N0", Inv) : "";
    public string DisplayNat => Player.NationalityName;
    public string DisplayClub => Player.ClubName;
    public string DisplayName => Player.DisplayName;
}

public static class SquadFormatters
{
    public static string FormatPositions(PlayerRecord player)
    {
        var naturalAndAccomplished = new List<(string Label, int Rating)>();
        foreach (var pos in Enum.GetValues<PlayerPosition>())
        {
            int rating = player.Position(pos);
            if (rating >= 15)
                naturalAndAccomplished.Add((PositionLabels.Short(pos), rating));
        }

        if (naturalAndAccomplished.Count > 0)
        {
            return string.Join(", ", naturalAndAccomplished
                .OrderByDescending(p => p.Rating)
                .Select(p => p.Label));
        }

        var best = Enum.GetValues<PlayerPosition>()
            .Select(pos => (Label: PositionLabels.Short(pos), Rating: player.Position(pos)))
            .OrderByDescending(p => p.Rating)
            .FirstOrDefault();

        return best.Rating > 0 ? best.Label : "";
    }

    public static string FormatInfFlags(PlayerRecord player, bool isInjured = false, bool isBanned = false, bool isUnhappy = false, bool isFrozen = false) =>
        string.Join(", ", new[] { (isInjured, "Inj"), (isBanned, "Ban"), (isUnhappy, "Unh"), (isFrozen, "Frz") }.Where(f => f.Item1).Select(f => f.Item2));
}

public sealed record PlayerScalarDescriptor(PlayerField Field, string Label, long Min, long Max, double Width);

public static class SquadScalarDefinitions
{
    public static readonly IReadOnlyList<PlayerScalarDescriptor> Descriptors =
    [
        new(PlayerField.Condition, "Condition", 0, 10000, 110),
        new(PlayerField.MatchSharpness, "Sharpness", 0, 10000, 110),
        new(PlayerField.Jadedness, "Jadedness", -500, 1000, 110),
        new(PlayerField.Morale, "Morale", 1, 20, 85),
        new(PlayerField.AskingPrice, "Asking Price", 0, 2_147_483_647, 145),
        new(PlayerField.Caps, "Caps", 0, 999, 85),
        new(PlayerField.InternationalGoals, "Int Goals", 0, 999, 85),
        new(PlayerField.SubCaps, "Youth Caps", 0, 255, 95),
        new(PlayerField.SubGoals, "Youth Goals", 0, 255, 95),
    ];
}
