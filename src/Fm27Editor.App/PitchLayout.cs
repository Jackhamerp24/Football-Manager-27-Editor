using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed record PitchPosition(PlayerPosition Position, string Label, double X, double Y);

public static class PitchLayout
{
    // Vertical pitch: goalkeeper at the bottom, attacking left on the left of the screen.
    public static IReadOnlyList<PitchPosition> Positions { get; } =
    [
        new(PlayerPosition.Goalkeeper, "Goalkeeper", .50, .93),
        new(PlayerPosition.DefenderRight, "Defender (Right)", .82, .76),
        new(PlayerPosition.DefenderCentral, "Defender (Centre)", .50, .76),
        new(PlayerPosition.DefenderLeft, "Defender (Left)", .18, .76),
        new(PlayerPosition.WingBackRight, "Wing-Back (Right)", .82, .59),
        new(PlayerPosition.DefensiveMidfielder, "Defensive Midfielder", .50, .59),
        new(PlayerPosition.WingBackLeft, "Wing-Back (Left)", .18, .59),
        new(PlayerPosition.MidfielderRight, "Midfielder (Right)", .82, .42),
        new(PlayerPosition.MidfielderCentral, "Midfielder (Centre)", .50, .42),
        new(PlayerPosition.MidfielderLeft, "Midfielder (Left)", .18, .42),
        new(PlayerPosition.AttackingMidRight, "Attacking Midfielder (Right)", .82, .25),
        new(PlayerPosition.AttackingMidCentral, "Attacking Midfielder (Centre)", .50, .25),
        new(PlayerPosition.AttackingMidLeft, "Attacking Midfielder (Left)", .18, .25),
        new(PlayerPosition.Striker, "Striker", .50, .08)
    ];
}
