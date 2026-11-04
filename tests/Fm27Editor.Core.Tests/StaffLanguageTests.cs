using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A staff member's languages sit on the person at the same offset as a player's person part (person + 0xF0 = player + 0x378):
/// Mikel Arteta's vector there held French 5, English 10, Catalan 10, Basque 5, Portuguese 2, Italian 2 (15/8/2026).
/// </summary>
public class StaffLanguageTests
{
    const uint LanguageVt = 0x4F64938, StaffPersonVt = 0x4D8BE78;
    const ulong Person = 0x6000_0000, Vector = 0x6001_0000, Records = 0x6002_0000, French = 0x6003_0000, English = 0x6003_1000;
    readonly TestWorld _w = new();

    void Language(ulong at, int id, string name)
    {
        var l = _w.Memory.Map(at, 0x40);
        TestWorld.Set64(l, 0, TestWorld.ModuleBase + LanguageVt);
        BitConverter.GetBytes(id).CopyTo(l, 8);
        _w.Memory.MapString(at + 0x800, name);
        TestWorld.Set64(l, 0x18, at + 0x800);
    }

    public StaffLanguageTests()
    {
        TestWorld.Set64(_w.Memory.Map(Person, 0x100), 0, TestWorld.ModuleBase + StaffPersonVt);
        Language(French, 3, "French"); Language(English, 1, "English");
        var r = _w.Memory.Map(Records, 0x20);
        TestWorld.Set64(r, 0, French); r[8] = 5;
        TestWorld.Set64(r, 0x10, English); r[0x18] = 10;
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, Records); TestWorld.Set64(v, 8, Records + 0x10);
        Assert.True(_w.Memory.TryWrite(Person + 0xF0, [.. BitConverter.GetBytes(Vector), .. BitConverter.GetBytes(Vector + 16)]));
    }

    PersonOwner Arteta() => PersonOwner.Of(new StaffRecord
    {
        Address = Person, StaffAddress = Person - 0x100, Attributes = new byte[64], Personality = new byte[8], Kind = _w.Profile.Staff!.Kinds[0],
    }, _w.Profile);

    [Fact]
    public void Reads_and_sets_a_staff_members_languages()
    {
        var l = new LanguageReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Arteta());
        Assert.Equal([("French", 5), ("English", 10)], l.Select(x => (x.Name!, x.Value)));
        new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetValue(Arteta(), l[0], 6);
        Assert.Equal(6, _w.Bytes(Records + 8, 1)[0]);
        Assert.Equal(10, _w.Bytes(Records + 0x18, 1)[0]);
    }

    [Fact]
    public void A_player_list_is_not_read_from_a_staff_person_and_vice_versa()
    {
        // The same address read as a player would look at +0x378, and a staff owner refuses a non-staff vtable.
        Assert.Equal((ulong)Person + 0xF0, Arteta().At(0x378));
        Assert.True(_w.Memory.TryWrite(Person, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.PlayerVtable)));
        Assert.Empty(new LanguageReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Arteta()));
    }
}
