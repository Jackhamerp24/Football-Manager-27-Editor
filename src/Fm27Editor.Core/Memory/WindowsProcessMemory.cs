using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Fm27Editor.Core.Memory;

/// <summary>ReadProcessMemory / WriteProcessMemory on an opened process handle.</summary>
public sealed partial class WindowsProcessMemory : IProcessMemory, IDisposable
{
    const uint QueryInformation = 0x0400, VmRead = 0x0010, VmWrite = 0x0020, VmOperation = 0x0008;

    readonly nint _handle;

    public int ProcessId { get; }
    public bool CanWrite { get; }

    public WindowsProcessMemory(int processId, bool writable)
    {
        uint access = QueryInformation | VmRead | (writable ? VmWrite | VmOperation : 0);
        _handle = OpenProcess(access, false, processId);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenProcess({processId}) failed");
        ProcessId = processId;
        CanWrite = writable;
    }

    public unsafe bool TryRead(ulong address, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
            return ReadProcessMemory(_handle, (nint)address, p, buffer.Length, out var read) && read == buffer.Length;
    }

    /// <summary>
    /// Called after every successful write with the address, the bytes that were there and the bytes written. Used by the edit
    /// history; the old bytes are only read when someone listens.
    /// </summary>
    public Action<ulong, byte[], byte[]>? OnWrite { get; set; }

    /// <summary>Called with the address and size of each fresh block, so the history can ignore writes into it.</summary>
    public Action<ulong, int>? OnAllocate { get; set; }

    public unsafe bool TryWrite(ulong address, ReadOnlySpan<byte> data)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        var listener = OnWrite;
        byte[]? before = null;
        if (listener is not null)
        {
            before = new byte[data.Length];
            if (!TryRead(address, before)) before = null;
        }
        bool ok;
        fixed (byte* p = data)
            ok = WriteProcessMemory(_handle, (nint)address, p, data.Length, out var written) && written == data.Length;
        if (ok && listener is not null && before is not null) listener(address, before, data.ToArray());
        return ok;
    }

    /// <summary>Committed regions that are writable data: private heap pages and, inside
    /// [imageStart, imageEnd), writable image pages (a module's static data).</summary>
    public IEnumerable<MemoryRegion> EnumerateWritableRegions(ulong imageStart = 0, ulong imageEnd = 0)
    {
        const uint Commit = 0x1000, Private = 0x20000, Image = 0x1000000, ReadWrite = 0x04, WriteCopy = 0x08;
        nint address = 0;
        int infoSize = Marshal.SizeOf<MemoryBasicInformation>();
        while (VirtualQueryEx(_handle, address, out var info, infoSize) != 0)
        {
            uint protect = info.Protect & 0xFF;
            ulong start = (ulong)info.BaseAddress;
            bool heap = info.Type == Private && protect == ReadWrite;
            bool moduleData = info.Type == Image && (protect == ReadWrite || protect == WriteCopy) && start >= imageStart && start < imageEnd;
            if (info.State == Commit && (heap || moduleData))
                yield return new MemoryRegion(start, (ulong)info.RegionSize, moduleData);
            address = info.BaseAddress + info.RegionSize;
            if (address <= 0) yield break;
        }
    }

    /// <summary>
    /// Fresh zeroed read/write memory inside the game process (VirtualAllocEx). The game's own allocator does
    /// not know it, so the game may fail when it later frees or regrows a container pointing here.
    /// </summary>
    public ulong Allocate(int bytes) => AllocateWith(bytes, 0x04);

    /// <summary>Executable page as close as possible to <paramref name="near"/> so a rel32 jump can reach it.</summary>
    public ulong AllocateNear(ulong near, int bytes)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        for (ulong step = 0x10000; step < 0x7F000000; step += 0x10000)
        {
            foreach (ulong candidate in new[] { near + step, near - step })
            {
                if (candidate < 0x10000) continue;
                nint p = VirtualAllocEx(_handle, (nint)candidate, bytes, 0x3000, 0x40);
                if (p != 0) return (ulong)p;
            }
        }
        throw new Win32Exception(Marshal.GetLastWin32Error(), "no executable page within rel32 reach");
    }
    /// <summary>Fresh executable page inside the game process, used for a hook trampoline.</summary>
    public ulong AllocateExecutable(int bytes) => AllocateWith(bytes, 0x40);

    ulong AllocateWith(int bytes, uint protect)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        nint p = VirtualAllocEx(_handle, 0, bytes, 0x3000, protect);
        if (p == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed");
        OnAllocate?.Invoke((ulong)p, bytes);
        return (ulong)p;
    }

    /// <summary>Changes page protection and returns the previous value, so a code patch can be made in RAM only.</summary>
    public uint Protect(ulong address, int bytes, uint protect)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        if (!VirtualProtectEx(_handle, (nint)address, bytes, protect, out uint old))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualProtectEx failed");
        return old;
    }

    /// <summary>Flushes the instruction cache over a range after a code patch (or its undo) changed it, so no thread runs prefetched bytes.</summary>
    public void FlushInstructionCache(ulong address, int bytes) =>
        FlushInstructionCache(_handle, (nint)address, (nint)bytes);

    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "FlushInstructionCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, nint baseAddress, nint size);

    public void Free(ulong address) => VirtualFreeEx(_handle, (nint)address, 0, 0x8000);

    public void Dispose() => CloseHandle(_handle);

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryBasicInformation
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nint RegionSize;
        public uint State, Protect, Type;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualAllocEx(nint process, nint address, nint size, uint type, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualProtectEx(nint process, nint address, nint size, uint protect, out uint old);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFreeEx(nint process, nint address, nint size, uint freeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation info, nint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadProcessMemory(nint process, nint address, byte* buffer, nint size, out nint read);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WriteProcessMemory(nint process, nint address, byte* buffer, nint size, out nint written);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
