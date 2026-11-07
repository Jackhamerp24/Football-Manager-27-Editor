using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Nations from FM27 RAM: [module + 0x56F5078] + 0x20 → nation pointers; national-team values in the men's team object at
/// nation + 0x108 (attendances one more hop away at +0xB0, as on clubs). Bytes are Vietnam's as dumped (ID 145, nation
/// +0x1E8.., team +0x274, +0x864..); expected values are FMRTE 27's Information / National Teams tabs. Writing FIFA
/// Position 98, Youth Rating 71, FA Financial Power 11, Years To Gain 4 and Development 1 showed in FMRTE as written,
/// which settled the neighbouring duplicate bytes (+0x27C, +0x86A, +0x882).
/// </summary>
public class NationTests
{
    const ulong Root = 0x7000_0000, Vector = 0x7000_1000, Vietnam = 0x7001_0000, Team = 0x7002_0000, Gate = 0x7003_0000;
    readonly TestWorld _w = new();

    public NationTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F5078, 8), 0, Root);
        var r = _w.Memory.Map(Root, 0x30);
        TestWorld.Set64(r, 0x20, Vector); TestWorld.Set64(r, 0x28, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, TestWorld.Mainoo); // not a nation: skipped
        TestWorld.Set64(v, 8, Vietnam);
        var n = _w.Memory.Map(Vietnam, 0x200);
        Convert.FromHexString("58F6E4F0FA7F00005A00000091000000").CopyTo(n, 0);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
        Convert.FromHexString("000000010205FFFF0A").CopyTo(n, 0x1E8);
        TestWorld.Set64(n, 0x108, Team);
        var t = _w.Memory.Map(Team, 0x900);
        Convert.FromHexString("000099440400630063006600").CopyTo(t, 0x274);
        Convert.FromHexString("AC07AC07AA0746004600").CopyTo(t, 0x864);
        Convert.FromHexString("010100").CopyTo(t, 0x87C); // Vietnam: Importance 1/1 (FMRTE Very Important), assistants 0
        Convert.FromHexString("0A0A15090C").CopyTo(t, 0x882);
        TestWorld.Set64(t, 0xB0, Gate);
        var g = _w.Memory.Map(Gate, 0x80);
        BitConverter.GetBytes(37000).CopyTo(g, 0x68); BitConverter.GetBytes(10000).CopyTo(g, 0x6C); BitConverter.GetBytes(37500).CopyTo(g, 0x70);
        // Women's team at nation + 0x110: England women's bytes as dumped (FMRTE: FIFA 2027 / position 4, 1972/1972/1863,
        // Youth Rating 161, FA Financial Power 18, Max Youth Age 21, FA Patience 19, Foreign Manager 16, 60000/45000/90000).
        TestWorld.Set64(n, 0x110, WomenTeam);
        var wt = _w.Memory.Map(WomenTeam, 0x900);
        Convert.FromHexString("0060FD440400040004000400").CopyTo(wt, 0x274);
        Convert.FromHexString("B407B4074707A100A100").CopyTo(wt, 0x864);
        Convert.FromHexString("1212151310").CopyTo(wt, 0x882);
        TestWorld.Set64(wt, 0xB0, WomenGate);
        Convert.FromHexString("60EA0000C8AF0000905F0100").CopyTo(_w.Memory.Map(WomenGate, 0x80), 0x68);
    }

    const ulong WomenTeam = 0x7004_0000, WomenGate = 0x7005_0000;

    NationFieldAccess Fields() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Finds_nations_when_the_database_holder_moves_to_a_neighbouring_slot()
    {
        // In the Game Pass session the old slot was invalid and the nation vector moved to holder +0x180.
        Assert.True(_w.Memory.TryWrite(TestWorld.ModuleBase + 0x56F5078, new byte[8]));
        const ulong movedHolder = 0x7100_0000, movedVector = 0x7100_1000;
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F5070, 8), 0, movedHolder);
        var holder = _w.Memory.Map(movedHolder, 0x810);
        TestWorld.Set64(holder, 0x180, movedVector);
        TestWorld.Set64(holder, 0x188, movedVector + 8);
        TestWorld.Set64(holder, 0x190, movedVector + 8);
        TestWorld.Set64(_w.Memory.Map(movedVector, 8), 0, Vietnam);
        var nation = Assert.Single(new NationRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).LoadAll());
        Assert.Equal(145, nation.Id);
    }

    [Fact]
    public void Lists_nations_and_reads_Vietnam_as_FMRTE_shows_it()
    {
        var nation = Assert.Single(new NationRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).LoadAll());
        Assert.Equal(145, nation.Id);
        var f = Fields().ReadAll(nation.Address);
        (NationField, long)[] expected =
        [
            (NationField.EconomicFactor, 10), (NationField.YearsToGainNationality, 5), (NationField.Development, 2), (NationField.GainNationalityType, 1),
            (NationField.AllowMultipleAssistantManagers, 0), (NationField.DoesNotAllowDualNationality, 0), (NationField.Importance, 1),
            (NationField.FifaRankingPoints, 1224), (NationField.FifaPosition, 99), (NationField.YearAffiliatedToFifa, 1964),
            (NationField.YearAffiliatedToFederation, 1964), (NationField.YearAssociationFormed, 1962), (NationField.YouthRating, 70),
            (NationField.FaFinancialPower, 10), (NationField.MaxYouthAge, 21), (NationField.FaPatience, 9),
            (NationField.ForeignManagerLikelihood, 12), (NationField.AverageAttendance, 37000), (NationField.MinimumAttendance, 10000),
            (NationField.MaximumAttendance, 37500),
            (NationField.WomenFifaRankingPoints, 2027), (NationField.WomenFifaPosition, 4), (NationField.WomenYearAffiliatedToFifa, 1972),
            (NationField.WomenYearAffiliatedToFederation, 1972), (NationField.WomenYearAssociationFormed, 1863), (NationField.WomenYouthRating, 161),
            (NationField.WomenFaFinancialPower, 18), (NationField.WomenMaxYouthAge, 21), (NationField.WomenFaPatience, 19),
            (NationField.WomenForeignManagerLikelihood, 16), (NationField.WomenAverageAttendance, 60000),
            (NationField.WomenMinimumAttendance, 45000), (NationField.WomenMaximumAttendance, 90000),
        ];
        Assert.Equal(Enum.GetValues<NationField>().Length, expected.Length);
        Assert.All(expected, e => Assert.Equal(e.Item2, f[e.Item1]));
    }

    [Fact]
    public void Dual_nationality_uses_only_bit_two_and_preserves_other_flags()
    {
        // FMRTE 27: Spain 00 unchecked, China PR 02 checked; both toggles were observed and restored.
        Assert.True(_w.Memory.TryWrite(Vietnam + 0x1E9, [0xA0]));
        Assert.Equal(0, Fields().Read(Vietnam, NationField.DoesNotAllowDualNationality));
        Fields().Set(Vietnam, NationField.DoesNotAllowDualNationality, 0, 1);
        Assert.Equal(0xA2, _w.Bytes(Vietnam, 0x200)[0x1E9]);
        Assert.Equal(1, Fields().Read(Vietnam, NationField.DoesNotAllowDualNationality));
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.DoesNotAllowDualNationality, 0, 1));
        Fields().Set(Vietnam, NationField.DoesNotAllowDualNationality, 1, 0);
        Assert.Equal(0xA0, _w.Bytes(Vietnam, 0x200)[0x1E9]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Fields().Set(Vietnam, NationField.DoesNotAllowDualNationality, 0, 2));
    }

    [Fact]
    public void Multiple_assistants_uses_the_observed_team_byte_and_guards_stale_changes()
    {
        // FM27 / FMRTE: Spain disabled (00), France enabled (01), with adjacent bytes captured from each team.
        Assert.True(_w.Memory.TryWrite(Team + 0x874, Convert.FromHexString("00000000000000010101000100001111")));
        Assert.Equal(0, Fields().Read(Vietnam, NationField.AllowMultipleAssistantManagers));
        Fields().Set(Vietnam, NationField.AllowMultipleAssistantManagers, 0, 1);
        Assert.Equal(1, _w.Bytes(Team, 0x900)[0x87E]);
        Assert.Equal(1, _w.Bytes(Team, 0x900)[0x87D]);
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.AllowMultipleAssistantManagers, 0, 1));
        Assert.True(_w.Memory.TryWrite(Team + 0x874, Convert.FromHexString("00000000000000010101010100001111")));
        Assert.Equal(1, Fields().Read(Vietnam, NationField.AllowMultipleAssistantManagers));
        Fields().Set(Vietnam, NationField.AllowMultipleAssistantManagers, 1, 0);
        Assert.Equal(0, _w.Bytes(Team, 0x900)[0x87E]);
        Assert.Equal(1, _w.Bytes(Team, 0x900)[0x87F]);
    }

    /// <summary>
    /// FMRTE 26 on FM26 writes Importance to two bytes (+0x874/+0x875, Spain and France: 1 Very Important … 4 Noone Cares);
    /// on FM27 the pair +0x87C/+0x87D is equal on all 251 nations, and writing +0x87C alone changed FMRTE 27's label
    /// (Spain 1 → 3 Unimportant, France 1 → 2 Important). Both copies are written; differing copies are refused.
    /// </summary>
    [Fact]
    public void Importance_writes_both_copies_and_refuses_differing_ones()
    {
        var before = _w.Bytes(Team, 0x900);
        Fields().Set(Vietnam, NationField.Importance, 1, 3);
        before[0x87C] = 3; before[0x87D] = 3;
        Assert.Equal(before, _w.Bytes(Team, 0x900));
        Assert.Equal(3, Fields().Read(Vietnam, NationField.Importance));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fields().Set(Vietnam, NationField.Importance, 3, 5));
        Assert.True(_w.Memory.TryWrite(Team + 0x87D, [2]));
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.Importance, 3, 1));
        Assert.Equal(2, _w.Bytes(Team + 0x87D, 1)[0]);
    }

    [Fact]
    public void Nationality_type_reads_and_writes_only_the_observed_byte()
    {
        // Vietnam: FMRTE showed Continuous for byte 1 and Accumulative after changing it to 2.
        var before = _w.Bytes(Vietnam, 0x200);
        Assert.Equal(1, Fields().Read(Vietnam, NationField.GainNationalityType));
        Fields().Set(Vietnam, NationField.GainNationalityType, 1, 2);
        before[0x1EB] = 2;
        Assert.Equal(before, _w.Bytes(Vietnam, 0x200));
        Assert.Equal(2, Fields().Read(Vietnam, NationField.GainNationalityType));
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.GainNationalityType, 1, 2));
    }

    [Fact]
    public void Writes_touch_only_their_bytes_and_refuse_stale_values()
    {
        var team = _w.Bytes(Team, 0x900);
        Fields().Set(Vietnam, NationField.FifaPosition, 99, 98);
        Fields().Set(Vietnam, NationField.YouthRating, 70, 71);
        Fields().Set(Vietnam, NationField.FifaRankingPoints, 1224, 1300);
        team[0x27A] = 98; team[0x86C] = 71; BitConverter.GetBytes(1300f).CopyTo(team, 0x274);
        Assert.Equal(team, _w.Bytes(Team, 0x900));
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.FifaPosition, 99, 97));
        Assert.True(_w.Memory.TryWrite(Vietnam, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CDF000)));
        Assert.Throws<StaleObjectException>(() => Fields().Set(Vietnam, NationField.EconomicFactor, 10, 11));
        Assert.Empty(Fields().ReadAll(Vietnam));
    }
}
