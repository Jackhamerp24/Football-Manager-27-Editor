using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class NationPreferencesTests
{
    [Fact]
    public void Rejects_changed_capacity_and_misaligned_capacity()
    {
        // Spain–Austria bytes and values observed in FMRTE; fake pointers identify this test's objects.
        var w = new TestWorld();
        const ulong spain = 0x71000000, austria = 0x71002000, st = 0x72000000, at = 0x72002000, sr = 0x73000000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(austria, null, "Austria", null) };
        foreach (var (nation, team) in new[] { (spain, st), (austria, at) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        Assert.True(w.Memory.TryWrite(st + 0xF0, BitConverter.GetBytes(sr)));
        Assert.True(w.Memory.TryWrite(st + 0xF8, BitConverter.GetBytes(sr + 16)));
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 16)));
        var bytes = w.Memory.Map(sr, 16);
        Convert.FromHexString("0619120000000000D8E4C52633010000").CopyTo(bytes, 0);
        TestWorld.Set64(bytes, 8, at);
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var row = Assert.Single(access.Read(nations[0], nations));
        var original = w.Bytes(sr, 16);
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 32)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(row, 7, 19, 26, 1));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.Equal(original, w.Bytes(sr, 16));
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 17)));
        Assert.Empty(access.Read(nations[0], nations));
    }

    [Fact]
    public void Reads_observed_rows_writes_four_values_and_preserves_remaining_bytes()
    {
        // FMRTE 27: Spain–Austria 6/0/18/25; France–Belgium 12/12/14/45.
        var w = new TestWorld();
        const ulong spain = 0x71000000, france = 0x71001000, austria = 0x71002000, belgium = 0x71003000;
        const ulong st = 0x72000000, ft = 0x72001000, at = 0x72002000, bt = 0x72003000;
        const ulong sr = 0x73000000, fr = 0x73001000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(france, 769, "France", null),
            new NationRecord(austria, null, "Austria", null), new NationRecord(belgium, null, "Belgium", null) };
        foreach (var (nation, team) in new[] { (spain, st), (france, ft), (austria, at), (belgium, bt) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        void Vector(ulong team, ulong rows, ulong target, string raw)
        {
            Assert.True(w.Memory.TryWrite(team + 0xF0, BitConverter.GetBytes(rows)));
            Assert.True(w.Memory.TryWrite(team + 0xF8, BitConverter.GetBytes(rows + 16)));
            Assert.True(w.Memory.TryWrite(team + 0x100, BitConverter.GetBytes(rows + 16)));
            var bytes = w.Memory.Map(rows, 16);
            Convert.FromHexString(raw).CopyTo(bytes, 0);
            TestWorld.Set64(bytes, 8, target);
        }
        Vector(st, sr, at, "0619120000000000D8E4C52633010000");
        Vector(ft, fr, bt, "0C2D0E030C00000008F6C52633010000");
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var a = Assert.Single(access.Read(nations[0], nations));
        var b = Assert.Single(access.Read(nations[1], nations));
        Assert.Equal(("Austria", (sbyte)6, (byte)0, (byte)18, (byte)25), (a.Target, a.Rating, a.Knowledge, a.MinAge, a.MaxAge));
        Assert.Equal(("Belgium", (sbyte)12, (byte)12, (byte)14, (byte)45), (b.Target, b.Rating, b.Knowledge, b.MinAge, b.MaxAge));
        access.Set(a, 7, 19, 26, 1);
        Assert.Equal("071A130001000000", Convert.ToHexString(w.Bytes(sr, 8)));
        access.Set(b, 13, 15, 44, 13);
        Assert.Equal("0D2C0F030D000000", Convert.ToHexString(w.Bytes(fr, 8)));
        Assert.Throws<StaleObjectException>(() => access.Set(b, 12, 14, 45, 12));
        var fresh = Assert.Single(access.Read(nations[1], nations));
        Assert.True(w.Memory.TryWrite(fr + 3, [0x04]));
        Assert.Throws<StaleObjectException>(() => access.Set(fresh, 12, 14, 45, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => access.Set(a, 6, 46, 45, 0));
    }
}
