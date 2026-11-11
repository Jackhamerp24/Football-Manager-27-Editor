using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffTeamTests
{
    [Theory]
    [InlineData("27.0.0+2493426-steam.json")]
    [InlineData("27.0.0+2493426-gamepass.json")]
    public void Reads_team_names_observed_in_FMRTE_and_rejects_an_unrecognized_team(string profileName)
    {
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", profileName));
        var memory = new FakeMemory();
        const ulong module = TestWorld.ModuleBase;

        StaffRecord Read(ulong start, string clubName, string observedBytes, bool validVtable = true)
        {
            ulong person = start + 0x100, job = start + 0x1000, team = start + 0x2000, club = start + 0x3000, name = start + 0x4000;
            var staff = memory.Map(start, 0x1B0);
            TestWorld.Set64(staff, 0, module + profile.Staff!.Kinds[0].StaffVtableRva);
            TestWorld.Set64(staff, 0x100, module + profile.Staff.Kinds[0].PersonVtableRva);
            TestWorld.Set64(staff, 0x1A8, job);
            var jobBytes = memory.Map(job, 0x28);
            TestWorld.Set64(jobBytes, 8, person);
            TestWorld.Set64(jobBytes, 0x10, team);
            var teamBytes = memory.Map(team, 0x40);
            TestWorld.Set64(teamBytes, 0, module + profile.Team!.VtableRva + (validVtable ? 0u : 8u));
            Convert.FromHexString(observedBytes).CopyTo(teamBytes, 0x20);
            TestWorld.Set64(teamBytes, 0x30, club);
            var clubBytes = memory.Map(club, 0xD0);
            TestWorld.Set64(clubBytes, 0, module + profile.Club!.ClubVtableRva);
            TestWorld.Set64(clubBytes, 0xC8, name);
            memory.MapString(name, clubName);
            return new StaffRepository(memory, profile, module).Reload(person)!;
        }

        // FMRTE 2493426: George Willis (28059685) / Adam Lawrence (2000278197) are U21;
        // Jim O'Brien (5209579) / Darren Fletcher (127973) are U18. The 16 bytes match both Steam and Game Pass dumps.
        const string under21 = "00000000000000000A01000015FF3400";
        const string under18 = "00000000000000000C01000012FF3400";
        const string first = "00000000000000000000000064FF2400";
        Assert.Equal("Sheffield United U21", Read(0x61000000, "Sheffield United", under21).TeamName);
        Assert.Equal("Sheffield United U18", Read(0x61010000, "Sheffield United", under18).TeamName);
        Assert.Equal("Man Utd U21", Read(0x61020000, "Man Utd", under21).TeamName);
        Assert.Equal("Man Utd U18", Read(0x61030000, "Man Utd", under18).TeamName);
        Assert.Equal("Man Utd", Read(0x61040000, "Man Utd", first).TeamName);
        Assert.Null(Read(0x61050000, "Man Utd", under21, validVtable: false).TeamName);
        Assert.Null(Read(0x61060000, "Man Utd", "00000000000000000A01000012FF3400").TeamName);
    }
}
