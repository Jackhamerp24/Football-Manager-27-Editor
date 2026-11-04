using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubBalanceFreezerTests
{
    [Fact]
    public void Balance_freezer_keeps_increases_restores_decreases_and_rejects_changed_identity()
    {
        var w = new TestWorld();
        const ulong club = 0x53000000, finance = 0x53020000;
        var c = w.Memory.Map(club, 0x170);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + 0x4CDA1F8);
        BitConverter.GetBytes(868).CopyTo(c, 0xC);
        TestWorld.Set64(c, 0x150, finance);
        var f = w.Memory.Map(finance, 0x8B0);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + 0x4CF0CC8);
        BitConverter.GetBytes(1000).CopyTo(f, 0xC);
        var access = new ClubFieldAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
        var frozen = ClubBalanceFreezer.Capture(new ClubEntry(club, 868, "PSG", "PSG", finance, []), access, FreezeMode.AllowIncrease);
        Assert.True(w.Memory.TryWrite(finance + 0xC, BitConverter.GetBytes(1200)));
        Assert.False(frozen.Reconcile(w.Memory, w.Profile, access));
        Assert.True(w.Memory.TryWrite(finance + 0xC, BitConverter.GetBytes(700)));
        Assert.True(frozen.Reconcile(w.Memory, w.Profile, access));
        Assert.Equal(1200, BitConverter.ToInt32(w.Bytes(finance + 0xC, 4)));
        Assert.True(w.Memory.TryWrite(club + 0xC, BitConverter.GetBytes(869)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => frozen.Reconcile(w.Memory, w.Profile, access));
        Assert.Equal(writes, w.Memory.Writes);
    }
}
