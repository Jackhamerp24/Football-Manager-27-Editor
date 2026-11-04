using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Career plans as dumped on 15/8/2026 (item pointers re-pointed at fakes). Bruno Fernandes: Doesn't want to be a (2),
/// Club (1), Player (1), Nation (9) → "68.. 02 01 01 09 02000000"; Jari Litmanen: Wants to be a Coach (2) at Ajax (club, 3).
/// FMRTE 27 showed Litmanen's rows as "Wants to be a / Coach / AFC Ajax / Club"; writing "Has Knowledge Of" on Bruno changed
/// +8 to 03 only; + allocated a 16-byte plan and a new pointer vector; − moved the end pointer back 8 (2/11/2026).
/// </summary>
public class CareerPlanTests
{
    const uint ClubVt = 0x4CDA1F8, NationVt = 0x4CDF658, NationTeamVt = 0x4CEC0B8;
    const ulong Container = 0x5000_0000, Vector = 0x5000_1000, Plans = 0x5001_0000, Objects = 0x5100_0000;
    readonly TestWorld _w = new();
    ulong _next = Objects;
    ulong Ajax, Saudi;

    ulong Obj(uint vt, int size = 0x200) { ulong at = _next; _next += 0x1000; TestWorld.Set64(_w.Memory.Map(at, size), 0, TestWorld.ModuleBase + vt); return at; }
    ulong Text(string s) { ulong at = _next; _next += 0x100; _w.Memory.MapString(at, s); return at; }

