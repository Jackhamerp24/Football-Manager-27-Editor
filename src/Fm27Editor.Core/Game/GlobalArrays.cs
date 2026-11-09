using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Game;

/// <summary>
/// The game-wide pointer arrays whose addresses sit at fixed distances from the clubs array in the module's data
/// block. FM Studio 1.1.1 finds the clubs array with an AOB signature (`FF D7 48 8B 1D ?? ?? ?? ?? 4C 8B 35 ?? ?? ?? ??
/// 4C 39 F3`) and derives the other slots from it: person -440, staff -368, referees -320, agents -32, teams +144
/// (bytes, relative to the clubs array start slot). The same relative layout is used on FM27 by that editor
/// (`ResolveDynamicPointersFM27`), so this class only has to find the anchor once per session.
/// </summary>
public sealed record GlobalArrays(ulong ClubsBegin, ulong ClubsEnd, ulong StaffBegin, ulong StaffEnd,
    ulong RefereesBegin, ulong RefereesEnd, ulong AgentsBegin, ulong AgentsEnd, ulong TeamsBegin, ulong TeamsEnd)
{
    public static int Count(ulong begin, ulong end) =>
        begin != 0 && end > begin && (end - begin) % 8 == 0 ? (int)((end - begin) / 8) : 0;

    public int ClubsCount => Count(ClubsBegin, ClubsEnd);
    public int StaffCount => Count(StaffBegin, StaffEnd);
    public int RefereeCount => Count(RefereesBegin, RefereesEnd);
    public int AgentCount => Count(AgentsBegin, AgentsEnd);
    public int TeamsCount => Count(TeamsBegin, TeamsEnd);
}

/// <summary>Finds the clubs-array anchor and the arrays derived from it; read only.</summary>
public static class GlobalArrayFinder
{
    /// <summary>call rdi; mov rbx,[rip+d]; mov r14,[rip+d]; cmp rbx,r14 — the pair of array slots FM Studio reads.</summary>
    static readonly byte?[] Signature =
        [0xFF, 0xD7, 0x48, 0x8B, 0x1D, null, null, null, null, 0x4C, 0x8B, 0x35, null, null, null, null, 0x4C, 0x39, 0xF3];

    /// <summary>The module slots holding the clubs array begin and end pointers, or null when the signature is absent.</summary>
    public static (ulong StartSlot, ulong EndSlot)? FindClubsSlots(IProcessMemory memory, ulong moduleBase, ulong moduleSize)
    {
        const int chunk = 1 << 23;
        var buffer = new byte[chunk + Signature.Length];
        for (ulong offset = 0; offset < moduleSize; offset += (ulong)chunk)
        {
            int length = (int)Math.Min((ulong)chunk + (ulong)Signature.Length, moduleSize - offset);
            if (!memory.TryRead(moduleBase + offset, buffer.AsSpan(0, length))) continue;
            int found = IndexOf(buffer, length);
            if (found < 0) continue;
            ulong at = moduleBase + offset + (ulong)found;
            int disp1 = BitConverter.ToInt32(buffer, found + 5), disp2 = BitConverter.ToInt32(buffer, found + 12);
            ulong start = at + 2 + 7 + (ulong)(long)disp1, end = at + 9 + 7 + (ulong)(long)disp2;
            if (start < moduleBase || end < moduleBase || start >= moduleBase + moduleSize || end >= moduleBase + moduleSize)
                continue;
            return (start, end);
        }
        return null;
    }

    /// <summary>Reads the begin/end vector at each derived slot; null counts stay zero.</summary>
    public static GlobalArrays? Read(IProcessMemory memory, ulong moduleBase, ulong moduleSize)
    {
        if (FindClubsSlots(memory, moduleBase, moduleSize) is not { } slots) return null;
        ulong Q(ulong at)
        {
            Span<byte> b = stackalloc byte[8];
            return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
        }
        ulong clubs = slots.StartSlot;
        return new(ClubsBegin: Q(clubs), ClubsEnd: Q(clubs + 8),
            StaffBegin: Q(clubs - 368), StaffEnd: Q(clubs - 360),
            RefereesBegin: Q(clubs - 320), RefereesEnd: Q(clubs - 312),
            AgentsBegin: Q(clubs - 32), AgentsEnd: Q(clubs - 24),
            TeamsBegin: Q(clubs + 144), TeamsEnd: Q(clubs + 152));
    }

    static int IndexOf(byte[] data, int length)
    {
        for (int i = 0; i + Signature.Length <= length; i++)
        {
            bool match = true;
            for (int j = 0; j < Signature.Length; j++)
                if (Signature[j] is { } b && data[i + j] != b) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
