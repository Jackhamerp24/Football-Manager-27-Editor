using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class AttributeScalingTests
{
    // Game stores 1-100; the UI shows round(raw / 5), never below 1.
    [Theory]
    [InlineData(35, 7)]
    [InlineData(75, 15)]
    [InlineData(62, 12)]
    [InlineData(63, 13)]
    [InlineData(100, 20)]
    [InlineData(3, 1)]
    [InlineData(0, 1)]
    public void Raw_value_maps_to_displayed_value(byte raw, int shown) =>
        Assert.Equal(shown, AttributeScaling.ToDisplay(raw, 5));
}

public class PlayerRepositoryTests
{
    readonly TestWorld _w = new();

    PlayerRepository Repo() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Lists_only_player_objects_from_the_static_root()
    {
        var players = Repo().LoadAll();
        Assert.Equal(new[] { TestWorld.Mainoo, TestWorld.Yamal }, players.Select(p => p.Address));
    }

    [Fact]
    public void Decodes_ability_reputation_height_and_birth_date()
    {
        var p = Repo().LoadAll().Single(x => x.Address == TestWorld.Mainoo);
        Assert.Equal(153, p.CurrentAbility);
        Assert.Equal(177, p.PotentialAbility);
        Assert.Equal((7382, 7551, 6501), (p.HomeReputation, p.CurrentReputation, p.WorldReputation));
        Assert.Equal(184, p.Height);
        Assert.Equal(new DateOnly(2005, 4, 19), p.DateOfBirth);
        Assert.Equal(21, p.AgeOn(new DateOnly(2026, 11, 4)));
    }

    [Fact]
    public void Decodes_attributes_positions_and_personality_at_their_bytes()
    {
        var p = Repo().LoadAll().Single(x => x.Address == TestWorld.Mainoo);
        Assert.Equal(9, p.Attribute(PlayerAttribute.Crossing));
        Assert.Equal(13, p.Attribute(PlayerAttribute.Pace));
        Assert.Equal(14, p.Attribute(PlayerAttribute.Concentration));
        Assert.Equal(20, p.Position(PlayerPosition.Striker));
        Assert.Equal(10, p.PersonalityValue(PersonalityAttribute.Ambition));
    }

    [Fact]
    public void Display_name_is_first_and_last_name_without_a_common_name()
    {
        var p = Repo().LoadAll().Single(x => x.Address == TestWorld.Mainoo);
        Assert.Null(p.CommonName);
        Assert.Equal("Kobbie Boateng Mainoo", p.FullName);
        Assert.Equal("Kobbie Mainoo", p.DisplayName);
    }

    [Fact]
    public void Display_name_prefers_the_common_name()
    {
        var p = Repo().LoadAll().Single(x => x.Address == TestWorld.Yamal);
        Assert.Equal("Nasraoui", p.LastName);
        Assert.Equal("Lamine Yamal", p.DisplayName);
    }

    [Fact]
    public void Rejects_a_root_whose_end_is_before_its_begin()
    {
        var root = TestWorld.ModuleBase + 0x57037C0;
        Assert.True(_w.Memory.TryWrite(root + 0x18, BitConverter.GetBytes(TestWorld.ListArray - 8)));
        Assert.Throws<MemoryAccessException>(() => Repo().LoadAll());
    }

    [Fact]
    public void Reload_returns_null_for_an_object_that_is_not_a_player()
    {
        Assert.Null(Repo().Reload(TestWorld.NotAPlayer));
        Assert.Equal("Kobbie Mainoo", Repo().Reload(TestWorld.Mainoo)!.DisplayName);
    }
}
