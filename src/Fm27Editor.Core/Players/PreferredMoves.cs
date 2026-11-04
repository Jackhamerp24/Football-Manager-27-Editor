using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Preferred moves: 8 bytes of bits at player + <see cref="OffsetProfile.PreferredMoves"/>. Bits 0–60 are FMRTE 27's boxes in
/// row order (Kane 80 04 08 00 00 0A 00 40 and Mbappé 00 04 00 00 08 00 08 02 match the boxes FMRTE ticks; writing bit 0 and
/// bits 58–60 ticked the matching boxes). Bits 61–63 tick no box (Kane has bit 62), so they are never changed here.
/// </summary>
public sealed class PreferredMovesAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int Count = 61;
    public const ulong Editable = (1UL << Count) - 1;

    public ulong? Read(PlayerRecord player)
    {
        if (profile.PreferredMoves is not { } at) return null;
        var b = new byte[8];
        if (!memory.TryRead(player.Address, b) || !profile.PlayerVtableRvas.Any(r => BitConverter.ToUInt64(b) == moduleBase + r)) return null;
        return memory.TryRead(player.Address + (ulong)at, b) ? BitConverter.ToUInt64(b) : null;
    }

    /// <summary>Writes <paramref name="moves"/> (bits 0–60 only) if RAM still holds <paramref name="current"/>; other bits are kept.</summary>
    public void Set(PlayerRecord player, ulong current, ulong moves)
    {
        if ((moves & ~Editable) != 0) throw new ArgumentOutOfRangeException(nameof(moves), "Only bits 0–60 are preferred moves");
        int at = profile.PreferredMoves ?? throw new InvalidOperationException("Profile has no preferred moves offset");
        if (Read(player) != current) throw new StaleObjectException("Preferred moves changed since they were read");
        var next = BitConverter.GetBytes((current & ~Editable) | moves);
        ulong address = player.Address + (ulong)at;
        if (!memory.TryWrite(address, next) || !memory.Read(address, 8).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write preferred moves");
    }
}
