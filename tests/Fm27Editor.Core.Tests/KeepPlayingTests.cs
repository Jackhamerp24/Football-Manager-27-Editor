using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class KeepPlayingTests
{
    const ulong Module = 0x7000_0000_0000, Manager = 0x1000_0000, Pointers = 0x2000_0000,
        Index = 0x2001_0000, Entry1 = 0x3000_0000, Entry2 = 0x3000_0100, Entry3 = 0x3000_0200,
        Ronaldo = 0x4000_0000, Zimmermann = 0x4000_1000;
    readonly FakeMemory memory = new();
    readonly Fm27Editor.Core.Offsets.OffsetProfile profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");

    public KeepPlayingTests()
    {
        TestWorld.Set64(memory.Map(Module + 0x5675060, 8), 0, Manager);
        var manager = memory.Map(Manager, 0xA8);
        TestWorld.Set64(manager, 0, Module + 0x4E6B440);
        Vector(manager, 0x30, Pointers, 3, 8);
        Vector(manager, 0x90, Index, 3, 16);
        var pointers = memory.Map(Pointers, 24);
        TestWorld.Set64(pointers, 0, Entry2); TestWorld.Set64(pointers, 8, Entry1); TestWorld.Set64(pointers, 16, Entry3);
        var index = memory.Map(Index, 48);
        IndexEntry(index, 0, 1038, Entry1); IndexEntry(index, 16, 1759, Entry2); IndexEntry(index, 32, 20965, Entry3);
        // Three 24-byte records copied from the live Game Pass retirement manager; only pointers above are relocated.
        Convert.FromHexString("0E040000FFFFFFFF4F01E8076D01EA074E01E80700000500").CopyTo(memory.Map(Entry1, 24), 0);
        Convert.FromHexString("DF0600001E0800004F01E807B500EB074E01E80700000500").CopyTo(memory.Map(Entry2, 24), 0);
        Convert.FromHexString("E5510000330200004F01E807B600EB074E01E80780000500").CopyTo(memory.Map(Entry3, 24), 0);
        Player(Ronaldo, 735216, 1759, 2);
        Player(Zimmermann, 35017462, 20965, 0);
    }

    void Player(ulong address, int uid, uint person, byte traits)
    {
        var p = memory.Map(address, 0x3C8);
        TestWorld.Set64(p, 0, Module + 0x4CD6318);
        BitConverter.GetBytes(person).CopyTo(p, 0x290);
        BitConverter.GetBytes(uid).CopyTo(p, 0x294);
        p[0x34E] = traits;
    }
    static void Vector(byte[] owner, int offset, ulong begin, int count, int size)
    {
        TestWorld.Set64(owner, offset, begin); TestWorld.Set64(owner, offset + 8, begin + (ulong)(count * size));
        TestWorld.Set64(owner, offset + 16, begin + (ulong)(count * size));
    }
    static void IndexEntry(byte[] index, int at, uint person, ulong entry)
    {
        BitConverter.GetBytes(person).CopyTo(index, at);
        Convert.FromHexString("72020000").CopyTo(index, at + 4);
        TestWorld.Set64(index, at + 8, entry);
    }
    PlayerRecord Read(ulong address) => new PlayerRepository(memory, profile, Module).Reload(address)!;

    [Fact]
    public void Removes_and_restores_both_indexes_and_retirement_traits()
    {
        var reader = new PlannedRetirements(memory, profile, Module);
        var writer = new KeepPlayingWriter(memory, profile, Module);
        Assert.Equal(new DateOnly(2027, 6, 30), reader.ReadAll()[1759].Date);
        Assert.Equal(new DateOnly(2027, 7, 1), reader.ReadAll()[20965].Date);
        var initialPointers = memory.Read(Pointers, 24);
        var initialIndex = memory.Read(Index, 48);
        var first = writer.Plan(Read(Ronaldo));
        writer.Apply(first);
        Assert.False(reader.ReadAll().ContainsKey(1759));
        Assert.Equal(new DateOnly(2027, 7, 1), reader.ReadAll()[20965].Date);
        Assert.Equal(2, memory.Read(Ronaldo + 0x34E, 1)[0]);
        writer.Undo(first);
        Assert.Equal(initialPointers, memory.Read(Pointers, 24));
        Assert.Equal(initialIndex, memory.Read(Index, 48));
        var second = writer.Plan(Read(Zimmermann));
        writer.Apply(second);
        Assert.False(reader.ReadAll().ContainsKey(20965));
        Assert.Equal(2, memory.Read(Zimmermann + 0x34E, 1)[0]);
        writer.Undo(second);
        Assert.Equal(0, memory.Read(Zimmermann + 0x34E, 1)[0]);
        Assert.Equal(initialPointers, memory.Read(Pointers, 24));
        Assert.Equal(initialIndex, memory.Read(Index, 48));
    }

    sealed class FailSecondWrite(FakeMemory inner) : IProcessMemory
    {
        int writes;
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> data) => ++writes == 2 ? false : inner.TryWrite(address, data);
    }

    [Fact]
    public void Failed_second_write_restores_first_list_change()
    {
        var beforeIndex = memory.Read(Index, 48);
        var beforeManager = memory.Read(Manager, 0xA8);
        var writer = new KeepPlayingWriter(new FailSecondWrite(memory), profile, Module);
        var plan = writer.Plan(Read(Ronaldo));
        Assert.Throws<MemoryAccessException>(() => writer.Apply(plan));
        Assert.Equal(beforeIndex, memory.Read(Index, 48));
        Assert.Equal(beforeManager, memory.Read(Manager, 0xA8));
    }

    [Fact]
    public void Batch_replans_shared_indexes_but_refuses_changed_player_traits()
    {
        var writer = new KeepPlayingWriter(memory, profile, Module);
        var first = writer.Plan(Read(Ronaldo));
        var second = writer.Plan(Read(Zimmermann));
        writer.Apply(first);
        Assert.Throws<StaleObjectException>(() => writer.Apply(second));
        var rebased = writer.Replan(second);
        writer.Apply(rebased);
        Assert.DoesNotContain(1759u, new PlannedRetirements(memory, profile, Module).ReadAll().Keys);
        Assert.DoesNotContain(20965u, new PlannedRetirements(memory, profile, Module).ReadAll().Keys);
        writer.Undo(rebased);
        writer.Undo(first);
        Assert.True(memory.TryWrite(Zimmermann + 0x34E, [1]));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.Replan(second));
        Assert.Equal(writes, memory.Writes);
    }
}
