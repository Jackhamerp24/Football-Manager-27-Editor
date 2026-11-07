using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerPositionSummaryTests
{
    [Fact]
    public void Formats_positions_seen_in_fmrte_for_five_players()
    {
        static byte[] Positions(params (PlayerPosition Position, byte Rating)[] values)
        {
            var result = new byte[Enum.GetValues<PlayerPosition>().Length];
            foreach (var (position, rating) in values) result[(int)position] = rating;
            return result;
        }
        Assert.Equal("M/AM C", PlayerPositionSummary.Format(Positions((PlayerPosition.MidfielderCentral, 17), (PlayerPosition.AttackingMidCentral, 20), (PlayerPosition.DefensiveMidfielder, 13))));
        Assert.Equal("M/AM C", PlayerPositionSummary.Format(Positions((PlayerPosition.MidfielderCentral, 20), (PlayerPosition.AttackingMidCentral, 19))));
        Assert.Equal("DM/M/AM C", PlayerPositionSummary.Format(Positions((PlayerPosition.DefensiveMidfielder, 20), (PlayerPosition.MidfielderCentral, 20), (PlayerPosition.AttackingMidCentral, 20))));
        Assert.Equal("AM RL", PlayerPositionSummary.Format(Positions((PlayerPosition.AttackingMidLeft, 17), (PlayerPosition.AttackingMidRight, 20))));
        Assert.Equal("GK", PlayerPositionSummary.Format(Positions((PlayerPosition.Goalkeeper, 20))));
        Assert.Equal("D C", PlayerPositionSummary.Format(Positions((PlayerPosition.DefenderCentral, 20), (PlayerPosition.DefensiveMidfielder, 15))));
    }
}
