using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerRatingMassEditTests
{
    [Fact]
    public void Position_batch_keeps_each_original_and_refuses_a_changed_snapshot()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, cunha = 0x10000000, mbeumo = 0x10001000;
        void Map(ulong address, int id, string bytes)
        {
            var obj = memory.Map(address, 0x3C8);
            BitConverter.GetBytes(module + 0x4CD6318u).CopyTo(obj, 0);
            BitConverter.GetBytes(id).CopyTo(obj, 0x294);
            Convert.FromHexString(bytes).CopyTo(obj, 0x150);
        }
        // Original Game Pass position bytes captured from Cunha and Mbeumo.
        Map(cunha, 19334410, "0101010101010C010C11140F120101");
        Map(mbeumo, 48042658, "0101010101010D010E111214110101");
        var repo = new PlayerRepository(memory, profile, module);
        var writer = new PlayerWriter(memory, profile, module);
        var plan = PlayerRatingMassEdit.Plan([repo.Reload(cunha)!, repo.Reload(mbeumo)!], new(Position: PlayerPosition.AttackingMidRight), MassEditMode.Set, 18);
        Assert.Equal(new[] { (15, 18), (20, 18) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) PlayerRatingMassEdit.Apply(change, repo, writer);
        Assert.Equal(18, memory.Read(cunha + 0x15B, 1)[0]);
        Assert.Equal(18, memory.Read(mbeumo + 0x15B, 1)[0]);
        foreach (var change in plan.Reverse()) PlayerRatingMassEdit.Undo(change, repo, writer);
        Assert.Equal(15, memory.Read(cunha + 0x15B, 1)[0]);
        Assert.Equal(20, memory.Read(mbeumo + 0x15B, 1)[0]);
        Assert.True(memory.TryWrite(cunha + 0x15B, [16]));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerRatingMassEdit.Apply(plan[0], repo, writer));
        Assert.Equal(writes, memory.Writes);
    }
}
