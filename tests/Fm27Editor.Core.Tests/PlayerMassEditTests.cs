using Fm27Editor.Core.Players;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

public class PlayerMassEditTests
{
    [Fact]
    public void Multiple_attributes_can_be_applied_and_undone_together()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(w.Memory, w.Profile, TestWorld.ModuleBase);
        var player = repo.Reload(TestWorld.Mainoo)!;
        PlayerMassOperation[] actions = [new(PlayerAttribute.Crossing, MassEditMode.Set, 12), new(PlayerAttribute.Pace, MassEditMode.Change, 2)];
        var plan = PlayerMassEdit.Plan([player], actions);
        Assert.Equal(new[] { (PlayerAttribute.Crossing, 9, 12), (PlayerAttribute.Pace, 13, 15) }, plan.Select(c => (c.Attribute, c.Before, c.After)));
        foreach (var item in plan) PlayerMassEdit.Apply(item, repo, writer);
        Assert.Equal(60, w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(75, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        foreach (var item in plan.Reverse()) PlayerMassEdit.Undo(item, repo, writer);
        Assert.Equal(45, w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(66, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Throws<ArgumentException>(() => PlayerMassEdit.Plan([player], [actions[0], actions[0]]));
    }

    sealed class ReloadFailureMemory(FakeMemory inner) : IProcessMemory
    {
        bool wrote;
        public bool TryRead(ulong address, Span<byte> bytes) => wrote && bytes.Length >= 0x3C8 ? false : inner.TryRead(address, bytes);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> bytes) { bool ok = inner.TryWrite(address, bytes); wrote |= ok; return ok; }
    }

    [Fact]
    public void Successful_write_is_recorded_even_if_player_reload_fails()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var memory = new ReloadFailureMemory(w.Memory);
        var repo = new PlayerRepository(memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(memory, w.Profile, TestWorld.ModuleBase);
        var plan = PlayerMassEdit.Plan([repo.Reload(TestWorld.Mainoo)!], PlayerAttribute.Pace, MassEditMode.Set, 15);
        bool recorded = false;
        Assert.Throws<StaleObjectException>(() => PlayerMassEdit.Apply(plan[0], repo, writer, () => recorded = true));
        Assert.True(recorded);
        Assert.Equal(75, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
    }

    [Fact]
    public void Batch_restores_exact_raw_values_and_refuses_stale_undo()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + 0x294, BitConverter.GetBytes(2000256231)));
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + 0x185, [40]));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(w.Memory, w.Profile, TestWorld.ModuleBase);
        var plan = PlayerMassEdit.Plan([repo.Reload(TestWorld.Mainoo)!, repo.Reload(TestWorld.Yamal)!], PlayerAttribute.Pace, MassEditMode.Change, 2);
        Assert.Equal(new[] { (13, 15), (8, 10) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) PlayerMassEdit.Apply(change, repo, writer);
        Assert.Equal(75, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Equal(50, w.Bytes(TestWorld.Yamal + 0x185, 1)[0]);
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + 0x185, [55]));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerMassEdit.Undo(plan[1], repo, writer));
        Assert.Equal(writes, w.Memory.Writes);
        PlayerMassEdit.Undo(plan[0], repo, writer);
        Assert.Equal(66, w.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
    }

    [Fact]
    public void Max_attributes_sets_performance_fields_and_leaves_the_two_negative_traits_unchanged()
    {
        var w = new TestWorld();
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(29179241)));
        // Haaland's 54 consecutive attribute bytes from Game Pass RAM, 5/11/2026.
        var raw = Convert.FromHexString("323D5F4C41235F4155233D140A0A050A055F4B0A280F464664373C195A41190F050F55325A465F5A412D504B504B504632245F64554B");
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x15F, raw));
        var repo = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(w.Memory, w.Profile, TestWorld.ModuleBase);
        var plan = MaxAttributes.Plan([repo.Reload(TestWorld.Mainoo)!]);
        Assert.Equal(0, plan.Unavailable);
        Assert.Contains(plan.Changes, c => c.Attribute == PlayerAttribute.Crossing && c.BeforeRaw == 50 && c.AfterRaw == 100);
        Assert.DoesNotContain(plan.Changes, c => c.Attribute is PlayerAttribute.Dirtiness or PlayerAttribute.InjuryProneness);
        foreach (var item in plan.Changes) PlayerMassEdit.Apply(item, repo, writer);
        Assert.Equal(100, w.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        // Dirtiness raw 45 and Injury Proneness raw 50 from the same Haaland block: unchanged by the preset.
        Assert.Equal(45, w.Bytes(TestWorld.Mainoo + 0x188, 1)[0]);
        Assert.Equal(50, w.Bytes(TestWorld.Mainoo + 0x18F, 1)[0]);
        foreach (var item in plan.Changes.Reverse()) PlayerMassEdit.Undo(item, repo, writer);
        Assert.Equal(raw, w.Bytes(TestWorld.Mainoo + 0x15F, raw.Length));
    }
}
