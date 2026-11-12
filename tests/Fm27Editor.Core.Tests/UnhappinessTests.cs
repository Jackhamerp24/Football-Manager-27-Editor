using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Unhappiness from FM27 RAM: contract + 0x30 → header (int32 count, then one 8-byte pointer per motive from +8), each
/// pointer to its own 0x1C-byte record. Records live in a shared pool, so each test record sits next to a foreign
/// record (another player's) that a back-to-back read would wrongly pick up.
/// Record bytes and expected values:
/// - Man Utd, game date 8/11/2026, FMRTE 27 Contract tab: Bruno Fernandes "Unhappy at being stripped of the captaincy"
///   (600) from 15/8/2026, End Date 1/1/1900; Patrick Dorgu (ended 26/10/2026) and Mauro Icardi (same motive, ends
///   8/11/2026) show nothing.
/// - Steam 27.0.1, game date 15/12/2026: Tahith Chong's four records as dumped through his pointer array; FMRTE lists
///   exactly two, "Thinks 'Person' should be given the playing time proposed to him" (802, Jamal Baptiste) and
///   "Thinks 'Person' deserves the playing time he's getting" (902, Matt Doherty), both Concerned, from 2/12/2026,
///   End Date 1/1/1900.
/// </summary>
public class UnhappinessTests
{
    const ulong Contract = 0x4000_0000, Header = 0x4200_0000, Pool = 0x4200_1000;
    const uint ContractVtable = 0x4B00AE8;
    const string Dorgu = "2B4FEA072B5FEA07FFFFFFFFFFFFFFFF000000000000820401250000",
        Bruno = "E33CEA0701006C0702540000FFFFFFFF000000000000580201050004",
        Icardi = "E51AEA073813EA0702540000FFFFFFFF000000000000580201050004",
        // Another player's record, placed right after each of ours in the pool.
        Foreign = "352BEA0701006C07FFFFFFFFFFFFFFFFFFFFFFFF0200750201080006";
    static readonly string[] Chong =
    [
        "C828EA07D038EA075A480000FFFFFFFF0000000000002B0301130000",
        "043DEA072A25EA075A480000FFFFFFFF0000000000002A0301130000",
        "5015EA0701006C07AD4A0000FFFFFFFF000000000000220301130000",
        "5027EA0701006C0754660000FFFFFFFF000000000000860301140000",
    ];
    static readonly DateOnly GameDate = new(2026, 11, 8);
    readonly TestWorld _w = new();
    readonly List<ulong> _pointers = [];

    public UnhappinessTests()
    {
        var c = _w.Memory.Map(Contract, 0xC0);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        TestWorld.Set64(c, 0x30, Header);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
        // Writers check the player's id in memory.
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        _w.Memory.Map(Header, 0x100);
        _w.Memory.Map(Pool, 0x1000);
        SetRecords(Dorgu, Bruno, Icardi);
    }

    /// <summary>Lays the records out in the pool in reverse order, each followed by <see cref="Foreign"/>, and points the header at them.</summary>
    void SetRecords(params string[] hex)
    {
        _pointers.Clear();
        for (int i = 0; i < hex.Length; i++)
        {
            ulong at = Pool + (ulong)((hex.Length - 1 - i) * 2 * 0x1C);
            Assert.True(_w.Memory.TryWrite(at, Convert.FromHexString(hex[i])));
            Assert.True(_w.Memory.TryWrite(at + 0x1C, Convert.FromHexString(Foreign)));
            _pointers.Add(at);
        }
        byte[] header = [.. BitConverter.GetBytes((long)hex.Length), .. _pointers.SelectMany(BitConverter.GetBytes)];
        Assert.True(_w.Memory.TryWrite(Header, header));
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    IReadOnlyList<PlayerUnhappiness> Read() => new UnhappinessReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());
    ulong Slot(int i) => BitConverter.ToUInt64(_w.Bytes(Header + 8 + (ulong)(i * 8), 8));

    [Fact]
    public void Reads_each_motive_through_its_pointer_and_only_Bruno_is_active_on_the_game_date()
    {
        var m = Read();
        Assert.Equal(3, m.Count);
        var bruno = m[1];
        Assert.Equal(new DateOnly(2026, 8, 15), bruno.Start);
        Assert.Null(bruno.End);
        Assert.Equal(600, bruno.Motive);
        Assert.Equal(new DateOnly(2026, 10, 26), m[0].End);
        Assert.Equal(GameDate, m[2].End);
        Assert.Equal([1], m.Where(x => x.IsActive(GameDate)).Select(x => x.Index));
        Assert.DoesNotContain(m, x => x.Motive == 629);
    }

    [Fact]
    public void Tahith_Chong_shows_the_two_open_motives_FMRTE_lists()
    {
        SetRecords(Chong);
        var today = new DateOnly(2026, 12, 15);
        var open = Read().Where(x => x.IsActive(today)).ToList();
        Assert.Equal([(short)802, (short)902], open.Select(x => x.Motive));
        Assert.All(open, x => Assert.Equal(new DateOnly(2026, 12, 2), x.Start));
        Assert.All(open, x => Assert.Null(x.End));
        Assert.All(open, x => Assert.Equal(1, x.Type));
        // Person indexes of Jamal Baptiste and Matt Doherty (player + 0x290 on Steam 27.0.1).
        Assert.Equal([19117, 26196], open.Select(x => x.Item1));
    }

