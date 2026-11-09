namespace Fm27Editor.Core.Memory;

/// <summary>
/// Finds a game list again when its static root slot or vector offset moved (Game Pass sessions shift them by a few slots):
/// looks in the neighbouring slots for a holder that has a vector of pointers whose first objects carry <paramref name="vtable"/>.
/// </summary>
public static class PointerListFinder
{
    public static IReadOnlyList<ulong> Find(IProcessMemory memory, ulong moduleBase, uint rootRva, ulong vtable, int maxCount, int holderSize = 0x810)
    {
        IReadOnlyList<ulong> best = [];
        var holderBytes = new byte[holderSize];
        Span<byte> pointer = stackalloc byte[8];
        for (long delta = -0x40; delta <= 0x40; delta += 8)
        {
            if ((long)rootRva + delta < 0 || !memory.TryRead(moduleBase + (ulong)((long)rootRva + delta), pointer)) continue;
            ulong holder = BitConverter.ToUInt64(pointer);
            if (holder == 0 || !memory.TryRead(holder, holderBytes)) continue;
            for (int off = 0; off + 24 <= holderBytes.Length; off += 8)
            {
                ulong begin = BitConverter.ToUInt64(holderBytes, off), end = BitConverter.ToUInt64(holderBytes, off + 8), cap = BitConverter.ToUInt64(holderBytes, off + 16);
                if (begin == 0 || end <= begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 > (ulong)maxCount) continue;
                int count = (int)((end - begin) / 8);
                if (count <= best.Count) continue;
                var raw = new byte[count * 8];
                if (!memory.TryRead(begin, raw)) continue;
                bool plausible = true;
                for (int i = 0; i < Math.Min(count, 3); i++)
                {
                    ulong obj = BitConverter.ToUInt64(raw, i * 8);
                    if (obj == 0 || !memory.TryRead(obj, pointer) || BitConverter.ToUInt64(pointer) != vtable) { plausible = false; break; }
                }
                if (!plausible) continue;
                var list = new ulong[count];
                Buffer.BlockCopy(raw, 0, list, 0, raw.Length);
                best = list;
            }
        }
        return best;
    }
}
