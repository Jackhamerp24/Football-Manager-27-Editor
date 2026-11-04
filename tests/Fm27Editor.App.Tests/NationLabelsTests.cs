using Fm27Editor.App;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App.Tests;

public class NationLabelsTests
{
    /// <summary>England (FMRTE): FIFA points 1941, Youth Rating 135, attendance up to 100000; all must be accepted.</summary>
    [Fact]
    public void Accepts_the_values_FMRTE_shows_for_England()
    {
        var changes = new Dictionary<NationField, string>
        {
            [NationField.FifaRankingPoints] = "1941", [NationField.YouthRating] = "135", [NationField.MaximumAttendance] = "100000",
            [NationField.YearAssociationFormed] = "1863", [NationField.Development] = "1",
        };
        Assert.True(NationLabels.TryParse(changes, out var v, out var error));
        Assert.Null(error);
        Assert.Equal(135, v[NationField.YouthRating]);
    }

    [Theory]
    [InlineData(NationField.EconomicFactor, "21")]
    [InlineData(NationField.Development, "3")]
    [InlineData(NationField.FifaPosition, "0")]
    [InlineData(NationField.YearAffiliatedToFifa, "19x")]
    public void Any_invalid_value_rejects_the_whole_draft(NationField field, string text)
    {
        var changes = new Dictionary<NationField, string> { [NationField.FaPatience] = "9", [field] = text };
        Assert.False(NationLabels.TryParse(changes, out var v, out _));
        Assert.Empty(v);
    }

    [Fact]
    public void Every_field_is_on_the_screen_once_and_development_codes_are_named()
    {
        var shown = NationLabels.Groups.SelectMany(g => g.Items).Select(i => i.Field).ToList();
        Assert.Equal(Enum.GetValues<NationField>().Length, shown.Count);
        Assert.Equal(shown.Count, shown.Distinct().Count());
        Assert.Equal("Developed", NationLabels.DevelopmentName(1));   // England
        Assert.Equal("Developing", NationLabels.DevelopmentName(2));  // Vietnam
        Assert.Equal("Level 4", NationLabels.DevelopmentName(4));
        Assert.Equal("Continuous", NationLabels.GainNationalityTypeName(1));
        Assert.Equal("Accumulative", NationLabels.GainNationalityTypeName(2));
        Assert.Equal("Very Important", NationLabels.ImportanceName(1));  // England, Vietnam
        Assert.Equal("No One Cares", NationLabels.ImportanceName(4));    // FMRTE 27 after writing 4 on Spain
    }
}
