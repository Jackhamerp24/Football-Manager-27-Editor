using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Every expectation comes from the game's Club Culture screen for Manchester United (7/11/2026), row by row against
/// the 25 records read from RAM: type code, importance and dates had to line up before a name was accepted.
/// </summary>
public class ClubVisionTypesTests
{
    [Theory]
    [InlineData(1, "Win the Competition")]
    [InlineData(2, "Qualify For Competition")]
    [InlineData(5, "Challenge for the Competition title")]
    [InlineData(10, "Reach the latter stages of the Competition")]
    [InlineData(33, "Be competitive in the competition")]
    [InlineData(44, "Work within wage budget")]
    [InlineData(45, "Sign players to sell for a profit")]
    [InlineData(60, "Become the most reputable team in Nation")]
    [InlineData(61, "Become the most reputable team in continent")]
    [InlineData(62, "Become the most reputable team in the world")]
    [InlineData(64, "Grow the club's reputation")]
    [InlineData(81, "Maximum one-year contracts for players over the age of X")]
    [InlineData(82, "Maximum two-year contracts for players over the age of X")]
    [InlineData(84, "Minimum X-year contracts for first-team players")]
    [InlineData(104, "Sign players under the age of X for the first team")]
    [InlineData(105, "Sign players under the age of X for the future")]
    [InlineData(120, "Develop players using the club's youth system")]
    [InlineData(121, "Develop the best youth system in the country")]
    [InlineData(122, "Develop division best youth system")]
    [InlineData(150, "Play attacking football")]
    [InlineData(155, "Play entertaining football")]
    public void Names_match_the_rows_the_game_shows(byte code, string name) =>
        Assert.Equal(name, ClubVisionTypes.Name(code));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(182)]
    [InlineData(184)]
    [InlineData(255)]
    public void Codes_never_seen_in_the_game_stay_unnamed(byte code) =>
        Assert.Null(ClubVisionTypes.Name(code));

    [Theory]
    [InlineData(1)]
    [InlineData(44)]
    [InlineData(155)]
    [InlineData(182)]
    [InlineData(184)]
    [InlineData(255)]
    public void Display_text_never_shows_the_numeric_code(byte code)
    {
        string text = ClubVisionTypes.DisplayName(code);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain(text, char.IsDigit);
        Assert.DoesNotContain($"({code})", text);
        Assert.Equal(ClubVisionTypes.Name(code) ?? ClubVisionTypes.UnnamedLabel, text);
    }

    [Theory]
    [InlineData(104, ClubVisionTargetKind.Age)]
    [InlineData(105, ClubVisionTargetKind.Age)]
    [InlineData(81, ClubVisionTargetKind.Age)]
    [InlineData(82, ClubVisionTargetKind.Age)]
    [InlineData(84, ClubVisionTargetKind.ContractYears)]
    [InlineData(33, ClubVisionTargetKind.CompetitionStage)]
    [InlineData(2, ClubVisionTargetKind.CompetitionStage)]
    [InlineData(44, ClubVisionTargetKind.Unknown)]
    public void Target_kind_follows_the_measured_rows(byte code, ClubVisionTargetKind kind) =>
        Assert.Equal(kind, ClubVisionTypes.TargetKind(code));
}
