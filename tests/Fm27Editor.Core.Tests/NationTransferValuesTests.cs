using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A nation object with the two national-team pointers and both transfer-value tables laid out as measured on
/// 27.0.0+2493426: nation +0x108 men's team, +0x110 women's team, array pointer at that team +0x1A8, twenty int32.
/// The expected amounts are the ones read from the live save (England men 2,000..115,000,000 and women
/// 1,500..1,150,000), so they are an independent oracle rather than a restatement of the layout.
/// </summary>
public class NationTransferValuesTests
{
    const ulong Nation = 0x800000, MenTeam = 0x810000, WomenTeam = 0x820000, MenValues = 0x830000, WomenValues = 0x840000;

    static readonly int[] EnglandMen =
        [2000, 2500, 5000, 5000, 10000, 20000, 40000, 75000, 200000, 350000, 800000, 2000000, 8000000, 20000000,
         35000000, 50000000, 65000000, 80000000, 100000000, 115000000];
    static readonly int[] EnglandWomen =
        [1500, 2000, 3000, 4000, 5000, 7500, 10000, 15000, 20000, 25000, 50000, 90000, 125000, 275000, 400000, 500000,
         700000, 800000, 925000, 1150000];

    readonly TestWorld _w = new();

    public NationTransferValuesTests()
    {
        var nation = _w.Memory.Map(Nation, 0x120);
        TestWorld.Set64(nation, 0x108, MenTeam);
        TestWorld.Set64(nation, 0x110, WomenTeam);
        var men = _w.Memory.Map(MenTeam, 0x200);
        TestWorld.Set64(men, 0x1A8, MenValues);
        var women = _w.Memory.Map(WomenTeam, 0x200);
        TestWorld.Set64(women, 0x1A8, WomenValues);
        var values = _w.Memory.Map(MenValues, 80);
        for (int i = 0; i < 20; i++) BitConverter.GetBytes(EnglandMen[i]).CopyTo(values, i * 4);
        var womenValues = _w.Memory.Map(WomenValues, 80);
        for (int i = 0; i < 20; i++) BitConverter.GetBytes(EnglandWomen[i]).CopyTo(womenValues, i * 4);
    }

    NationTransferValuesReader Reader() => new(_w.Memory);
    NationTransferValuesWriter Writer() => new(_w.Memory);
    int I32(ulong at) => BitConverter.ToInt32(_w.Bytes(at, 4), 0);

    [Fact]
    public void Reads_twenty_tiers_of_the_men_and_women_tables()
    {
        var reader = Reader();
        var men = reader.Read(Nation)!;
        var women = reader.Read(Nation, women: true)!;
        Assert.Equal(MenValues, men.ArrayAddress);
        Assert.Equal(EnglandMen, men.Values);
        Assert.Equal(WomenValues, women.ArrayAddress);
        Assert.Equal(EnglandWomen, women.Values);
        Assert.Equal(MenValues, reader.Read(Nation)!.ArrayAddress);
        Assert.Equal(WomenValues, reader.Read(Nation, women: true)!.ArrayAddress);
    }

    [Fact]
    public void Reads_null_when_a_pointer_is_missing()
    {
        Assert.True(_w.Memory.TryWrite(MenTeam + 0x1A8, new byte[8]));
        Assert.Null(Reader().Read(Nation));
        Assert.NotNull(Reader().Read(Nation, women: true));
        Assert.True(_w.Memory.TryWrite(Nation + 0x110, new byte[8]));
        Assert.Null(Reader().Read(Nation, women: true));
    }

    [Fact]
    public void Set_writes_one_tier_and_reads_it_back()
    {
        var reader = Reader();
        var men = reader.Read(Nation)!;
        Writer().Set(men, 3, 7777);
        Assert.Equal(7777, I32(MenValues + 12));
        Assert.Equal(2000, I32(MenValues));
        Assert.Equal(5000, I32(MenValues + 8));
        Assert.Equal(10000, I32(MenValues + 16));
        Assert.Equal(7777, reader.Read(Nation)!.Values[3]);
        Assert.Equal(EnglandWomen, reader.Read(Nation, women: true)!.Values);
    }

    [Fact]
    public void SetMany_saves_two_tiers_and_rejects_a_replaced_table()
    {
        const ulong nationVtable = 0x12345678, teamVtable = 0x87654321;
        Assert.True(_w.Memory.TryWrite(Nation, BitConverter.GetBytes(nationVtable)));
        Assert.True(_w.Memory.TryWrite(MenTeam, BitConverter.GetBytes(teamVtable)));
        var snapshot = Reader().Read(Nation)!;
        Writer().SetMany(Nation, nationVtable, teamVtable, snapshot, new Dictionary<int, int> { [0] = 1234, [19] = 987654 });
        var after = Reader().Read(Nation)!;
        Assert.Equal((1234, 2500, 987654), (after.Values[0], after.Values[1], after.Values[19]));
        var replacement = _w.Memory.Map(0x850000, 80);
        Convert.FromHexString(after.Snapshot).CopyTo(replacement, 0);
        Assert.True(_w.Memory.TryWrite(MenTeam + 0x1A8, BitConverter.GetBytes(0x850000UL)));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetMany(Nation, nationVtable, teamVtable, after, new Dictionary<int, int> { [0] = 1 }));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(1234, I32(MenValues));
        Assert.True(_w.Memory.TryWrite(MenTeam + 0x1A8, BitConverter.GetBytes(MenValues)));
        Assert.True(_w.Memory.TryWrite(MenTeam, BitConverter.GetBytes(0x42UL)));
        writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetMany(Nation, nationVtable, teamVtable, after, new Dictionary<int, int> { [0] = 1 }));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Set_refuses_a_table_that_changed_since_it_was_read()
    {
        var reader = Reader();
        var men = reader.Read(Nation)!;
        Assert.True(_w.Memory.TryWrite(MenValues + 40, BitConverter.GetBytes(1)));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().Set(men, 0, 999));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(2000, I32(MenValues));
    }

    [Fact]
    public void Set_rejects_index_and_value_out_of_range()
    {
        var men = Reader().Read(Nation)!;
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(men, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(men, 20, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(men, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(men, 0, 2_000_000_001));
        Assert.Equal(writes, _w.Memory.Writes);
    }
}
