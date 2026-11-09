using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// FMRTE 27's Background / Foreground Colour on the nation's National Teams page, measured 6/11 with a Frida write trace
/// and a pointer search from the nation object: <c>nation+0x108</c> (team/branding object) <c>+0xB0</c> (colour block)
/// <c>+0xA0</c> foreground, <c>+0xA8</c> background — the same layout and offsets the club profile uses for clubs.
/// Colours are BGRA dwords (England: foreground 0xFF2F55B5, background 0xFFFAFAFA).
/// </summary>
public sealed record NationColours(uint Background, uint Foreground, ulong Team, ulong Detail)
{
    public static string Format(uint bgra) => $"#{(bgra >> 16) & 0xFF:X2}{(bgra >> 8) & 0xFF:X2}{bgra & 0xFF:X2}";

    public static bool TryParse(string? text, out uint bgra)
    {
        bgra = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return false;
        byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
        bgra = (uint)(b | (g << 8) | (r << 16) | 0xFF000000);
        return true;
    }
}

public sealed class NationColourAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int TeamPointer = 0x108, DetailPointer = 0xB0, Foreground = 0xA0, Background = 0xA8;

    public NationColours? Read(ulong nation)
    {
        if (!IsNation(nation)) return null;
        ulong team = memory.ReadUInt64(nation + TeamPointer);
        if (!IsTeam(team)) return null;
        ulong detail = memory.ReadUInt64(team + DetailPointer);
        if (detail == 0) return null;
        var bytes = memory.Read(detail + Foreground, 16);
        return new(BitConverter.ToUInt32(bytes, Background - Foreground), BitConverter.ToUInt32(bytes, 0), team, detail);
    }

    /// <summary>Guarded write of one colour: nation vtable, team vtable, colour block unchanged, current dword equal to the snapshot.</summary>
    public void Set(ulong nation, NationColours snapshot, bool background, uint bgra)
    {
        if (!IsNation(nation)) throw new StaleObjectException("The nation changed since it was read");
        ulong team = memory.ReadUInt64(nation + TeamPointer);
        if (team != snapshot.Team || !IsTeam(team))
            throw new StaleObjectException("The national team changed since it was read");
        ulong detail = memory.ReadUInt64(team + DetailPointer);
        if (detail != snapshot.Detail || detail == 0)
            throw new StaleObjectException("The colour block changed since it was read");
        int offset = background ? Background : Foreground;
        uint before = background ? snapshot.Background : snapshot.Foreground;
        ulong address = detail + (ulong)offset;
        if (BitConverter.ToUInt32(memory.Read(address, 4)) != before)
            throw new StaleObjectException("The colour changed since it was read");
        var bytes = BitConverter.GetBytes(bgra);
        using var scope = Fm27Editor.Core.History.ChangeContext.Describe(new Fm27Editor.Core.History.ChangeLabel(background ? "Background Colour" : "Foreground Colour", Kind: Fm27Editor.Core.History.ChangeValueKind.Colour));
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 4).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write the nation colour at 0x{address:X}");
    }

    bool IsNation(ulong address) =>
        address != 0 && profile.Nation is { } n && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + n.VtableRva;

    bool IsTeam(ulong address) =>
        address != 0 && profile.Nation?.Preferences is { } p && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + p.TeamVtable;
}
