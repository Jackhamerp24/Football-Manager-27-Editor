using Fm27Editor.App;
using Fm27Editor.Core.Stadiums;

namespace Fm27Editor.App.Tests;

public class StadiumLabelsTests
{
    /// <summary>Codes read back from FMRTE after writing them on Old Trafford; unverified codes stay raw.</summary>
    [Fact]
    public void Enum_codes_match_what_FMRTE_showed()
    {
        Assert.Equal("Standard", StadiumLabels.Choice(StadiumField.Environment, 3));
        Assert.Equal("State of the art", StadiumLabels.Choice(StadiumField.Environment, 5));
        Assert.Equal("Council", StadiumLabels.Choice(StadiumField.OwnerType, 2));
        Assert.Equal("Slow", StadiumLabels.Choice(StadiumField.PitchDeteriorationRate, 3));
        Assert.Equal("Code 2", StadiumLabels.Choice(StadiumField.Environment, 2));
    }

    /// <summary>FMRTE 27 dropdowns on Wembley and Old Trafford: Pitch Type Grass/Synthetic Mix (8), Sand (7), Grass (1); Used By National Team All Matches (1), Major Matches (2), Minor Matches and Some Medium Matches (6), No Matches (0).</summary>
    [Fact]
    public void Pitch_type_and_national_team_use_match_FMRTE_dropdowns()
    {
        Assert.Equal("Grass/Synthetic Mix", StadiumLabels.Choice(StadiumField.PitchType, 8));
        Assert.Equal("Sand", StadiumLabels.Choice(StadiumField.PitchType, 7));
        Assert.Equal("Grass", StadiumLabels.Choice(StadiumField.PitchType, 1));
        Assert.Equal("All Matches", StadiumLabels.Choice(StadiumField.UsedByNationalTeam, 1));
        Assert.Equal("Major Matches", StadiumLabels.Choice(StadiumField.UsedByNationalTeam, 2));
        Assert.Equal("Minor Matches and Some Medium Matches", StadiumLabels.Choice(StadiumField.UsedByNationalU21Team, 6));
        Assert.Equal("No Matches", StadiumLabels.Choice(StadiumField.UsedByNationalU19Team, 0));
        Assert.Equal(["Is Training Ground", "Not Used In Continental Final", "Extinct", "Is Covered", "Retractable Roof", "Has Undersoil Heating", "Digital Advertising Boards"],
            StadiumLabels.Flags.Select(f => f.Label));
        Assert.Equal(Enum.GetValues<StadiumFlag>().Length, StadiumLabels.Flags.Length);
    }

    [Fact]
    public void Dates_round_trip_and_empty_means_no_date()
    {
        Assert.Equal("1909-06-01", StadiumLabels.Format(StadiumField.BuildDate, 19090601));
        Assert.Equal("", StadiumLabels.Format(StadiumField.PitchRelayRequired, 0));
        Assert.True(StadiumLabels.TryParse(StadiumField.BuildDate, "2006-08-01", out long v, out _));
        Assert.Equal(20060801, v);
        Assert.True(StadiumLabels.TryParse(StadiumField.RebuildDate, " ", out v, out _));
        Assert.Equal(0, v);
        Assert.False(StadiumLabels.TryParse(StadiumField.BuildDate, "1/8/2006", out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(StadiumField.PitchLength, "105", true)]  // Old Trafford
    [InlineData(StadiumField.MaxPitchLength, "120", true)] // Etihad
    [InlineData(StadiumField.PitchWidth, "200", false)]
    [InlineData(StadiumField.Decay, "0", false)]
    [InlineData(StadiumField.Capacity, "74879", true)]
    public void Numbers_are_range_checked(StadiumField field, string text, bool ok)
    {
        Assert.Equal(ok, StadiumLabels.TryParse(field, text, out _, out _));
    }

    [Fact]
    public void Every_field_is_on_the_screen_once()
    {
        var shown = StadiumLabels.Groups.SelectMany(g => g.Items).Select(i => i.Field).ToList();
        Assert.Equal(Enum.GetValues<StadiumField>().Length, shown.Count);
        Assert.Equal(shown.Count, shown.Distinct().Count());
        Assert.Equal(8, StadiumLabels.Stands.Length);
    }
}
