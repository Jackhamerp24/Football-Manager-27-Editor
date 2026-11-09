using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fm27Editor.Core.Memory;

/// <summary>
/// Calls the target's ucrtbase!calloc on a temporary remote thread. FMRTE used calloc for the objects of an arranged
/// transfer. Only the temporary code page is released here; the returned allocation belongs to the target CRT.
/// </summary>
public static class RemoteCrtAllocator
{
    const uint Access = 0x0400 | 0x0010 | 0x0020 | 0x0008 | 0x0002;
    const uint WaitSuccess = 0;
    const int PageSize = 4096, ResultOffset = 256, MaxAllocation = 16 * 1024 * 1024;

    public static ulong Allocate(WindowsProcessMemory memory, int bytes)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Requires 64-bit Windows");
        if (!memory.CanWrite) throw new InvalidOperationException("The process was opened read-only");
        if (bytes is < 1 or > MaxAllocation) throw new ArgumentOutOfRangeException(nameof(bytes));

        using var target = Process.GetProcessById(memory.ProcessId);
        using var current = Process.GetCurrentProcess();
        var remoteCrt = target.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("ucrtbase.dll", StringComparison.OrdinalIgnoreCase));
        var localCrt = current.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("ucrtbase.dll", StringComparison.OrdinalIgnoreCase));
        if (remoteCrt.ModuleMemorySize != localCrt.ModuleMemorySize ||
            !string.Equals(remoteCrt.FileVersionInfo.FileVersion, localCrt.FileVersionInfo.FileVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The target CRT does not match the local CRT");
        nint localLibrary = NativeLibrary.Load("ucrtbase.dll");
        ulong calloc;
        try
        {
            long rva = NativeLibrary.GetExport(localLibrary, "calloc").ToInt64() - localCrt.BaseAddress.ToInt64();
            if (rva < 0 || rva >= remoteCrt.ModuleMemorySize) throw new InvalidOperationException("Invalid calloc export");
            calloc = checked((ulong)(remoteCrt.BaseAddress.ToInt64() + rva));
        }
        finally { NativeLibrary.Free(localLibrary); }

        nint process = OpenProcess(Access, false, memory.ProcessId);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the target for allocation");
        nint thread = 0;
        ulong page = 0;
        bool safeToFreePage = true;
        try
        {
            page = memory.AllocateExecutable(PageSize);
            ulong result = page + ResultOffset;
            var stub = BuildStub(result, calloc, bytes);
            if (!memory.TryWrite(page, stub) || !memory.Read(page, stub.Length).AsSpan().SequenceEqual(stub))
                throw new MemoryAccessException("Could not write the temporary allocator code");
            thread = CreateRemoteThread(process, 0, 0, (nint)page, 0, 0, out _);
            if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the allocator thread");
            safeToFreePage = false;
            uint wait = WaitForSingleObject(thread, 5000);
            if (wait != WaitSuccess) throw new InvalidOperationException($"Allocator thread did not finish (wait 0x{wait:X})");
            safeToFreePage = true;
            if (!GetExitCodeThread(thread, out uint exit) || exit != 0) throw new InvalidOperationException("Allocator thread failed");
            ulong allocated = memory.ReadUInt64(result);
            if (allocated == 0) throw new InvalidOperationException("The target CRT could not allocate memory");
            var check = memory.Read(allocated, Math.Min(bytes, 16));
            if (check.Any(b => b != 0)) throw new MemoryAccessException("The target allocation was not zero-initialized");
            return allocated;
        }
        finally
        {
            if (thread != 0) CloseHandle(thread);
            if (page != 0 && safeToFreePage) memory.Free(page);
            CloseHandle(process);
        }
    }

    // The call shape and x64 stack alignment are taken from the allocator code observed in FMRTE's trace.
    static byte[] BuildStub(ulong result, ulong calloc, int bytes)
    {
        var code = new List<byte> { 0x53, 0x56, 0x48, 0x83, 0xEC, 0x28, 0x48, 0xBE };
        code.AddRange(BitConverter.GetBytes(result));
        code.AddRange([0xB9, 0x01, 0x00, 0x00, 0x00, 0xBA]);
        code.AddRange(BitConverter.GetBytes(bytes));
        code.AddRange([0x48, 0xB8]);
        code.AddRange(BitConverter.GetBytes(calloc));
        code.AddRange([0xFF, 0xD0, 0x48, 0x89, 0x06, 0x48, 0x83, 0xC4, 0x28, 0x5E, 0x5B, 0x31, 0xC0, 0xC3]);
        return [.. code];
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize, nint start, nint parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeThread(nint thread, out uint exitCode);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
}
