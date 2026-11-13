using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class RoleScoresTests
{
    static PlayerRecord MakePlayer(Action<byte[]>? fillRaw = null, byte[]? positions = null)
    {
        var raw = new byte[64];
        Array.Fill(raw, (byte)50); // Attribute 10 across the board
        fillRaw?.Invoke(raw);
        var pos = positions ?? new byte[14];
        return new PlayerRecord
        {
            Address = 0x1000,
            VtableRva = 1,
            Id = 100,
            CurrentAbility = 100,
            PotentialAbility = 120,
            HomeReputation = 100,
            CurrentReputation = 100,
            WorldReputation = 100,
            Height = 180,
            RawAttributes = raw,
            AttributeScale = 5,
            Positions = pos,
            Personality = new byte[8],
        };
    }

    static void SetAttr(byte[] raw, PlayerAttribute attr, int value20) =>
        raw[(int)attr] = (byte)(value20 * 5);

    [Fact]
    public void CatalogMatches85UniqueRolesWithNonEmptyWeights()
    {
        Assert.Equal(85, RoleScores.All.Count);
        Assert.Equal(85, RoleScores.All.Select(r => r.Code).Distinct().Count());
        foreach (var r in RoleScores.All)
        {
            Assert.NotEmpty(r.Primary);
            Assert.NotEmpty(r.Secondary);
        }
    }

    [Fact]
    public void SweeperKeeperDefendMatchesHandCalculation()
    {
        // Primary: CommandOfArea, Kicking, OneOnOnes, Reflexes, Anticipation, Concentration, Positioning, Agility -> 20
        // Secondary: AerialReach, Communication, FirstTouch, Handling, Passing, RushingOut, Throwing, Composure, Decisions, Vision, Acceleration -> 10
        // Score = 20 * 3 + 10 * 2 = 80.0
        var player = MakePlayer(raw =>
        {
            foreach (var a in new[]
            {
                PlayerAttribute.CommandOfArea, PlayerAttribute.Kicking, PlayerAttribute.OneOnOnes,
                PlayerAttribute.Reflexes, PlayerAttribute.Anticipation, PlayerAttribute.Concentration,
                PlayerAttribute.Positioning, PlayerAttribute.Agility,
            })
                SetAttr(raw, a, 20);
            foreach (var a in new[]
            {
                PlayerAttribute.AerialReach, PlayerAttribute.Communication, PlayerAttribute.FirstTouch,
                PlayerAttribute.Handling, PlayerAttribute.Passing, PlayerAttribute.RushingOut,
                PlayerAttribute.Throwing, PlayerAttribute.Composure, PlayerAttribute.Decisions,
                PlayerAttribute.Vision, PlayerAttribute.Acceleration,
            })
                SetAttr(raw, a, 10);
        });

        var skd = RoleScores.All.Single(r => r.Code == "SK (D)");
        Assert.Equal(80.0, RoleScores.Evaluate(skd, player), 6);
    }

    [Fact]
    public void UniformExtremesScaleFromFiveToOneHundred()
    {
        var minPlayer = MakePlayer(raw => Array.Fill(raw, (byte)5));   // 1*3 + 1*2 = 5.0
        var maxPlayer = MakePlayer(raw => Array.Fill(raw, (byte)100)); // 20*3 + 20*2 = 100.0
        var af = RoleScores.All.Single(r => r.Code == "AF (A)");
        Assert.Equal(5.0, RoleScores.Evaluate(af, minPlayer), 6);
        Assert.Equal(100.0, RoleScores.Evaluate(af, maxPlayer), 6);
    }

    [Fact]
    public void ProgressRateAndCoreMentalMatchHandCalculation()
    {
        var player = MakePlayer(raw =>
        {
            SetAttr(raw, PlayerAttribute.Determination, 15);
            SetAttr(raw, PlayerAttribute.Bravery, 12);
            SetAttr(raw, PlayerAttribute.Composure, 14);
            SetAttr(raw, PlayerAttribute.Concentration, 16);
            SetAttr(raw, PlayerAttribute.Teamwork, 18);
        });
        player.Personality[(int)PersonalityAttribute.Ambition] = 18;
        player.Personality[(int)PersonalityAttribute.Professionalism] = 15;

        // ProgressRate = avg(15, 18, 15) / 2 = 16 / 2 = 8.0
        Assert.Equal(8.0, RoleScores.ProgressRate(player), 6);
        // CoreMental = 12 + 14 + 16 + 15 + 18 = 75%
        Assert.Equal(75.0, RoleScores.CoreMentalPercent(player), 6);
    }

    [Fact]
    public void BestFitRoleRespectsGoalkeeperVsOutfielder()
    {
        var pos = new byte[14];
        pos[(int)PlayerPosition.Goalkeeper] = 20;
        pos[(int)PlayerPosition.Striker] = 1;
        var gk = MakePlayer(raw =>
        {
            // Give striker attributes 20 but player is GK (20) -> BestFitRole constrains to Goalkeeper roles
            SetAttr(raw, PlayerAttribute.Finishing, 20);
            SetAttr(raw, PlayerAttribute.Dribbling, 20);
            SetAttr(raw, PlayerAttribute.OffTheBall, 20);
        }, pos);

        var best = RoleScores.BestFitRole(gk);
        Assert.Equal(RoleGroup.Goalkeeper, best.Role.Group);
    }
}
