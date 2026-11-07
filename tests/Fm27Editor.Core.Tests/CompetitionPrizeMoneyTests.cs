using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CompetitionPrizeMoneyTests
{
    [Fact]
    public void Classification_prizes_use_FMRTE_values_and_refuse_a_replaced_vector()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        CompetitionRecord Map(ulong at, int id, string amount)
        {
            ulong holder = at + 0x1000, stages = at + 0x2000, stage = at + 0x3000, group = at + 0x4000, positions = at + 0x5000;
            var c = memory.Map(at, 0x1B0);
            TestWorld.Set64(c, 0, module + profile.Competition!.VtableRva);
            BitConverter.GetBytes(id).CopyTo(c, 0xC);
            TestWorld.Set64(c, 0xB0, holder);
            var h = memory.Map(holder, 0x1D0);
            TestWorld.Set64(h, 0, module + 0x4C91958);
            TestWorld.Set64(h, 0x1C0, stages);
            TestWorld.Set64(h, 0x1C8, stages + 8);
            TestWorld.Set64(memory.Map(stages, 8), 0, stage);
            var s = memory.Map(stage, 0xF8);
            TestWorld.Set64(s, 0, module + 0x4D7C7B8);
            TestWorld.Set64(s, 0x70, at);
            TestWorld.Set64(s, 0xF0, group);
            var g = memory.Map(group, 0x1F8);
            TestWorld.Set64(g, 0, module + 0x4D7EA18);
            TestWorld.Set64(g, 0x1E0, positions);
            TestWorld.Set64(g, 0x1E8, positions + 4);
            TestWorld.Set64(g, 0x1F0, positions + 4);
            Convert.FromHexString(amount).CopyTo(memory.Map(positions, 4), 0);
            return new(at, 0, id, null, null, 0, 0, 0, 0, 0, null, null);
        }
        // FMRTE position 1: Premier League £53,100,001; Bundesliga £54,098,526.
        var premier = Map(0x100000, 11, "E13D2A03");
        var bundesliga = Map(0x200000, 22, "5E7A3903");
        // Champions League has two non-league stages followed by one league stage.
        var stageVector = memory.Map(0x400000, 24);
        TestWorld.Set64(stageVector, 0, 0x401000);
        TestWorld.Set64(stageVector, 8, 0x402000);
        TestWorld.Set64(stageVector, 16, 0x103000);
        TestWorld.Set64(memory.Map(0x401000, 8), 0, module + 0x4D852B8);
        TestWorld.Set64(memory.Map(0x402000, 8), 0, module + 0x4D852B8);
        Assert.True(memory.TryWrite(0x1011C0, BitConverter.GetBytes(0x400000UL)));
        Assert.True(memory.TryWrite(0x1011C8, BitConverter.GetBytes(0x400018UL)));
        var access = new CompetitionPrizeMoneyAccess(memory, profile, module);
        var a = access.ReadClassification(access.Read(premier)!)!;
        var b = access.ReadClassification(access.Read(bundesliga)!)!;
        Assert.Equal(53100001, a.Amounts[0]);
        Assert.Equal(54098526, b.Amounts[0]);
        TestWorld.Set64(stageVector, 0, 0x103000);
        Assert.Throws<StaleObjectException>(() => access.SetClassification(a, 1, 53100002));
        TestWorld.Set64(stageVector, 0, 0x401000);
        access.SetClassification(a, 1, 53100002);
        Assert.Equal("E23D2A03", Convert.ToHexString(memory.Read(a.Begin, 4)));
        var replacement = memory.Map(0x300000, 4);
        Convert.FromHexString("5E7A3903").CopyTo(replacement, 0);
        Assert.True(memory.TryWrite(b.Prize.Group + 0x1E0, BitConverter.GetBytes(0x300000UL)));
        Assert.True(memory.TryWrite(b.Prize.Group + 0x1E8, BitConverter.GetBytes(0x300004UL)));
        Assert.True(memory.TryWrite(b.Prize.Group + 0x1F0, BitConverter.GetBytes(0x300004UL)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetClassification(b, 1, 54098527));
        Assert.Equal(writes, memory.Writes);

        // FMRTE disables Classification by setting end to begin, keeping the old buffer intact.
        var enabled = access.ReadClassification(access.Read(premier)!)!;
        access.SetClassificationEnabled(enabled, false, _ => throw new InvalidOperationException("Disable must not allocate"));
        Assert.Equal(enabled.Begin, BitConverter.ToUInt64(memory.Read(enabled.Prize.Group + 0x1E0, 8)));
        Assert.Equal(enabled.Begin, BitConverter.ToUInt64(memory.Read(enabled.Prize.Group + 0x1E8, 8)));
        Assert.Equal(enabled.Capacity, BitConverter.ToUInt64(memory.Read(enabled.Prize.Group + 0x1F0, 8)));
        Assert.Equal("E23D2A03", Convert.ToHexString(memory.Read(enabled.Begin, 4)));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetClassificationEnabled(enabled, false, _ => 0));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Writes_FMRTE_prizes_and_refuses_a_replaced_prize_group()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var copy = Fm27Editor.Core.Offsets.OffsetProfile.Load(profile.ToJson());
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        CompetitionRecord Map(ulong at, int id, string values)
        {
            ulong holder = at + 0x1000, vector = at + 0x2000, stage = at + 0x3000, group = at + 0x4000;
            var comp = memory.Map(at, 0x1B0);
            TestWorld.Set64(comp, 0, module + copy.Competition!.VtableRva);
            BitConverter.GetBytes(id).CopyTo(comp, 0xC);
            TestWorld.Set64(comp, 0xB0, holder);
            var h = memory.Map(holder, 0x1D0);
            TestWorld.Set64(h, 0, module + 0x4C91958);
            TestWorld.Set64(h, 0x1C0, vector);
            TestWorld.Set64(h, 0x1C8, vector + 8);
            TestWorld.Set64(memory.Map(vector, 8), 0, stage);
            var s = memory.Map(stage, 0xF8);
            TestWorld.Set64(s, 0, module + 0x4D7C7B8);
            TestWorld.Set64(s, 0x70, at);
            TestWorld.Set64(s, 0xF0, group);
            var g = memory.Map(group, 0x158);
            TestWorld.Set64(g, 0, module + 0x4D7EA18);
            Convert.FromHexString(values).CopyTo(g, 0x14C);
            return new(at, 0, id, null, null, 0, 0, 0, 0, 0, null, null);
        }
        // FMRTE: Premier League win/draw/defeat 0/0/0; Championship win 12,345 after FMRTE Save.
        var premier = Map(0x100000, 11, "000000000000000000000000");
        var championship = Map(0x200000, 12, "393000000000000000000000");
        var access = new CompetitionPrizeMoneyAccess(memory, copy, module);
        var a = access.Read(premier)!;
        var b = access.Read(championship)!;
        Assert.Equal(0, a.PerWin);
        Assert.Equal(12345, b.PerWin);
        access.Set(a, CompetitionPrizeField.PerWin, 1000);
        access.Set(b, CompetitionPrizeField.PerDraw, 2000);
        Assert.Equal("E80300000000000000000000", Convert.ToHexString(memory.Read(a.Group + 0x14C, 12)));
        Assert.Equal("39300000D007000000000000", Convert.ToHexString(memory.Read(b.Group + 0x14C, 12)));
        var fresh = access.Read(championship)!;
        const ulong replacement = 0x300000;
        var replacementBytes = memory.Map(replacement, 0x158);
        memory.Read(fresh.Group, 0x158).CopyTo(replacementBytes, 0);
        Assert.True(memory.TryWrite(fresh.Stage + 0xF0, BitConverter.GetBytes(replacement)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(fresh, CompetitionPrizeField.PerWin, 12346));
        Assert.Equal(writes, memory.Writes);
    }
}
