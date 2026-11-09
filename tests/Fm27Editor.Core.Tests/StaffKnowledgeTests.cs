using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffKnowledgeTests
{
    [Fact]
    public void Reads_fmrte_values_and_writes_only_the_value_byte_with_stale_guard()
    {
        var w = new TestWorld();
        const ulong staff = 0x60000000, holder = 0x60001000, vector = 0x60002000, records = 0x60003000, france = 0x60004000, scotland = 0x60005000;
        var obj = w.Memory.Map(staff, 0x240);
        TestWorld.Set64(obj, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(obj, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        TestWorld.Set64(obj, 0xB0, holder);
        var h = w.Memory.Map(holder, 24); TestWorld.Set64(h, 0, vector); TestWorld.Set64(h, 8, vector + 24);
        var v = w.Memory.Map(vector, 24); TestWorld.Set64(v, 0, records); TestWorld.Set64(v, 8, records + 40); TestWorld.Set64(v, 16, records + 80);
        var bytes = w.Memory.Map(records, 120);
        Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000004F01EA0736360000").CopyTo(bytes, 0);
        Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000004F01EA0749490000").CopyTo(bytes, 40);
        Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000004F01EA0764640000").CopyTo(bytes, 80);
        const ulong franceTeam = 0x60006000, scotlandTeam = 0x60007000, franceWomen = 0x60008000;
        TestWorld.Set64(bytes, 0, franceTeam); TestWorld.Set64(bytes, 40, scotlandTeam); TestWorld.Set64(bytes, 80, franceWomen);
        void Nation(ulong address, ulong team, ulong women, string name)
        {
            var n = w.Memory.Map(address, 0x200); TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team); TestWorld.Set64(n, 0x110, women);
            TestWorld.Set64(w.Memory.Map(team, 8), 0, TestWorld.ModuleBase + 0x4CEC0B8);
            TestWorld.Set64(w.Memory.Map(women, 8), 0, TestWorld.ModuleBase + 0x4CEC0B8);
            w.Memory.MapString(address + 0x1000, name); TestWorld.Set64(n, 0x18, address + 0x1000);
        }
        Nation(france, franceTeam, franceWomen, "France"); Nation(scotland, scotlandTeam, 0, "Scotland");
        var person = new StaffRecord { Address = staff + 0x100, StaffAddress = staff, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var access = new StaffKnowledge(w.Memory, w.Profile, TestWorld.ModuleBase);
        var nations = new[] { new Fm27Editor.Core.Nations.NationRecord(france, null, "France", null), new Fm27Editor.Core.Nations.NationRecord(scotland, null, "Scotland", null) };
        var found = access.Read(person, nations);
        Assert.Equal(new[] { ("France", (byte)54), ("Scotland", (byte)73), ("France (Women)", (byte)100) }, found.Select(r => (r.Nation, r.Value)));
        access.SetValue(person, found[0], 55);
        Assert.Equal((byte)55, w.Bytes(records + 36, 1)[0]);
        Assert.Equal((byte)54, w.Bytes(records + 37, 1)[0]);
        Assert.Equal((byte)73, w.Bytes(records + 40 + 36, 1)[0]);
        Assert.Throws<StaleObjectException>(() => access.SetValue(person, found[0], 54));
    }

    [Fact]
    public void UnknownNationalTeamStillThrows()
    {
        var w = new TestWorld();
        const ulong staff = 0x61000000, holder = 0x61001000, vector = 0x61002000, record = 0x61003000, nationAt = 0x61004000;
        var obj = w.Memory.Map(staff, 0x240);
        TestWorld.Set64(obj, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(obj, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        TestWorld.Set64(obj, 0xB0, holder);
        var h = w.Memory.Map(holder, 24); TestWorld.Set64(h, 0, vector); TestWorld.Set64(h, 8, vector + 8);
        var v = w.Memory.Map(vector, 8); TestWorld.Set64(v, 0, record);
        var bytes = w.Memory.Map(record, 40);
        const ulong known = 0x61005000, stranger = 0x61006000;
        TestWorld.Set64(bytes, 0, stranger);
        Convert.FromHexString("4F01EA0764640000").CopyTo(bytes, 32);
        var n = w.Memory.Map(nationAt, 0x200); TestWorld.Set64(n, 0x108, known);
        TestWorld.Set64(w.Memory.Map(known, 8), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        TestWorld.Set64(w.Memory.Map(stranger, 8), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        var person = new StaffRecord { Address = staff + 0x100, StaffAddress = staff, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var access = new StaffKnowledge(w.Memory, w.Profile, TestWorld.ModuleBase);
        var nations = new[] { new Fm27Editor.Core.Nations.NationRecord(nationAt, null, "Nowhere", null) };
        Assert.Throws<Fm27Editor.Core.Memory.MemoryAccessException>(() => access.Read(person, nations));
    }

    /// <summary>
    /// Diego Simeone on 15/8/2026: Italy, Spain, Argentina, all 100, in a full vector (cap = end). Record tails as dumped:
    /// Italy "4F01EA0764010000", the others "4F01EA0764640000". FMRTE 27's + then Portugal wrote a new record that copies
    /// Italy's with Portugal's team at +0 ("…4F01EA0764010000") and moved the four pointers to a new buffer; − on the last
    /// row only moved end back 8 (2/11/2026).
    /// </summary>
    [Fact]
    public void Adds_and_removes_rows_like_fmrte()
    {
        var w = new TestWorld();
        const ulong staff = 0x60000000, holder = 0x60001000, vector = 0x60002000, records = 0x60003000, teams = 0x60010000, nationsAt = 0x60020000, pool = 0x70000000;
        var obj = w.Memory.Map(staff, 0x240);
        TestWorld.Set64(obj, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(obj, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        TestWorld.Set64(obj, 0xB0, holder);
        string[] names = ["Italy", "Spain", "Argentina", "Portugal"];
        var nations = new List<Fm27Editor.Core.Nations.NationRecord>();
        for (int i = 0; i < names.Length; i++)
        {
            ulong n = nationsAt + (ulong)i * 0x2000, team = teams + (ulong)i * 0x100;
            var nb = w.Memory.Map(n, 0x200); TestWorld.Set64(nb, 0, TestWorld.ModuleBase + 0x4CDF658); TestWorld.Set64(nb, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 8), 0, TestWorld.ModuleBase + 0x4CEC0B8);
            nations.Add(new(n, null, names[i], null));
        }
        var rb = w.Memory.Map(records, 120);
        string[] tails = ["4F01EA0764010000", "4F01EA0764640000", "4F01EA0764640000"];
        for (int i = 0; i < 3; i++) { TestWorld.Set64(rb, i * 40, teams + (ulong)i * 0x100); Convert.FromHexString(tails[i]).CopyTo(rb, i * 40 + 32); }
        var v = w.Memory.Map(vector, 24);
        for (int i = 0; i < 3; i++) TestWorld.Set64(v, i * 8, records + (ulong)i * 40);
        var h = w.Memory.Map(holder, 24); TestWorld.Set64(h, 0, vector); TestWorld.Set64(h, 8, vector + 24); TestWorld.Set64(h, 16, vector + 24);
        ulong next = pool;
        ulong Allocate(int bytes) { ulong at = next; w.Memory.Map(at, bytes); next += 0x1000; return at; }
        var person = new StaffRecord { Address = staff + 0x100, StaffAddress = staff, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var access = new StaffKnowledge(w.Memory, w.Profile, TestWorld.ModuleBase);

        var rows = access.Read(person, nations);
        ulong portugal = access.Catalogue(nations).Single(t => t.Name == "Portugal").Team;
        access.Add(person, rows, portugal, 100, Allocate);
        Assert.Equal([.. BitConverter.GetBytes(portugal), .. new byte[24], .. Convert.FromHexString("4F01EA0764010000")], w.Bytes(pool, 40));
        ulong buffer = BitConverter.ToUInt64(w.Bytes(holder, 8));
        Assert.NotEqual(vector, buffer);
        Assert.Equal(buffer + 32, BitConverter.ToUInt64(w.Bytes(holder + 8, 8)));
        rows = access.Read(person, nations);
        Assert.Equal(["Italy", "Spain", "Argentina", "Portugal"], rows.Select(r => r.Nation));
        Assert.Throws<InvalidOperationException>(() => access.Add(person, rows, portugal, 50, Allocate));

        access.Remove(person, rows, rows[1]);
        Assert.Equal(["Italy", "Argentina", "Portugal"], access.Read(person, nations).Select(r => r.Nation));
        Assert.Throws<StaleObjectException>(() => access.Remove(person, rows, rows[0]));
    }
}
