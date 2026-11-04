using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerQualityMassEditTests
{
    [Fact]
    public void Height_preview_write_and_undo_keep_each_players_original_and_reject_stale_identity()
    {
        var world = new TestWorld();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        Assert.True(world.Memory.TryWrite(TestWorld.Yamal + 0x294, BitConverter.GetBytes(2000123587)));
        // Heights from independent in-game golden: Mainoo 184, Lamine Yamal 178.
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x22E, Convert.FromHexString("B800")));
        Assert.True(world.Memory.TryWrite(TestWorld.Yamal + 0x22E, Convert.FromHexString("B200")));
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase);
        var players = new[] { repo.Reload(TestWorld.Mainoo)!, repo.Reload(TestWorld.Yamal)! };
        var changes = PlayerQualityMassEdit.Plan(players, PlayerQuality.Height, MassEditMode.Change, 1);
        Assert.Equal(new[] { (184, 185), (178, 179) }, changes.Select(c => (c.Before, c.After)));
        foreach (var change in changes) PlayerQualityMassEdit.Apply(change, repo, writer);
        Assert.Equal(Convert.FromHexString("B900"), world.Bytes(TestWorld.Mainoo + 0x22E, 2));
        Assert.Equal(Convert.FromHexString("B300"), world.Bytes(TestWorld.Yamal + 0x22E, 2));
        foreach (var change in changes.Reverse()) PlayerQualityMassEdit.Undo(change, repo, writer);
        Assert.Equal(Convert.FromHexString("B800"), world.Bytes(TestWorld.Mainoo + 0x22E, 2));
        Assert.Equal(Convert.FromHexString("B200"), world.Bytes(TestWorld.Yamal + 0x22E, 2));
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123588)));
        int writes = world.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerQualityMassEdit.Apply(changes[0], repo, writer));
        Assert.Equal(writes, world.Memory.Writes);
    }
}
