using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Rule-lifting presets on single-copy rules (the UEFA/MLS shape: one validated value per code, no
/// nation object). Values are hand-picked so every planner branch is hit: lifted, already permissive,
/// minimum-type, duplicated, absent and missing-maximum cases.
/// </summary>
public class CompetitionRulePresetsTests
{
    readonly FakeMemory _memory = new();
    const ulong Module = 0x700000000000;
    ulong _next = 0x100000;

    ulong Allocate(int size = 0x1000) { ulong p = _next; _next += 0x2000; _memory.Map(p, size); return p; }
    void Put(ulong p, byte[] b) => Assert.True(_memory.TryWrite(p, b));
    void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
    void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));
    void Vector(ulong header, params ulong[] values)
    {
        ulong data = Allocate();
        for (int i = 0; i < values.Length; i++) Q(data + (ulong)(i * 8), values[i]);
        Q(header, data); Q(header + 8, data + (ulong)(values.Length * 8));
    }

    ulong Primary(int code, int value, ushort flag)
    {
        ulong p = Allocate(0x20);
        I(p + 0x18, value);
        Put(p + 0x1C, [(byte)(code & 0xFF), (byte)(code >> 8)]);
        Put(p + 0x1E, [(byte)(flag & 0xFF), (byte)(flag >> 8)]);
        return p;
    }

    (CompetitionRecord Competition, CompetitionSquadRulesAccess Access) Fixture(
        params (int Code, int Value, ushort Flag, int Copies)[] rules)
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        ulong compAddr = Allocate(), holder = Allocate(), common = Allocate(), groups = Allocate(), group = Allocate();
        ulong primaryHolder = Allocate();
        Q(compAddr, Module + profile.Competition!.VtableRva); I(compAddr + 0xC, 11); Q(compAddr + 0xB0, holder);
        Q(holder, Module + 0x4C91958); Put(holder + 0x31, [6]); Q(holder + 0x370, common); Q(common + 0x1E0, groups);
        Vector(groups + 0x60, group); Put(group + 0x120, [6]); Q(group + 0x70, primaryHolder);
        var primaries = new List<ulong>();
        foreach (var (code, value, flag, copies) in rules)
            for (int i = 0; i < copies; i++) primaries.Add(Primary(code, value, flag));
        Vector(primaryHolder, primaries.ToArray());
        Q(compAddr + 0x60, 0);
        var competition = new CompetitionRecord(compAddr, 0, 11, null, null, 0, 0, 0, 0, 0, null, null);
        return (competition, new CompetitionSquadRulesAccess(_memory, profile, Module));
    }

    const ushort Max = 0x00FF, Min = 0x0001;

    (CompetitionRecord Competition, CompetitionSquadRulesAccess Access) Standard() => Fixture(
        (0x09, 25, Max, 1), (0x12, 3, Max, 1), (0x04, 5, Max, 1), (0x03, 30, Max, 1),
        (0x05, 2, Min, 1), (0x3E, 4, Max, 1), (0xCF, 4, Max, 2), (0xA5, 18, Min, 1));

    [Fact]
    public void Nationality_preset_lifts_foreign_maximums_to_the_squad_maximum()
    {
        var (competition, access) = Standard();
        var plan = CompetitionRulePresets.LiftNationality(competition, access);
        Assert.Equal([(0x04, 25), (0x12, 25)],
            plan.Edits.Select(e => (e.Code, e.Value)).ToArray());
        var skipped = plan.Skipped.ToDictionary(s => s.Code, s => s.Reason);
        Assert.Equal("competition has no such rule", skipped[0x02]);
        Assert.Equal("already at or above the squad maximum", skipped[0x03]);
        Assert.Equal("minimum-type rules are never raised", skipped[0x05]);
        Assert.Equal("competition has no such rule", skipped[0x06]);
        Assert.Equal("competition has no such rule", skipped[0x07]);
        Assert.Equal("competition has no such rule", skipped[0x15]);

        access.SetMany(plan.Edits.Select(e => (e.Snapshot, e.Value)).ToArray());
        Assert.Equal(25, access.ReadRule(competition, 0x12)!.MaximumSquadSize);
        Assert.Equal(25, access.ReadRule(competition, 0x04)!.MaximumSquadSize);
        Assert.Equal(30, access.ReadRule(competition, 0x03)!.MaximumSquadSize);
    }

    [Fact]
    public void Loan_preset_lifts_loan_maximums_and_refuses_duplicated_codes()
    {
        var (competition, access) = Standard();
        var plan = CompetitionRulePresets.LiftLoans(competition, access);
        var edit = Assert.Single(plan.Edits);
        Assert.Equal((0x3E, 25), (edit.Code, edit.Value));
        var skipped = plan.Skipped.ToDictionary(s => s.Code, s => s.Reason);
        Assert.Equal("no single validated value", skipped[0xCF]);
        Assert.Equal("competition has no such rule", skipped[0x0C]);

        access.SetMany(plan.Edits.Select(e => (e.Snapshot, e.Value)).ToArray());
        Assert.Equal(25, access.ReadRule(competition, 0x3E)!.MaximumSquadSize);
    }

    [Fact]
    public void Without_a_squad_maximum_every_present_rule_is_refused()
    {
        var (competition, access) = Fixture((0x12, 3, Max, 1), (0x3E, 4, Max, 1));
        var plan = CompetitionRulePresets.LiftNationality(competition, access);
        Assert.Empty(plan.Edits);
        Assert.Equal("no validated squad maximum to lift to",
            plan.Skipped.Single(s => s.Code == 0x12).Reason);
    }

    [Fact]
    public void Planner_never_touches_rules_outside_its_lists()
    {
        var (competition, access) = Standard();
        var plan = CompetitionRulePresets.LiftNationality(competition, access);
        Assert.DoesNotContain(plan.Edits, e => e.Code == 0xA5);
        Assert.DoesNotContain(plan.Skipped, s => s.Code == 0xA5);
        Assert.Null(access.ReadRule(competition, 0x27));
    }
}
