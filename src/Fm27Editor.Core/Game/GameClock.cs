using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Game;

/// <summary>Reads the current in-game date.</summary>
public static class GameClock
{
    /// <summary>
    /// Null when the profile has no date chain, a pointer is missing (no save loaded), the value is
    /// not a date, or the cross-check chain disagrees.
    /// </summary>
    public static DateOnly? Read(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
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
        if (obj == 0 || !memory.TryRead(obj + (ulong)c.PointerOffset, q)) return null;
        ulong node = BitConverter.ToUInt64(q);
        Span<byte> d = stackalloc byte[4];
        if (node == 0 || !memory.TryRead(node + (ulong)c.DateOffset, d)) return null;
        short day = BitConverter.ToInt16(d), year = BitConverter.ToInt16(d[2..]);
        if (year is < 1800 or > 2300 || day is < 1 or > 366) return null;
        var date = new DateOnly(year, 1, 1).AddDays(day - 1);
        return date.Year == year ? date : null;
    }
}
