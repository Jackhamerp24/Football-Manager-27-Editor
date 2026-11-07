using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubKitsTests
{
    [Fact]
    public void Gk_home_short_uses_the_FMRTE_item_in_the_PSG_vector_and_rejects_ambiguity()
    {
        ClubKitItem Item(int slot, string prefix, string colour) => new(0x100000, 868, 0x110000, 0x120000, 0x120090,
            0x120090, 0x120000 + (ulong)slot * 8, 0x130000 + (ulong)slot * 0x40,
            prefix + colour + "00000000000000000000000000000000000000000000000000000000", "fixture");
        var items = Enumerable.Range(0, 18).Select(i => Item(i, "0000000100000000", "F8F8F8FF")).ToArray();
        // PSG: outfield Home Shorts at vector slot 10, FMRTE's GK Home Shorts at slot 11.
        items[10] = Item(10, "0000000400000000", "1828D8FF");
        items[11] = Item(11, "0000010400010000", "365B1EFF");
        // PSG: GK Home Shirt at slot 3 and the FMRTE GK Home Icon at slot 4.
        items[3] = Item(3, "0000000200000000", "F8F8F8FF");
        items[4] = Item(4, "0000012100010000", "4141A7FF");
        var icon = ClubKitMapping.GoalkeeperIcon(items, 0);
        Assert.NotNull(icon);
        Assert.Equal(0x120020UL, icon!.Slot);
        Assert.Equal(0xFFA74141U, icon.Colour(0));
        var selected = ClubKitMapping.GoalkeeperPart(items, 0, 4);
        Assert.NotNull(selected);
        Assert.Equal(0x120058UL, selected!.Slot);
        Assert.Equal(0xFF1E5B36U, selected.Colour(0));
        items[6] = Item(6, "0000000400000000", "1828D8FF");
        items[7] = Item(7, "0000010400010000", "365B1EFF");
        Assert.Null(ClubKitMapping.GoalkeeperPart(items, 0, 4));

        // Independent FMRTE writes: Bayern GK Away Shorts and Arsenal GK Home Icon.
        var bayern = new[] { Item(0, "0000010400000000", "F8F8F8FF"), Item(1, "0000020401010000", "4747C1FF") };
        Assert.Equal(0xFFC14747U, ClubKitMapping.GoalkeeperPart(bayern, 1, 4)!.Colour(0));
        var arsenal = new[] { Item(0, "0000000200000000", "2008A0FF"), Item(1, "0000010100010000", "3939DFFF") };
        Assert.Equal(0xFFDF3939U, ClubKitMapping.GoalkeeperIcon(arsenal, 0)!.Colour(0));
        // Inter Milano and West Brom both store the FMRTE GK Home Shorts in a flags 0x102 record.
        var inter = new[] { Item(0, "0000000400000000", "080808FF"), Item(1, "0000010402010000", "3232D1FF") };
        var westBrom = new[] { Item(0, "0000000400000000", "F8F8F8FF"), Item(1, "0000010402010000", "5E5EB0FF") };
        Assert.Equal(0xFFD13232U, ClubKitMapping.GoalkeeperPart(inter, 0, 4)!.Colour(0));
        Assert.Equal(0xFFB05E5EU, ClubKitMapping.GoalkeeperPart(westBrom, 0, 4)!.Colour(0));
    }

    [Fact]
    public void Writes_FMRTE_away_colour_and_style_and_rejects_a_stale_item()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, club = 0x100000, holder = 0x110000, vector = 0x120000, item = 0x130000;
        var c = memory.Map(club, 0x200);
        TestWorld.Set64(c, 0, module + profile.Club!.ClubVtableRva);
        BitConverter.GetBytes(680).CopyTo(c, profile.Club.Id!.Value);
        TestWorld.Set64(c, profile.Club.TransferClauses!.Pointer, holder);
        var h = memory.Map(holder, 0x90);
        TestWorld.Set64(h, 0x78, vector); TestWorld.Set64(h, 0x80, vector + 8); TestWorld.Set64(h, 0x88, vector + 8);
        TestWorld.Set64(memory.Map(vector, 8), 0, item + 8);
        var record = memory.Map(item, 0x28);
        Convert.FromHexString("00000103000100002000C0FFF8F8F8FFF8F8F8FFF8F8F8FF080808FF19000000FFFFFFFFFFFFFFFF").CopyTo(record, 0);
        var kits = new ClubKits(memory, profile, module);
        var snapshot = Assert.Single(kits.Read(club, 680));
        Assert.Equal(1, snapshot.Kit); Assert.Equal(3, snapshot.Type); Assert.Equal(25, snapshot.Style);
        kits.SetColour(snapshot, 0, 0xFFFF7C92);
        Assert.Equal("927CFFFF", Convert.ToHexString(memory.Read(item + 8, 4)));
        Assert.Throws<StaleObjectException>(() => kits.SetStyle(snapshot, 27));
        kits.SetStyle(Assert.Single(kits.Read(club, 680)), 27);
        Assert.Equal("1B000000", Convert.ToHexString(memory.Read(item + 0x1C, 4)));
        var fresh = Assert.Single(kits.Read(club, 680));
        const ulong replacement = 0x140000;
        memory.Read(item, 0x28).CopyTo(memory.Map(replacement, 0x28), 0);
        Assert.True(memory.TryWrite(vector, BitConverter.GetBytes(replacement + 8)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => kits.SetColour(fresh, 0, 0xFF123456));
        Assert.Equal(writes, memory.Writes);
        const ulong twoSlots = 0x150000, neighbour = 0x160000;
        var slotBytes = memory.Map(twoSlots, 16);
        TestWorld.Set64(slotBytes, 0, item + 8); TestWorld.Set64(slotBytes, 8, neighbour + 8);
        memory.Read(item, 0x28).CopyTo(memory.Map(neighbour, 0x28), 0);
        Assert.True(memory.TryWrite(holder + 0x78, BitConverter.GetBytes(twoSlots)));
        Assert.True(memory.TryWrite(holder + 0x80, BitConverter.GetBytes(twoSlots + 16)));
        Assert.True(memory.TryWrite(holder + 0x88, BitConverter.GetBytes(twoSlots + 16)));
        var selected = kits.Read(club, 680)[0];
        Assert.True(memory.TryWrite(neighbour + 3, new byte[] { 5 }));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => kits.SetColour(selected, 0, 0xFF123456));
        Assert.Equal(writes, memory.Writes);
        Assert.Empty(kits.Read(club, 708));
    }
}
