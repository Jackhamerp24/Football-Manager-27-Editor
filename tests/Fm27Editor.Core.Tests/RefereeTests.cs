using Fm27Editor.Core.Game;
using Fm27Editor.Core.Officials;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A fake module carrying FM Studio's clubs-anchor signature so the derived officials array can be tested, plus three
/// referees laid out like the live game (vtable +0x4F11568, names +0x28/+0x30/+0x38, CA +0x11C, PA +0x11E,
/// reputation +0x120, skills +0x122..+0x128, flags +0x129/+0x12A/+0x12B, unique id +0x144).
/// Values are taken from the live read of Michael Oliver (CA 180, PA 180, reputation 8358, elite and continental 1).
/// </summary>
public class RefereeTests
{
    sealed class JournalMemory(IProcessMemory inner, ChangeJournal journal) : IProcessMemory
    {
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> bytes)
        {
            var before = inner.Read(address, bytes.Length);
            if (!inner.TryWrite(address, bytes)) return false;
            journal.Record(address, before, bytes.ToArray());
            return true;
        }
    }
    const ulong Signature = 0x100000, ClubsSlot = 0x200800, ModuleSize = 0x300000;
    const uint RefereeVt = 0x200000;
    const ulong RefBegin = 0x300000, RefEnd = 0x300018;
    const ulong Ref1 = 0x400000, Ref2 = 0x401000, Ref3 = 0x402000;

    readonly TestWorld _w = new();

    public RefereeTests()
    {
        var module = _w.Memory.Map(TestWorld.ModuleBase, (int)ModuleSize);
        byte[] pattern = [0xFF, 0xD7, 0x48, 0x8B, 0x1D, 0, 0, 0, 0, 0x4C, 0x8B, 0x35, 0, 0, 0, 0, 0x4C, 0x39, 0xF3];
        BitConverter.GetBytes(unchecked((int)(ClubsSlot - (Signature + 2 + 7)))).CopyTo(pattern, 5);
        BitConverter.GetBytes(unchecked((int)(ClubsSlot + 8 - (Signature + 9 + 7)))).CopyTo(pattern, 12);
        pattern.CopyTo(module, (int)Signature);

        TestWorld.Set64(module, (int)ClubsSlot, 0x500000);
        TestWorld.Set64(module, (int)ClubsSlot + 8, 0x500010);
        TestWorld.Set64(module, (int)ClubsSlot - 320, RefBegin);
        TestWorld.Set64(module, (int)ClubsSlot - 312, RefEnd);
        var vector = _w.Memory.Map(RefBegin, 24);
        TestWorld.Set64(vector, 0, Ref1);
        TestWorld.Set64(vector, 8, Ref2);
        TestWorld.Set64(vector, 16, Ref3);
        Referee(Ref1, "Michael", "Oliver", 180, 180, 8358, flow: 15, discipline: 10, big: 15, pressure: 14, refereeing: 16, line: 9, time: 7, elite: 1, continental: 1, euro: 1, uid: 103994);
        Referee(Ref2, "Thomas", "Bramall", 175, 175, 8107, flow: 8, discipline: 11, big: 17, pressure: 17, refereeing: 15, line: 11, time: 5, elite: 0, continental: 0, euro: 0, uid: 103896);
        Referee(Ref3, "Oliver", "Andréasson", 196, 198, 8252, flow: 17, discipline: 15, big: 15, pressure: 19, refereeing: 17, line: 7, time: 3, elite: 0, continental: 0, euro: 255, uid: 2002071423);
    }

    void Set(ulong at, ulong value, string what) => Assert.True(_w.Memory.TryWrite(at, BitConverter.GetBytes(value)), what);

    void Referee(ulong address, string first, string last, short ca, short pa, short reputation,
        byte flow, byte discipline, byte big, byte pressure, byte refereeing, byte line, byte time,
        byte elite, byte continental, byte euro, int uid)
    {
        var d = _w.Memory.Map(address, 0x148);
        TestWorld.Set64(d, 0, TestWorld.ModuleBase + RefereeVt);
        Name(d, 0x28, first);
        Name(d, 0x30, last);
        TestWorld.Set16(d, 0x11C, ca);
        TestWorld.Set16(d, 0x11E, pa);
        TestWorld.Set16(d, 0x120, reputation);
        d[0x122] = flow;
        d[0x123] = discipline;
        d[0x124] = big;
        d[0x125] = pressure;
        d[0x126] = refereeing;
        d[0x127] = line;
        d[0x128] = time;
        d[0x129] = elite;
        d[0x12A] = continental;
        d[0x12B] = euro;
        BitConverter.GetBytes(uid).CopyTo(d, 0x144);
    }

    void Name(byte[] referee, int offset, string text)
    {
        ulong entry = 0x480000 + (ulong)offset * 0x1000, stringAt = entry + 0x40000;
        TestWorld.Set64(referee, offset, entry);
        _w.Memory.MapString(stringAt, text);
        TestWorld.Set64(_w.Memory.Map(entry, 8), 0, stringAt);
    }

    RefereeDirectory Directory() => new(_w.Memory, TestWorld.ModuleBase, ModuleSize);

    [Fact]
    public void Reads_every_referee_with_names_ratings_and_skills()
    {
        var all = Directory().ReadAll();
        Assert.Equal(3, all.Count);
        var oliver = all.Single(r => r.UniqueId == 103994);
        Assert.Equal("Michael Oliver", oliver.DisplayName);
        Assert.Equal((180, 180, 8358), (oliver.CurrentAbility, oliver.PotentialAbility, oliver.Reputation));
        Assert.Equal((15, 10, 15, 14, 16, 9, 7),
            (oliver.AllowingFlow, oliver.Discipline, oliver.ImportantMatches, oliver.Pressure, oliver.Refereeing, oliver.RunningLine, oliver.TimeKeeping));
        Assert.Equal((1, 1, 1), (oliver.EliteList, oliver.Continental, oliver.EuropeanCategory));
        var andreasson = all.Single(r => r.UniqueId == 2002071423);
        Assert.Equal(255, andreasson.EuropeanCategory);
        Assert.Equal((196, 198), (andreasson.CurrentAbility, andreasson.PotentialAbility));   // distinct, so a swapped CA/PA read fails
    }

    [Fact]
    public void Reads_nothing_when_the_clubs_anchor_is_missing()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.ModuleBase + Signature, new byte[0x20]));
        Assert.Empty(Directory().ReadAll());
        Assert.Null(GlobalArrayFinder.FindClubsSlots(_w.Memory, TestWorld.ModuleBase, ModuleSize));
    }

    [Fact]
    public void Reads_null_for_an_object_whose_vtable_is_outside_the_module()
    {
        var referee = Directory().Read(Ref1)!;
        Assert.True(_w.Memory.TryWrite(Ref2, BitConverter.GetBytes(0x1234UL)));
        Assert.Null(Directory().Read(Ref2));
        Assert.NotNull(referee);
    }

    [Fact]
    public void Set_writes_int16_and_byte_fields_and_reads_back()
    {
        var directory = Directory();
        var writer = new RefereeWriter(_w.Memory, TestWorld.ModuleBase);
        var oliver = directory.Read(Ref1)!;

        void Write(RefereeField field, int value)
        {
            var current = directory.Read(Ref1) ?? throw new InvalidOperationException("referee gone");
            writer.Set(current, field, value);
        }
        Write(RefereeField.CurrentAbility, 200);
        Write(RefereeField.Reputation, 9000);
        Write(RefereeField.Discipline, 20);
        Write(RefereeField.EuropeanCategory, 4);

        var after = directory.Read(Ref1)!;
        Assert.Equal((200, 9000), (after.CurrentAbility, after.Reputation));
        Assert.Equal((180, 20, 4), (after.PotentialAbility, after.Discipline, after.EuropeanCategory));
        Assert.Equal(oliver.AllowingFlow, after.AllowingFlow);
        Assert.Equal(oliver.DisplayName, after.DisplayName);
    }

    [Fact]
    public void Referee_history_names_the_edited_field_and_undoes_only_that_field()
    {
        var journal = new ChangeJournal();
        var memory = new JournalMemory(_w.Memory, journal);
        var record = Directory().Read(Ref1)!;
        using (journal.Begin("Referee", "Michael Oliver", "Page save", EntityChangeLabels.ForReferee(Ref1)))
            new RefereeWriter(memory, TestWorld.ModuleBase).Set(record, RefereeField.Discipline, 19);
        var entry = Assert.Single(Assert.Single(journal.Groups).Entries);
        Assert.Equal("Discipline", entry.Name);
        Assert.Equal(("10", "19"), (entry.BeforeText, entry.AfterText));
        Assert.True(journal.Undo(entry, memory, out _));
        Assert.Equal(10, Directory().Read(Ref1)!.Discipline);
        Assert.Equal(15, Directory().Read(Ref1)!.AllowingFlow);
    }

    [Fact]
    public void Set_refuses_an_object_that_changed_since_it_was_read()
    {
        var writer = new RefereeWriter(_w.Memory, TestWorld.ModuleBase);
        var oliver = Directory().Read(Ref1)!;
        Assert.Throws<StaleObjectException>(() => writer.Set(oliver with { VtableRva = RefereeVt + 1 }, RefereeField.CurrentAbility, 190));
        Assert.True(_w.Memory.TryWrite(Ref1 + 0x125, [19]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.Set(oliver, RefereeField.CurrentAbility, 190));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(180, Directory().Read(Ref1)!.CurrentAbility);
    }


    [Fact]
    public void Set_rejects_values_outside_the_measured_ranges()
    {
        var writer = new RefereeWriter(_w.Memory, TestWorld.ModuleBase);
        var oliver = Directory().Read(Ref1)!;
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.CurrentAbility, 201));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.CurrentAbility, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.AllowingFlow, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.Refereeing, 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.EliteList, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Set(oliver, RefereeField.EuropeanCategory, 6));
        Assert.Equal(writes, _w.Memory.Writes);
    }
}
