using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffMassEditTests
{
    [Fact]
    public void Ability_batch_preserves_other_ability_and_undoes_each_staff_member()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, alan = 0x10000000, omar = 0x20000000;
        void Map(ulong address, int id, string ability)
        {
            var staff = memory.Map(address, 0x100);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(staff, 0);
            Convert.FromHexString(ability).CopyTo(staff, 0xDA);
            var person = memory.Map(address + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(person, 0);
            BitConverter.GetBytes(id).CopyTo(person, 0xC);
        }
        // RAM bytes and FMRTE values: Alan Wright 80/114; Omar Berrada 135/145.
        Map(alan, 2006027, "50007200");
        Map(omar, 2000336892, "87009100");
        var repository = new StaffRepository(memory, profile, module);
        var writer = new StaffWriter(memory, profile, module);
        var plan = StaffMassEdit.Plan([repository.Reload(alan + 0x100)!, repository.Reload(omar + 0x100)!], StaffMassField.CurrentAbility, MassEditMode.Change, 1);
        Assert.Equal(new[] { (80, 81), (135, 136) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) StaffMassEdit.Apply(change, repository, writer);
        Assert.Equal(Convert.FromHexString("51007200"), memory.Read(alan + 0xDA, 4));
        Assert.Equal(Convert.FromHexString("88009100"), memory.Read(omar + 0xDA, 4));
        foreach (var change in plan.Reverse()) StaffMassEdit.Undo(change, repository, writer);
        Assert.Equal(Convert.FromHexString("50007200"), memory.Read(alan + 0xDA, 4));
        Assert.Equal(Convert.FromHexString("87009100"), memory.Read(omar + 0xDA, 4));
        Assert.True(memory.TryWrite(alan + 0x10C, BitConverter.GetBytes(2006028)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => StaffMassEdit.Apply(plan[0], repository, writer));
        Assert.Equal(writes, memory.Writes);
    }
}
