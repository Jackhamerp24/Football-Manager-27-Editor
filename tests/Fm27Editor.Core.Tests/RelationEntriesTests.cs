using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Every relation type of FMRTE 27's dropdown. Expected record bytes are the ones FMRTE wrote when each type was added on Erling
/// Haaland (Frida WriteProcessMemory trace, 10/11/2026) or the ones the game itself holds on him (Dislikes Team Viking 85, International
/// Withdrawal), so the expectations do not come from the code under test.
/// </summary>
public class RelationEntriesTests
{
    const ulong Holder = 0x5000_0000, Records = 0x5001_0000, Objects = 0x5100_0000, Spare = 0x5200_0000;
    readonly TestWorld _w = new();
    readonly ulong _team, _club, _nation, _person;
    ulong _next = Spare;

    public RelationEntriesTests()
    {
        var o = _w.Profile.Relations!;
        _club = Object(TestWorld.ModuleBase + _w.Profile.Club!.ClubVtableRva);
        _team = Object(TestWorld.ModuleBase + _w.Profile.Team!.VtableRva);
        _nation = Object(TestWorld.ModuleBase + _w.Profile.Nation!.VtableRva);
        _person = Object(TestWorld.ModuleBase + o.PersonVtable);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x300, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x18);
        // One existing row: Training Happiness (no target, value 0, extra 3), room for more.
        TestWorld.Set64(h, 0, Records); TestWorld.Set64(h, 8, Records + 16); TestWorld.Set64(h, 0x10, Records + 16 * 8);
        var r = _w.Memory.Map(Records, 16 * 8);
        Convert.FromHexString("000000000000000000000047000003FF").CopyTo(r, 0);
    }

    ulong Object(ulong vtable)
    {
        ulong at = _object; _object += 0x100;
        TestWorld.Set64(_w.Memory.Map(at, 0x100), 0, vtable);
        return at;
    }

    ulong _object = Objects;

    ulong Allocate(int size) { ulong at = _next; _next += 0x1000; _w.Memory.Map(at, size); return at; }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    PlayerStatuses Read() => new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());
    RelationshipWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    PersonOwner Owner() => PersonOwner.Of(Player(), _w.Profile);
    RelationDraft Draft(byte code, ulong target, byte? value = null) { var t = RelationCatalog.Find(code)!; return new(t, target, t.Plus8, value ?? t.Value, t.Flags); }

    string RecordTail(int index) => Convert.ToHexString(_w.Bytes(Records + (ulong)index * 16 + 8, 8));

    [Theory]
    [InlineData(0x1B, "TEAM", null, "0000041B000000FF")]      // Competition Unregistration, UEFA (code 0): 0000 04 1B 00 00 00 FF
    [InlineData(0x39, "NATION", null, "1300083900FF00FF")]    // Invited to represent nation: 1300 08 39 00 FF 00 FF
    [InlineData(0x4A, "CLUB", null, "0000014A16FF00FF")]          // Developed at Country: 0000 01 4A 16 FF 00 FF
    [InlineData(0x68, "NATION", null, "00000868000000FF")]    // Other Nation Caps: 0000 08 68 00 00 00 FF
    [InlineData(0x6B, "NATION", null, "0000086B000000FF")]    // Other Nation Youth Caps: 0000 08 6B 00 00 00 FF
    [InlineData(0x04, "CLUB", 85, "00000104554F00FF")]        // Dislikes Team Viking 85: 0000 01 04 55 4F 00 FF
    [InlineData(0x51, "NONE", null, "00000051325000FF")]          // International Withdrawal: 0000 00 51 32 50 00 FF
    public void A_new_row_has_the_bytes_FMRTE_writes(int code, string target, int? value, string expectedTail)
    {
        ulong address = target switch { "TEAM" => _team, "NATION" => _nation, "CLUB" => _club, _ => 0UL };
        Writer().AddEntry(Owner(), Read(), Draft((byte)code, address, (byte?)value), Allocate);
        Assert.Equal(expectedTail, RecordTail(1));
        Assert.Equal(address, BitConverter.ToUInt64(_w.Bytes(Records + 16, 8)));
        var entry = Read().Entries[1];
        Assert.Equal((byte)code, entry.Type);
        Assert.Equal(RelationCatalog.NameOf(code), RelationCatalog.NameOf(entry.Type));
    }

    [Theory]
    [InlineData(0x68, 37, 12, "00000868250C00FF")]   // Other Nation Caps 37 / 12 (Frida trace, Haaland)
    [InlineData(0x6B, 23, 7, "0000086B170700FF")]    // Other Nation Youth Caps 23 / 7
    public void Caps_and_goals_are_the_bytes_FMRTE_wrote(int code, byte caps, byte goals, string expectedTail)
    {
        var type = RelationCatalog.Find(code)!;
        Writer().AddEntry(Owner(), Read(), new RelationDraft(type, _nation, type.Plus8, caps, goals), Allocate);
        Assert.Equal(expectedTail, RecordTail(1));
        var entry = Read().Entries.Single(e => e.Type == code);
        Assert.Equal(caps, entry.Value);
        Assert.Equal(goals, entry.Flags);
    }

    [Fact]
    public void Caps_and_goals_update_in_place_and_stop_at_127()
    {
        var type = RelationCatalog.Find(0x68)!;
        Writer().AddEntry(Owner(), Read(), new RelationDraft(type, _nation, type.Plus8, 0, 0), Allocate);
        var entry = Read().Entries.Single(e => e.Type == 0x68);
        Writer().UpdateEntry(Owner(), entry, entry.Plus8, 127, 5);
        Assert.Equal("000008687F0500FF", RecordTail(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Writer().UpdateEntry(Owner(), Read().Entries.Single(e => e.Type == 0x68), entry.Plus8, 128, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Writer().AddEntry(Owner(), Read(), new RelationDraft(type, _nation, type.Plus8, 0, 128), Allocate));
    }

    [Fact]
    public void Every_row_is_listed_with_FMRTEs_name_and_the_target_name()
    {
        Writer().AddEntry(Owner(), Read(), Draft(0x1B, _team), Allocate);
        var entries = Read().Entries;
        Assert.Equal([0x47, 0x1B], entries.Select(e => (int)e.Type));
        Assert.Equal(["Training Happiness", "Competition Unregistration"], entries.Select(e => RelationCatalog.NameOf(e.Type)));
        Assert.Equal("Unknown (36)", RelationCatalog.NameOf(0x24));
        Assert.Equal("Unknown (112)", RelationCatalog.NameOf(0x70));
    }

    [Fact]
    public void A_target_of_the_wrong_kind_is_refused_before_any_write()
    {
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentException>(() => Writer().AddEntry(Owner(), Read(), Draft(0x1B, _club), Allocate));      // a club where a team is needed
        Assert.Throws<ArgumentException>(() => Writer().AddEntry(Owner(), Read(), Draft(0x68, 0), Allocate));          // no target where a nation is needed
        Assert.Throws<ArgumentException>(() => Writer().AddEntry(Owner(), Read(), Draft(0x51, _nation), Allocate));    // a target where none is allowed
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Update_changes_only_reason_value_and_flags_and_remove_closes_the_gap()
    {
        Writer().AddEntry(Owner(), Read(), Draft(0x04, _club, 60), Allocate);
        Writer().AddEntry(Owner(), Read(), Draft(0x68, _nation), Allocate);
        var entry = Read().Entries[1];
        Writer().UpdateEntry(Owner(), entry, 3, 70, 0x4E);
        Assert.Equal("03000104464E00FF", RecordTail(1));                                // reason 3, value 0x46 = 70, flags 4E, kind and type untouched
        Writer().RemoveEntry(Owner(), Read().Entries[1]);
        Assert.Equal([0x47, 0x68], Read().Entries.Select(e => (int)e.Type));
        Assert.Equal(Records + 16 * 2, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));   // three rows, one removed
    }

    [Fact]
    public void A_row_changed_after_it_was_read_is_refused_and_nothing_is_written()
    {
        Writer().AddEntry(Owner(), Read(), Draft(0x68, _nation), Allocate);
        var entry = Read().Entries[1];
        Assert.True(_w.Memory.TryWrite(entry.Address + 0xC, [9]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().UpdateEntry(Owner(), entry, 0, 1, 0));
        Assert.Throws<StaleObjectException>(() => Writer().RemoveEntry(Owner(), entry));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void A_full_vector_moves_to_a_new_buffer_and_keeps_every_row()
    {
        for (int i = 0; i < 7; i++) Writer().AddEntry(Owner(), Read(), Draft(0x68, _nation, (byte)(i + 1)), Allocate);   // 1 + 7 = full (capacity 8)
        Writer().AddEntry(Owner(), Read(), Draft(0x6B, _nation), Allocate);
        var status = Read();
        Assert.Equal(9, status.Entries.Count);
        Assert.NotEqual(Records, status.Begin);
        Assert.Equal(0x6B, status.Entries[^1].Type);
        Assert.Equal(0x47, status.Entries[0].Type);
    }
}
