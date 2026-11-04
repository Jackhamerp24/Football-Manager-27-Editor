using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffFormationsTests
{
    [Theory]
    [InlineData("1D041D1D16161616", 1, 0x03, "1D031D1D16161616")]
    [InlineData("1C402304163F3F20", 2, 0x03, "1C400304163F3F20")]
    // FMRTE 27 on Simeone (2/11/2026): Attacking → 4-4-2 Diamond Narrow, Second / Defensive / Attacking Out of Possession → 4-4-2.
    [InlineData("1C402304163F3F20", 3, 0x07, "1C402307163F3F20")]
    [InlineData("1C402304163F3F20", 5, 0x03, "1C40230416033F20")]
    [InlineData("1C402304163F3F20", 6, 0x03, "1C402304163F0320")]
    [InlineData("1C402304163F3F20", 7, 0x03, "1C402304163F3F03")]
    public void Reads_and_writes_independently_observed_formation_bytes(string observed, int slot, byte replacement, string expected)
    {
        var w = new TestWorld();
        const ulong address = 0x60000000;
        var bytes = w.Memory.Map(address, 0x240);
        TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(bytes, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        Convert.FromHexString(observed).CopyTo(bytes, 0xDF);
        var person = new StaffRecord { Address = address + 0x100, StaffAddress = address, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var service = new StaffFormations(w.Memory, w.Profile, TestWorld.ModuleBase);
        byte[] snapshot = service.Read(person);
        Assert.Equal(Convert.FromHexString(observed), snapshot);
        var changed = (byte[])snapshot.Clone(); changed[slot] = replacement;
        service.Save(person, snapshot, changed);
        Assert.Equal(Convert.FromHexString(expected), w.Bytes(address + 0xDF, 8));
        Assert.Throws<StaleObjectException>(() => service.Save(person, snapshot, changed));
        var invalid = (byte[])changed.Clone(); invalid[(slot + 1) % 8] = 0x05; // not in FMRTE's list
        Assert.Throws<ArgumentException>(() => service.Save(person, changed, invalid));
        Assert.Equal(Convert.FromHexString(expected), w.Bytes(address + 0xDF, 8));
    }
}
