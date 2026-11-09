using System.Runtime.InteropServices;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

public class RemoteCrtAllocatorTests
{
    [Fact]
    public void AllocatedMemoryIsZeroedWritableAndOwnedByCrt()
    {
        using var memory = new WindowsProcessMemory(Environment.ProcessId, writable: true);
        ulong address = RemoteCrtAllocator.Allocate(memory, 64);
        try
        {
            Assert.Equal(new byte[64], memory.Read(address, 64));
            byte[] expected = [0x12, 0x34, 0x56, 0x78];
            Assert.True(memory.TryWrite(address + 60, expected));
            Assert.Equal(expected, memory.Read(address + 60, expected.Length));
        }
        finally { Free((nint)address); }
    }

    [DllImport("ucrtbase.dll", EntryPoint = "free")]
    static extern void Free(nint address);
}
