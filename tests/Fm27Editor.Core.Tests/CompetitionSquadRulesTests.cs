using System.Text;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CompetitionSquadRulesTests
{
    [Fact]
    public void Maximum_squad_writes_three_FMRTE_copies_and_refuses_a_stale_copy()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        ulong next = 0x100000;
        ulong Allocate(int size = 0x1000) { ulong p = next; next += 0x2000; memory.Map(p, size); return p; }
        void Put(ulong p, byte[] b) => Assert.True(memory.TryWrite(p, b));
        void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
        void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
        void Vector(ulong header, params ulong[] values)
        {
            ulong data = Allocate();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
            Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
        }
        ulong Primitive(int value) { ulong node = Allocate(); I(node + 8, value); return node; }
        ulong DictionaryNode(params (string Key, ulong Value)[] entries)
        {
            ulong node = Allocate(), header = Allocate(), data = Allocate();
            Q(node + 8, header); Q(header, data); Q(header + 8, data + (ulong)(entries.Length * 16));
            for (int i = 0; i < entries.Length; i++)
            {
                Put(data + (ulong)(i * 16), Encoding.ASCII.GetBytes(entries[i].Key));
                Q(data + (ulong)(i * 16 + 8), entries[i].Value);
            }
            return node;
        }
        ulong ListNode(params ulong[] entries) { ulong node = Allocate(), header = Allocate(); Q(node + 8, header); Vector(header, entries); return node; }
        ulong CopyRules(out ulong amount)
        {
            ulong v = Primitive(33); amount = v + 8;
            ulong rule = DictionaryNode(("epyt", Primitive(9)), ("ulav", v));
            ulong id = DictionaryNode(("pmoc", Primitive(11)));
            ulong comp = DictionaryNode(("pmoc", id), ("rsqs", ListNode(rule)));
            // The game indexes the source list with holder+0x31 (six in this fixture).
            return ListNode(comp, comp, comp, comp, comp, comp, comp);
        }

        ulong compAddr = Allocate(), holder = Allocate(), common = Allocate(), groups = Allocate(), group = Allocate();
        ulong primaryHolder = Allocate(), primary = Allocate(), detail = Allocate(), root = Allocate();
        Q(compAddr, module + profile.Competition!.VtableRva); I(compAddr + 0xC, 11); Q(compAddr + 0xB0, holder);
        Q(holder, module + 0x4C91958); Put(holder + 0x31, [6]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        Vector(groups + 0x60, group); Put(group + 0x120, [6]); Q(group + 0x70, primaryHolder);
        Vector(primaryHolder, primary); Put(primary + 0x1C, [9, 0]); I(primary + 0x18, 33);
        Q(compAddr + 0x60, detail); Q(detail, module + 0x4C8EFA8); Q(detail + 0x108, root); Q(root, module + 0x4C9B0E8);
        ulong first = CopyRules(out ulong copy1), second = CopyRules(out ulong copy2);
        ulong rootDictionary = DictionaryNode(("lrxf", first));
        ulong rootHeader = BitConverter.ToUInt64(memory.Read(rootDictionary + 8, 8));
        Put(root + 0x570, memory.Read(rootHeader, 16));
        Vector(root + 0x588, DictionaryNode(("lrxf", second)));
        var competition = new CompetitionRecord(compAddr, 0, 11, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionSquadRulesAccess(memory, profile, module);
        var snapshot = access.Read(competition)!;
        Assert.Equal(33, snapshot.MaximumSquadSize);
        access.SetMaximumSquadSize(snapshot, 34);
        foreach (ulong address in new[] { primary + 0x18, copy1, copy2 })
            Assert.Equal("22000000", Convert.ToHexString(memory.Read(address, 4)));
        var fresh = access.Read(competition)!;
        I(copy2, 35);
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetMaximumSquadSize(fresh, 36));
        Assert.Equal(writes, memory.Writes);
        Assert.Equal("22000000", Convert.ToHexString(memory.Read(primary + 0x18, 4)));
    }

    // UEFA Champions League has no nation/rules object at competition+0x60; FMRTE wrote exactly one copy of
    // Maximum squad size there (0x2722FCEB928, 25 -> 26, measured 5/11/2026 with a Frida write hook).
    [Fact]
    public void Rule_without_a_nation_object_reads_and_writes_one_copy()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        ulong next = 0x100000;
        ulong Allocate(int size = 0x1000) { ulong p = next; next += 0x2000; memory.Map(p, size); return p; }
        void Put(ulong p, byte[] b) => Assert.True(memory.TryWrite(p, b));
        void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
        void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
        void Vector(ulong header, params ulong[] values)
        {
            ulong data = Allocate();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
            Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
        }
        ulong Primitive(int value) { ulong node = Allocate(); I(node + 8, value); return node; }
        ulong DictionaryNode(params (string Key, ulong Value)[] entries)
        {
            ulong node = Allocate(), header = Allocate(), data = Allocate();
            Q(node + 8, header); Q(header, data); Q(header + 8, data + (ulong)(entries.Length * 16));
            for (int i = 0; i < entries.Length; i++)
            {
                Put(data + (ulong)(i * 16), Encoding.ASCII.GetBytes(entries[i].Key));
                Q(data + (ulong)(i * 16 + 8), entries[i].Value);
            }
            return node;
        }
        ulong ListNode(params ulong[] entries) { ulong node = Allocate(), header = Allocate(); Q(node + 8, header); Vector(header, entries); return node; }

        ulong compAddr = Allocate(), holder = Allocate(), common = Allocate(), groups = Allocate(), group = Allocate();
        ulong primaryHolder = Allocate(), primary = Allocate();
        Q(compAddr, module + profile.Competition!.VtableRva); I(compAddr + 0xC, 1301394); Q(compAddr + 0xB0, holder);
        Q(holder, module + 0x4C91958); Put(holder + 0x31, [1]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        Vector(groups + 0x60, group); Put(group + 0x120, [1]); Q(group + 0x70, primaryHolder);
        Vector(primaryHolder, primary); Put(primary + 0x1C, [9, 0]); I(primary + 0x18, 26);
        Q(compAddr + 0x60, 0);
        var competition = new CompetitionRecord(compAddr, 0, 1301394, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionSquadRulesAccess(memory, profile, module);

        var snapshot = access.ReadRule(competition, 9)!;
        Assert.Equal(26, snapshot.MaximumSquadSize);
        int before = memory.Writes;
        access.SetMaximumSquadSize(snapshot, 27);
        Assert.Equal(before + 1, memory.Writes);
        Assert.Equal("1B000000", Convert.ToHexString(memory.Read(primary + 0x18, 4)));

        var fresh = access.ReadRule(competition, 9)!;
        I(primary + 0x18, 28);
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetMaximumSquadSize(fresh, 29));
        Assert.Equal(writes, memory.Writes);
        Assert.Equal("1C000000", Convert.ToHexString(memory.Read(primary + 0x18, 4)));
    }

    // FMRTE's "Also Applies To": another competition entry that holds the same value object as the rule being edited.
    [Fact]
    public void Rule_reports_competitions_that_share_its_value_object()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        ulong next = 0x100000;
        ulong Allocate(int size = 0x1000) { ulong p = next; next += 0x2000; memory.Map(p, size); return p; }
        void Put(ulong p, byte[] b) => Assert.True(memory.TryWrite(p, b));
        void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
        void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
        void Vector(ulong header, params ulong[] values)
        {
            ulong data = Allocate();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
            Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
        }
        ulong Primitive(int value) { ulong node = Allocate(); I(node + 8, value); return node; }
        ulong DictionaryNode(params (string Key, ulong Value)[] entries)
        {
            ulong node = Allocate(), header = Allocate(), data = Allocate();
            Q(node + 8, header); Q(header, data); Q(header + 8, data + (ulong)(entries.Length * 16));
            for (int i = 0; i < entries.Length; i++)
            {
                Put(data + (ulong)(i * 16), Encoding.ASCII.GetBytes(entries[i].Key));
                Q(data + (ulong)(i * 16 + 8), entries[i].Value);
            }
            return node;
        }
        ulong ListNode(params ulong[] entries) { ulong node = Allocate(), header = Allocate(); Q(node + 8, header); Vector(header, entries); return node; }

        ulong compAddr = Allocate(), holder = Allocate(), common = Allocate(), groups = Allocate(), group = Allocate();
        ulong primaryHolder = Allocate(), primary = Allocate(), detail = Allocate(), root = Allocate();
        Q(compAddr, module + profile.Competition!.VtableRva); I(compAddr + 0xC, 11); Q(compAddr + 0xB0, holder);
        Q(holder, module + 0x4C91958); Put(holder + 0x31, [6]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        Vector(groups + 0x60, group); Put(group + 0x120, [6]); Q(group + 0x70, primaryHolder);
        Vector(primaryHolder, primary); Put(primary + 0x1C, [9, 0]); I(primary + 0x18, 33);
        Q(compAddr + 0x60, detail); Q(detail, module + 0x4C8EFA8); Q(detail + 0x108, root); Q(root, module + 0x4C9B0E8);

        ulong v1 = Primitive(33), v2 = Primitive(33);
        ulong ourRuleA = DictionaryNode(("epyt", Primitive(9)), ("ulav", v1));
        ulong ourRuleB = DictionaryNode(("epyt", Primitive(9)), ("ulav", v2));
        ulong otherRule = DictionaryNode(("epyt", Primitive(9)), ("ulav", v1));
        ulong ourCompA = DictionaryNode(("pmoc", DictionaryNode(("pmoc", Primitive(11)))), ("rsqs", ListNode(ourRuleA)));
        ulong ourCompB = DictionaryNode(("pmoc", DictionaryNode(("pmoc", Primitive(11)))), ("rsqs", ListNode(ourRuleB)));
        ulong otherComp = DictionaryNode(("pmoc", DictionaryNode(("pmoc", Primitive(12)))), ("rsqs", ListNode(otherRule)));
        ulong listA = ListNode(otherComp, otherComp, otherComp, otherComp, otherComp, otherComp, ourCompA);
        ulong listB = ListNode(otherComp, otherComp, otherComp, otherComp, otherComp, otherComp, ourCompB);
        ulong rootDictionary = DictionaryNode(("lrxf", listA));
        ulong rootHeader = BitConverter.ToUInt64(memory.Read(rootDictionary + 8, 8));
        Put(root + 0x570, memory.Read(rootHeader, 16));
        Vector(root + 0x588, DictionaryNode(("lrxf", listB)));

        var competition = new CompetitionRecord(compAddr, 0, 11, null, null, 0, 0, 0, 0, 0, null, null);
        var snapshot = new CompetitionSquadRulesAccess(memory, profile, module).Read(competition)!;
        Assert.Equal(33, snapshot.MaximumSquadSize);
        Assert.Equal([12], snapshot.AlsoAppliesTo);
    }

    // The three UEFA competitions keep separate holders but resolve to the same rule value object, and FMRTE
    // lists the other two under "Also Applies To" (Champions League screenshot, 5/11/2026: Europa + Conference).
    [Fact]
    public void Rule_reports_competitions_that_resolve_to_the_same_value_address()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        ulong next = 0x100000;
        ulong Allocate(int size = 0x1000) { ulong p = next; next += 0x2000; memory.Map(p, size); return p; }
        void Put(ulong p, byte[] b) => Assert.True(memory.TryWrite(p, b));
        void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
        void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
        void Vector(ulong header, params ulong[] values)
        {
            ulong data = Allocate();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
            Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
        }

        ulong holder = Allocate(), common = Allocate(), groups = Allocate(), group = Allocate();
        ulong primaryHolder = Allocate(), primary = Allocate();
        Q(holder, module + 0x4C91958); Put(holder + 0x31, [1]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        Vector(groups + 0x60, group); Put(group + 0x120, [1]); Q(group + 0x70, primaryHolder);
        Vector(primaryHolder, primary); Put(primary + 0x1C, [9, 0]); I(primary + 0x18, 26);
        ulong first = Allocate(), second = Allocate();
        Q(first, module + profile.Competition!.VtableRva); I(first + 0xC, 1301394); Q(first + 0xB0, holder);
        Q(second, module + profile.Competition.VtableRva); I(second + 0xC, 1301396); Q(second + 0xB0, holder);
        var champions = new CompetitionRecord(first, 0, 1301394, null, null, 0, 0, 0, 0, 0, null, null);
        var europa = new CompetitionRecord(second, 0, 1301396, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionSquadRulesAccess(memory, profile, module);

        var withPeers = access.ReadRule(champions, 9, null, [champions, europa])!;
        Assert.Equal([1301396], withPeers.AlsoAppliesTo);
        Assert.Empty(access.ReadRule(champions, 9)!.AlsoAppliesTo);
    }

    // A competition only shares a rule when it resolves to the same common object AND the same group index: two
    // competitions on one common object with different indexes keep separate rules (measured on 24 leagues).
    [Fact]
    public void Rule_ignores_competitions_with_the_same_common_but_another_group_index()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        ulong next = 0x100000;
        ulong Allocate(int size = 0x1000) { ulong p = next; next += 0x2000; memory.Map(p, size); return p; }
        void Put(ulong p, byte[] b) => Assert.True(memory.TryWrite(p, b));
        void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
        void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
        void Vector(ulong header, params ulong[] values)
        {
            ulong data = Allocate();
            for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
            Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
        }
        ulong RuleRecord(ulong valueHolder, int value)
        {
            I(valueHolder + 0x18, value); Put(valueHolder + 0x1C, [9, 0]);
            return valueHolder;
        }

        ulong common = Allocate(), groups = Allocate(), groupA = Allocate(), groupB = Allocate();
        ulong holderA = Allocate(), holderB = Allocate(), holderC = Allocate();
        ulong rulesA = Allocate(), rulesB = Allocate(), recordA = Allocate(), recordB = Allocate();
        Q(common + 0x1E0, groups);
        Vector(groups + 0x60, groupA, groupB);
        Put(groupA + 0x120, [6]); Put(groupB + 0x120, [7]);
        Q(groupA + 0x70, rulesA); Q(groupB + 0x70, rulesB);
        Vector(rulesA, RuleRecord(recordA, 33));
        Vector(rulesB, RuleRecord(recordB, 44));
        foreach (var (holder, index) in new[] { (holderA, (byte)6), (holderB, (byte)6), (holderC, (byte)7) })
        {
            Q(holder, module + 0x4C91958); Put(holder + 0x31, [index]); Q(holder + 0x370, common);
        }
        ulong a = Allocate(), b = Allocate(), c = Allocate();
        Q(a, module + profile.Competition!.VtableRva); I(a + 0xC, 11); Q(a + 0xB0, holderA);
        Q(b, module + profile.Competition.VtableRva); I(b + 0xC, 12); Q(b + 0xB0, holderB);
        Q(c, module + profile.Competition.VtableRva); I(c + 0xC, 13); Q(c + 0xB0, holderC);
        var pl = new CompetitionRecord(a, 0, 11, null, null, 0, 0, 0, 0, 0, null, null);
        var championship = new CompetitionRecord(b, 0, 12, null, null, 0, 0, 0, 0, 0, null, null);
        var leagueOne = new CompetitionRecord(c, 0, 13, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionSquadRulesAccess(memory, profile, module);

        var snapshot = access.ReadRule(pl, 9, null, [pl, championship, leagueOne])!;
        Assert.Equal(33, snapshot.MaximumSquadSize);
        Assert.Equal([12], snapshot.AlsoAppliesTo);
        Assert.Equal(44, access.ReadRule(leagueOne, 9, null, [pl, championship, leagueOne])!.MaximumSquadSize);
    }
}
