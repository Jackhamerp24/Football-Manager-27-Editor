using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

public class ReleaseServiceTests
{
    const ulong Module = 0x7000_0000_0000, Player = 0x1000_0000, Contract = 0x2000_0000,
        Club = 0x3000_0000, Team1 = 0x4000_0000, Team2 = 0x4001_0000,
        Teams = 0x5000_0000, Squad1 = 0x5001_0000, Squad2 = 0x5002_0000,
        Other = 0x6000_0000, Manager = 0x6100_0000, ManagerPerson = Manager + 0x440, ManagerContract = 0x6200_0000;
    readonly FakeMemory memory = new();
    readonly Fm27Editor.Core.Offsets.OffsetProfile profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
    readonly PlayerRecord player;
    readonly StaffRecord manager;
    readonly ReleaseService service;

    public ReleaseServiceTests()
    {
        var p = memory.Map(Player, 0x3C8);
        TestWorld.Set64(p, 0, Module + 0x4CD6318);
        BitConverter.GetBytes(19334410).CopyTo(p, 0x294);
        TestWorld.Set64(p, 0x130, Team1);
        TestWorld.Set64(p, 0x330, Contract);
        var contract = memory.Map(Contract, 0x60);
        // Cunha's Game Pass contract; only pointers are relocated into the fixture.
        Convert.FromHexString("F80562DDFA7F000030B80A3772020000003EA72D72020000003EA72D72020000400D03000B0001000000000000000000000000000000000032660000FFFFFFFFFFFFFFFFA300E907B500EE07A300E90701006C07020002000000505C140A0000").CopyTo(contract, 0);
        TestWorld.Set64(contract, 0, Module + 0x4AC05F8);
        TestWorld.Set64(contract, 8, Player + 0x288);
        TestWorld.Set64(contract, 0x10, Team1);
        TestWorld.Set64(contract, 0x18, Team1);
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, Module + 0x4C89B68);
        BitConverter.GetBytes(680).CopyTo(club, 0xC);
        Vector(club, 0x18, Teams, [Team1, Team2]);
        var first = memory.Map(Team1, 0xA0);
        var second = memory.Map(Team2, 0xA0);
        TestWorld.Set64(first, 0, Module + 0x4C90A78);
        TestWorld.Set64(second, 0, Module + 0x4C90A78);
        TestWorld.Set64(first, 0x30, Club); TestWorld.Set64(second, 0x30, Club);
        TestWorld.Set64(first, 0x88, Player + 0x288);
        TestWorld.Set64(second, 0x90, Player + 0x288);
        Vector(first, 0x38, Squad1, [Other, Player, Other + 8]);
        Vector(second, 0x38, Squad2, [Player, Other + 16]);
        var m = memory.Map(Manager, 0x580);
        TestWorld.Set64(m, 0, Module + 0x4C701FC);
        TestWorld.Set64(m, 0x440, Module + 0x4C70268);
        BitConverter.GetBytes(2002081897).CopyTo(m, 0x44C);
        manager = new StaffRecord { Address = ManagerPerson, StaffAddress = Manager, Id = 2002081897,
            DateOfBirth = new DateOnly(2004, 8, 20), Attributes = [], Personality = [],
            Kind = profile.Staff!.Kinds.Single(k => k.Manager) };
        player = new PlayerRepository(memory, profile, Module).Reload(Player)!;
        service = new ReleaseService(memory, profile, Module, () => [manager]);
    }

    void Vector(byte[] owner, int offset, ulong address, ulong[] values)
    {
        TestWorld.Set64(owner, offset, address);
        TestWorld.Set64(owner, offset + 8, address + (ulong)values.Length * 8);
        TestWorld.Set64(owner, offset + 16, address + (ulong)values.Length * 8);
        var data = memory.Map(address, values.Length * 8);
        for (int i = 0; i < values.Length; i++) TestWorld.Set64(data, i * 8, values[i]);
    }
    ulong Q(ulong address) => BitConverter.ToUInt64(memory.Read(address, 8));

    [Fact]
    public void Release_and_undo_cover_each_squad_and_captaincy()
    {
        var original = memory.Read(Contract, 0x60);
        var plan = service.Plan(player);
        Assert.Equal(2, plan.Squads);
        Assert.Equal(2, plan.Captaincies);
        service.Apply(plan);
        Assert.Equal(0UL, Q(Player + 0x130));
        Assert.Equal(0UL, Q(Player + 0x330));
        Assert.Equal(0UL, Q(Team1 + 0x88));
        Assert.Equal(0UL, Q(Team2 + 0x90));
        Assert.Equal(new[] { Other, Other + 8, 0UL }, new[] { Q(Squad1), Q(Squad1 + 8), Q(Squad1 + 16) });
        Assert.Equal(new[] { Other + 16, 0UL }, new[] { Q(Squad2), Q(Squad2 + 8) });
        Assert.Equal(original, memory.Read(Contract, 0x60));
        service.Undo(plan);
        Assert.Equal(Team1, Q(Player + 0x130));
        Assert.Equal(Contract, Q(Player + 0x330));
        Assert.Equal(Player + 0x288, Q(Team1 + 0x88));
        Assert.Equal(Player + 0x288, Q(Team2 + 0x90));
        Assert.Equal(new[] { Other, Player, Other + 8 }, new[] { Q(Squad1), Q(Squad1 + 8), Q(Squad1 + 16) });
        Assert.Equal(new[] { Player, Other + 16 }, new[] { Q(Squad2), Q(Squad2 + 8) });
        Assert.True(memory.TryWrite(Team2 + 0x88, BitConverter.GetBytes(Player + 0x288)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => service.Apply(plan));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Refuses_club_with_human_manager()
    {
        var contract = memory.Map(ManagerContract, 0x60);
        TestWorld.Set64(contract, 0, Module + 0x4AC05F8);
        TestWorld.Set64(contract, 8, ManagerPerson);
        TestWorld.Set64(contract, 0x10, Team1);
        Assert.True(memory.TryWrite(ManagerPerson + 0xA8, BitConverter.GetBytes(ManagerContract)));
        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => service.Plan(player));
        Assert.Equal(writes, memory.Writes);
    }
}
