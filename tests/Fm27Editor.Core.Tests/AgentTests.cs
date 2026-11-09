using Fm27Editor.Core.Officials;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A fake agent pool laid out like the live Game Pass 27.0.0+2493426 one: module slot +0x5685B30 → holder → chunk,
/// 0xE0-byte records whose first qword is vtable +0x4DDEB18, names at +0x28/+0x30, clients vector +0x70, scope +0xA8,
/// patience +0xB0, business +0xB1, agency size +0xB2, touting +0xB3, row id +0xC8, unique id +0xCC.
/// The values are the live read of the first agents (Toufik Boufennara, row 97749, uid 2002071602).
/// </summary>
public class AgentTests
{
    const ulong Slot = 0x5685B30, Holder = 0x900000, Chunk = 0x910000, Clients = 0x920000;
    const uint Vt = 0x4DDEB18;
    const int Stride = 0xE0;

    readonly TestWorld _w = new();
    readonly byte[] _pool;

    public AgentTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + Slot, 8), 0, Holder);
        TestWorld.Set64(_w.Memory.Map(Holder, 8), 0, Chunk);
        var pool = _pool = _w.Memory.Map(Chunk, Stride * 3);
        Agent(pool, 0, "Toufik", "Boufennara", scope: 1, patience: 4, business: 17, agency: 18, touting: 10, row: 97749, uid: 2002071602);
        Agent(pool, 1, "José", "Pedro", scope: 1, patience: 3, business: 17, agency: 18, touting: 3, row: 97750, uid: 2002071603);
        Agent(pool, 2, "Hussein", "Diab", scope: 1, patience: 2, business: 15, agency: 15, touting: 6, row: 97751, uid: 2002071604);
        var clients = _w.Memory.Map(Clients, 16);
        TestWorld.Set64(clients, 0, 0x1000_0000);
        TestWorld.Set64(clients, 8, 0x1000_1000);
    }

    void Agent(byte[] pool, int index, string first, string last, byte scope, byte patience, byte business, byte agency,
        byte touting, int row, uint uid)
    {
        int at = index * Stride;
        TestWorld.Set64(pool, at, TestWorld.ModuleBase + Vt);
        Name(pool, at + 0x28, first);
        Name(pool, at + 0x30, last);
        TestWorld.Set64(pool, at + 0x70, Clients);
        TestWorld.Set64(pool, at + 0x78, Clients + 16);
        pool[at + 0xA8] = scope;
        pool[at + 0xB0] = patience;
        pool[at + 0xB1] = business;
        pool[at + 0xB2] = agency;
        pool[at + 0xB3] = touting;
        BitConverter.GetBytes(row).CopyTo(pool, at + 0xC8);
        BitConverter.GetBytes(uid).CopyTo(pool, at + 0xCC);
    }

    void Name(byte[] pool, int at, string text)
    {
        ulong entry = 0x800000 + (ulong)at * 0x10, stringAt = entry + 0x100000;
        TestWorld.Set64(pool, at, entry);
        _w.Memory.MapString(stringAt, text);
        TestWorld.Set64(_w.Memory.Map(entry, 8), 0, stringAt);
    }

    AgentDirectory Directory() => new(_w.Memory, TestWorld.ModuleBase);

    [Fact]
    public void Reads_every_agent_with_names_rates_and_client_count()
    {
        var all = Directory().ReadAll();
        Assert.Equal(3, all.Count);
        var first = all[0];
        Assert.Equal("Toufik Boufennara", first.DisplayName);
        Assert.Equal((1, 4, 17, 18, 10), (first.Scope, first.Patience, first.Business, first.AgencySize, first.ToutingClients));
        Assert.Equal((2, 97749, 2002071602u), (first.Clients, first.RowId, first.Uid));
        Assert.Equal((97751, 2002071604u), (all[2].RowId, all[2].Uid));
    }

    [Fact]
    public void Reads_nothing_when_the_slot_or_holder_is_empty()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.ModuleBase + Slot, new byte[8]));
        Assert.Empty(Directory().ReadAll());
        Assert.Equal(0UL, Directory().Chunk);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + Slot, 8), 0, Holder);
        Assert.True(_w.Memory.TryWrite(Holder, new byte[8]));
        Assert.Empty(Directory().ReadAll());
    }

    [Fact]
    public void Stops_at_the_first_record_that_is_not_an_agent()
    {
        Assert.Equal(3, Directory().ReadAll().Count);
        TestWorld.Set64(_pool, 2 * Stride, TestWorld.ModuleBase + 0x1234);
        Assert.Equal(2, Directory().ReadAll().Count);
        Assert.True(_w.Memory.TryWrite(Chunk, BitConverter.GetBytes(0x1234UL)));
        Assert.Empty(Directory().ReadAll());
    }

    [Fact]
    public void Set_writes_one_rate_and_reads_it_back()
    {
        var directory = Directory();
        var first = directory.ReadAll()[0];
        new AgentWriter(_w.Memory).Set(first, AgentField.Patience, 20);
        var after = directory.ReadAll()[0];
        Assert.Equal((20, 17), (after.Patience, after.Business));
        Assert.Equal(first.Scope, after.Scope);
        Assert.Equal(first.DisplayName, after.DisplayName);
        Assert.Equal(3, _w.Bytes(Chunk + Stride + 0xB0, 1)[0]);                   // the next agent is untouched
    }

    [Fact]
    public void Set_refuses_an_agent_that_changed_since_it_was_read()
    {
        var directory = Directory();
        var first = directory.ReadAll()[0];
        Assert.True(_w.Memory.TryWrite(Chunk + 0xB1, [5]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => new AgentWriter(_w.Memory).Set(first, AgentField.Patience, 20));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(4, directory.ReadAll()[0].Patience);
    }

    [Fact]
    public void Set_rejects_rates_outside_one_to_twenty()
    {
        var first = Directory().ReadAll()[0];
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentWriter(_w.Memory).Set(first, AgentField.Patience, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentWriter(_w.Memory).Set(first, AgentField.Scope, 21));
        Assert.Equal(writes, _w.Memory.Writes);
    }
}
