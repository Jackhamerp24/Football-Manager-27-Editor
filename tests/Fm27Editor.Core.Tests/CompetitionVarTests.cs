using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CompetitionVarTests
{
    [Fact]
    public void Writes_FMRTE_bytes_and_refuses_a_replaced_VAR_object()
    {
        var p = GameSession.BuiltInProfiles().Single(x => x.Build == "27.0.0+2493426 Game Pass");
        var m = new FakeMemory(); const ulong b = 0x700000000000, comp = 0x100000, settings = 0x102000;
        var c = m.Map(comp, 0x200); TestWorld.Set64(c, 0, b + p.Competition!.VtableRva);
        BitConverter.GetBytes(22).CopyTo(c, 0xC); TestWorld.Set64(c, 0xD0, settings);
        var varBytes = m.Map(settings, 0x20);
        // Bundesliga initially: Referee And/Or Remote, TV off, custom off. FMRTE trace: 02 01 01.
        Convert.FromHexString("030000").CopyTo(varBytes, 0x14);
        Convert.FromHexString("01006C07").CopyTo(varBytes, 0x10);
        var competition = new CompetitionRecord(comp, 0, 22, null, null, 0, 0, 0, 0, 0, null, null);
        var access = new CompetitionVarAccess(m, p, b);
        var snapshot = access.Read(competition)!;
        access.Set(snapshot, CompetitionVarField.Type, 2);
        access.Set(snapshot, CompetitionVarField.TvOnly, 1);
        access.Set(snapshot, CompetitionVarField.Enabled, 1);
        Assert.Equal("020101", Convert.ToHexString(m.Read(settings + 0x14, 3)));
        access.SetFromDate(snapshot, new DateOnly(2027, 7, 1));
        Assert.Equal("B600EB07", Convert.ToHexString(m.Read(settings + 0x10, 4)));
        Assert.Equal(new DateOnly(2027, 7, 1), access.Read(competition)!.FromDate);
        access.SetFromDate(access.Read(competition)!, null);
        Assert.Equal("01006C07", Convert.ToHexString(m.Read(settings + 0x10, 4)));
        TestWorld.Set64(c, 0xD0, settings + 0x1000);
        int writes = m.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(snapshot, CompetitionVarField.Type, 3));
        Assert.Equal(writes, m.Writes);
    }
}
