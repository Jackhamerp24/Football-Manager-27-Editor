using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Game;

/// <summary>Reads the current in-game date.</summary>
public static class GameClock
{
    /// <summary>
    /// Null when the profile has no date chain, a pointer is missing (no save loaded), the object has
    /// the wrong vtable, the value is not a date, or the cross-check chain disagrees.
    /// </summary>
    public static DateOnly? Read(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
        if (profile.DirectGameDateRva is { } rva)
        {
            Span<byte> raw = stackalloc byte[4];
            if (!memory.TryRead(moduleBase + rva, raw)) return null;
            int day = BitConverter.ToUInt16(raw) & 0x1FF;
            int year = BitConverter.ToUInt16(raw[2..]);
            if (year is < 1901 or > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
            return new DateOnly(year, 1, 1).AddDays(day - 1);
        }
        if (profile.GameDate is not { } g) return null;
        var primary = Follow(memory, moduleBase, g.Primary);
        if (primary is null || g.CrossCheck is null) return primary;
        return Follow(memory, moduleBase, g.CrossCheck) == primary ? primary : null;
    }

    static DateOnly? Follow(IProcessMemory memory, ulong moduleBase, DateChain c)
    {
        Span<byte> q = stackalloc byte[8];
        if (!memory.TryRead(moduleBase + c.RootRva, q)) return null;
        ulong obj = BitConverter.ToUInt64(q);
        if (obj == 0) return null;
        if (c.ObjectVtableRva is { } vt && (!memory.TryRead(obj, q) || BitConverter.ToUInt64(q) != moduleBase + vt)) return null;
        ulong node = obj;
        if (c.PointerOffset is { } po)
        {
            if (!memory.TryRead(obj + (ulong)po, q)) return null;
            node = BitConverter.ToUInt64(q);
        }
        Span<byte> d = stackalloc byte[4];
        if (node == 0 || !memory.TryRead(node + (ulong)c.DateOffset, d)) return null;
        short day = BitConverter.ToInt16(d), year = BitConverter.ToInt16(d[2..]);
        if (year is < 1800 or > 2300 || day is < 1 or > 366) return null;
        var date = new DateOnly(year, 1, 1).AddDays(day - 1);
        return date.Year == year ? date : null;
    }
}
