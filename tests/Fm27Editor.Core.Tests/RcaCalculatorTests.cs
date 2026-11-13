using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class RcaCalculatorTests
{
    static PlayerRecord MakePlayer(byte rawValue, params (PlayerPosition Pos, byte Rating)[] posRatings)
    {
        var raw = new byte[64];
        Array.Fill(raw, rawValue);
        var pos = new byte[14];
        foreach (var (p, r) in posRatings) pos[(int)p] = r;
        return new PlayerRecord
        {
            Address = 0x2000,
            VtableRva = 1,
            Id = 200,
            CurrentAbility = 100,
            PotentialAbility = 150,
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

    [Fact]
    public void UniformRawAttributesMatchFormulaForNaturalStriker()
    {
        // Uniform raw 50 -> ((50 - 30) * 4 - 1) = 79
        Assert.Equal(79, RcaCalculator.Calculate(MakePlayer(50, (PlayerPosition.Striker, 20))));
        // Uniform raw 80 -> ((80 - 30) * 4 - 1) = 199
        Assert.Equal(199, RcaCalculator.Calculate(MakePlayer(80, (PlayerPosition.Striker, 20))));
        // Clamp extremes
        Assert.Equal(1, RcaCalculator.Calculate(MakePlayer(1, (PlayerPosition.Striker, 20))));
        Assert.Equal(200, RcaCalculator.Calculate(MakePlayer(100, (PlayerPosition.Striker, 20))));
    }

    [Fact]
    public void TierBlendingUsesAverageForNaturalAndSumsLowerTiers()
    {
        // Two Natural (20) positions still average to 79
        var twoNaturals = MakePlayer(50, (PlayerPosition.Striker, 20), (PlayerPosition.AttackingMidCentral, 20));
        Assert.Equal(79, RcaCalculator.Calculate(twoNaturals));

        // 1 Natural (79*1) + 18 (79*0.025) + 15 (79*0.00625) + 12 (79*0.0015625) = 81.5921875 -> 82
        var multi = MakePlayer(50,
            (PlayerPosition.Goalkeeper, 20),
            (PlayerPosition.Striker, 18),
            (PlayerPosition.AttackingMidCentral, 15),
            (PlayerPosition.MidfielderCentral, 12));
        Assert.Equal(82, RcaCalculator.Calculate(multi));
    }

    [Fact]
    public void WeakerFootUsesMinimumOfLeftAndRightFoot()
    {
        var pLeftWeak = MakePlayer(50, (PlayerPosition.Striker, 20));
        pLeftWeak.RawAttributes[(int)PlayerAttribute.LeftFoot] = 40;
        pLeftWeak.RawAttributes[(int)PlayerAttribute.RightFoot] = 100;

        var pRightWeak = MakePlayer(50, (PlayerPosition.Striker, 20));
        pRightWeak.RawAttributes[(int)PlayerAttribute.LeftFoot] = 100;
        pRightWeak.RawAttributes[(int)PlayerAttribute.RightFoot] = 40;

        var pBothStrong = MakePlayer(50, (PlayerPosition.Striker, 20));
        pBothStrong.RawAttributes[(int)PlayerAttribute.LeftFoot] = 100;
        pBothStrong.RawAttributes[(int)PlayerAttribute.RightFoot] = 100;

        Assert.Equal(RcaCalculator.Calculate(pLeftWeak), RcaCalculator.Calculate(pRightWeak));
        Assert.True(RcaCalculator.Calculate(pBothStrong) > RcaCalculator.Calculate(pLeftWeak));
    }

    [Fact]
    public void StaffRcaHandlesUniformAttributesMissingRolesAndDataAnalyst()
    {
        var kind = new StaffKind(1, 2, 0x100, true);
        var noRoles = new StaffRecord
        {
            Address = 0x3000, StaffAddress = 0x2F00,
            Attributes = new byte[64], Personality = new byte[8], Kind = kind,
            Roles = null,
        };
        Assert.Null(StaffRcaCalculator.Calculate(noRoles).Score);

        var rolesAnalyst = new byte[15];
        rolesAnalyst[(int)StaffRole.DataAnalyst] = 15;
        var analyst = noRoles with { Roles = rolesAnalyst };
        var analystRes = StaffRcaCalculator.Calculate(analyst);
        Assert.Null(analystRes.Score);
        Assert.True(analystRes.HasUnavailableDataAnalystInput);

        // Uniform 16 on all staff attributes + adaptability 16 for Manager=20 -> round(16 * 10) = 160
        var attrs = new byte[64];
        Array.Fill(attrs, (byte)16);
        var pers = new byte[8];
        Array.Fill(pers, (byte)16);
        var mgrRoles = new byte[15];
        mgrRoles[(int)StaffRole.Manager] = 20;
        var mgr = new StaffRecord
        {
            Address = 0x3100, StaffAddress = 0x3000,
            Attributes = attrs, Personality = pers, Kind = kind,
            Roles = mgrRoles,
        };
        Assert.Equal(160, StaffRcaCalculator.Calculate(mgr).Score);
    }
}
