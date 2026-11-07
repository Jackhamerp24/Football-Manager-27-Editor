using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Nation squads share the club team squad vector (team+0x38) but the national team vtable, measured on England 765
/// (26 senior players in the same order FMRTE lists them).
/// </summary>
public class NationSquadsTests
{
    const ulong Team = 0x7800_0000, Squad = 0x7801_0000;
    readonly TestWorld _w = new();

    [Fact]
    public void Reads_the_squad_vector_with_the_national_team_vtable_and_skips_non_players()
    {
        var nationVt = _w.Profile.Nation?.Teams?.TeamVtable ?? 0x4D71998;
        var team = _w.Memory.Map(Team, 0x50);
        TestWorld.Set64(team, 0, TestWorld.ModuleBase + (uint)nationVt);
        TestWorld.Set64(team, 0x38, Squad);
        TestWorld.Set64(team, 0x40, Squad + 3 * 8);
        var squad = _w.Memory.Map(Squad, 3 * 8);
        TestWorld.Set64(squad, 0, TestWorld.Yamal);
        TestWorld.Set64(squad, 8, TestWorld.NotAPlayer);
        TestWorld.Set64(squad, 16, TestWorld.Mainoo);

        var access = new NationSquads(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        Assert.Equal([TestWorld.Yamal, TestWorld.Mainoo], access.Read(Team).Select(p => p.Address));

        // A club team vtable must not be accepted as a national team.
        TestWorld.Set64(team, 0, TestWorld.ModuleBase + 0x4CE1108);
        Assert.Empty(access.Read(Team));
    }

}
