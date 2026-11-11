using System.ComponentModel;
using System.Runtime.InteropServices;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.Core.Memory;

public sealed partial class WindowsProcessMemory : ICodePatchHost
{
    const uint SuspendResume = 0x0800;

    /// <summary>
    /// Executable block near <paramref name="near"/>. The edit history is told about it like any fresh block, so writes into
    /// the routine's block are not recorded (undoing them while the jump points there would break the game).
    /// </summary>
    ulong ICodePatchHost.AllocateNear(ulong near, int bytes)
    {
        ulong at = AllocateNear(near, bytes);
        OnAllocate?.Invoke(at, bytes);
        return at;
    }

    /// <summary>Suspends the whole process (NtSuspendProcess) until the result is disposed.</summary>
    public IDisposable Pause()
    {
        nint handle = OpenProcess(SuspendResume, false, ProcessId);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot pause the game process");
        int status = NtSuspendProcess(handle);
        if (status != 0) { CloseHandle(handle); throw new Win32Exception($"Pausing the game failed (0x{status:X8})"); }
        return new Resume(handle);
    }

    public void FlushCode(ulong address, int bytes) => FlushInstructionCache(address, bytes);

    sealed class Resume(nint handle) : IDisposable
    {
        bool done;
        public void Dispose()
        {
            if (done) return;
            done = true;
            NtResumeProcess(handle);
            CloseHandle(handle);
        }
    }

    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(nint process);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(nint process);
}
