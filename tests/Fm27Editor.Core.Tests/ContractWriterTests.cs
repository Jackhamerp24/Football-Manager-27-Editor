using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Contract layout measured on FM27 27.0.0 (Acerbi at PSG, checked in game): wage int32 +0x20, expiry +0x48,
/// agreed playing time byte +0x54 (Squad Player = 4, Impact Sub = 5, as the game UI showed after the write).
/// </summary>
public class ContractWriterTests
{
    const ulong Contract = 0x4000_0000;
    const uint ContractVtable = 0x4B00AE8;
    readonly TestWorld _w = new();

    public ContractWriterTests()
    {
        var c = _w.Memory.Map(Contract, 0xC0);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        BitConverter.GetBytes(40130).CopyTo(c, 0x20);
        TestWorld.Set16(c, 0x48, 182); TestWorld.Set16(c, 0x4A, 2028); // 30 June 2028, leap year: day 182
        c[0x54] = 4;
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    ContractWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Writes_wage_playing_time_and_expiry_only_in_their_fields()
    {
        var before = _w.Bytes(Contract, 0x60);
        Writer().SetWage(Player(), 55555);
        Writer().SetPlayingTime(Player(), PlayingTime.ImpactSub);
        Writer().SetExpiry(Player(), new DateOnly(2030, 6, 30));
        var after = _w.Bytes(Contract, 0x60);

        Assert.Equal(BitConverter.GetBytes(55555), after[0x20..0x24]);
        Assert.Equal(5, after[0x54]);
        Assert.Equal(new byte[] { 181, 0, 0xEE, 0x07 }, after[0x48..0x4C]); // 30 June 2030 = day 181, 2030 = 0x7EE
        after[0x20..0x24].CopyTo(before, 0x20); before[0x54] = after[0x54]; after[0x48..0x4C].CopyTo(before, 0x48);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Start_date_keeps_the_flag_bits_above_the_day()
    {
        // Acerbi in FM27: start 0x3CCB = flags 0x3C00 | day 0xCB (203 = 22 July 2026).
        Assert.True(_w.Memory.TryWrite(Contract + 0x44, [0xCB, 0x3C, 0xEA, 0x07]));
        var d = Writer().Read(Player())!;
        Assert.Equal(new DateOnly(2026, 7, 22), d.Started);

        Writer().SetStarted(Player(), d, new DateOnly(2027, 1, 2));
        Assert.Equal(new byte[] { 0x02, 0x3C, 0xEB, 0x07 }, _w.Bytes(Contract + 0x44, 4)); // day 2, flags kept, 2027
    }

    [Fact]
    public void Edits_a_clause_value_and_parameter_but_keeps_its_type()
    {
        // Clause vector of Lacey's FM26 contract: wage after 15 club league games -> 25,767 (type 13).
        const ulong Terms = 0x4000_1000;
        var t = _w.Memory.Map(Terms, 8);
        BitConverter.GetBytes(25767).CopyTo(t, 0); TestWorld.Set16(t, 4, 15); TestWorld.Set16(t, 6, 13);
        Assert.True(_w.Memory.TryWrite(Contract + 0x68, BitConverter.GetBytes(Terms)));
        Assert.True(_w.Memory.TryWrite(Contract + 0x70, BitConverter.GetBytes(Terms + 8)));
        var d = Writer().Read(Player())!;
        Assert.Equal(new ContractTerm(0, 25767, 15, 13), Assert.Single(d.Terms));

        Writer().SetTerm(Player(), d, 0, 30000, 10);
        Assert.Equal(new ContractTerm(0, 30000, 10, 13), Assert.Single(Writer().Read(Player())!.Terms));
    }

    const ulong TermBuffer = 0x4000_2000;

    /// <summary>Lacey's three FM26 bonuses: appearance 4,771 (32), assist 2,982 (39), unused sub 3,181 (38); room for <paramref name="capacity"/>.</summary>
    void LaceyTerms(int capacity)
    {
        var t = _w.Memory.Map(TermBuffer, capacity * 8);
        (int v, short p, short ty)[] items = [(4771, -1, 32), (2982, -1, 39), (3181, -1, 38)];
        for (int i = 0; i < items.Length; i++)
        { BitConverter.GetBytes(items[i].v).CopyTo(t, i * 8); TestWorld.Set16(t, i * 8 + 4, items[i].p); TestWorld.Set16(t, i * 8 + 6, items[i].ty); }
        Assert.True(_w.Memory.TryWrite(Contract + 0x68, [.. BitConverter.GetBytes(TermBuffer), .. BitConverter.GetBytes(TermBuffer + 24), .. BitConverter.GetBytes(TermBuffer + (ulong)capacity * 8)]));
    }

    [Fact]
    public void Adds_a_clause_in_place_when_the_list_has_room()
    {
        LaceyTerms(capacity: 4);
        Writer().AddTerm(Player(), Writer().Read(Player())!, 22, -1, 1, allocate: null);
        var terms = Writer().Read(Player())!.Terms;
        Assert.Equal(4, terms.Count);
        Assert.Equal(new ContractTerm(3, -1, 1, 22), terms[3]);
        Assert.Equal(TermBuffer, BitConverter.ToUInt64(_w.Bytes(Contract + 0x68, 8)));
    }

    [Fact]
    public void Adds_a_clause_into_a_new_buffer_when_the_list_is_full()
    {
        LaceyTerms(capacity: 3);
        const ulong Grown = 0x4100_0000;
        Writer().AddTerm(Player(), Writer().Read(Player())!, 13, 25767, 15, bytes => { _w.Memory.Map(Grown, bytes); return Grown; });
        Assert.Equal(new[] { Grown, Grown + 32, Grown + 48 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(Contract + 0x68 + (ulong)i * 8, 8))));
        Assert.Equal(new[] { 4771, 2982, 3181, 25767 }, Writer().Read(Player())!.Terms.Select(t => t.Value));
    }

    [Fact]
    public void Removing_a_clause_moves_the_later_ones_up()
    {
        LaceyTerms(capacity: 3);
        Writer().RemoveTerm(Player(), Writer().Read(Player())!, 0);
        var terms = Writer().Read(Player())!.Terms;
        Assert.Equal(new short[] { 39, 38 }, terms.Select(t => t.Type));
        Assert.Equal(new[] { 2982, 3181 }, terms.Select(t => t.Value));
    }

    [Fact]
    public void Refuses_a_second_option_clause_of_the_same_type()
    {
        LaceyTerms(capacity: 4);
        Assert.Throws<InvalidOperationException>(() => Writer().AddTerm(Player(), Writer().Read(Player())!, 32, 5000, -1, null));
    }

    [Fact]
    public void Refuses_when_the_contract_object_is_no_longer_a_contract()
    {
        var p = Player();
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234)));
        Assert.Throws<StaleObjectException>(() => Writer().SetWage(p, 55555));
        Assert.Equal(BitConverter.GetBytes(40130), _w.Bytes(Contract + 0x20, 4));
    }

    [Fact]
    public void Refuses_when_the_wage_changed_since_the_snapshot()
    {
        var p = Player();
        Assert.True(_w.Memory.TryWrite(Contract + 0x20, BitConverter.GetBytes(41000)));
        Assert.Throws<StaleObjectException>(() => Writer().SetWage(p, 55555));
        Assert.Equal(BitConverter.GetBytes(41000), _w.Bytes(Contract + 0x20, 4));
    }
}
