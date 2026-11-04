using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Mikel Arteta's job record as dumped on 15/8/2026 (person + 0xA8 → full contract, vtable re-pointed). FMRTE 27's staff Contract
/// tab shows Full Time Contract, £375,000, 13/7/2026 – 30/6/2030, not rolling. +0x26 holds his job (16, Manager), not the type.
/// </summary>
public class StaffContractTests
{
    const string Arteta = "E80A5B19FB7F0000C00CDC605A020000B8F2D1595A020000B8F2D1595A020000D8B80500050010000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFC21AEA07B500EE076101E30701006C07030003000000354814FF000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001006C07000000006C07000000000001FF000000";
    const ulong Person = 0x6000_0000, Contract = 0x6001_0000;
    readonly TestWorld _w = new();
    readonly StaffRecord _arteta;

    public StaffContractTests()
    {
        TestWorld.Set64(_w.Memory.Map(Person, 0x100), 0, TestWorld.ModuleBase + 0x4D8BE78);
        Assert.True(_w.Memory.TryWrite(Person + 0xA8, BitConverter.GetBytes(Contract)));
        var c = Convert.FromHexString(Arteta);
        BitConverter.GetBytes(TestWorld.ModuleBase + 0x4B00AE8).CopyTo(c, 0);
        _w.Memory.Map(Contract, 0xD0);
        Assert.True(_w.Memory.TryWrite(Contract, c));
        _arteta = new StaffRecord
        {
            Address = Person, StaffAddress = Person - 0x100, Attributes = new byte[64], Personality = new byte[8],
            Kind = _w.Profile.Staff!.Kinds[0], FirstName = "Mikel", LastName = "Arteta",
        };
    }

    StaffContractAccess Access() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_contract_FMRTE_shows()
    {
        var c = Access().Read(_arteta)!;
        Assert.Equal((375_000, (int)ContractType.FullTime, false, new DateOnly(2026, 7, 13), new DateOnly(2030, 6, 30)),
            (c.Wage, c.Type, c.Rolling, c.Started!.Value, c.Expires!.Value));
        Assert.Equal((short)16, BitConverter.ToInt16(_w.Bytes(Contract + 0x26, 2)));
    }

    [Fact]
    public void Writes_keep_the_job_and_refuse_a_replaced_contract()
    {
        var c = Access().Read(_arteta)!;
        var before = _w.Bytes(Contract, 0xD0);
        Access().Set(_arteta, c, new(380_000, ContractType.PartTime, true, new DateOnly(2026, 7, 13), new DateOnly(2031, 6, 30)));
        var after = _w.Bytes(Contract, 0xD0);
        Assert.Equal(380_000, BitConverter.ToInt32(after, 0x20));
        Assert.Equal(0, after[0xC3]);
        Assert.Equal(0x08, after[0x2B]);
        Assert.Equal(Convert.FromHexString("B500EF07"), after[0x48..0x4C]);
        after.AsSpan(0x20, 4).CopyTo(before.AsSpan(0x20)); before[0xC3] = after[0xC3]; before[0x2B] = after[0x2B]; after.AsSpan(0x48, 4).CopyTo(before.AsSpan(0x48));
        Assert.Equal(before, after);

        Assert.True(_w.Memory.TryWrite(Person + 0xA8, BitConverter.GetBytes(0x6002_0000UL)));
        Assert.Throws<StaleObjectException>(() => Access().Set(_arteta, Access().Read(_arteta) ?? c, new(1, ContractType.FullTime, false, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))));
    }
}
