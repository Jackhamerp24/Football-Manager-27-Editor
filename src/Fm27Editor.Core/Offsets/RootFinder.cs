using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Offsets;

/// <summary>How to find a static root slot again on a new build: the slot holds a vector whose elements carry
/// <paramref name="Vtable"/> at <paramref name="Offset"/>, or the slot points at one object carrying it.</summary>
public sealed record RootFormula(string Kind, string Vtable, int Offset, int Min, int Max)
{
    public const string Vector = "vector";
    public const string Object = "object";
}

/// <summary>Static-root discovery for a new build. Both scans read only the module image plus a few heap reads, so a
/// wrong profile cannot make them agree with themselves.</summary>
public static class RootFinder
{
    const ulong HeapLow = 0x1_0000_0000UL, HeapHigh = 0x7FFF_FFFF_FFFFUL;

    static bool IsHeap(ulong value) => value >= HeapLow && value < HeapHigh;

    static bool HasVtable(IProcessMemory memory, ulong address, ulong expected)
    {
        try { return memory.ReadUInt64(address) == expected; }
        catch (MemoryAccessException) { return false; }
    }

    /// <summary>Every module slot whose vector at +Offset holds objects with the expected vtable, in image order.</summary>
    public static IReadOnlyList<uint> FindVector(ModuleImage image, IProcessMemory memory, uint vtableRva, int offset,
        int min, int max)
    {
        ulong expected = image.ModuleBase + vtableRva;
        var found = new List<uint>();
        var bytes = image.Bytes;
        for (int slot = 0; slot + offset + 16 <= bytes.Length; slot += 8)
        {
            int header = slot + offset;
            ulong begin = BitConverter.ToUInt64(bytes, header), end = BitConverter.ToUInt64(bytes, header + 8);
            if (!IsHeap(begin) || end < begin || (end - begin) % 8 != 0) continue;
            ulong count = (end - begin) / 8;
            if (count < (ulong)min || count > (ulong)max) continue;
            ulong first;
            try { first = memory.ReadUInt64(begin); }
            catch (MemoryAccessException) { continue; }
            if (!HasVtable(memory, first, expected)) continue;
            found.Add((uint)slot);
        }
        return found;
    }

    /// <summary>Every module slot that points at an object carrying the expected vtable, in image order.</summary>
    public static IReadOnlyList<uint> FindObject(ModuleImage image, IProcessMemory memory, uint vtableRva)
    {
        ulong expected = image.ModuleBase + vtableRva;
        var found = new List<uint>();
        var bytes = image.Bytes;
        for (int slot = 0; slot + 8 <= bytes.Length; slot += 8)
        {
            ulong target = BitConverter.ToUInt64(bytes, slot);
            if (!IsHeap(target)) continue;
            if (!HasVtable(memory, target, expected)) continue;
            found.Add((uint)slot);
        }
        return found;
    }

    /// <summary>RVA of the build string ("27.0.0+2493426") in the image, found by shape rather than by a stored RVA.</summary>
    public static uint? FindBuildString(ModuleImage image)
    {
        var bytes = image.Bytes;
        for (int i = 0; i + 8 < bytes.Length; i++)
        {
            if (bytes[i] < (byte)'0' || bytes[i] > (byte)'9') continue;
            int length = 0;
            while (length < 40 && i + length < bytes.Length && IsBuildChar(bytes[i + length])) length++;
            if (length < 8 || i + length >= bytes.Length || bytes[i + length] != 0) continue;
            string text = System.Text.Encoding.ASCII.GetString(bytes, i, length);
            var parts = text.Split('.', '+');
            if (parts.Length != 4 || parts[0] != "27") continue;
            if (!parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit))) continue;
            return (uint)i;
        }
        return null;
    }

    static bool IsBuildChar(byte b) => b is >= (byte)'0' and <= (byte)'9' || b == (byte)'.' || b == (byte)'+';
}
