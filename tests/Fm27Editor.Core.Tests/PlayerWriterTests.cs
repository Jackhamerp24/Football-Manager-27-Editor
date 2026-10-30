using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerWriterTests
{
    readonly TestWorld _w = new();

    PlayerRecord Mainoo() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    PlayerWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Attribute_write_stores_value_times_five_and_touches_only_that_byte()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Writer().SetAttribute(Mainoo(), PlayerAttribute.Crossing, 15);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);

        Assert.Equal(75, after[0x15F]);
        after[0x15F] = before[0x15F];
        Assert.Equal(before, after);
    }

    [Fact]
    public void Ability_writes_are_little_endian_int16()
    {
        var w = Writer();
        w.SetCurrentAbility(Mainoo(), 190);
        w.SetPotentialAbility(Mainoo(), 200);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(new byte[] { 190, 0, 200, 0 }, after[0x264..0x268]);
    }

    [Fact]
    public void Position_and_personality_writes_are_unscaled()
    {
        Writer().SetPosition(Mainoo(), PlayerPosition.Goalkeeper, 1);
        Writer().SetPersonality(Mainoo(), PersonalityAttribute.Ambition, 20);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(1, after[0x150]);
        Assert.Equal(20, after[0x2F9]);
    }

    [Fact]
    public void Refuses_to_write_when_the_object_is_no_longer_a_player()
    {
        var snapshot = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.OtherVtable)));
        int writes = _w.Memory.Writes;

        Assert.Throws<StaleObjectException>(() => Writer().SetAttribute(snapshot, PlayerAttribute.Crossing, 15));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(45, _w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }

    [Fact]
    public void Refuses_to_write_when_the_value_changed_since_the_snapshot()
    {
        var snapshot = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x15F, [50]));

        Assert.Throws<StaleObjectException>(() => Writer().SetAttribute(snapshot, PlayerAttribute.Crossing, 15));
        Assert.Equal(50, _w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Refuses_attribute_values_outside_1_to_20(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetAttribute(Mainoo(), PlayerAttribute.Crossing, value));
        Assert.Equal(45, _w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }

    [Fact]
    public void Refuses_ability_above_200()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetCurrentAbility(Mainoo(), 201));
        Assert.Equal(new byte[] { 153, 0 }, _w.Bytes(TestWorld.Mainoo + 0x264, 2));
    }
}
