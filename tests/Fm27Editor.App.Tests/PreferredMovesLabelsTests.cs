using Fm27Editor.App;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

public class PreferredMovesLabelsTests
{
    /// <summary>Bits FMRTE 27 showed ticked for Kane and Mbappé, and the boxes ticked by writing bits 0 and 58–60.</summary>
    [Theory]
    [InlineData(0, "Runs With Ball Down Left")]
    [InlineData(7, "Tries Killer Balls Often")]
    [InlineData(10, "Places Shots")]
    [InlineData(19, "Comes Deep To Get Ball")]
    [InlineData(35, "Tries Tricks")]
    [InlineData(41, "Gets Crowd Going")]
    [InlineData(43, "Tries Long Range Passes")]
    [InlineData(51, "Runs With Ball Often")]
    [InlineData(57, "Cuts Inside From Left Wing")]
    [InlineData(58, "Cuts Inside From Right Wing")]
    [InlineData(59, "Crosses Early")]
    [InlineData(60, "Brings Ball Out Of Defence")]
    public void Bit_names_match_FMRTE(int bit, string name) => Assert.Equal(name, PreferredMovesLabels.Names[bit]);

    [Fact]
    public void One_name_per_editable_bit() =>
        Assert.Equal(PreferredMovesAccess.Count, PreferredMovesLabels.Names.Distinct().Count());
}
