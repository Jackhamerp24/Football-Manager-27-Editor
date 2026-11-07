using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class WonderkidSearchTests
{
    static PlayerRecord Player(DateOnly? birth, short pa, DateOnly? gameDate) => new()
    {
        Address = 1, VtableRva = 1, CurrentAbility = 100, PotentialAbility = pa,
        HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 180,
        RawAttributes = [], AttributeScale = 5, Positions = [], Personality = [], DateOfBirth = birth, GameDate = gameDate
    };

    [Fact]
    public void UserDefinitionRequiresAgeAtMost20AndPotentialAbove150()
    {
        var date = new DateOnly(2026, 11, 7);
        Assert.True(WonderkidSearch.IsWonderkid(Player(new DateOnly(2006, 11, 7), 151, date)));
        Assert.False(WonderkidSearch.IsWonderkid(Player(new DateOnly(2006, 11, 7), 150, date)));
        Assert.False(WonderkidSearch.IsWonderkid(Player(new DateOnly(2005, 11, 7), 151, date)));
        Assert.Null(WonderkidSearch.IsWonderkid(Player(null, 151, date)));
        Assert.False(WonderkidSearch.IsWonderkid(Player(null, 150, date)));
    }
}
