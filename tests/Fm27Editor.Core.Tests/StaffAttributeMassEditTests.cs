using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffAttributeMassEditTests
{
    [Fact]
    public void Undo_restores_exact_scaled_bytes_and_stale_raw_is_rejected()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, alan = 0x10000000, omar = 0x20000000;
        void Map(ulong address, int id, string head)
        {
            var staff = memory.Map(address, 0x100);
            Convert.FromHexString(head).CopyTo(staff, 0);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(staff, 0);
            var person = memory.Map(address + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(person, 0);
            BitConverter.GetBytes(id).CopyTo(person, 0xC);
        }
        // Captured Game Pass staff bytes: Coaching Attacking +0x32 is 34 (Alan), 86 (Omar).
        Map(alan, 2006027, "58B389DDFA7F0000E82A7CDDFA7F0000100B0F0511080807100B0E0810400A0506120B0809030E0A0E114B05222C1A310F40223606222C3B231937050F4B4114411F0619000000000000000000000000");
        Map(omar, 2000336892, "58B389DDFA7F0000E82A7CDDFA7F00001209060B0E0B0A0A0B0B0E0212420F0C0C120A0F09111108020E190F605E51380F5C565828515658464B4B4B054B370F1E64511E000000000000000000000000");
        var repo = new StaffRepository(memory, profile, module);
        var writer = new StaffWriter(memory, profile, module);
        var plan = StaffAttributeMassEdit.Plan([repo.Reload(alan + 0x100)!, repo.Reload(omar + 0x100)!], StaffAttribute.CoachingAttacking, MassEditMode.Set, 10, writer);
        Assert.Equal(new[] { (7, 10, 34, 50), (17, 10, 86, 50) }, plan.Select(c => (c.Before, c.After, (int)c.BeforeRaw, (int)c.AfterRaw)));
        foreach (var change in plan) StaffAttributeMassEdit.Apply(change, repo, writer);
        Assert.Equal(50, memory.Read(alan + 0x32, 1)[0]);
        Assert.Equal(50, memory.Read(omar + 0x32, 1)[0]);
        foreach (var change in plan.Reverse()) StaffAttributeMassEdit.Undo(change, repo, writer);
        Assert.Equal(34, memory.Read(alan + 0x32, 1)[0]);
        Assert.Equal(86, memory.Read(omar + 0x32, 1)[0]);
        Assert.True(memory.TryWrite(alan + 0x32, [33])); // Still displays 7, but the byte changed.
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => StaffAttributeMassEdit.Apply(plan[0], repo, writer));
        Assert.Equal(writes, memory.Writes);
    }
}
