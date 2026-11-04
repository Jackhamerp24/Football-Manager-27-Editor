using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

/// <summary>Expected texts are FMRTE 27's City page and Inhabitants Range dropdown (Manchester (MA), 3/11/2026).</summary>
public class CityLabelsTests
{
    [Fact]
    public void Inhabitant_ranges_follow_FMRTE_dropdown_and_the_stored_index()
    {
        Assert.Equal(16, CityLabels.InhabitantRanges.Length);
        Assert.Equal("Not Set", CityLabels.Inhabitants(0));
        Assert.Equal("5001 - 10000", CityLabels.Inhabitants(4));       // Manchester (MA), byte 4 in RAM
        Assert.Equal("250001 - 500000", CityLabels.Inhabitants(9));    // Manchester and London (ON), byte 9
        Assert.Equal("20000001 +", CityLabels.Inhabitants(15));
        Assert.Equal("40", CityLabels.Inhabitants(40));
    }

    [Fact]
    public void Fields_format_like_FMRTE()
    {
        var manchester = new CityRecord(1, 100062, "Manchester", 2, 53.47932f, -2.248485f, 0, 9, 18);
        Assert.Equal("53.479320", CityLabels.Format(manchester, CityField.Latitude));
        Assert.Equal("-2.248485", CityLabels.Format(manchester, CityField.Longitude));
        Assert.Equal("0", CityLabels.Format(manchester, CityField.Altitude));
        Assert.Equal("18", CityLabels.Format(manchester, CityField.Attraction));
        Assert.Equal("30", CityLabels.Format(manchester with { Altitude = 30 }, CityField.Altitude));
    }

    [Theory]
    [InlineData(CityField.Attraction, "20", true)]
    [InlineData(CityField.Attraction, "21", false)]      // FMRTE refused 21 and 25 on Save
    [InlineData(CityField.Attraction, "-1", false)]
    [InlineData(CityField.Attraction, "1.5", false)]
    [InlineData(CityField.Inhabitants, "15", true)]
    [InlineData(CityField.Inhabitants, "16", false)]
    [InlineData(CityField.Latitude, "-90", true)]
    [InlineData(CityField.Latitude, "90.1", false)]
    [InlineData(CityField.Longitude, "-81.24318", true)]
    [InlineData(CityField.Longitude, "181", false)]
    [InlineData(CityField.Altitude, "-12", true)]
    [InlineData(CityField.Altitude, "40000", false)]
    [InlineData(CityField.Altitude, "abc", false)]
    public void Input_rules_match_the_writer(CityField field, string text, bool valid)
    {
        Assert.Equal(valid, CityLabels.TryParse(field, text, out _, out var error));
        Assert.Equal(valid, error is null);
    }
}
