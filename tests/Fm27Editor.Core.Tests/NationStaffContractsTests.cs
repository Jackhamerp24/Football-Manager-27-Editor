using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>Carsley's expiry as observed on 26/11/2026: renew to 26/11/2030, expire to 25/11/2026.</summary>
public class NationStaffContractsTests
{
    const ulong Person = 0x7700_0000, Holder = 0x7701_0000, Contract = 0x7702_0000;
    readonly FakeMemory memory = new();
    readonly Fm27Editor.Core.Offsets.OffsetProfile profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
    readonly StaffRecord staff;
    readonly NationStaffContracts access;

    public NationStaffContractsTests()
    {
        var person = memory.Map(Person, 0xC0);
        TestWorld.Set64(person, 0xB0, Holder);
        TestWorld.Set64(memory.Map(Holder, 0x18), 0x10, Contract);
        var contract = memory.Map(Contract, 0xD0);
        TestWorld.Set64(contract, 0, TestWorld.ModuleBase + profile.FutureTransfers!.FullContractVtable);
        TestWorld.Set64(contract, 8, Person);
        Convert.FromHexString("0100E907").CopyTo(contract, profile.FutureTransfers.Started);
        Convert.FromHexString("01A06C07").CopyTo(contract, profile.FutureTransfers.Expires);
        contract[profile.FutureTransfers.Type] = 1;
        staff = new StaffRecord { Address = Person, StaffAddress = Person - 0x100, Attributes = [], Personality = [], Kind = profile.Staff!.Kinds[0] };
        access = new NationStaffContracts(memory, profile, TestWorld.ModuleBase);
    }

    [Theory]
    [InlineData(true, "4AA1EE07")]
    [InlineData(false, "49A1EA07")]
    public void RenewAndExpireChangeOnlyExpiryAndPreserveFlags(bool renew, string expectedExpiry)
    {
        var snapshot = access.Read(staff)!;
        var expected = memory.Read(Contract, 0xD0);
        Convert.FromHexString(expectedExpiry).CopyTo(expected, profile.FutureTransfers!.Expires);
        if (renew) access.Renew(staff, snapshot, 4, new DateOnly(2026, 11, 26));
        else access.Expire(staff, snapshot, new DateOnly(2026, 11, 26));
        Assert.Equal(expected, memory.Read(Contract, 0xD0));
    }

    [Fact]
    public void RefusesAppointmentOwnedByAnotherPerson()
    {
        Assert.True(memory.TryWrite(Contract + 8, BitConverter.GetBytes(Person + 1)));
        var snapshot = new FutureTransferReader(memory, profile, TestWorld.ModuleBase).ReadContract(Contract, 0)!;
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Renew(staff, snapshot, 4, new DateOnly(2026, 11, 26)));
        Assert.Equal(writes, memory.Writes);
    }
}
