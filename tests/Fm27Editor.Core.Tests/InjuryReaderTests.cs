using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Injury layout from FM27 RAM (player + 0xF8 → holder; holder+0 vector of pointers; records 0x38 bytes; injury type
/// vtable +0x4CB0238, ID +0x8, name +0x18). Record bytes are Tyrese Campbell's and Jay Tinsdale's as dumped; expected values
/// are FMRTE 27's Injury tab: Concussion 14 + 14 days, torn anterior cruciate ligament 538 + 508 days.
/// </summary>
public class InjuryReaderTests
{
    const uint TypeVt = 0x4CB0238;
    const ulong Holder = 0x5000_0000, Vector = 0x5001_0000, Rec1 = 0x5002_0000, Rec2 = 0x5003_0000;
    const ulong Type1 = 0x5004_0000, Type2 = 0x5005_0000;
    readonly TestWorld _w = new();

    public InjuryReaderTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x18);
        TestWorld.Set64(h, 0, Vector); TestWorld.Set64(h, 8, Vector + 16); TestWorld.Set64(h, 0x10, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, Rec1); TestWorld.Set64(v, 8, Rec2);
        Record(Rec1, Type1, "CB80EA07FFFFFFFF0E000E0000000201", 65, "concussion");
        Record(Rec2, Type2, "2E00E907FFFFFFFF1A02FC0100000003", 27, "torn anterior cruciate ligament");
    }

    void Record(ulong at, ulong type, string tailFrom20, int id, string name)
    {
        var r = _w.Memory.Map(at, 0x38);
        TestWorld.Set64(r, 8, type);
        Convert.FromHexString(tailFrom20).CopyTo(r, 0x20);
        var t = _w.Memory.Map(type, 0x28);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + TypeVt);
        BitConverter.GetBytes(id).CopyTo(t, 8);
        TestWorld.Set64(t, 0x18, type + 0x1000);
        _w.Memory.MapString(type + 0x1000, name);
    }

    IReadOnlyList<PlayerInjury> Read() =>
        new InjuryReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!);

    [Fact]
    public void SetTypeWritesFmrtePointerAndDurationOnly()
    {
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var injury = Read()[0];
        var expected = _w.Bytes(Rec1, 0x38);
        BitConverter.GetBytes(Type2).CopyTo(expected, 8);
        Convert.FromHexString("1C000E00").CopyTo(expected, 0x28);
        var writer = new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        writer.SetType(player, injury, new InjuryType(Type2, 27, "torn anterior cruciate ligament", 28, 14));
        Assert.Equal(expected, _w.Bytes(Rec1, 0x38));
        Assert.Throws<StaleObjectException>(() => writer.SetType(player, injury, new InjuryType(Type2, 27, null, 28, 14)));
    }

    [Fact]
    public void AddToFullVectorCopiesTemplateAndMovesHeaderToNewBuffer()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var template = Read()[0];
        var expected = _w.Bytes(Rec1, 0x38);
        BitConverter.GetBytes(Type2).CopyTo(expected, 8);
        Convert.FromHexString("4A01EA07").CopyTo(expected, 0x20); // 26/11/2026
        Convert.FromHexString("1C000E00").CopyTo(expected, 0x28);
        expected[0x30] = (byte)((expected[0x30] & 0xF0) | 1);
        const ulong record = 0x7400_0000, buffer = 0x7401_0000;
        var alloc = new Queue<ulong>([record, buffer]);
        ulong Allocate(int size) { ulong at = alloc.Dequeue(); _w.Memory.Map(at, size); return at; }
        var added = new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Add(player, template,
            new InjuryType(Type2, 27, null, 28, 14), new DateOnly(2026, 11, 26), InjuryTreatment.Physio, Allocate);
        Assert.Equal(record, added.Address);
        Assert.Equal(expected, _w.Bytes(record, 0x38));
        Assert.Equal(new[] { Rec1, Rec2, record }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(buffer + (ulong)(i * 8), 8))));
        Assert.Equal(new[] { buffer, buffer + 24, buffer + 24 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(Holder + (ulong)(i * 8), 8))));
    }

    [Fact]
    public void RemoveLastInjuryChangesOnlyVectorEnd()
    {
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var injury = Read()[1];
        var header = _w.Bytes(Holder, 24);
        var records = _w.Bytes(Vector, 16);
        new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Remove(player, injury);
        BitConverter.GetBytes(Vector + 8).CopyTo(header, 8);
        Assert.Equal(header, _w.Bytes(Holder, 24));
        Assert.Equal(records, _w.Bytes(Vector, 16));
        Assert.Throws<StaleObjectException>(() => new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Remove(player, injury));
    }

    [Fact]
    public void AddWithSpareCapacityKeepsTheBufferAndAppendsOnePointer()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        const ulong buffer = 0x7600_0000, record = 0x7601_0000;
        var pointers = _w.Memory.Map(buffer, 24);
        TestWorld.Set64(pointers, 0, Rec1); TestWorld.Set64(pointers, 8, Rec2);
        Assert.True(_w.Memory.TryWrite(Holder, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + 16), .. BitConverter.GetBytes(buffer + 24)]));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var injury = Read()[0];
        int allocations = 0;
        ulong Allocate(int size) { allocations++; _w.Memory.Map(record, size); return record; }
        new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Add(player, injury, new InjuryType(Type2, 27, null, 28, 14),
            new DateOnly(2026, 11, 26), InjuryTreatment.Physio, Allocate);
        Assert.Equal(1, allocations);
        Assert.Equal(new[] { Rec1, Rec2, record }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(buffer + (ulong)(8 * i), 8))));
        Assert.Equal(new[] { buffer, buffer + 24, buffer + 24 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(Holder + (ulong)(8 * i), 8))));
    }

    [Fact]
    public void Reversible_heal_keeps_records_and_refuses_to_restore_changed_entries()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var writer = new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var plan = writer.PlanRemoveAll(player)!;
        Assert.Equal(2, plan.Injuries.Count);
        writer.ApplyRemoval(plan);
        Assert.Empty(Read());
        Assert.True(_w.Memory.TryWrite(Rec1 + 0x28, BitConverter.GetBytes((ushort)15)));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.UndoRemoval(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.True(_w.Memory.TryWrite(Rec1 + 0x28, BitConverter.GetBytes((ushort)14)));
        writer.UndoRemoval(plan);
        Assert.Equal(new[] { "concussion", "torn anterior cruciate ligament" }, Read().Select(i => i.Name));
    }

    [Fact]
    public void Duration_edits_preserve_record_and_reject_stale_or_detached_injuries()
    {
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var injuries = Read();
        var writer = new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var first = _w.Bytes(Rec1, 0x38);
        writer.SetDuration(player, injuries[0], 15, 13);
        Convert.FromHexString("0F000D00").CopyTo(first, 0x28);
        Assert.Equal(first, _w.Bytes(Rec1, 0x38));
        Assert.Throws<StaleObjectException>(() => writer.SetDuration(player, injuries[0], 14, 14));
        var second = _w.Bytes(Rec2, 0x38);
        writer.SetDuration(player, injuries[1], 539, 507);
        Convert.FromHexString("1B02FB01").CopyTo(second, 0x28);
        Assert.Equal(second, _w.Bytes(Rec2, 0x38));
        var fresh = Read()[1];
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Vector + 8)));
        Assert.Throws<StaleObjectException>(() => writer.SetDuration(player, fresh, 538, 508));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetDuration(player, fresh, -1, 0));
    }

    /// <summary>
    /// Bytes +0x20..+0x37 of Toma Bašić's (bruised thigh, FMRTE: By Physio) and Alphonso Davies' (partially torn hamstring,
    /// By Specialist) records as dumped on 22/11/2026, then Tammy Abraham's +0x2C.. tail (sports hernia, no box, byte 0x20).
    /// </summary>
    [Fact]
    public void Treatment_reads_the_FMRTE_boxes_and_keeps_bits_without_a_box()
    {
        Assert.True(_w.Memory.TryWrite(Rec1 + 0x20, Convert.FromHexString("4343EA07FFFFFFFF0500010000020301010000FF00000000")));
        Assert.True(_w.Memory.TryWrite(Rec2 + 0x20, Convert.FromHexString("305DEA07FFFFFFFF3000210000000102020000FF00000000")));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var writer = new InjuryWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var i = Read();
        Assert.Equal([InjuryTreatment.Physio, InjuryTreatment.Specialist], i.Select(x => x.Treatment));

        var basic = _w.Bytes(Rec1, 0x38);
        writer.SetTreatment(player, i[0], InjuryTreatment.Injection); // FMRTE showed only By Injection for 04
        basic[0x30] = 0x04;
        Assert.Equal(basic, _w.Bytes(Rec1, 0x38));
        writer.SetTreatment(player, i[1], InjuryTreatment.Physio | InjuryTreatment.Specialist); // FMRTE ticked both for 03
        Assert.Equal(0x03, _w.Bytes(Rec2, 0x38)[0x30]);
        Assert.Throws<StaleObjectException>(() => writer.SetTreatment(player, i[0], InjuryTreatment.Physio));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetTreatment(player, Read()[0], (InjuryTreatment)0x10));

        Assert.True(_w.Memory.TryWrite(Rec1 + 0x2C, Convert.FromHexString("00010303200000FF00000000")));
        var hernia = Read()[0];
        Assert.Equal(InjuryTreatment.None, hernia.Treatment);
        writer.SetTreatment(player, hernia, InjuryTreatment.Physio);
        Assert.Equal(0x21, _w.Bytes(Rec1, 0x38)[0x30]);
        Assert.Equal(InjuryTreatment.Physio, Read()[0].Treatment);
    }

    [Fact]
    public void Reads_both_injuries_as_FMRTE_shows_them()
    {
        var i = Read();
        Assert.Equal(["concussion", "torn anterior cruciate ligament"], i.Select(x => x.Name));
        Assert.Equal([65, 27], i.Select(x => x.TypeId));
        Assert.Equal([14, 538], i.Select(x => x.MinimumDays));
        Assert.Equal([14, 508], i.Select(x => x.ExtraDays));
        // Start raw 0x80CB: day 0xCB = 203 → 22/7/2026 (flags above bit 9 masked); 46/2025 = 15/2/2025.
        Assert.Equal([new DateOnly(2026, 7, 22), new DateOnly(2025, 2, 15)], i.Select(x => x.Start!.Value));
    }

    /// <summary>
    /// FMRTE 27's Heal: no injuries, Condition and Match Sharpness 10000, Jadedness 0. Starts from Harrison Burrows' RAM
    /// bytes (sharpness 7100, jadedness -178, condition 8500, as FMRTE shows).
    /// </summary>
    [Fact]
    public void Heal_empties_the_list_and_restores_condition_sharpness_and_jadedness()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var records = _w.Bytes(Rec1, 0x38);

        new PlayerHealer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Heal(player);

        Assert.Empty(Read());
        Assert.Equal(Vector, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));          // end = begin
        Assert.Equal(Vector + 16, BitConverter.ToUInt64(_w.Bytes(Holder + 0x10, 8)));  // capacity untouched
        Assert.Equal(records, _w.Bytes(Rec1, 0x38));                                    // records kept, not freed
        // 10000 = 0x2710, 0 = 0x0000: sharpness +0x258, jadedness +0x25A, condition +0x25C.
        Assert.Equal(Convert.FromHexString("102700001027"), _w.Bytes(TestWorld.Mainoo + 0x258, 6));
    }

    [Fact]
    public void Heal_of_a_healthy_player_writes_only_the_fields_that_differ()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(0UL)));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("102700001027")));
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        int writes = _w.Memory.Writes;
        new PlayerHealer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Heal(player);
        Assert.Equal(writes, _w.Memory.Writes);
    }

    /// <summary>
    /// Inspire without a contract and without a club: Morale (byte +0x26C, 1–20) to 20 and Jadedness (short +0x25A) to 0;
    /// the contract, unhappiness and cohesion steps have nothing to act on.
    /// </summary>
    [Fact]
    public void Inspire_without_a_contract_or_club_sets_morale_and_jadedness_only()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [5]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        new PlayerInspirer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Inspire(
            new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!, new DateOnly(2026, 11, 25));
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(20, after[0x26C]);
        Assert.Equal(0, BitConverter.ToInt16(after, 0x25A));
        after[0x26C] = before[0x26C];
        after[0x25A] = before[0x25A];
        after[0x25B] = before[0x25B];
        Assert.Equal(before, after);
    }

    /// <summary>
    /// User's definition of Destroy, with FM26 IGE's limits (Condition and Match Sharpness 5000–10000, Fatigue up to 1000):
    /// morale 0 (user request), condition 5000, sharpness 5000, jadedness 1000. Injuries stay as they are.
    /// </summary>
    [Fact]
    public void Destroy_sets_the_lowest_morale_condition_sharpness_and_highest_jadedness()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [5]));
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        new PlayerDestroyer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Destroy(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);

        // 5000 = 0x1388, 1000 = 0x03E8: sharpness +0x258, jadedness +0x25A, condition +0x25C; morale +0x26C.
        Assert.Equal(Convert.FromHexString("8813E8038813"), after[0x258..0x25E]);
        Assert.Equal(0, after[0x26C]);
        after[0x258..0x25E].CopyTo(before, 0x258); before[0x26C] = after[0x26C];
        Assert.Equal(before, after);
        Assert.Equal(2, Read().Count); // injuries untouched
    }

    [Fact]
    public void Healthy_player_and_foreign_records_give_nothing()
    {
        Assert.True(_w.Memory.TryWrite(Type2, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        Assert.Single(Read());
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(0UL)));
        Assert.Empty(Read());
    }
}
