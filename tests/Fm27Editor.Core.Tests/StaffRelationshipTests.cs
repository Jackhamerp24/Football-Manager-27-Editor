using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A staff member's relations holder sits on the person at +0x78 (= player + 0x300 for a player's person part). The two
/// records are Mikel Arteta's first two on 15/8/2026, bytes as dumped: Other Nationality Pays Basque (type 9, nation
/// target, +0xD 0x50) and Likes Team Arsenal (Player, 90). FMRTE 27 showed those rows, with Nationality Info "Eligible For
/// Nation"; choosing "Has Played For Nation" there turned +0xD into 0x51.
/// </summary>
public class StaffRelationshipTests
{
    const ulong Person = 0x6000_0000, Holder = 0x6001_0000, Records = 0x6002_0000, Club = 0x6003_0000, Nation = 0x6004_0000;
    readonly TestWorld _w = new();

    public StaffRelationshipTests()
    {
        TestWorld.Set64(_w.Memory.Map(Person, 0x100), 0, TestWorld.ModuleBase + 0x4D8BE78);
        var c = _w.Memory.Map(Club, 0xD0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + 0x4CDA1F8);
        _w.Memory.MapString(Club + 0x1000, "Arsenal");
        TestWorld.Set64(c, 0xC8, Club + 0x1000);
        var n = _w.Memory.Map(Nation, 0x30);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
        _w.Memory.MapString(Nation + 0x1000, "Pays Basque");
        TestWorld.Set64(n, 0x18, Nation + 0x1000);
        var r = _w.Memory.Map(Records, 32);
        TestWorld.Set64(r, 0, Nation);
        Convert.FromHexString("00000809645000FF").CopyTo(r, 8);
        TestWorld.Set64(r, 16, Club);
        Convert.FromHexString("020001035A4F00FF").CopyTo(r, 24);
        var h = _w.Memory.Map(Holder, 0x18);
        TestWorld.Set64(h, 0, Records); TestWorld.Set64(h, 8, Records + 32); TestWorld.Set64(h, 16, Records + 32);
        Assert.True(_w.Memory.TryWrite(Person + 0x78, BitConverter.GetBytes(Holder)));
    }

    PersonOwner Arteta() => PersonOwner.Of(new StaffRecord
    {
        Address = Person, StaffAddress = Person - 0x100, Attributes = new byte[64], Personality = new byte[8], Kind = _w.Profile.Staff!.Kinds[0],
    }, _w.Profile);

    [Fact]
    public void Reads_and_edits_a_staff_members_relationships()
    {
        var s = new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Arteta());
        Assert.Equal(2, s.Relationships.Count);
        var row = s.Relationships[1];
        Assert.Equal((RelationshipKind.LikesTeam, "Arsenal", 90, 2), (row.Kind, row.Name, row.Value, row.Reason));
        new RelationshipWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetValue(Arteta(), row, 89);
        Assert.Equal(89, _w.Bytes(Records + 0x1C, 1)[0]);
    }

    [Fact]
    public void Other_nationality_reads_the_nation_and_sets_nationality_info()
    {
        var row = new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Arteta()).Relationships[0];
        Assert.Equal((RelationshipKind.OtherNationality, "Pays Basque", 0x50), (row.Kind, row.Name, row.Flags));
        new RelationshipWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetNationalityInfo(Arteta(), row, 81);
        Assert.Equal("4040B6565A02000000000809645100FF"[16..], Convert.ToHexString(_w.Bytes(Records + 8, 8)));
    }

    [Fact]
    public void Add_other_nationality_preserves_old_rows_and_checks_the_snapshot()
    {
        const ulong portugal = 0x6005_0000, buffer = 0x6006_0000;
        TestWorld.Set64(_w.Memory.Map(portugal, 0x30), 0, TestWorld.ModuleBase + 0x4CDF658);
        var before = _w.Bytes(Records, 32);
        var reader = new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var old = reader.Read(Arteta());
        var writer = new RelationshipWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        ulong Allocate(int bytes) { _w.Memory.Map(buffer, bytes); return buffer; }
        writer.AddOtherNationality(Arteta(), old, portugal, 80, Allocate);
        Assert.Equal([.. before, .. BitConverter.GetBytes(portugal), .. Convert.FromHexString("00000809645000FF")], _w.Bytes(buffer, 48));
        Assert.Equal(before, _w.Bytes(Records, 32));
        var current = reader.Read(Arteta());
        Assert.Throws<InvalidOperationException>(() => writer.AddOtherNationality(Arteta(), current, portugal, 80, Allocate));
        Assert.True(_w.Memory.TryWrite(buffer + 0x0D, new byte[] { 81 }));
        Assert.Throws<StaleObjectException>(() => writer.AddOtherNationality(Arteta(), current, Nation, 80, Allocate));
    }
}
