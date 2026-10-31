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
    public void Height_and_birth_date_writes_touch_only_their_fields()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Writer().SetHeight(Mainoo(), 190);
        Writer().SetDateOfBirth(Mainoo(), new DateOnly(2004, 3, 1));
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);

        Assert.Equal(new byte[] { 190, 0 }, after[0x22E..0x230]);
        // 1 March 2004 is day 61 of a leap year (31 + 29 + 1); 2004 = 0x07D4.
        Assert.Equal(new byte[] { 61, 0, 0xD4, 0x07 }, after[0x310..0x314]);
        after[0x22E..0x230].CopyTo(before, 0x22E);
        after[0x310..0x314].CopyTo(before, 0x310);
        Assert.Equal(before, after);
        Assert.Equal(new DateOnly(2004, 3, 1), Mainoo().DateOfBirth);
    }

    /// <summary>Mainoo's reputations are 7382 / 7551 / 6501 at +0x25E / +0x260 / +0x262.</summary>
    [Fact]
    public void Reputation_writes_go_to_their_own_int16()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Writer().SetReputation(Mainoo(), Reputation.Home, 9000);
        Writer().SetReputation(Mainoo(), Reputation.Current, 9600);
        Writer().SetReputation(Mainoo(), Reputation.World, 10000);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);

        // 9000 = 0x2328, 9600 = 0x2580, 10000 = 0x2710
        Assert.Equal(new byte[] { 0x28, 0x23, 0x80, 0x25, 0x10, 0x27 }, after[0x25E..0x264]);
        after[0x25E..0x264].CopyTo(before, 0x25E);
        Assert.Equal(before, after);
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetReputation(Mainoo(), Reputation.World, 10001));
    }

    [Fact]
    public void Refuses_birth_date_write_when_the_stored_date_changed()
    {
        var snapshot = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x310, BitConverter.GetBytes((short)110)));
        Assert.Throws<StaleObjectException>(() => Writer().SetDateOfBirth(snapshot, new DateOnly(2004, 3, 1)));
        Assert.Equal(new byte[] { 110, 0, 0xD5, 0x07 }, _w.Bytes(TestWorld.Mainoo + 0x310, 4));
    }

    [Fact]
    public void Refuses_ability_above_200()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetCurrentAbility(Mainoo(), 201));
        Assert.Equal(new byte[] { 153, 0 }, _w.Bytes(TestWorld.Mainoo + 0x264, 2));
    }
}
