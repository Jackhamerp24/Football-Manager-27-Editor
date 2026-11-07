using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// FMRTE 27's Kits and Colours page for a national team. The game shares the club "holder" structure: the nation's
/// branding object (nation+0x108) holds the same pointer at +0xB0 that a club does, so kits are the vector at
/// holder+0x78 of pointers to 0x28-byte records whose colour block starts eight bytes in — measured on England (21 items,
/// Home shirt 0x802000FF / 0xF8F8F8FF with Item Style 10, matching FMRTE's Home Kit page).
/// </summary>
public sealed class NationKits(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int HolderPointer = 0xB0, KitVector = 0x78, RecordSize = 0x28;

    /// <summary>Resolves the nation's kit holder (the same object that holds its colours).</summary>
    public ulong? Holder(ulong nation)
    {
        if (!IsNation(nation)) return null;
        ulong team = memory.ReadUInt64(nation + NationColourAccess.TeamPointer);
        if (!IsTeam(team)) return null;
        ulong holder = memory.ReadUInt64(team + HolderPointer);
        return holder == 0 ? null : holder;
    }

    public IReadOnlyList<ClubKitItem> Read(ulong nation)
    {
        if (Holder(nation) is not { } holder) return [];
        try
        {
            byte[] header = memory.Read(holder + KitVector, 24);
            ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
            if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || end - begin > 4096) return [];
            var records = new List<(ulong Slot, ulong Address, byte[] Bytes)>();
            var identity = new List<byte>();
            for (ulong slot = begin; slot < end; slot += 8)
            {
                ulong colour = memory.ReadUInt64(slot);
                if (colour < 0x10008) return [];
                byte[] bytes = memory.Read(colour - 8, RecordSize);
                identity.AddRange(BitConverter.GetBytes(colour));
                identity.AddRange(bytes.AsSpan(0, 8).ToArray());
                records.Add((slot, colour - 8, bytes));
            }
            string signature = Convert.ToHexString(identity.ToArray());
            return records.Select(r => new ClubKitItem(nation, 0, holder, begin, end, cap, r.Slot, r.Address,
                Convert.ToHexString(r.Bytes), signature)).ToArray();
        }
        catch (MemoryAccessException) { return []; }
    }

    /// <summary>Writes RGB of one colour slot and leaves the stored alpha alone, exactly like FMRTE.</summary>
    public void SetColour(ClubKitItem snapshot, int index, uint colour)
    {
        if (index is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(index));
        RequireCurrent(snapshot);
        var after = BitConverter.GetBytes(colour);
        Write(snapshot.Address + (ulong)(8 + index * 4), after.AsSpan(0, 3));
    }

    public void SetStyle(ClubKitItem snapshot, int style) => SetStyle(snapshot, style, snapshot.Style);

    public void SetStyle(ClubKitItem snapshot, int style, int expected)
    {
        if (snapshot.Style != expected) throw new StaleObjectException("Kit item style changed since it was read");
        RequireCurrent(snapshot);
        Write(snapshot.Address + 0x1C, BitConverter.GetBytes(style));
    }

    void RequireCurrent(ClubKitItem snapshot)
    {
        var current = Read(snapshot.Club).SingleOrDefault(x => x.Slot == snapshot.Slot);
        if (current != snapshot) throw new StaleObjectException("Kit item or containing vector changed since it was read");
    }

    void Write(ulong address, ReadOnlySpan<byte> bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException("Could not write the national kit item");
    }

    bool IsNation(ulong address) =>
        address != 0 && profile.Nation is { } n && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + n.VtableRva;

    bool IsTeam(ulong address) =>
        address != 0 && profile.Nation?.Preferences is { } p && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + p.TeamVtable;
}
