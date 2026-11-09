using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>Work-permit bytes and vector operations observed in FMRTE on 9/11/2026.</summary>
public class WorkPermitTests
{
    const ulong Header = 0x7800_0000, Vector = 0x7801_0000, TempVector = 0x7802_0000;
    const ulong Nation = 0x7803_0000, R0 = 0x7804_0000, R1 = 0x7805_0000, R2 = 0x7806_0000;
    const ulong NewRecord = 0x7807_0000, NewVector = 0x7808_0000;
    readonly TestWorld w = new();
    readonly WorkPermitTable table;
    readonly PlayerRecord player;
    int allocations;

    public WorkPermitTests()
    {
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x290, BitConverter.GetBytes(100u)));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x2A0, Convert.FromHexString("A122334455667788")));
        var nation = w.Memory.Map(Nation, 8);
        TestWorld.Set64(nation, 0, TestWorld.ModuleBase + w.Profile.Nation!.VtableRva);
        Record(R0, 50, false); Record(R1, 100, true); Record(R2, 150, false);
        var h = w.Memory.Map(Header, 0x78);
        TestWorld.Set64(h, 0, Vector); TestWorld.Set64(h, 8, Vector + 24); TestWorld.Set64(h, 16, Vector + 32);
        TestWorld.Set64(h, 0x60, TempVector); TestWorld.Set64(h, 0x68, TempVector + 24); TestWorld.Set64(h, 0x70, TempVector + 24);
        var v = w.Memory.Map(Vector, 32);
        TestWorld.Set64(v, 0, R0); TestWorld.Set64(v, 8, R1); TestWorld.Set64(v, 16, R2);
        var temp = w.Memory.Map(TempVector, 24);
        TestWorld.Set64(temp, 0, R0); TestWorld.Set64(temp, 8, R1); TestWorld.Set64(temp, 16, R2);
        player = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        table = new WorkPermitTable(w.Memory, w.Profile, TestWorld.ModuleBase, Header);
    }

    void Record(ulong at, uint index, bool temporary)
    {
        var b = w.Memory.Map(at, 0x20);
        TestWorld.Set64(b, 0, Nation);
        BitConverter.GetBytes(index).CopyTo(b, 8);
        Convert.FromHexString(temporary ? "EA07B700EA070100" : "EA0701006C070000").CopyTo(b, 0x10);
    }
    ulong Allocate(int size)
    {
        ulong at = allocations++ == 0 ? NewRecord : NewVector;
        w.Memory.Map(at, size);
        return at;
    }
    ulong[] Pointers(ulong start, int count) => Enumerable.Range(0, count).Select(i => w.Memory.ReadUInt64(start + (ulong)(i * 8))).ToArray();
    IReadOnlyList<WorkPermitGrant> Read() => table.Read(player, n => n == Nation ? "England" : null);

    [Fact]
    public void ReadFiltersPersonAndDecodesReviewDate()
    {
        var grant = Assert.Single(Read());
        Assert.Equal((R1, Nation, "England", 2026, new DateOnly(2026, 7, 2), true),
            (grant.Record, grant.Nation, grant.NationName, grant.Year, grant.ReviewFrom, grant.Temporary));
        Assert.True(w.Memory.TryWrite(R1 + 0x12, Convert.FromHexString("01006C070000")));
        grant = Assert.Single(Read());
        Assert.Null(grant.ReviewFrom);
        Assert.False(grant.Temporary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddMaintainsSortedVectorAndSetsOnlyPermitBit(bool full)
    {
        if (full) Assert.True(w.Memory.TryWrite(Header + 16, BitConverter.GetBytes(Vector + 24)));
        table.Add(player, Nation, 2026, Allocate);
        var expected = new byte[0x20];
        BitConverter.GetBytes(Nation).CopyTo(expected, 0);
        Convert.FromHexString("64000000").CopyTo(expected, 8);
        Convert.FromHexString("EA0701006C070000").CopyTo(expected, 0x10);
        Assert.Equal(expected, w.Bytes(NewRecord, 0x20));
        ulong buffer = full ? NewVector : Vector;
        Assert.Equal(new[] { R0, R1, NewRecord, R2 }, Pointers(buffer, 4));
        Assert.Equal(new[] { buffer, buffer + 32, full ? buffer + 512 : buffer + 32 }, Pointers(Header, 3));
        Assert.Equal(full ? 2 : 1, allocations);
        Assert.Equal(Convert.FromHexString("A522334455667788"), w.Bytes(TestWorld.Mainoo + 0x2A0, 8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoveCompactsVectorAndClearsFlagOnlyAfterLastPermit(bool another)
    {
        if (another) Assert.True(w.Memory.TryWrite(R2 + 8, BitConverter.GetBytes(100u)));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x2A0, [0xA5]));
        table.Remove(player, Read().First(), _ => "England");
        Assert.Equal(new[] { R0, R2 }, Pointers(Vector, 2));
        Assert.Equal(Vector + 16, w.Memory.ReadUInt64(Header + 8));
        Assert.Equal(another ? 0xA5 : 0xA1, w.Bytes(TestWorld.Mainoo + 0x2A0, 1)[0]);
    }

    [Fact]
    public void RemoveRejectsPermitBelongingToAnotherPlayer()
    {
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.Remove(player, new(R0, Nation, "England", 2026, null, false), _ => "England"));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void RemoveRejectsPermitChangedAfterItWasRead()
    {
        var grant = Assert.Single(Read());
        Assert.True(w.Memory.TryWrite(R1 + 0x10, Convert.FromHexString("E807")));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.Remove(player, grant, _ => "England"));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void SetYearWritesOnlyTwoBytesAndRejectsStale()
    {
        var grant = Assert.Single(Read());
        var expected = w.Bytes(R1, 0x20);
        Convert.FromHexString("E807").CopyTo(expected, 0x10);
        table.SetYear(player, grant, 2024);
        Assert.Equal(expected, w.Bytes(R1, 0x20));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.SetYear(player, grant, 2025));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void SetYearRejectsRecordWhosePersonChangedAfterRead()
    {
        var grant = Assert.Single(Read());
        Assert.True(w.Memory.TryWrite(R1 + 8, BitConverter.GetBytes(101u)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.SetYear(player, grant, 2024));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void SetYearRejectsAnyChangedSnapshotByte()
    {
        var grant = Assert.Single(Read());
        Assert.True(w.Memory.TryWrite(R1 + 0x1C, [0x55]));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.SetYear(player, grant, 2024));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void SetYearRejectsSnapshotForAnotherPlayer()
    {
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + 0x290, BitConverter.GetBytes(150u)));
        var other = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Yamal)!;
        var grant = Assert.Single(table.Read(other, _ => "England"));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.SetYear(player, grant, 2024));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void SetYearRejectsPermitRemovedFromTheMainVector()
    {
        var grant = Assert.Single(Read());
        Assert.True(w.Memory.TryWrite(Vector + 8, BitConverter.GetBytes(R2)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.SetYear(player, grant, 2024));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void MakePermanentCompactsTemporaryVectorAndWritesFmrteBytes()
    {
        var grant = Assert.Single(Read());
        var expected = w.Bytes(R1, 0x20);
        Convert.FromHexString("01006C070000").CopyTo(expected, 0x12);
        var main = w.Bytes(Header, 24);
        table.MakePermanent(player, grant);
        Assert.Equal(expected, w.Bytes(R1, 0x20));
        Assert.Equal(main, w.Bytes(Header, 24));
        Assert.Equal(new[] { R0, R2 }, Pointers(TempVector, 2));
        Assert.Equal(TempVector + 16, w.Memory.ReadUInt64(Header + 0x68));
        Assert.Throws<InvalidOperationException>(() => table.MakePermanent(player, Assert.Single(Read())));
    }

    [Fact]
    public void ChangeYearAndMakePermanentCanBeSavedTogether()
    {
        var grant = Assert.Single(Read());
        table.SetYear(player, grant, 2024);
        table.MakePermanent(player, grant);
        Assert.Equal(Convert.FromHexString("E80701006C070000"), w.Bytes(R1 + 0x10, 8));
        Assert.Equal(new[] { R0, R2 }, Pointers(TempVector, 2));
        Assert.Equal(TempVector + 16, w.Memory.ReadUInt64(Header + 0x68));
    }

    [Fact]
    public void MakePermanentWithoutTemporaryVectorEntryDoesNotWrite()
    {
        var grant = Assert.Single(Read());
        Assert.True(w.Memory.TryWrite(TempVector + 8, BitConverter.GetBytes(R0)));
        int writes = w.Memory.Writes;
        Assert.Throws<InvalidOperationException>(() => table.MakePermanent(player, grant));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void MakePermanentRejectsPermitOwnedByAnotherPlayer()
    {
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + 0x290, BitConverter.GetBytes(150u)));
        Assert.True(w.Memory.TryWrite(R2 + 0x12, Convert.FromHexString("B700EA070100")));
        var other = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Yamal)!;
        var grant = Assert.Single(table.Read(other, _ => "England"));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => table.MakePermanent(player, grant));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void DeniedReadsTeamAndRemoveCompactsFollowingRelations()
    {
        const ulong holder = 0x7900_0000, rows = 0x7901_0000, team = 0x7902_0000;
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x300, BitConverter.GetBytes(holder)));
        var h = w.Memory.Map(holder, 24);
        TestWorld.Set64(h, 0, rows); TestWorld.Set64(h, 8, rows + 48); TestWorld.Set64(h, 16, rows + 48);
        var v = w.Memory.Map(rows, 48);
        BitConverter.GetBytes(team).CopyTo(v, 0);
        Convert.FromHexString("2C000475000000FF").CopyTo(v, 8);
        Convert.FromHexString("112233445566778800000169051A00FF").CopyTo(v, 16);
        Convert.FromHexString("887766554433221100000147010000FF").CopyTo(v, 32);
        var expectedTail = v[16..48];
        var access = new WorkPermitAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
        var denial = Assert.Single(access.ReadDenied(player, addr => addr == team ? "Manchester United" : null));
        Assert.Equal((team, "Manchester United"), (denial.Team, denial.ClubName));
        Assert.True(access.RemoveDenied(player, denial));
        Assert.Equal(expectedTail, w.Bytes(rows, 32));
        Assert.Equal(rows + 32, w.Memory.ReadUInt64(holder + 8));
        Assert.Empty(access.ReadDenied(player, _ => "Manchester United"));
        int writes = w.Memory.Writes;
        Assert.False(access.RemoveDenied(player, denial));
        Assert.Equal(writes, w.Memory.Writes);
    }
}
