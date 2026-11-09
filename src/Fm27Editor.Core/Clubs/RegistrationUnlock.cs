using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Captures the game's squad-registration rules object so its flags can be flipped without knowing where the game
/// keeps it. A seven-byte prologue in <c>game_plugin</c> that loads two fields of that object is replaced by a jump to a
/// small trampoline allocated inside the game: the trampoline stores the object pointer, runs the bytes it stole, then
/// jumps back. Reads and writes of the object itself stay ordinary guarded data writes.
/// </summary>
/// <remarks>
/// Site and stolen bytes were read from the live image of <c>27.0.0+2493426</c>; the trampoline and the patch are
/// written here from scratch. Anything unexpected (other build, other bytes) makes <see cref="Install"/> refuse.
/// </remarks>
public sealed class RegistrationUnlock(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, int? targetRva = null)
{
    /// <summary>Prologue of the function that reads the registration rules; measured on <c>27.0.0+2493426</c>.</summary>
    public const int TargetRva = 0x5270FA;

    /// <summary><c>mov r14,[rbx]</c> + <c>mov r15,[rbx+8]</c> — the two loads the trampoline has to replay.</summary>
    public static readonly byte[] StolenBytes = [0x4C, 0x8B, 0x33, 0x4C, 0x8B, 0x7B, 0x08];

    const int PageSize = 0x400;
    const int StubOffset = 0x10;
    const uint PageExecuteReadWrite = 0x40;

    ulong page;
    byte[]? original;

    public ulong Target => moduleBase + (ulong)(targetRva ?? TargetRva);
    public bool IsInstalled => page != 0;

    /// <summary>Pointer the trampoline stored on its last call, or 0 when the game has not called it yet.</summary>
    public bool TryCaptureRules(out ulong rules)
    {
        rules = 0;
        if (page == 0) return false;
        Span<byte> slot = stackalloc byte[8];
        if (!memory.TryRead(page, slot)) return false;
        rules = BitConverter.ToUInt64(slot);
        return rules != 0;
    }

    public void Install()
    {
        if (IsInstalled) return;
        if (memory is not WindowsProcessMemory windows || !windows.CanWrite)
            throw new InvalidOperationException("Registration unlock needs a writable game process");
        var current = memory.Read(Target, StolenBytes.Length);
        if (!current.AsSpan().SequenceEqual(StolenBytes))
            throw new InvalidOperationException(
                $"build {profile.Build} does not carry the registration prologue at 0x{Target:X}");

        ulong allocated = windows.AllocateNear(Target, PageSize);
        try
        {
            var stub = BuildStub(allocated, StolenBytes, Target + (ulong)StolenBytes.Length);
            if (!memory.TryWrite(allocated + StubOffset, stub))
                throw new MemoryAccessException("trampoline write failed");
            if (!memory.Read(allocated + StubOffset, stub.Length).AsSpan().SequenceEqual(stub))
                throw new MemoryAccessException("trampoline read-back failed");

            var patch = BuildPatch(Target, allocated + StubOffset);
            uint previous = windows.Protect(Target, 16, PageExecuteReadWrite);
            try
            {
                if (!memory.TryWrite(Target, patch))
                    throw new MemoryAccessException("registration patch write failed");
            }
            finally
            {
                windows.Protect(Target, 16, previous);
            }
            if (!memory.Read(Target, patch.Length).AsSpan().SequenceEqual(patch))
                throw new MemoryAccessException("registration patch read-back failed");

            original = current;
            page = allocated;
        }
        catch
        {
            windows.Free(allocated);
            throw;
        }
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        if (memory is WindowsProcessMemory windows && original is not null)
        {
            uint previous = windows.Protect(Target, 16, PageExecuteReadWrite);
            try { memory.TryWrite(Target, original); }
            finally { windows.Protect(Target, 16, previous); }
            windows.Free(page);
        }
        original = null;
        page = 0;
    }

    /// <summary>
    /// Trampoline: save the rules pointer, replay the stolen loads, jump back to the instruction after them.
    /// <c>mov r11, page; mov [r11], rbx</c> then the stolen bytes then <c>jmp qword ptr [rip+0]</c> with the address.
    /// </summary>
    public static byte[] BuildStub(ulong page, byte[] stolen, ulong resume)
    {
        var stub = new List<byte>(stolen.Length + 28) { 0x49, 0xBB };
        stub.AddRange(BitConverter.GetBytes(page));      // mov r11, page
        stub.AddRange([0x4D, 0x89, 0x1B]);               // mov [r11], rbx
        stub.AddRange(stolen);                           // mov r14,[rbx]; mov r15,[rbx+8]
        stub.AddRange([0xFF, 0x25, 0, 0, 0, 0]);         // jmp qword ptr [rip+0]
        stub.AddRange(BitConverter.GetBytes(resume));
        return [.. stub];
    }

    /// <summary>Patch: <c>jmp rel32</c> to the trampoline, padded with two nops to cover the stolen bytes exactly.</summary>
    public static byte[] BuildPatch(ulong from, ulong stub)
    {
        long delta = (long)stub - (long)(from + 5);
        if (delta is < int.MinValue or > int.MaxValue)
            throw new InvalidOperationException("trampoline is out of reach for a rel32 jump");
        var patch = new byte[StolenBytes.Length];
        patch[0] = 0xE9;
        BitConverter.GetBytes((int)delta).CopyTo(patch, 1);
        for (int i = 5; i < patch.Length; i++) patch[i] = 0x90;
        return patch;
    }
}
