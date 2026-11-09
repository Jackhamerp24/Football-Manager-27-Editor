using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class FutureTransferVectorSnapshotTests
{
    [Fact]
    public void FullVectorPreparesCopyWithoutWritingAndDetectsChangedItems()
    {
        var memory = new FakeMemory();
        var header = memory.Map(0x1000, 24);
        BitConverter.GetBytes(0x2000UL).CopyTo(header, 0);
        BitConverter.GetBytes(0x2008UL).CopyTo(header, 8);
        BitConverter.GetBytes(0x2008UL).CopyTo(header, 16);
        var existing = memory.Map(0x2000, 8);
        BitConverter.GetBytes(42).CopyTo(existing, 0); BitConverter.GetBytes(73).CopyTo(existing, 4);
        memory.Map(0x3000, 12);
        var snapshot = FutureTransferVectorSnapshot.Capture(memory, 0x1000, 4, 100);
        var writes = snapshot.Append(memory, BitConverter.GetBytes(99), _ => 0x3000);
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(0, memory.Writes);
        Assert.Equal(new byte[] { 42, 0, 0, 0, 73, 0, 0, 0, 99, 0, 0, 0 }, writes[0].Value);
        Assert.Equal(0x300CUL, BitConverter.ToUInt64(writes[1].Value, 8));
        existing[0] = 43;
        Assert.Throws<StaleObjectException>(() => snapshot.Validate(memory));
    }

    [Theory]
    [InlineData(0, new[] { 20, 30, 0 })]
    [InlineData(1, new[] { 10, 30, 0 })]
    [InlineData(2, new[] { 10, 20, 0 })]
    public void RemovingAnItemShiftsTheRestDownAndLowersTheEnd(int index, int[] expected)
    {
        var memory = new FakeMemory();
        var header = memory.Map(0x1000, 24);
        BitConverter.GetBytes(0x2000UL).CopyTo(header, 0);
        BitConverter.GetBytes(0x200CUL).CopyTo(header, 8);
        BitConverter.GetBytes(0x2010UL).CopyTo(header, 16);
        var items = memory.Map(0x2000, 0x10);
        BitConverter.GetBytes(10).CopyTo(items, 0); BitConverter.GetBytes(20).CopyTo(items, 4); BitConverter.GetBytes(30).CopyTo(items, 8);
        var snapshot = FutureTransferVectorSnapshot.Capture(memory, 0x1000, 4, 100);
        var writes = snapshot.RemoveAt(memory, index);
        Assert.Equal(0, memory.Writes);                                   // preparing writes nothing
        FutureTransferTransaction.Apply(memory, snapshot.Guards, writes);
        Assert.Equal(expected, Enumerable.Range(0, 3).Select(k => BitConverter.ToInt32(items, k * 4)));
        Assert.Equal(0x2008UL, BitConverter.ToUInt64(memory.Read(0x1000, 24), 8));
        Assert.Equal(0x2010UL, BitConverter.ToUInt64(memory.Read(0x1000, 24), 16));   // capacity untouched
    }

    [Fact]
    public void RemovingFromAVectorThatChangedIsRefused()
    {
        var memory = new FakeMemory();
        var header = memory.Map(0x1000, 24);
        BitConverter.GetBytes(0x2000UL).CopyTo(header, 0); BitConverter.GetBytes(0x2004UL).CopyTo(header, 8); BitConverter.GetBytes(0x2004UL).CopyTo(header, 16);
        var items = memory.Map(0x2000, 4);
        BitConverter.GetBytes(10).CopyTo(items, 0);
        var snapshot = FutureTransferVectorSnapshot.Capture(memory, 0x1000, 4, 100);
        items[0] = 11;
        Assert.Throws<StaleObjectException>(() => snapshot.RemoveAt(memory, 0));
    }
}
