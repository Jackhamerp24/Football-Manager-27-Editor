using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerPresetBatchTests
{
    [Fact]
    public void Fitness_preview_apply_and_undo_use_original_values_and_refuse_stale_fields()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        // Independently recorded Harrison Burrows bytes: sharpness 7100, jadedness -178, condition 8500.
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var access = new PlayerFieldAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
        var plan = PlayerPresetBatch.Plan([repo.Reload(TestWorld.Mainoo)!], access, PlayerPreset.Fitness);
        Assert.Equal(new[] { (PlayerField.Condition, 8500L, 10000L), (PlayerField.MatchSharpness, 7100L, 10000L), (PlayerField.Jadedness, -178L, -500L) }, plan.Select(c => (c.Field, c.Before, c.After)));
        foreach (var c in plan) PlayerPresetBatch.Apply(c, repo, access);
        Assert.Equal(Convert.FromHexString("10270CFE1027"), w.Bytes(TestWorld.Mainoo + 0x258, 6));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x25C, BitConverter.GetBytes((short)9000)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerPresetBatch.Undo(plan[0], repo, access));
        Assert.Equal(writes, w.Memory.Writes);
        PlayerPresetBatch.Undo(plan[2], repo, access);
        PlayerPresetBatch.Undo(plan[1], repo, access);
        Assert.Equal(Convert.FromHexString("BC1B4EFF"), w.Bytes(TestWorld.Mainoo + 0x258, 4));
    }
}
