using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CompetitionStandingsTests
{
    [Fact]
    public void Reads_FMRTE_rows_and_refuses_to_write_a_replaced_team()
    {
        var p = GameSession.BuiltInProfiles().Single(x => x.Build == "27.0.0+2493426 Game Pass");
        var m = new FakeMemory(); const ulong b = 0x700000000000, comp = 0x100000, holder = 0x102000,
            stages = 0x104000, stage = 0x106000, group = 0x108000, vector = 0x10A000;
        var c = m.Map(comp, 0x200); TestWorld.Set64(c, 0, b + p.Competition!.VtableRva);
        BitConverter.GetBytes(11).CopyTo(c, 0xC); TestWorld.Set64(c, 0xB0, holder);
        var h = m.Map(holder, 0x1D0); TestWorld.Set64(h, 0, b + 0x4C91958);
        TestWorld.Set64(h, 0x1C0, stages); TestWorld.Set64(h, 0x1C8, stages + 8);
        TestWorld.Set64(m.Map(stages, 8), 0, stage);
        var s = m.Map(stage, 0xF8); TestWorld.Set64(s, 0, b + 0x4D7C7B8);
        TestWorld.Set64(s, 0x70, comp); TestWorld.Set64(s, 0xC0, vector); TestWorld.Set64(s, 0xC8, vector + 16); TestWorld.Set64(s, 0xF0, group);
        TestWorld.Set64(m.Map(group, 0x1F8), 0, b + 0x4D7EA18);
        var v = m.Map(vector, 16);
        void Row(int index, ulong row, ulong team, ulong club, int id, string data)
        {
            TestWorld.Set64(v, index * 8, row);
            var r = m.Map(row, 0x90); Convert.FromHexString(data).CopyTo(r, 8); TestWorld.Set64(r, 0x88, team);
            var t = m.Map(team, 0x38); TestWorld.Set64(t, 0, b + p.Team!.VtableRva); TestWorld.Set64(t, 0x30, club);
            var cb = m.Map(club, 0x100); TestWorld.Set64(cb, 0, b + p.Club!.ClubVtableRva);
            BitConverter.GetBytes(id).CopyTo(cb, p.Club.Id!.Value);
        }
        // Game Pass bytes recorded from FMRTE: Man Utd 34/15/37, Arsenal 22/7/27.
        Row(0, 0x10C000, 0x10E000, 0x110000, 680, "22000F0025000B0B0A01");
        Row(1, 0x112000, 0x114000, 0x116000, 602, "160007001B000A0A0802");
        var competition = new CompetitionRecord(comp, 0, 11, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionStandingsAccess(m, p, b);
        var rows = access.Read(competition)!;
        Assert.Equal((680, (short)37), (rows[0].ClubId, rows[0].Points));
        Assert.Equal((602, (short)27), (rows[1].ClubId, rows[1].Points));
        // FMRTE creates one zero prize per standing when enabling an empty table.
        var prizeAccess = new CompetitionPrizeMoneyAccess(m, p, b);
        var empty = prizeAccess.ReadClassification(prizeAccess.Read(competition)!)!;
        const ulong buffer = 0x120000;
        prizeAccess.SetClassificationEnabled(empty, true, size => { m.Map(buffer, size); return buffer; });
        var enabled = prizeAccess.ReadClassification(prizeAccess.Read(competition)!)!;
        Assert.Equal(new[] { 0, 0 }, enabled.Amounts);
        Assert.Equal(buffer + 8, enabled.End);
        prizeAccess.SetClassificationEnabled(enabled, false, _ => throw new InvalidOperationException());
        var disabled = prizeAccess.ReadClassification(prizeAccess.Read(competition)!)!;
        prizeAccess.SetClassificationEnabled(disabled, true, _ => throw new InvalidOperationException("Existing capacity should be reused"));
        Assert.Equal(new[] { 0, 0 }, prizeAccess.ReadClassification(prizeAccess.Read(competition)!)!.Amounts);
        access.SetPoints(competition, rows[1], 28);
        Assert.Equal("1C00", Convert.ToHexString(m.Read(0x11200C, 2)));
        access.Set(competition, rows[1], CompetitionStandingField.Played, 11);
        access.Set(competition, rows[1], CompetitionStandingField.Won, 9);
        access.Set(competition, rows[1], CompetitionStandingField.Drawn, 3);
        access.Set(competition, rows[1], CompetitionStandingField.Lost, 1);
        access.Set(competition, rows[1], CompetitionStandingField.GoalsFor, 23);
        access.Set(competition, rows[1], CompetitionStandingField.GoalsAgainst, 8);
        Assert.Equal("170008001C000A0B090301", Convert.ToHexString(m.Read(0x112008, 11)));
        Assert.True(m.TryWrite(0x10C088, BitConverter.GetBytes(0x114000UL)));
        int count = m.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetPoints(competition, rows[0], 38));
        Assert.Equal(count, m.Writes);
    }
}
