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

    public unsafe bool TryWrite(ulong address, ReadOnlySpan<byte> data)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        fixed (byte* p = data)
            return WriteProcessMemory(_handle, (nint)address, p, data.Length, out var written) && written == data.Length;
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
    public ulong Allocate(int bytes)
    {
        if (!CanWrite) throw new InvalidOperationException("Process was opened read-only");
        nint p = VirtualAllocEx(_handle, 0, bytes, 0x3000, 0x04);
        if (p == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed");
        return (ulong)p;
    }

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
