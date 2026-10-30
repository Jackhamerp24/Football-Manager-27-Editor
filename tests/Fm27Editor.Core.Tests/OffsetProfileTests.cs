using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class OffsetProfileTests
{
    static string Json => File.ReadAllText(TestWorld.ProfilePath);

    [Fact]
    public void Fm27_profile_has_the_measured_root_and_vtables()
    {
        var p = OffsetProfile.Load(Json);
        Assert.Equal("27.0.0+2486695", p.Build);
        Assert.Equal(0x4AE1492u, p.BuildStringRva);
        Assert.Equal(0x57037C0u, p.PlayerListRootRva);
        Assert.Equal(new[] { 0x4D27228u, 0x4F75A08u }, p.PlayerVtableRvas);
    }

    [Fact]
    public void Missing_attribute_offset_fails_at_load()
    {
        var broken = Json.Replace("\"Concentration\": \"0x194\"", "\"Unused\": \"0x194\"");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(broken));
    }

    [Fact]
    public void Decimal_offsets_are_rejected()
    {
        var broken = Json.Replace("\"height\": \"0x22E\"", "\"height\": \"558\"");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(broken));
    }

    [Fact]
    public void Written_json_loads_back_to_the_same_offsets()
    {
        var original = OffsetProfile.Load(Json);
        var copy = OffsetProfile.Load(original.ToJson());

        Assert.Equal(original.Build, copy.Build);
        Assert.Equal(original.PlayerListRootRva, copy.PlayerListRootRva);
        Assert.Equal(original.PlayerVtableRvas, copy.PlayerVtableRvas);
        Assert.Equal(original.Attributes.OrderBy(k => k.Key), copy.Attributes.OrderBy(k => k.Key));
        Assert.Equal(original.Positions.OrderBy(k => k.Key), copy.Positions.OrderBy(k => k.Key));
        Assert.Equal(original.Personality.OrderBy(k => k.Key), copy.Personality.OrderBy(k => k.Key));
        Assert.Equivalent(original.Fields, copy.Fields);
        Assert.Equal(0x15F, copy.Attributes[PlayerAttribute.Crossing]);
    }
}
