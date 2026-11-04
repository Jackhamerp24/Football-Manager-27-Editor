using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffWageMassEditTests
{
    [Fact]
    public void Changes_and_undoes_two_verified_contracts_and_rejects_stale_identity()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, first = 0x10000000, second = 0x20000000, contract1 = 0x40000000, contract2 = 0x40001000;
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
        // Captured RAM independently compared against FMRTE: 800 and 3500.
        Map(first, contract1, 5209579, "F80562DDFA7F0000401BDD347202000040005C2E720200002052A72D7202000020030000000010000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFDE00E807B600EC07DE00E80701006C070000000000001FE214FF0000");
        Map(second, contract2, 2019010, "F80562DDFA7F0000C003D2347202000038B65B2E720200007028A72D72020000AC0D0000000010000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFBE00EA07B600EC07BE00EA0701006C070000000000001DE214FF0000");
        var repo = new StaffRepository(memory, profile, module);
        var access = new StaffContractAccess(memory, profile, module);
        var targets = new[] { repo.Reload(first + 0x100)!, repo.Reload(second + 0x100)! };
        var old1 = memory.Read(contract1, 0xC4);
        var old2 = memory.Read(contract2, 0xC4);
        var plan = StaffWageMassEdit.Plan(targets, access, MassEditMode.Change, 10);
        Assert.Equal(new[] { 800, 3500 }, plan.Changes.Select(c => c.Before));
        Assert.Equal(new[] { 810, 3510 }, plan.Changes.Select(c => c.After));
        foreach (var change in plan.Changes) StaffWageMassEdit.Apply(change, repo, access);
        var actual1 = memory.Read(contract1, 0xC4);
        var actual2 = memory.Read(contract2, 0xC4);
        Assert.Equal(810, BitConverter.ToInt32(actual1, 0x20));
        Assert.Equal(3510, BitConverter.ToInt32(actual2, 0x20));
        old1[0x20..0x24].CopyTo(actual1, 0x20);
        old2[0x20..0x24].CopyTo(actual2, 0x20);
        Assert.Equal(old1, actual1);
        Assert.Equal(old2, actual2);
        foreach (var change in plan.Changes.Reverse()) StaffWageMassEdit.Undo(change, repo, access);
        Assert.Equal(old1, memory.Read(contract1, 0xC4));
        Assert.Equal(old2, memory.Read(contract2, 0xC4));
        Assert.True(memory.TryWrite(first + 0x10C, BitConverter.GetBytes(5209580)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => StaffWageMassEdit.Apply(plan.Changes[0], repo, access));
        Assert.Equal(writes, memory.Writes);
    }
}
