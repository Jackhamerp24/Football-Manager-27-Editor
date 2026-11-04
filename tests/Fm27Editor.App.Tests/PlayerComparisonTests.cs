using Fm27Editor.App;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

/// <summary>
/// FMRTE 27's Comparison of Ibrahim Dosso with Kobbie Mainoo (2/11/2026): Pressure 10 / 15, Corners 9 / 8,
/// CA 93 / 151, PA 121 / 177, Home reputation 4600 / 7400; groups Personality, Reputation, Ability, Technical, Mental,
/// Physical, Hidden, and Positions on its own tab.
/// </summary>
public class PlayerComparisonTests
{
    static PlayerRecord Player(short ca, short pa, short home, byte pressure, byte cornersRaw, byte strikerRating) => new()
    {
        Address = 0x1000_0000, VtableRva = 0x4D27228, CurrentAbility = ca, PotentialAbility = pa,
        HomeReputation = home, CurrentReputation = home, WorldReputation = home, Height = 180,
        RawAttributes = Raw(cornersRaw), AttributeScale = 5,
        Positions = Enumerable.Range(0, 14).Select(i => i == (int)PlayerPosition.Striker ? strikerRating : (byte)1).ToArray(),
        Personality = [1, 1, 1, pressure, 1, 1, 1, 1],
    };

    static byte[] Raw(byte corners)
    {
        var b = new byte[64];
        b[(int)PlayerAttribute.Corners] = corners;
        return b;
    }

    [Fact]
    public void Attribute_rows_pair_both_players_in_FMRTEs_groups()
    {
        var dosso = Player(93, 121, 4600, 10, 45, 20);
        var mainoo = Player(151, 177, 7400, 15, 40, 1);
        var rows = PlayerComparison.Attributes(dosso, mainoo);

        Assert.Equal(["Personality", "Reputation", "Ability", "Technical", "Mental", "Physical", "Goalkeeping", "Hidden"], rows.Select(r => r.Group).Distinct());
        Assert.Equal((10, 15), Pair(rows, "Pressure"));
        Assert.Equal((9, 8), Pair(rows, "Corners"));
        Assert.Equal((93, 151), Pair(rows, "CA"));
        Assert.Equal((121, 177), Pair(rows, "PA"));
        Assert.Equal((4600, 7400), Pair(rows, "Home"));
        Assert.DoesNotContain(rows, r => r.Group == "Positions");

        var positions = PlayerComparison.Positions(dosso, mainoo);
        Assert.Equal(14, positions.Count);
        Assert.Equal((20, 1), Pair(positions, "Striker"));
    }

    static (int, int) Pair(IReadOnlyList<PlayerComparison.Row> rows, string label) =>
        rows.Single(r => r.Label == label) is var r ? (r.Left, r.Right) : default;
}
