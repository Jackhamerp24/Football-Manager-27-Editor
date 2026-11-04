using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class TeamCaptainTests
{
    // Away from TestWorld's name strings at 0x3000_0000; FakeMemory matches the first mapped segment.
    const ulong Club = 0x7000_0000, Team = 0x7001_0000, Squad = 0x7002_0000, PreviousPerson = 0x7003_0000;
    readonly TestWorld w = new();
    readonly TeamCaptainService service;
    readonly PlayerRecord player;

    public TeamCaptainTests()
    {
        // Mainoo already exists in TestWorld, so writes go through the memory rather than a second mapping.
        Set(TestWorld.Mainoo, 0x294, 12345);
        Set(TestWorld.Mainoo, 0x130, Team);
        Set(TestWorld.Mainoo, 0x288, TestWorld.ModuleBase + w.Profile.Relations!.PersonVtable);
        var club = w.Memory.Map(Club, 0x60);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + w.Profile.Club!.ClubVtableRva);
        var team = w.Memory.Map(Team, 0xA0);
        TestWorld.Set64(team, 0, TestWorld.ModuleBase + w.Profile.Team!.VtableRva);
        TestWorld.Set64(team, 0x30, Club);
        TestWorld.Set64(team, 0x88, PreviousPerson);
        TestWorld.Set64(team, 0x90, PreviousPerson);
        TestWorld.Set64(team, 0x38, Squad);
        TestWorld.Set64(team, 0x40, Squad + 8);
        TestWorld.Set64(team, 0x48, Squad + 8);
        var squad = w.Memory.Map(Squad, 8);
        TestWorld.Set64(squad, 0, TestWorld.Mainoo);
        player = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        service = new TeamCaptainService(w.Memory, w.Profile, TestWorld.ModuleBase);
    }

    void Set(ulong address, int offset, ulong value) =>
        Assert.True(w.Memory.TryWrite(address + (ulong)offset, BitConverter.GetBytes(value)));

    ulong Q(ulong address) => BitConverter.ToUInt64(w.Memory.Read(address, 8), 0);

    [Fact]
    public void Captain_and_vice_use_the_team_person_slot_and_undo()
    {
        var captain = service.Plan(player, CaptainRole.Captain);
        Assert.Equal(TestWorld.Mainoo + 0x288, captain.Person);
        Assert.Equal(PreviousPerson, captain.Previous);
        service.Apply(captain);
        Assert.Equal(TestWorld.Mainoo + 0x288, Q(Team + 0x88));
        service.Undo(captain);
        Assert.Equal(PreviousPerson, Q(Team + 0x88));

        var vice = service.Plan(player, CaptainRole.ViceCaptain);
        service.Apply(vice);
        Assert.Equal(TestWorld.Mainoo + 0x288, Q(Team + 0x90));
        service.Undo(vice);
        Assert.Equal(PreviousPerson, Q(Team + 0x90));
    }

    [Fact]
    public void Refuses_a_captaincy_changed_after_the_plan()
    {
        var plan = service.Plan(player, CaptainRole.Captain);
        Assert.True(w.Memory.TryWrite(Team + 0x88, BitConverter.GetBytes(TestWorld.Yamal)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => service.Apply(plan));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.Equal(TestWorld.Yamal, Q(Team + 0x88));
    }
}
