using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffPersonalityMassEditTests
{
    [Fact]
    public void Personality_batch_updates_only_selected_byte_and_undoes_each_value()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, alan = 0x10000000, omar = 0x20000000;
        void Map(ulong address, int id, string personality)
        {
            var staff = memory.Map(address, 0x100);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(staff, 0);
            var person = memory.Map(address + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(person, 0);
            BitConverter.GetBytes(id).CopyTo(person, 0xC);
            Convert.FromHexString(personality).CopyTo(person, 0x70);
        }
        // Person +0x70 bytes observed on Game Pass, Alan Wright and Omar Berrada.
        Map(alan, 2006027, "050C0E0C120F0F04");
        Map(omar, 2000336892, "140B0C0C0C0C1407");
        var repo = new StaffRepository(memory, profile, module);
        var writer = new StaffWriter(memory, profile, module);
        var plan = StaffPersonalityMassEdit.Plan([repo.Reload(alan + 0x100)!, repo.Reload(omar + 0x100)!], StaffPersonality.Ambition, MassEditMode.Set, 10);
        Assert.Equal(new[] { (12, 10), (11, 10) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) StaffPersonalityMassEdit.Apply(change, repo, writer);
        Assert.Equal(Convert.FromHexString("050A0E0C120F0F04"), memory.Read(alan + 0x170, 8));
        Assert.Equal(Convert.FromHexString("140A0C0C0C0C1407"), memory.Read(omar + 0x170, 8));
        foreach (var change in plan.Reverse()) StaffPersonalityMassEdit.Undo(change, repo, writer);
        Assert.Equal(Convert.FromHexString("050C0E0C120F0F04"), memory.Read(alan + 0x170, 8));
        Assert.Equal(Convert.FromHexString("140B0C0C0C0C1407"), memory.Read(omar + 0x170, 8));
        Assert.True(memory.TryWrite(alan + 0x171, [13]));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => StaffPersonalityMassEdit.Apply(plan[0], repo, writer));
        Assert.Equal(writes, memory.Writes);
    }
}