    [Fact]
    public void Reversible_removal_compacts_the_pointer_array_and_leaves_records_alone()
    {
        var writer = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var pool = _w.Bytes(Pool, 6 * 0x1C);
        var plan = writer.PlanRemoveActive(Player(), GameDate)!;
        Assert.Equal((3, 2), (plan.OldCount, plan.NewCount));
        int changes = 0;
        writer.ApplyRemoval(plan, () => changes++);
        Assert.True(changes >= 1);
        Assert.Equal(2, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal([_pointers[0], _pointers[2]], [Slot(0), Slot(1)]);
        Assert.Equal(pool, _w.Bytes(Pool, 6 * 0x1C));

        var changed = _w.Bytes(Header + 8, 1);
        Assert.True(_w.Memory.TryWrite(Header + 8, [(byte)(changed[0] ^ 8)]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.UndoRemoval(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.True(_w.Memory.TryWrite(Header + 8, changed));
        writer.UndoRemoval(plan);
        Assert.Equal(3, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal(_pointers, [Slot(0), Slot(1), Slot(2)]);
    }

    [Fact]
    public void Remove_takes_out_active_motives_by_pointer_and_never_writes_the_pool()
    {
        var pool = _w.Bytes(Pool, 6 * 0x1C);
        int removed = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate);

        Assert.Equal(1, removed);
        Assert.Equal(2, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal([_pointers[0], _pointers[2]], [Slot(0), Slot(1)]);
        Assert.Equal(pool, _w.Bytes(Pool, 6 * 0x1C));
        Assert.DoesNotContain(Read(), x => x.IsActive(GameDate));
        Assert.Equal(0, new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate));
    }

    [Fact]
    public void Other_contract_kinds_and_bad_headers_give_nothing()
    {
        Assert.True(_w.Memory.TryWrite(Header, BitConverter.GetBytes(-605633096)));
        Assert.Empty(Read());
        Assert.True(_w.Memory.TryWrite(Header, BitConverter.GetBytes(3)));
        Assert.True(_w.Memory.TryWrite(Header + 8, BitConverter.GetBytes(0UL)));
        Assert.Empty(Read());
        Assert.True(_w.Memory.TryWrite(Header + 8, BitConverter.GetBytes(_pointers[0])));
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4B00000)));
        Assert.Empty(Read());
        Assert.Equal(0, new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate));
    }

    const string BrunoNow = "6101EA0701006C0702540000FFFFFFFF000000000000580201050004";

    [Fact]
    public void Edit_writes_through_the_pointer_and_keeps_the_other_bytes()
    {
        SetRecords(BrunoNow);
        var writer = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var row = Read()[0];
        writer.Edit(Player(), row, new(802, 1, new DateOnly(2026, 12, 10), new DateOnly(2027, 1, 31), 17751));
        var r = _w.Bytes(_pointers[0], 0x1C);
        // 10/12/2026 = day 344 (0x158) of 0x07EA; 31/1/2027 = day 31 (0x1F) of 0x07EB; person 17751 = 0x4557.
        Assert.Equal("5801EA071F00EB0757450000", Convert.ToHexString(r, 0, 12));
        Assert.Equal(802, BitConverter.ToInt16(r, 0x16));
        Assert.Equal(Convert.FromHexString(BrunoNow)[0x0C..0x16], r[0x0C..0x16]);
        Assert.Equal(Convert.FromHexString(BrunoNow)[0x18..], r[0x18..]);
        Assert.Equal(Convert.FromHexString(Foreign), _w.Bytes(_pointers[0] + 0x1C, 0x1C));
        Assert.Throws<StaleObjectException>(() => writer.Edit(Player(), row, new(600, 1, new DateOnly(2026, 8, 15), null)));
    }

    [Fact]
    public void Add_writes_a_new_record_and_a_new_header_and_switches_the_contract_pointer()
    {
        SetRecords(BrunoNow);
        const ulong Blocks = 0x4300_0000;
        _w.Memory.Map(Blocks, 0x100);
        var sizes = new List<int>();
        ulong next = Blocks;
        var writer = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        writer.Add(Player(), new(802, 1, new DateOnly(2026, 12, 19), null, 17751), size =>
        {
            sizes.Add(size);
            ulong at = next;
            next += 0x40;
            return at;
        });

        Assert.Equal([0x1C, 8 + 2 * 8], sizes);
        ulong newHeader = Blocks + 0x40;
        Assert.Equal(newHeader, BitConverter.ToUInt64(_w.Bytes(Contract + 0x30, 8)));
        Assert.Equal(2, BitConverter.ToInt32(_w.Bytes(newHeader, 4)));
        Assert.Equal(_pointers[0], BitConverter.ToUInt64(_w.Bytes(newHeader + 8, 8)));
        Assert.Equal(Blocks, BitConverter.ToUInt64(_w.Bytes(newHeader + 16, 8)));
        Assert.Equal(1, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        var m = Read();
        Assert.Equal(2, m.Count);
        Assert.Equal((short)802, m[1].Motive);
        Assert.Equal(new DateOnly(2026, 12, 19), m[1].Start);
        Assert.Null(m[1].End);
        Assert.Equal(17751, m[1].Item1);
    }
}
