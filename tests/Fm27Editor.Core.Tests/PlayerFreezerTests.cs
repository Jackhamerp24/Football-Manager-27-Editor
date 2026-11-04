using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerFreezerTests
{
    [Fact]
    public void Freezer_restores_fixed_values_accepts_increases_and_rejects_changed_identity()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var players = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(w.Memory, w.Profile, TestWorld.ModuleBase);
        var player = players.Reload(TestWorld.Mainoo)!;
        var frozen = FrozenPlayer.Capture(player, [(PlayerAttribute.Crossing, FreezeMode.Fixed), (PlayerAttribute.Pace, FreezeMode.AllowIncrease)]);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x15F, [50]));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x185, [70]));
        Assert.Equal(1, frozen.Reconcile(players, writer));
        Assert.Equal(45, w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(70, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x185, [60]));
        Assert.Equal(1, frozen.Reconcile(players, writer));
        Assert.Equal(70, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123587)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => frozen.Reconcile(players, writer));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void Allow_decrease_accepts_lower_negative_traits_and_rejects_rises()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(29179241)));
        // Haaland's Game Pass bytes for Dirtiness and Injury Proneness: 45 and 50.
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x188, [45]));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x18F, [50]));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(w.Memory, w.Profile, TestWorld.ModuleBase);
        var frozen = FrozenPlayer.Capture(repo.Reload(TestWorld.Mainoo)!,
            [(PlayerAttribute.Dirtiness, FreezeMode.AllowDecrease), (PlayerAttribute.InjuryProneness, FreezeMode.AllowDecrease)]);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x188, [40]));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x18F, [45]));
        Assert.Equal(0, frozen.Reconcile(repo, writer));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x188, [50]));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x18F, [55]));
        Assert.Equal(2, frozen.Reconcile(repo, writer));
        Assert.Equal(40, w.Bytes(TestWorld.Mainoo + 0x188, 1)[0]);
        Assert.Equal(45, w.Bytes(TestWorld.Mainoo + 0x18F, 1)[0]);
    }
}
