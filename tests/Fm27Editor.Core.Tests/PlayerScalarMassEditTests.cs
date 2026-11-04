using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerScalarMassEditTests
{
    [Fact]
    public void Preview_change_apply_and_undo_use_captured_scalar_and_reject_stale_value()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        // Harrison Burrows: match sharpness 7100, jadedness -178, condition 8500 (FMRTE / RAM).
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var access = new PlayerFieldAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
        var player = repo.Reload(TestWorld.Mainoo)!;
        var plan = PlayerScalarMassEdit.Plan([player], access, PlayerField.Condition, MassEditMode.Change, -500);
        Assert.Equal((8500L, 8000L), (plan[0].Before, plan[0].After));
        PlayerPresetBatch.Apply(plan[0], repo, access);
        Assert.Equal(Convert.FromHexString("BC1B4EFF401F"), w.Bytes(TestWorld.Mainoo + 0x258, 6));
        PlayerPresetBatch.Undo(plan[0], repo, access);
        Assert.Equal(Convert.FromHexString("BC1B4EFF3421"), w.Bytes(TestWorld.Mainoo + 0x258, 6));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x25C, BitConverter.GetBytes((short)9000)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerPresetBatch.Apply(plan[0], repo, access));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerScalarMassEdit.Plan([player], access, PlayerField.Condition, MassEditMode.Set, 40000));
    }
}
