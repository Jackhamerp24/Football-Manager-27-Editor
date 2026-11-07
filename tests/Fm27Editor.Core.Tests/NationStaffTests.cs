using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Tests;

public class NationStaffTests
{
    [Fact]
    public void National_appointments_use_the_correct_person_and_team()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong nation = 0x79000000, holder = 0x79001000, teams = 0x79002000, senior = 0x79003000,
            u21 = 0x79004000, coaching = 0x79005000, barry = 0x79006000, carsley = 0x79007000,
            barryHolder = 0x79008000, carsleyHolder = 0x79009000, barryJob = 0x7900A000, carsleyJob = 0x7900B000;
        const int staffPart = 0x100;
        var n = memory.Map(nation, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + profile.Nation!.VtableRva);
        TestWorld.Set64(n, 0x108, holder);
        var h = memory.Map(holder, 0x100);
        TestWorld.Set64(h, 0x18, teams); TestWorld.Set64(h, 0x20, teams + 16); TestWorld.Set64(h, 0x28, teams + 16);
        TestWorld.Set64(h, 0x78, coaching); TestWorld.Set64(h, 0x80, coaching + 8); TestWorld.Set64(h, 0x88, coaching + 8);
        var teamPointers = memory.Map(teams, 16);
        TestWorld.Set64(teamPointers, 0, senior); TestWorld.Set64(teamPointers, 8, u21);
        foreach (var (address, type) in new[] { (senior, (byte)0), (u21, (byte)10) })
        {
            var team = memory.Map(address, 0xB0);
            TestWorld.Set64(team, 0, TestWorld.ModuleBase + profile.Nation.Teams!.TeamVtable);
            team[0x28] = type;
            if (address == u21) TestWorld.Set64(team, 0x80, carsley);
        }
        TestWorld.Set64(memory.Map(coaching, 8), 0, barry);
        var kind = profile.Staff!.Kinds.First(k => k.StaffPart == staffPart && !k.Manager);
        foreach (var (address, id, jobHolder) in new[] { (barry, 5112804, barryHolder), (carsley, 2014535, carsleyHolder) })
        {
            var bytes = memory.Map(address, 0x1B8);
            TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + kind.StaffVtableRva);
            TestWorld.Set64(bytes, staffPart, TestWorld.ModuleBase + kind.PersonVtableRva);
            BitConverter.GetBytes(id).CopyTo(bytes, staffPart + profile.Staff.Id);
            TestWorld.Set64(bytes, staffPart + 0xB0, jobHolder);
        }
        TestWorld.Set64(memory.Map(barryHolder, 0x18), 0x10, barryJob);
        TestWorld.Set64(memory.Map(carsleyHolder, 0x18), 0x10, carsleyJob);
        foreach (var (address, person, team, job) in new[] { (barryJob, barry + staffPart, senior, (byte)20), (carsleyJob, carsley + staffPart, u21, (byte)16) })
        {
            var bytes = memory.Map(address, 0x28);
            TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + 0x4AC0B68);
            TestWorld.Set64(bytes, 8, person); TestWorld.Set64(bytes, 0x10, team); bytes[0x26] = job;
        }
        var reader = new NationStaff(memory, profile, TestWorld.ModuleBase);
        var rows = reader.ReadDetails(nation);
        Assert.Equal([2014535, 5112804], rows.Select(r => r.Staff.Id));
        Assert.Equal(("Manager", " U21"), (rows[0].JobName, rows[0].TeamSuffix));
        Assert.Equal(("Ast. Manager", ""), (rows[1].JobName, rows[1].TeamSuffix));
        Assert.True(memory.TryWrite(u21 + 0x28, [12]));
        Assert.Equal(" U18", reader.ReadDetails(nation)[0].TeamSuffix);
        Assert.True(memory.TryWrite(barryJob + 8, BitConverter.GetBytes(carsley + staffPart)));
        var changed = reader.ReadDetails(nation).Single(r => r.Staff.Id == 5112804);
        Assert.Null(changed.Team);
        Assert.Null(changed.JobCode);
    }
}
