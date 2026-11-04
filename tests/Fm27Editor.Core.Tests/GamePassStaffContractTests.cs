using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class GamePassStaffContractTests
{
    [Fact]
    public void Reads_and_writes_verified_staff_wages_without_changing_other_fields()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var copy = Fm27Editor.Core.Offsets.OffsetProfile.Load(profile.ToJson());
        Assert.False(copy.ReadOnly);
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, jim = 0x10000000, hughes = 0x20000000,
            jimContract = 0x40000000, hughesContract = 0x40001000;
        void Map(ulong staff, ulong contract, int id, string bytes)
        {
            var s = memory.Map(staff, 0x100);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(s, 0);
            var person = memory.Map(staff + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(person, 0);
            BitConverter.GetBytes(id).CopyTo(person, 0xC);
            BitConverter.GetBytes(contract).CopyTo(person, 0xA8);
            var c = memory.Map(contract, 0xC4);
            Convert.FromHexString(bytes).CopyTo(c, 0);
            BitConverter.GetBytes(module + 0x4AC05F8u).CopyTo(c, 0);
            BitConverter.GetBytes(staff + 0x100).CopyTo(c, 8);
            c[0xC3] = 1;
        }
        // Game Pass RAM, independently compared with FMRTE: Jim £800, Hughes £3,500.
        Map(jim, jimContract, 5209579, "F80562DDFA7F0000401BDD347202000040005C2E720200002052A72D7202000020030000000010000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFDE00E807B600EC07DE00E80701006C070000000000001FE214FF0000");
        Map(hughes, hughesContract, 2019010, "F80562DDFA7F0000C003D2347202000038B65B2E720200007028A72D72020000AC0D0000000010000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFBE00EA07B600EC07BE00EA0701006C070000000000001DE214FF0000");
        var repo = new StaffRepository(memory, copy, module);
        var first = repo.Reload(jim + 0x100)!;
        var second = repo.Reload(hughes + 0x100)!;
        var access = new StaffContractAccess(memory, copy, module);
        var a = access.Read(first)!;
        var b = access.Read(second)!;
        Assert.Equal(800, a.Wage);
        Assert.Equal(3500, b.Wage);
        Assert.Equal(new DateOnly(2024, 8, 9), a.Started);
        Assert.Equal(new DateOnly(2026, 7, 9), b.Started);
        Assert.Equal(new DateOnly(2028, 6, 30), a.Expires);
        var before = memory.Read(jimContract, 0xC4);
        var fields = new OfferContractFields(801, ContractType.FullTime, false, new DateOnly(2024, 8, 9), new DateOnly(2028, 6, 30));
        access.Set(first, a, fields);
        var after = memory.Read(jimContract, 0xC4);
        Assert.Equal(801, BitConverter.ToInt32(after, 0x20));
        before[0x20..0x24].CopyTo(after, 0x20);
        Assert.Equal(before, after);
        Assert.True(memory.TryWrite(hughes + 0x10C, BitConverter.GetBytes(2019011)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(second, b, new OfferContractFields(3501, ContractType.FullTime, false, new DateOnly(2026, 7, 9), new DateOnly(2028, 6, 30))));
        Assert.Equal(writes, memory.Writes);
    }
}
