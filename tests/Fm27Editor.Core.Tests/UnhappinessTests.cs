using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Unhappiness from FM27 RAM: contract + 0x30 → header (int32 count, pointer to 0x1C-byte records). Record bytes are
/// Man Utd players' as dumped on game date 8/11/2026; expected values are FMRTE 27's Contract tab: Bruno Fernandes
/// "Unhappy at being stripped of the captaincy" (motive 600), Concerned (type 1), from 15/8/2026, End Date 1/1/1900;
/// Patrick Dorgu (ended 26/10/2026) and Mauro Icardi (same motive, ends 8/11/2026) show no unhappiness.
/// </summary>
public class UnhappinessTests
{
    const ulong Contract = 0x4000_0000, Header = 0x4200_0000, Records = 0x4200_1000;
    const uint ContractVtable = 0x4B00AE8;
    const string Dorgu = "2B4FEA072B5FEA07FFFFFFFFFFFFFFFF0000000000008204012500000",
        Bruno = "E33CEA0701006C0702540000FFFFFFFF0000000000005802010500040",
        Icardi = "E51AEA073813EA0702540000FFFFFFFF0000000000005802010500040";
    static readonly DateOnly GameDate = new(2026, 11, 8);
    readonly TestWorld _w = new();

    static byte[] Rec(string hex) => Convert.FromHexString(hex[..56]);

    public UnhappinessTests()
    {
        var c = _w.Memory.Map(Contract, 0xC0);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        TestWorld.Set64(c, 0x30, Header);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
        var h = _w.Memory.Map(Header, 0x10);
        BitConverter.GetBytes(3).CopyTo(h, 0);
        TestWorld.Set64(h, 8, Records);
        byte[] all = [.. Rec(Dorgu), .. Rec(Bruno), .. Rec(Icardi)];
        all.CopyTo(_w.Memory.Map(Records, all.Length), 0);
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    IReadOnlyList<PlayerUnhappiness> Read() => new UnhappinessReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());

    [Fact]
    public void Reversible_removal_restores_all_records_and_refuses_changed_data()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var writer = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var plan = writer.PlanRemoveActive(Player(), GameDate)!;
        Assert.Equal((3, 2), (plan.OldCount, plan.NewCount));
        int changes = 0;
        writer.ApplyRemoval(plan, () => changes++);
        Assert.Equal(1, changes);
        Assert.Equal([.. Rec(Dorgu), .. Rec(Icardi)], _w.Bytes(Records, 2 * 0x1C));
        var changed = _w.Bytes(Records, 1);
        Assert.True(_w.Memory.TryWrite(Records, [(byte)(changed[0] ^ 1)]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.UndoRemoval(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.True(_w.Memory.TryWrite(Records, changed));
        writer.UndoRemoval(plan);
        Assert.Equal(3, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal([.. Rec(Dorgu), .. Rec(Bruno), .. Rec(Icardi)], _w.Bytes(Records, 3 * 0x1C));
    }

    [Fact]
    public void Reads_the_motives_and_only_Bruno_is_active_on_the_game_date()
    {
        var m = Read();
        Assert.Equal(3, m.Count);
        var bruno = m[1];
        Assert.Equal(new DateOnly(2026, 8, 15), bruno.Start);
        Assert.Null(bruno.End);
        Assert.Equal(600, bruno.Motive);
        Assert.Equal(1, bruno.Type);
        Assert.Equal(new DateOnly(2026, 10, 26), m[0].End);
        Assert.Equal(GameDate, m[2].End);
        Assert.Equal([1], m.Where(x => x.IsActive(GameDate)).Select(x => x.Index));
    }

    [Fact]
    public void Remove_takes_out_active_motives_and_keeps_ended_ones_packed()
    {
        int removed = new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate);

        Assert.Equal(1, removed);
        Assert.Equal(2, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal([.. Rec(Dorgu), .. Rec(Icardi)], _w.Bytes(Records, 2 * 0x1C));
        Assert.DoesNotContain(Read(), x => x.IsActive(GameDate));
        Assert.Equal(0, new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate));
    }

    [Fact]
    public void Other_contract_kinds_and_bad_headers_give_nothing()
    {
        Assert.True(_w.Memory.TryWrite(Header, BitConverter.GetBytes(-605633096)));
        Assert.Empty(Read());
        Assert.True(_w.Memory.TryWrite(Header, BitConverter.GetBytes(3)));
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4B00000)));
        Assert.Empty(Read());
        Assert.Equal(0, new UnhappinessWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).RemoveActive(Player(), GameDate));
    }
}
