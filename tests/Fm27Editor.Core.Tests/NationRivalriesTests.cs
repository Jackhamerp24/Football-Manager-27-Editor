using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class NationRivalriesTests
{
    [Fact]
    public void Refuses_changed_or_misaligned_vector_capacity()
    {
        // Captured Spain–Italy relationship: FMRTE value 60, relocated object pointers.
        var w = new TestWorld();
        const ulong spain = 0x71000000, italy = 0x71002000, st = 0x72000000, it = 0x72002000, rows = 0x73000000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(italy, 768, "Italy", null) };
        foreach (var (nation, team) in new[] { (spain, st), (italy, it) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        Assert.True(w.Memory.TryWrite(st + 0x48, BitConverter.GetBytes(rows)));
        Assert.True(w.Memory.TryWrite(st + 0x50, BitConverter.GetBytes(rows + 16)));
        Assert.True(w.Memory.TryWrite(st + 0x58, BitConverter.GetBytes(rows + 16)));
        var bytes = w.Memory.Map(rows, 16);
        Convert.FromHexString("5099C62633010000000002063C0100FF").CopyTo(bytes, 0);
        TestWorld.Set64(bytes, 0, it);
        var access = new NationRivalries(w.Memory, w.Profile, TestWorld.ModuleBase);
        var row = Assert.Single(access.Read(nations[0], nations));
        var original = w.Bytes(rows, 16);
        Assert.True(w.Memory.TryWrite(st + 0x58, BitConverter.GetBytes(rows + 32)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.SetValue(row, 61));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.Equal(original, w.Bytes(rows, 16));
        Assert.True(w.Memory.TryWrite(st + 0x58, BitConverter.GetBytes(rows + 17)));
        Assert.Empty(access.Read(nations[0], nations));
    }

    [Fact]
    public void Reads_and_writes_real_rivalry_records_and_refuses_stale_snapshots()
    {
        // FMRTE 27: Spain–Italy 60; France–Germany 70. Captured record payloads; addresses are relocated.
        var w = new TestWorld();
        const ulong spain = 0x71000000, france = 0x71001000, italy = 0x71002000, germany = 0x71003000;
        const ulong spainTeam = 0x72000000, franceTeam = 0x72001000, italyTeam = 0x72002000, germanyTeam = 0x72003000;
        const ulong spainRows = 0x73000000, franceRows = 0x73001000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(france, 769, "France", null),
            new NationRecord(italy, 768, "Italy", null), new NationRecord(germany, 771, "Germany", null) };
        foreach (var (nation, team) in new[] { (spain, spainTeam), (france, franceTeam), (italy, italyTeam), (germany, germanyTeam) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        void Vector(ulong team, ulong rows, ulong opponent, string raw)
        {
            Assert.True(w.Memory.TryWrite(team + 0x48, BitConverter.GetBytes(rows)));
            Assert.True(w.Memory.TryWrite(team + 0x50, BitConverter.GetBytes(rows + 16)));
            Assert.True(w.Memory.TryWrite(team + 0x58, BitConverter.GetBytes(rows + 16)));
            var entry = w.Memory.Map(rows, 16);
            Convert.FromHexString(raw).CopyTo(entry, 0);
            TestWorld.Set64(entry, 0, opponent);
        }
        Vector(spainTeam, spainRows, italyTeam, "5099C62633010000000002063C0100FF");
        Vector(franceTeam, franceRows, germanyTeam, "586EC6263301000000000206460100FF");
        var access = new NationRivalries(w.Memory, w.Profile, TestWorld.ModuleBase);
        var a = Assert.Single(access.Read(nations[0], nations));
        var b = Assert.Single(access.Read(nations[1], nations));
        Assert.Equal(("Italy", (byte)60), (a.Opponent, a.Value));
        Assert.Equal(("Germany", (byte)70), (b.Opponent, b.Value));
        access.SetValue(a, 61);
        Assert.Equal("3D0100FF", Convert.ToHexString(w.Bytes(spainRows + 12, 4)));
        Assert.Throws<StaleObjectException>(() => access.SetValue(a, 62));
        access.SetValue(b, 71);
        Assert.Equal("470100FF", Convert.ToHexString(w.Bytes(franceRows + 12, 4)));
        var refreshed = Assert.Single(access.Read(nations[1], nations));
        Assert.True(w.Memory.TryWrite(franceTeam + 0x50, BitConverter.GetBytes(franceRows)));
        Assert.Throws<StaleObjectException>(() => access.SetValue(refreshed, 70));
        Assert.Empty(access.Read(nations[1], nations));
        Assert.Throws<ArgumentOutOfRangeException>(() => access.SetValue(refreshed, 101));
    }
}
