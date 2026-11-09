using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffMoveTests
{
    const ulong Module = 0x7000_0000_0000, Staff = 0x1000_0000, Person = Staff + 0x100, Contract = 0x2000_0000;
    const ulong From = 0x3000_0000, To = 0x3001_0000, FromTeam = 0x4000_0000, ToTeam = 0x4001_0000;
    const ulong FromTeams = 0x5000_0000, ToTeams = 0x5001_0000, FromMedical = 0x5002_0000,
        FromCoaching = 0x5003_0000, FromScouting = 0x5004_0000, ToMedical = 0x5005_0000,
        ToCoaching = 0x5006_0000, ToScouting = 0x5007_0000, Other = 0x6000_0000;
    readonly FakeMemory memory = new();
    readonly Fm27Editor.Core.Offsets.OffsetProfile profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
    readonly StaffRecord member;
    readonly ClubEntry from = new(From, 680, "Manchester United", "Man Utd", 0, []);
    readonly ClubEntry to = new(To, 868, "Paris Saint-Germain", "PSG", 0, []);

    public StaffMoveTests()
    {
        var s = memory.Map(Staff, 0x240);
        TestWorld.Set64(s, 0, Module + 0x4D3B358);
        TestWorld.Set64(s, 0x100, Module + 0x4D3B3C8);
        BitConverter.GetBytes(4293).CopyTo(s, 0x10C);
        TestWorld.Set64(s, 0x1A8, Contract);
        var contract = memory.Map(Contract, 0x60);
        // Paulinho Santos's Game Pass contract bytes. Only process pointers are relocated.
        Convert.FromHexString("F80562DDFA7F000000A3B63472020000003EA72D72020000003EA72D72020000740A0000000002000000000000000000").CopyTo(contract, 0);
        TestWorld.Set64(contract, 0, Module + 0x4AC05F8);
        TestWorld.Set64(contract, 8, Person);
        TestWorld.Set64(contract, 0x10, FromTeam);
        TestWorld.Set64(contract, 0x18, FromTeam);
        // Signed/joined 5 Aug 2026 and expiry 30 Jun 2030, as read from this contract.
        Convert.FromHexString("D93CEA07B500EE07D93CEA07").CopyTo(contract, 0x44);
        var fromClub = memory.Map(From, 0x170);
        var toClub = memory.Map(To, 0x170);
        TestWorld.Set64(fromClub, 0, Module + 0x4C89B68);
        TestWorld.Set64(toClub, 0, Module + 0x4C89B68);
        BitConverter.GetBytes(680).CopyTo(fromClub, 0xC);
        BitConverter.GetBytes(868).CopyTo(toClub, 0xC);
        var fromTeam = memory.Map(FromTeam, 0x800);
        var toTeam = memory.Map(ToTeam, 0x800);
        TestWorld.Set64(fromTeam, 0, Module + 0x4C90A78);
        TestWorld.Set64(toTeam, 0, Module + 0x4C90A78);
        TestWorld.Set64(fromTeam, 0x30, From);
        TestWorld.Set64(toTeam, 0x30, To);
        Vector(fromClub, 0x18, FromTeams, [FromTeam], 1);
        Vector(toClub, 0x18, ToTeams, [ToTeam], 1);
        Vector(fromClub, 0x60, FromMedical, [Other], 3);
        Vector(fromClub, 0x78, FromCoaching, [Other, Staff, Other + 8], 3);
        Vector(fromClub, 0x90, FromScouting, [Other], 3);
        Vector(toClub, 0x60, ToMedical, [Other], 3);
        Vector(toClub, 0x78, ToCoaching, [Other], 3);
        Vector(toClub, 0x90, ToScouting, [Other], 3);
        member = new StaffRecord
        {
            Address = Person, StaffAddress = Staff, Id = 4293, FirstName = "Paulinho", LastName = "Santos",
            Attributes = [], Personality = [], Kind = profile.Staff!.Kinds.Single(k => k.PersonVtableRva == 0x4D3B3C8),
        };
    }

    void Vector(byte[] owner, int offset, ulong address, ulong[] values, int capacity)
    {
        TestWorld.Set64(owner, offset, address);
        TestWorld.Set64(owner, offset + 8, address + (ulong)values.Length * 8);
        TestWorld.Set64(owner, offset + 16, address + (ulong)capacity * 8);
        var data = memory.Map(address, capacity * 8);
        for (int i = 0; i < values.Length; i++) TestWorld.Set64(data, i * 8, values[i]);
    }

    ulong Q(ulong address) => BitConverter.ToUInt64(memory.Read(address, 8));

    [Fact]
    public void Move_and_undo_preserve_real_contract_and_change_both_teams_and_department()
    {
        var service = new StaffMoveService(memory, profile, Module);
        var original = memory.Read(Contract, 0x60);
        var plan = service.Plan(member, from, to);
        Assert.Equal(new[] { "Coaching" }, plan.Departments);
        service.Apply(plan);
        Assert.Equal(ToTeam, Q(Contract + 0x10));
        Assert.Equal(ToTeam, Q(Contract + 0x18));
        Assert.Equal(FromCoaching + 16, Q(From + 0x80));
        Assert.Equal(new[] { Other, Other + 8, 0UL }, new[] { Q(FromCoaching), Q(FromCoaching + 8), Q(FromCoaching + 16) });
        Assert.Equal(ToCoaching + 16, Q(To + 0x80));
        Assert.Equal(Staff, Q(ToCoaching + 8));
        service.Undo(plan);
        Assert.Equal(original, memory.Read(Contract, 0x60));
        Assert.Equal(FromCoaching + 24, Q(From + 0x80));
        Assert.Equal(new[] { Other, Staff, Other + 8 }, new[] { Q(FromCoaching), Q(FromCoaching + 8), Q(FromCoaching + 16) });
        Assert.Equal(ToCoaching + 8, Q(To + 0x80));
        Assert.Equal(0UL, Q(ToCoaching + 8));
    }

    [Fact]
    public void A_new_role_and_wage_move_the_staff_to_that_roles_department_and_undo_restores_everything()
    {
        var service = new StaffMoveService(memory, profile, Module);
        var original = memory.Read(Contract, 0x60);
        var plan = service.Plan(member, from, to, newJob: 12, newWage: 5000);   // 12 = a medical role
        Assert.Equal(new[] { "Medical" }, plan.Departments);
        service.Apply(plan);
        Assert.Equal(12, memory.Read(Contract + 0x26, 1)[0]);
        Assert.Equal(5000, BitConverter.ToInt32(memory.Read(Contract + 0x20, 4)));
        Assert.Equal(ToTeam, Q(Contract + 0x10));
        Assert.Equal(new[] { Other, Other + 8, 0UL }, new[] { Q(FromCoaching), Q(FromCoaching + 8), Q(FromCoaching + 16) });   // left the coaching list
        Assert.Equal(Staff, Q(ToMedical + 8));                                                                                   // joined the medical list
        Assert.Equal(ToMedical + 16, Q(To + 0x68));
        Assert.Equal(ToCoaching + 8, Q(To + 0x80));                                                                              // coaching list untouched
        service.Undo(plan);
        Assert.Equal(original, memory.Read(Contract, 0x60));
        Assert.Equal(ToMedical + 8, Q(To + 0x68));
        Assert.Equal(new[] { Other, Staff, Other + 8 }, new[] { Q(FromCoaching), Q(FromCoaching + 8), Q(FromCoaching + 16) });
    }

    [Fact]
    public void A_role_a_move_cannot_give_is_refused_before_any_write()
    {
        var service = new StaffMoveService(memory, profile, Module);
        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => service.Plan(member, from, to, newJob: 16));   // 16 = Manager
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Plan(member, from, to, newWage: -1));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Refuses_changed_team_before_any_write()
    {
        var service = new StaffMoveService(memory, profile, Module);
        var plan = service.Plan(member, from, to);
        Assert.True(memory.TryWrite(FromTeam + 0x88, BitConverter.GetBytes(Person)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => service.Apply(plan));
        Assert.Equal(writes, memory.Writes);
        Assert.Equal(FromTeam, Q(Contract + 0x10));
    }

    [Fact]
    public void Uses_verified_empty_allocator_spare_and_undoes_capacity()
    {
        // FMLibre's FM27 allocator witnesses: 19 pointers (152 bytes) occupy a 160-byte block.
        const ulong buffer = 0x5010_0000;
        var bytes = memory.Map(buffer, 160);
        for (int i = 0; i < 19; i++) TestWorld.Set64(bytes, i * 8, Other + (ulong)i * 8);
        Assert.True(memory.TryWrite(To + 0x78, BitConverter.GetBytes(buffer)));
        Assert.True(memory.TryWrite(To + 0x80, BitConverter.GetBytes(buffer + 152)));
        Assert.True(memory.TryWrite(To + 0x88, BitConverter.GetBytes(buffer + 152)));
        var service = new StaffMoveService(memory, profile, Module);
        var plan = service.Plan(member, from, to);
        service.Apply(plan);
        Assert.Equal(Staff, Q(buffer + 152));
        Assert.Equal(buffer + 160, Q(To + 0x80));
        Assert.Equal(buffer + 160, Q(To + 0x88));
        service.Undo(plan);
        Assert.Equal(0UL, Q(buffer + 152));
        Assert.Equal(buffer + 152, Q(To + 0x80));
        Assert.Equal(buffer + 152, Q(To + 0x88));
        Assert.True(memory.TryWrite(buffer + 152, BitConverter.GetBytes(1UL)));
        Assert.Throws<InvalidOperationException>(() => service.Plan(member, from, to));
    }

    [Fact]
    public void Dates_move_to_game_day_and_preserve_flags_and_expiry()
    {
        var service = new StaffMoveService(memory, profile, Module);
        var plan = service.Plan(member, from, to, new DateOnly(2027, 1, 10));
        service.Apply(plan);
        Assert.Equal("0A3CEB07B500EE070A3CEB07", Convert.ToHexString(memory.Read(Contract + 0x44, 12)));
        service.Undo(plan);
        Assert.Equal("D93CEA07B500EE07D93CEA07", Convert.ToHexString(memory.Read(Contract + 0x44, 12)));
    }

    [Fact]
    public void Moves_and_undoes_every_department_that_contains_staff()
    {
        Assert.True(memory.TryWrite(FromMedical + 8, BitConverter.GetBytes(Staff)));
        Assert.True(memory.TryWrite(From + 0x68, BitConverter.GetBytes(FromMedical + 16)));
        var service = new StaffMoveService(memory, profile, Module);
        var plan = service.Plan(member, from, to);
        Assert.Equal(new[] { "Medical", "Coaching" }, plan.Departments);
        service.Apply(plan);
        Assert.Equal(Staff, Q(ToMedical + 8));
        Assert.Equal(Staff, Q(ToCoaching + 8));
        Assert.Equal(0UL, Q(FromMedical + 8));
        Assert.Equal(Other + 8, Q(FromCoaching + 8));
        service.Undo(plan);
        Assert.Equal(Staff, Q(FromMedical + 8));
        Assert.Equal(Staff, Q(FromCoaching + 8));
        Assert.Equal(0UL, Q(ToMedical + 8));
        Assert.Equal(0UL, Q(ToCoaching + 8));
    }
}
