using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

public class FutureTransferTransactionTests
{
    [Fact]
    public void StaleGuardBlocksEveryWrite()
    {
        var memory = new FakeMemory(); memory.Map(0x1000, 1)[0] = 3; memory.Map(0x2000, 1)[0] = 4;
        var guard = new PlannedWrite(0x1000, [2], [], "guard");
        var write = new PlannedWrite(0x2000, [4], [5], "record");
        Assert.Throws<StaleObjectException>(() => FutureTransferTransaction.Apply(memory, [guard], [write]));
        Assert.Equal(0, memory.Writes);
        Assert.Equal(4, memory.Read(0x2000, 1)[0]);
    }

    [Fact]
    public void LaterWriteFailureRestoresEarlierWrite()
    {
        var inner = new FakeMemory(); inner.Map(0x1000, 1)[0] = 1; inner.Map(0x2000, 1)[0] = 2;
        var memory = new FailsSecondWrite(inner);
        Assert.Throws<MemoryAccessException>(() => FutureTransferTransaction.Apply(memory, [],
            [new(0x1000, [1], [8], "first"), new(0x2000, [2], [9], "second")]));
        Assert.Equal(1, inner.Read(0x1000, 1)[0]);
        Assert.Equal(2, inner.Read(0x2000, 1)[0]);
    }

    sealed class FailsSecondWrite(FakeMemory inner) : IProcessMemory
    {
        int calls;
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> data) => ++calls == 2 ? false : inner.TryWrite(address, data);
    }
}
