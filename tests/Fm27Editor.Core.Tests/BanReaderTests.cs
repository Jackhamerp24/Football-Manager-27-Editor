using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Bans from FM27 RAM: injury holder (player + 0xF8) + 0x18 → vector of pointers to 0x20-byte records. Record bytes are
/// Alex Oxlade-Chamberlain's and Hiram Boateng's as dumped; expected values are FMRTE 27's Unavailability tab: both from
/// 13/7/2026, all nation competitions (Scotland 167 / England 139), 3 matches (reason None) and 7 matches (Sending off).
/// The dump predates Boateng serving two matches: fulfilled is 0 here, while FMRTE showed 7/2 on 8/11/2026.
/// </summary>
public class BanReaderTests
{
    const ulong Holder = 0x5000_0000, Vector = 0x5001_0000, Rec1 = 0x5002_0000, Rec2 = 0x5003_0000;
    readonly TestWorld _w = new();

    public BanReaderTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x30); // injuries vector at +0 stays empty
        TestWorld.Set64(h, 0x18, Vector); TestWorld.Set64(h, 0x20, Vector + 16); TestWorld.Set64(h, 0x28, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, Rec1); TestWorld.Set64(v, 8, Rec2);
        Convert.FromHexString("A7000000FF0A00000000000000000000C21AEA07DE00FFFF0000FFFF03000000").CopyTo(_w.Memory.Map(Rec1, 0x20), 0);
        Convert.FromHexString("8B000000FF0A00000000000000000000C21AEA078F00FFFF000002FF07000000").CopyTo(_w.Memory.Map(Rec2, 0x20), 0);
    }

    IReadOnlyList<PlayerBan> Read() => new BanReader(_w.Memory, _w.Profile, TestWorld.ModuleBase)
        .Read(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!);

    [Fact]
    public void Reversible_lift_preserves_day_bans_and_rejects_modified_records()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        Assert.True(_w.Memory.TryWrite(Rec2 + 0x16, BitConverter.GetBytes((short)595)));
        Assert.True(_w.Memory.TryWrite(Rec2 + 0x1C, [0xFF]));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var writer = new BanWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var plan = writer.PlanRemoveMatchBans(player)!;
        Assert.Equal((2, 1), (plan.OldCount, plan.NewCount));
        writer.ApplyRemoval(plan);
        Assert.Equal((short)595, Assert.Single(Read()).Days);
        Assert.True(_w.Memory.TryWrite(Rec1 + 0x1C, [4]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.UndoRemoval(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.True(_w.Memory.TryWrite(Rec1 + 0x1C, [3]));
        writer.UndoRemoval(plan);
        Assert.Equal(new[] { Rec1, Rec2 }, Read().Select(b => b.Address));
        Assert.Equal((short)595, Read()[1].Days);
    }

    [Fact]
    public void Match_counts_touch_only_two_bytes_and_refuse_stale_records()
    {
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var records = Read();
        var writer = new BanWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var a = _w.Bytes(Rec1, 0x20);
        writer.SetMatches(player, records[0], 4, 1);
        a[0x1C] = 4; a[0x1D] = 1;
        Assert.Equal(a, _w.Bytes(Rec1, 0x20));
        var b = _w.Bytes(Rec2, 0x20);
        writer.SetMatches(player, records[1], 8, 2);
        b[0x1C] = 8; b[0x1D] = 2;
        Assert.Equal(b, _w.Bytes(Rec2, 0x20));
        Assert.Throws<StaleObjectException>(() => writer.SetMatches(player, records[0], 3, 0));
        var fresh = Read()[0];
        Assert.True(_w.Memory.TryWrite(Holder + 0x20, BitConverter.GetBytes(Vector)));
        Assert.Throws<StaleObjectException>(() => writer.SetMatches(player, fresh, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetMatches(player, fresh, 3, 4));
    }

    [Fact]
    public void Reads_both_bans_as_FMRTE_shows_them()
    {
        var b = Read();
        Assert.Equal([167, 139], b.Select(x => x.ItemId));
        Assert.All(b, x => Assert.Equal(10, x.Scope));
        Assert.All(b, x => Assert.Equal(new DateOnly(2026, 7, 13), x.Start));
        Assert.Equal(new sbyte[] { 3, 7 }, b.Select(x => x.Matches));
        Assert.Equal(new sbyte[] { -1, 2 }, b.Select(x => x.Reason));
        Assert.All(b, x => Assert.Equal(-1, x.Days));
        Assert.All(b, x => Assert.Equal(0, x.MatchesFulfilled));
        // The injury list in the same holder is a different vector and stays empty.
        Assert.Empty(new InjuryReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!));
    }

    /// <summary>
    /// User's rule: Remove bans removes match bans only. The list is Oxlade-Chamberlain (3 matches), a day ban laid out like
    /// Mykhaylo Mudryk's (scope 25, matches -1, 595 days, from 12/12/2024) and Boateng (7 matches): only the day ban stays.
    /// </summary>
    [Fact]
    public void Remove_match_bans_keeps_day_bans_and_compacts_the_vector()
    {
        const ulong DayBan = 0x5004_0000, Wide = 0x5005_0000;
        // Day 0x15A = 346 → 12/12/2024 (leap year); 595 = 0x0253 days; matches -1.
        Convert.FromHexString("FFFFFFFFFF19000000000000000000005A01E8070000530200000000FFFF000000").AsSpan(0, 0x20).ToArray()
            .CopyTo(_w.Memory.Map(DayBan, 0x20), 0);
        var v = _w.Memory.Map(Wide, 24);
        TestWorld.Set64(v, 0, Rec1); TestWorld.Set64(v, 8, DayBan); TestWorld.Set64(v, 16, Rec2);
        Assert.True(_w.Memory.TryWrite(Holder + 0x18, [.. BitConverter.GetBytes(Wide), .. BitConverter.GetBytes(Wide + 24), .. BitConverter.GetBytes(Wide + 24)]));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;

        int removed = new BanWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveMatchBans(player);

        Assert.Equal(2, removed);
        var left = Assert.Single(Read());
        Assert.Equal(595, left.Days);
        Assert.Equal(25, left.Scope);
        Assert.Equal(DayBan, BitConverter.ToUInt64(_w.Bytes(Wide, 8)));               // kept pointer moved to the front
        Assert.Equal(Wide + 8, BitConverter.ToUInt64(_w.Bytes(Holder + 0x20, 8)));    // end pulled back by two
        Assert.Equal(Wide + 24, BitConverter.ToUInt64(_w.Bytes(Holder + 0x28, 8)));   // capacity untouched
        Assert.Equal(0, new BanWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveMatchBans(player));
    }

    [Fact]
    public void No_holder_or_bad_vector_gives_nothing()
    {
        Assert.True(_w.Memory.TryWrite(Holder + 0x20, BitConverter.GetBytes(Vector + 17)));
        Assert.Empty(Read());
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(0UL)));
        Assert.Empty(Read());
    }

    // Expected records below are FMRTE 27's own writes on Kobbie Mainoo (2/11/2026, licensed): its new-ban default
    // (Global Ban, Sending off, 2 points, 25/11/2026, 3 matches) and the same ban switched to Competition (AIA Singapore
    // Premier League, item 736) with Reason None. Removing a ban moved only the end pointer back 8.

    const ulong Pool = 0x7000_0000;
    ulong _pool = Pool;
    readonly List<int> _allocations = [];
    ulong Allocate(int bytes) { _allocations.Add(bytes); ulong at = _pool; _w.Memory.Map(at, bytes); _pool += 0x1_0000; return at; }
    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    BanWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    static readonly DateOnly Nov25 = new(2026, 11, 25);

    [Fact]
    public void Add_writes_FMRTEs_default_ban_into_a_moved_vector()
    {
        byte[] oldRecords = [.. _w.Bytes(Rec1, 0x20), .. _w.Bytes(Rec2, 0x20)];
        Writer().Add(Player(), Read(), new(25, -1, 2, 2, Nov25, -1, 3, 0), Allocate);

        Assert.Equal([0x20, 4 * 8], _allocations);
        Assert.Equal(Convert.FromHexString("FFFFFFFFFF19000000000000000000004901EA070A00FFFF020002FF03000000"), _w.Bytes(Pool, 0x20));
        ulong buffer = Pool + 0x1_0000;
        Assert.Equal([.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + 24), .. BitConverter.GetBytes(buffer + 32)], _w.Bytes(Holder + 0x18, 24));
        Assert.Equal([.. BitConverter.GetBytes(Rec1), .. BitConverter.GetBytes(Rec2), .. BitConverter.GetBytes(Pool)], _w.Bytes(buffer, 24));
        Assert.Equal(oldRecords, (byte[])[.. _w.Bytes(Rec1, 0x20), .. _w.Bytes(Rec2, 0x20)]);
        var added = Read()[^1];
        Assert.Equal((25, 2, 3, (short)-1, Nov25), ((int)added.Scope, (int)added.Reason, (int)added.Matches, added.Days, added.Start!.Value));
    }

    [Fact]
    public void Add_with_spare_capacity_appends_in_place_and_matches_FMRTEs_competition_ban()
    {
        const ulong Roomy = 0x5006_0000;
        var v = _w.Memory.Map(Roomy, 24);
        TestWorld.Set64(v, 0, Rec1); TestWorld.Set64(v, 8, Rec2);
        Assert.True(_w.Memory.TryWrite(Holder + 0x18, [.. BitConverter.GetBytes(Roomy), .. BitConverter.GetBytes(Roomy + 16), .. BitConverter.GetBytes(Roomy + 24)]));
        Writer().Add(Player(), Read(), new(1, 736, -1, 2, Nov25, -1, 3, 0), Allocate);

        Assert.Equal([0x20], _allocations);
        Assert.Equal(Convert.FromHexString("E0020000FF01000000000000000000004901EA070A00FFFF0200FFFF03000000"), _w.Bytes(Pool, 0x20));
        Assert.Equal(Pool, BitConverter.ToUInt64(_w.Bytes(Roomy + 16, 8)));
        Assert.Equal(Roomy + 24, BitConverter.ToUInt64(_w.Bytes(Holder + 0x20, 8)));
    }

    [Fact]
    public void Fields_write_in_one_go_and_keep_the_start_dates_flag_bits()
    {
        var before = _w.Bytes(Rec1, 0x20);
        var oxlade = Read()[0];
        Writer().SetFields(Player(), oxlade, new(25, -1, 4, 1, new(2026, 7, 14), 10, 2, 1));

        var expected = (byte[])before.Clone();
        Convert.FromHexString("FFFFFFFF").CopyTo(expected, 0);
        expected[5] = 25;
        Convert.FromHexString("C31A").CopyTo(expected, 0x10);   // day 194 → 195, flag bits 0x1A00 kept
        Convert.FromHexString("0A00").CopyTo(expected, 0x16);
        expected[0x18] = 1; expected[0x1A] = 4; expected[0x1C] = 2; expected[0x1D] = 1;
        Assert.Equal(expected, _w.Bytes(Rec1, 0x20));
        Assert.Throws<StaleObjectException>(() => Writer().SetFields(Player(), oxlade, new(25, -1, 4, 1, Nov25, 10, 2, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetFields(Player(), Read()[1], new(25, -1, 4, 1, Nov25, 10, 2, 3)));
    }

    [Fact]
    public void Remove_moves_later_pointers_up_and_only_the_end_back()
    {
        byte[] records = [.. _w.Bytes(Rec1, 0x20), .. _w.Bytes(Rec2, 0x20)];
        Writer().Remove(Player(), Read()[0]);
        Assert.Equal(Rec2, BitConverter.ToUInt64(_w.Bytes(Vector, 8)));
        Assert.Equal(Vector + 8, BitConverter.ToUInt64(_w.Bytes(Holder + 0x20, 8)));
        Assert.Equal(Vector + 16, BitConverter.ToUInt64(_w.Bytes(Holder + 0x28, 8)));
        Assert.Equal(records, (byte[])[.. _w.Bytes(Rec1, 0x20), .. _w.Bytes(Rec2, 0x20)]);
        Assert.Equal([139], Read().Select(b => b.ItemId));
    }

    [Fact]
    public void Add_refuses_stale_lists_and_players_without_a_holder()
    {
        var bans = Read();
        Assert.True(_w.Memory.TryWrite(Rec2 + 0x1D, [3])); // the game counted a served match
        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), bans, new(25, -1, 2, 2, Nov25, -1, 3, 0), Allocate));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(0UL)));
        Assert.Throws<InvalidOperationException>(() => Writer().Add(Player(), [], new(25, -1, 2, 2, Nov25, -1, 3, 0), Allocate));
        Assert.Empty(_allocations);
    }
}