    public CareerPlanTests()
    {
        Ajax = Obj(ClubVt);
        Assert.True(_w.Memory.TryWrite(Ajax + 0xC0, BitConverter.GetBytes(Text("AFC Ajax"))));
        Assert.True(_w.Memory.TryWrite(Ajax + 0xC8, BitConverter.GetBytes(Text("Ajax"))));
        Saudi = Obj(NationTeamVt);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x350, BitConverter.GetBytes(Container)));
        var c = _w.Memory.Map(Container, 0x60);
        TestWorld.Set64(c, 0, Vector); TestWorld.Set64(c, 8, Vector + 16); TestWorld.Set64(c, 16, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, Plans); TestWorld.Set64(v, 8, Plans + 0x100);
        Plan(Plans, Saudi, "0201010902000000");
        Plan(Plans + 0x100, Ajax, "0101020302000000");
    }

    void Plan(ulong at, ulong item, string tail) => Assert.True(_w.Memory.Map(at, 16) is { } b && _w.Memory.TryWrite(at, [.. BitConverter.GetBytes(item), .. Convert.FromHexString(tail)]));

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    CareerPlanReader Reader() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    CareerPlanWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    const ulong Pool = 0x7000_0000;
    ulong _pool = Pool;
    readonly List<int> _allocations = [];
    ulong Allocate(int bytes) { _allocations.Add(bytes); ulong at = _pool; _w.Memory.Map(at, bytes); _pool += 0x1_0000; return at; }

    [Fact]
    public void Reads_plans_as_FMRTE_shows_them()
    {
        var p = Reader().Read(Player())!;
        Assert.Equal([(2, 1, 1, 9), (1, 1, 2, 3)], p.Select(x => (x.Condition, x.Scope, x.Job, x.ItemType)));
        Assert.Equal("AFC Ajax", p[1].ItemName);
        Assert.Equal(CareerItemType.Club, Reader().Item(Ajax)!.Type);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x350, BitConverter.GetBytes(0UL)));
        Assert.Null(Reader().Read(Player()));
    }

    [Fact]
    public void Setting_a_condition_changes_only_that_byte()
    {
        var plans = Reader().Read(Player())!;
        var before = _w.Bytes(Plans, 16);
        var item = new CareerItem(Saudi, CareerItemType.Nation, "Saudi Arabia");
        Writer().Set(Player(), plans, plans[0], new(3, 1, 1, item));
        before[8] = 3;
        Assert.Equal(before, _w.Bytes(Plans, 16));
        Assert.Throws<StaleObjectException>(() => Writer().Set(Player(), plans, plans[0], new(4, 1, 1, item)));
    }

    [Fact]
    public void One_save_can_set_several_rows()
    {
        var plans = Reader().Read(Player())!;
        var ajax = new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax");
        Writer().Set(Player(), plans, plans[0], new(3, 1, 1, new CareerItem(Saudi, CareerItemType.Nation, "Saudi Arabia")));
        Writer().Set(Player(), plans, plans[1], new(1, 2, 20, ajax));
        Assert.Equal(Convert.FromHexString("0102140302000000"), _w.Bytes(Plans + 0x108, 8));
    }

    [Fact]
    public void Add_appends_a_game_style_plan_and_remove_moves_the_end_back()
    {
        var plans = Reader().Read(Player())!;
        var ajax = new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax");
        Writer().Add(Player(), plans, new(1, 1, 16, ajax), Allocate);
        // Full vector: plan record, then a 4-slot pointer buffer. The game's rows all carry 2 at +0xC (FMRTE wrote 0).
        Assert.Equal([16, 32], _allocations);
        Assert.Equal([.. BitConverter.GetBytes(Ajax), .. Convert.FromHexString("0101100302000000")], _w.Bytes(Pool, 16));
        Assert.Equal([.. BitConverter.GetBytes(Plans), .. BitConverter.GetBytes(Plans + 0x100), .. BitConverter.GetBytes(Pool)], _w.Bytes(Pool + 0x1_0000, 24));
        Assert.Equal([.. BitConverter.GetBytes(Pool + 0x1_0000), .. BitConverter.GetBytes(Pool + 0x1_0000 + 24), .. BitConverter.GetBytes(Pool + 0x1_0000 + 32)], _w.Bytes(Container, 24));

        var now = Reader().Read(Player())!;
        Writer().Remove(Player(), now, now[0]);
        Assert.Equal([.. BitConverter.GetBytes(Plans + 0x100), .. BitConverter.GetBytes(Pool)], _w.Bytes(Pool + 0x1_0000, 16));
        Assert.Equal(Pool + 0x1_0000 + 16, BitConverter.ToUInt64(_w.Bytes(Container + 8, 8)));
    }

    [Fact]
    public void Writes_refuse_items_of_the_wrong_kind_and_stale_lists()
    {
        var plans = Reader().Read(Player())!;
        var nation = Obj(NationVt);
        Assert.Throws<StaleObjectException>(() => Writer().Set(Player(), plans, plans[1], new(1, 1, 2, new CareerItem(Saudi, CareerItemType.Club, "x"))));
        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), plans, new(1, 1, 2, new CareerItem(nation, CareerItemType.Nation, "x")), Allocate));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(Player(), plans, plans[1], new(1, 3, 2, new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax"))));
        Assert.True(_w.Memory.TryWrite(Container + 8, BitConverter.GetBytes(Vector + 8)));
        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), plans, new(1, 1, 2, new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax")), Allocate));
        Assert.Empty(_allocations);
    }

    [Fact]
    public void Add_rejects_misaligned_empty_vector_before_allocating_or_writing()
    {
        Assert.True(_w.Memory.TryWrite(Container + 8, BitConverter.GetBytes(Vector)));
        var plans = Reader().Read(Player())!;
        Assert.Empty(plans);
        Assert.True(_w.Memory.TryWrite(Container + 8, BitConverter.GetBytes(Vector + 1)));
        var header = _w.Bytes(Container, 24);
        var entries = _w.Bytes(Vector, 16);
        int writes = _w.Memory.Writes;
        var ajax = new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax");

        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), plans, new(1, 1, 16, ajax), Allocate));
        Assert.Empty(_allocations);
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(header, _w.Bytes(Container, 24));
        Assert.Equal(entries, _w.Bytes(Vector, 16));
    }

    [Fact]
    public void Set_rejects_misaligned_vector_capacity_before_writing()
    {
        var plans = Reader().Read(Player())!;
        Assert.True(_w.Memory.TryWrite(Container + 16, BitConverter.GetBytes(Vector + 17)));
        var record = _w.Bytes(Plans, 16);
        int writes = _w.Memory.Writes;
        var item = new CareerItem(Saudi, CareerItemType.Nation, "Saudi Arabia");

        Assert.Throws<StaleObjectException>(() => Writer().Set(Player(), plans, plans[0], new(3, 1, 1, item)));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(record, _w.Bytes(Plans, 16));
    }

    [Fact]
    public void Add_rejects_a_different_empty_vector_since_snapshot()
    {
        Assert.True(_w.Memory.TryWrite(Container + 8, BitConverter.GetBytes(Vector)));
        var plans = Reader().Read(Player())!;
        Assert.Empty(plans);
        const ulong replacement = 0x5000_3000;
        _w.Memory.Map(replacement, 16);
        Assert.True(_w.Memory.TryWrite(Container, [.. BitConverter.GetBytes(replacement), .. BitConverter.GetBytes(replacement), .. BitConverter.GetBytes(replacement + 16)]));
        int writes = _w.Memory.Writes;
        var ajax = new CareerItem(Ajax, CareerItemType.Club, "AFC Ajax");

        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), plans, new(1, 1, 16, ajax), Allocate));
        Assert.Empty(_allocations);
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(new byte[16], _w.Bytes(replacement, 16));
    }

    [Fact]
    public void Set_rejects_a_changed_capacity_since_snapshot()
    {
        var plans = Reader().Read(Player())!;
        Assert.True(_w.Memory.TryWrite(Container + 16, BitConverter.GetBytes(Vector + 24)));
        var record = _w.Bytes(Plans, 16);
        int writes = _w.Memory.Writes;
        var item = new CareerItem(Saudi, CareerItemType.Nation, "Saudi Arabia");

        Assert.Throws<StaleObjectException>(() => Writer().Set(Player(), plans, plans[0], new(3, 1, 1, item)));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(record, _w.Bytes(Plans, 16));
    }
}
