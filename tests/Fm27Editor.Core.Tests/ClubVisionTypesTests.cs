using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Every expectation comes from the game's Club Culture screen for Manchester United (7/11/2026), row by row against
/// the 25 records read from RAM: type code, importance and dates had to line up before a name was accepted.
/// R. Madrid (8/11/2026) added three more: the two rows that read "Finish above club in the league" (184, one per
/// rival — the club comes from the row's club item, the Target stays -1), and the importance-9 pair that reads
/// "Sign high-reputation players" (103, top) and "Increase commercial income" (42, bottom).
/// </summary>
public class ClubVisionTypesTests
{
    [Theory]
    [InlineData(1, "Win the Competition")]
    [InlineData(2, "Qualify For Competition")]
    [InlineData(5, "Challenge for the Competition title")]
    [InlineData(10, "Reach the latter stages of the Competition")]
    [InlineData(9, "Challenge for the Competition")]
    [InlineData(33, "Be competitive in the competition")]
    [InlineData(42, "Increase commercial revenue")]
    [InlineData(44, "Work within wage budget")]
    [InlineData(45, "Sign players to sell for a profit")]
    [InlineData(60, "Become the most reputable team in Nation")]
    [InlineData(61, "Become the most reputable team in continent")]
    [InlineData(62, "Become the most reputable team in the world")]
    [InlineData(64, "Grow the club's reputation")]
    [InlineData(81, "Maximum one-year contracts for players over the age of X")]
    [InlineData(82, "Maximum two-year contracts for players over the age of X")]
    [InlineData(84, "Minimum X-year contracts for first-team players")]
    [InlineData(103, "Sign high-reputation players")]
    [InlineData(104, "Sign players under the age of X for the first team")]
    [InlineData(105, "Sign players under the age of X for the future")]
    [InlineData(107, "Don't Sign Players Over X Age")]
    [InlineData(153, "Play direct football")]
    [InlineData(120, "Develop players using the club's youth system")]
    [InlineData(121, "Develop the best youth system in the country")]
    [InlineData(122, "Develop division best youth system")]
    [InlineData(101, "Sign Players Based In Nation")]   // Barcelona 11/11/2026: FMRTE's 22 rows against the 22 records in RAM
    [InlineData(150, "Play attacking football")]
    [InlineData(152, "Play possession football")]
    [InlineData(157, "Play high-tempo pressing football")]
    [InlineData(155, "Play entertaining football")]
    [InlineData(184, "Finish above club in the league")]
    public void Names_match_the_rows_the_game_shows(byte code, string name) =>
        Assert.Equal(name, ClubVisionTypes.Name(code));

    [Theory]
    [InlineData(40, "Become self-sustainable")]
    [InlineData(41, "Repair the club's financial damage")]
    [InlineData(43, "Sign young players to develop for profit")]
    [InlineData(46, "Spend the original transfer budget")]
    [InlineData(80, "Break even in the transfer market")]
    [InlineData(83, "Maximum three-year contracts for players over the age of X")]
    [InlineData(85, "Sell players before buying")]
    [InlineData(86, "Cannot sign non-Basque players")]
    [InlineData(87, "Can only sign African players")]
    [InlineData(88, "Cannot sign non-Nation players")]
    [InlineData(89, "Cannot sign non-Nation goalkeepers")]
    [InlineData(91, "Cannot sign players who cannot speak Welsh")]
    [InlineData(92, "Cannot sign any players")]
    [InlineData(100, "Sign Players Of Nationality")]
    [InlineData(102, "Sign players from the lower levels of the domestic game")]
    [InlineData(106, "Sign players from domestic rivals")]
    [InlineData(123, "Develop the best youth system in the world")]
    [InlineData(124, "Develop players for the main club")]
    [InlineData(125, "Give playing time to first team players")]
    [InlineData(151, "Play defensively solid football")]
    [InlineData(154, "Make the most of set pieces")]
    [InlineData(156, "Play counter-attacking football")]
    [InlineData(170, "Build a new stadium")]
    [InlineData(171, "Expand stadium")]
    [InlineData(172, "Buy current stadium")]
    public void New_vision_names_from_the_FMRTE_dropdown_match(byte code, string name) =>
        Assert.Equal(name, ClubVisionTypes.Name(code));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(182)]
    [InlineData(185)]
    [InlineData(255)]
    public void Codes_never_seen_in_the_game_stay_unnamed(byte code) =>
        Assert.Null(ClubVisionTypes.Name(code));

    [Theory]
    [InlineData(1)]
    [InlineData(44)]
    [InlineData(155)]
    public void Display_text_is_the_name_the_game_shows(byte code)
    {
        string text = ClubVisionTypes.DisplayName(code);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Equal(ClubVisionTypes.Name(code), text);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(182)]
    [InlineData(185)]
    [InlineData(255)]
    public void Codes_never_seen_in_the_game_produce_no_text(byte code)
    {
        Assert.False(ClubVisionTypes.IsKnown(code));
        Assert.Equal(string.Empty, ClubVisionTypes.DisplayName(code));
        Assert.DoesNotContain(ClubVisionTypes.All.Values, value => value.Contains("Other", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Finish_above_names_the_club_from_the_row_item()
    {
        // R. Madrid carried two 184 rows: Barcelona (importance 10) and A. Madrid (importance 6), both Target -1.
        Assert.Equal("Finish above Barcelona in the league", ClubVisionTypes.DisplayName(184, "Barcelona"));
        Assert.Equal("Finish above A. Madrid in the league", ClubVisionTypes.DisplayName(184, "A. Madrid"));
        Assert.Equal("Finish above club in the league", ClubVisionTypes.DisplayName(184, null));
        Assert.Equal(ClubVisionTargetKind.Club, ClubVisionTypes.TargetKind(184));
    }

    [Theory]
    [InlineData(104, ClubVisionTargetKind.Age)]
    [InlineData(105, ClubVisionTargetKind.Age)]
    [InlineData(81, ClubVisionTargetKind.Age)]
    [InlineData(82, ClubVisionTargetKind.Age)]
    [InlineData(83, ClubVisionTargetKind.Age)]
    [InlineData(84, ClubVisionTargetKind.ContractYears)]
    [InlineData(33, ClubVisionTargetKind.CompetitionStage)]
    [InlineData(2, ClubVisionTargetKind.CompetitionStage)]
    [InlineData(44, ClubVisionTargetKind.Unknown)]
    [InlineData(58, ClubVisionTargetKind.Nation)]
    [InlineData(60, ClubVisionTargetKind.Nation)]
    [InlineData(61, ClubVisionTargetKind.Continent)]
    [InlineData(121, ClubVisionTargetKind.Nation)]
    [InlineData(122, ClubVisionTargetKind.CompetitionStage)]
    [InlineData(59, ClubVisionTargetKind.Nation)]
    [InlineData(100, ClubVisionTargetKind.Nation)]
    public void Target_kind_follows_the_measured_rows(byte code, ClubVisionTargetKind kind) =>
        Assert.Equal(kind, ClubVisionTypes.TargetKind(code));
}
