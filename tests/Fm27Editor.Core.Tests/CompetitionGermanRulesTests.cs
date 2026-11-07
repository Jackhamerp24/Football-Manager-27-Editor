using System.Text;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CompetitionGermanRulesTests
{
    [Fact]
    public void Bundesliga_homegrown_writes_FIRST_copies_and_never_SECOND()
    {
        var p = GameSession.BuiltInProfiles().Single(x => x.Build == "27.0.0+2493426 Game Pass");
        var m = new FakeMemory(); const ulong b = 0x700000000000; ulong next = 0x100000;
        ulong Alloc() { ulong at = next; next += 0x2000; m.Map(at, 0x1000); return at; }
        void Put(ulong at, byte[] value) => Assert.True(m.TryWrite(at, value));
        void Q(ulong at, ulong value) => Put(at, BitConverter.GetBytes(value));
        void I(ulong at, int value) => Put(at, BitConverter.GetBytes(value));
        void Vector(ulong at, params ulong[] values)
        {
            ulong data = Alloc();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(8 * i), values[i]);
            Q(at, data); Q(at + 8, data + (ulong)(8 * values.Length));
        }
        ulong Primitive(int value) { ulong n = Alloc(); I(n + 8, value); return n; }
        ulong DictionaryNode(params (string Key, ulong Value)[] entries)
        {
            ulong node = Alloc(), header = Alloc(), data = Alloc();
            Q(node + 8, header); Q(header, data); Q(header + 8, data + (ulong)(16 * entries.Length));
            for (int i = 0; i < entries.Length; i++) { Put(data + (ulong)(16 * i), Encoding.ASCII.GetBytes(entries[i].Key)); Q(data + (ulong)(16 * i + 8), entries[i].Value); }
            return node;
        }
        ulong ListNode(params ulong[] values) { ulong n = Alloc(), header = Alloc(); Q(n + 8, header); Vector(header, values); return n; }
        ulong Entry(string name, out ulong amount, out ulong clubAmount, out ulong maxAmount)
        {
            ulong value = Primitive(9); amount = value + 8;
            ulong rule = DictionaryNode(("epyt", Primitive(0x27)), ("ulav", value));
            ulong clubValue = Primitive(5); clubAmount = clubValue + 8;
            ulong clubRule = DictionaryNode(("epyt", Primitive(0x26)), ("ulav", clubValue));
            ulong str = Alloc(), nameNode = Alloc(); I(str, name.Length); Put(str + 4, Encoding.ASCII.GetBytes(name)); Q(nameNode + 8, str);
            ulong max = Primitive(36); maxAmount = max + 8;
            ulong prns = DictionaryNode(("dnss", DictionaryNode(("smns", max))));
            return DictionaryNode(("eman", nameNode), ("rsqs", ListNode(rule, clubRule)), ("prns", prns));
        }
        ulong comp = Alloc(), holder = Alloc(), common = Alloc(), groups = Alloc(), group = Alloc(), primaryHolder = Alloc(), primary = Alloc(), detail = Alloc(), root = Alloc();
        Q(comp, b + p.Competition!.VtableRva); I(comp + 0xC, 22); Q(comp + 0xB0, holder);
        Q(holder, b + 0x4C91958); Put(holder + 0x31, [3]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        ulong clubPrimary = Alloc();
        Vector(groups + 0x60, group); Put(group + 0x120, [3]); Q(group + 0x70, primaryHolder); Vector(primaryHolder, primary, clubPrimary);
        Put(primary + 0x1C, [0x27, 0]); I(primary + 0x18, 9);
        Put(clubPrimary + 0x1C, [0x26, 0]); I(clubPrimary + 0x18, 5);
        Put(group + 0xF0, [36]);
        Q(comp + 0x60, detail); Q(detail, b + 0x4C8EFA8); Q(detail + 0x108, root); Q(root, b + 0x4C9B0E8);
        ulong first1 = Entry("FIRST", out ulong a, out ulong ca, out ulong max1), second1 = Entry("SECOND", out ulong ignored1, out ulong ignoredClub1, out _);
        ulong first2 = Entry("FIRST", out ulong c, out ulong cc, out ulong max2), second2 = Entry("SECOND", out ulong ignored2, out ulong ignoredClub2, out _);
        ulong dict = DictionaryNode(("lrxf", ListNode(second1, second1, second1, first1)));
        ulong header = BitConverter.ToUInt64(m.Read(dict + 8, 8)); Put(root + 0x570, m.Read(header, 16));
        Vector(root + 0x588, DictionaryNode(("lrxf", ListNode(second2, second2, second2, first2))));
        var competition = new CompetitionRecord(comp, 0, 22, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionSquadRulesAccess(m, p, b);
        var maximum = access.Read(competition)!;
        Assert.Equal(36, maximum.MaximumSquadSize);
        access.SetMaximumSquadSize(maximum, 37);
        Assert.Equal("25", Convert.ToHexString(m.Read(group + 0xF0, 1)));
        Assert.Equal("25000000", Convert.ToHexString(m.Read(max1, 4)));
        Assert.Equal("25000000", Convert.ToHexString(m.Read(max2, 4)));
        var snapshot = access.ReadHomegrown(competition)!;
        Assert.Equal(9, snapshot.MaximumSquadSize);
        var clubSnapshot = access.ReadHomegrownClub(competition)!;
        Assert.Equal(5, clubSnapshot.MaximumSquadSize);
        access.SetMany([(snapshot, 10), (clubSnapshot, 6)]);
        foreach (ulong at in new[] { primary + 0x18, a, c }) Assert.Equal("0A000000", Convert.ToHexString(m.Read(at, 4)));
        foreach (ulong at in new[] { clubPrimary + 0x18, ca, cc }) Assert.Equal("06000000", Convert.ToHexString(m.Read(at, 4)));
        foreach (ulong at in new[] { ignored1, ignored2 }) Assert.Equal("09000000", Convert.ToHexString(m.Read(at, 4)));
        foreach (ulong at in new[] { ignoredClub1, ignoredClub2 }) Assert.Equal("05000000", Convert.ToHexString(m.Read(at, 4)));
        var fresh = access.ReadHomegrown(competition)!;
        I(c, 11); int writes = m.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetMaximumSquadSize(fresh, 12));
        Assert.Equal(writes, m.Writes);
    }
}
