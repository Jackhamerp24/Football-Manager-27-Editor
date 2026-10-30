using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed record PitchPosition(PlayerPosition Position, string Label, double X, double Y);

public static class PitchLayout
{
    // Normalized coordinates on a horizontal pitch, goalkeeper on the left.
    // The upper touchline represents the player's right side, as in FM's overview.
    public static IReadOnlyList<PitchPosition> Positions { get; } =
    [
        new(PlayerPosition.Goalkeeper, "Goalkeeper", .08, .50),
        new(PlayerPosition.DefenderRight, "Defender (Right)", .25, .18),
        new(PlayerPosition.DefenderCentral, "Defender (Centre)", .25, .50),
        new(PlayerPosition.DefenderLeft, "Defender (Left)", .25, .82),
        new(PlayerPosition.WingBackRight, "Wing-Back (Right)", .42, .18),
        new(PlayerPosition.DefensiveMidfielder, "Defensive Midfielder", .42, .50),
        new(PlayerPosition.WingBackLeft, "Wing-Back (Left)", .42, .82),
        new(PlayerPosition.MidfielderRight, "Midfielder (Right)", .59, .18),
        new(PlayerPosition.MidfielderCentral, "Midfielder (Centre)", .59, .50),
        new(PlayerPosition.MidfielderLeft, "Midfielder (Left)", .59, .82),
        new(PlayerPosition.AttackingMidRight, "Attacking Midfielder (Right)", .76, .18),
        new(PlayerPosition.AttackingMidCentral, "Attacking Midfielder (Centre)", .76, .50),
        new(PlayerPosition.AttackingMidLeft, "Attacking Midfielder (Left)", .76, .82),
        new(PlayerPosition.Striker, "Striker", .93, .50)
    ];
}
