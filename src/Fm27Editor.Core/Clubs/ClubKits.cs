using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Existing kit item snapshot. The vector points to its first colour, eight bytes into the record.</summary>
public sealed record ClubKitItem(ulong Club, int ClubId, ulong Holder, ulong Begin, ulong End, ulong Capacity,
    ulong Slot, ulong Address, string Bytes, string VectorIdentity)
{
    public byte Kit => Convert.FromHexString(Bytes)[2];
    public byte Type => Convert.FromHexString(Bytes)[3];
    public int Flags => BitConverter.ToInt32(Convert.FromHexString(Bytes), 4);
    public int Style => BitConverter.ToInt32(Convert.FromHexString(Bytes), 0x1C);
    public uint Colour(int index) => BitConverter.ToUInt32(Convert.FromHexString(Bytes), 8 + index * 4);
}

public static class ClubKitMapping
{
    /// <summary>FMRTE's GK Icon follows the goalkeeper shirt in the PSG and Bayern snapshots.</summary>
    public static ClubKitItem? GoalkeeperIcon(IReadOnlyList<ClubKitItem> items, byte kit)
    {
        if (kit > 2) return null;
        var matches = Enumerable.Range(0, Math.Max(0, items.Count - 1)).Where(i =>
            items[i].Kit == kit && items[i].Type == 2 && items[i].Flags == 0 && items[i + 1].Flags == 0x100 &&
            (kit switch
            {
                0 => items[i + 1].Kit == 1 && items[i + 1].Type is 1 or 0x21,
                1 => (items[i + 1].Kit == 0 && items[i + 1].Type == 4) || (items[i + 1].Kit == 2 && items[i + 1].Type == 1),
                2 => items[i + 1].Kit == 0 && items[i + 1].Type == 4,
                _ => false,
            }))
            .ToArray();
        return matches.Length == 1 ? items[matches[0] + 1] : null;
    }

    /// <summary>FMRTE's GK shorts/socks follow the outfield record in the measured vectors.</summary>
    public static ClubKitItem? GoalkeeperPart(IReadOnlyList<ClubKitItem> items, byte kit, byte type)
    {
        if (kit > 2 || type is not (4 or 5)) return null;
        var matches = Enumerable.Range(0, Math.Max(0, items.Count - 1)).Where(i =>
            items[i].Kit == kit && items[i].Type == type && items[i].Flags is 0 or 1 &&
            (items[i + 1].Flags == 0x100 ||
                (items[i + 1].Flags == 0x101 && items[i + 1].Type == type) ||
                (items[i + 1].Flags == 0x102 && kit == 0 && type == 4 && items[i + 1].Type == 4)) &&
            ValidNext(items[i + 1], kit, type)).ToArray();
        return matches.Length == 1 ? items[matches[0] + 1] : null;
    }

    static bool ValidNext(ClubKitItem item, byte kit, byte type) => (kit, type) switch
    {
        (0, 4) => item.Kit == 1 && item.Type == 4,
        (0, 5) => item.Kit == 1 && item.Type == 5,
        (1, 4) => (item.Kit == 2 && item.Type == 4) || (item.Kit == 0 && item.Type == 5),
        (1, 5) => (item.Kit == 2 && item.Type == 5) || (item.Kit == 0 && item.Type is 1 or 0x21),
        (2, 4) => item.Kit == 0 && item.Type == 5,
        (2, 5) => item.Kit == 0 && item.Type is 1 or 0x21,
        _ => false,
    };
}

public sealed class ClubKits(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public IReadOnlyList<ClubKitItem> Read(ulong club, int clubId)
    {
        var c = profile.Club;
        // The object layout is shared by the matching Game Pass and Steam builds; Steam is not yet runtime-verified.
        if (profile.Build is not ("27.0.0+2493426 Game Pass" or "27.0.0+2493426") ||
            c?.Id is not { } idOffset || c.TransferClauses is not { } layout) return [];
        try
        {
            if (memory.ReadUInt64(club) != moduleBase + c.ClubVtableRva || BitConverter.ToInt32(memory.Read(club + (ulong)idOffset, 4)) != clubId) return [];
            ulong holder = memory.ReadUInt64(club + (ulong)layout.Pointer);
            if (holder == 0) return [];
            byte[] header = memory.Read(holder + 0x78, 24);
            ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
            if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || end - begin > 4096) return [];
            var records = new List<(ulong Slot, ulong Address, byte[] Bytes)>();
            var identity = new List<byte>();
            for (ulong slot = begin; slot < end; slot += 8)
            {
                ulong colour = memory.ReadUInt64(slot);
                if (colour < 0x10008) return [];
                byte[] bytes = memory.Read(colour - 8, 0x28);
                identity.AddRange(BitConverter.GetBytes(colour));
                identity.AddRange(bytes.AsSpan(0, 8).ToArray());
                records.Add((slot, colour - 8, bytes));
            }
            string signature = Convert.ToHexString(identity.ToArray());
            return records.Select(r => new ClubKitItem(club, clubId, holder, begin, end, cap, r.Slot, r.Address,
                Convert.ToHexString(r.Bytes), signature)).ToArray();
        }
        catch (MemoryAccessException) { return []; }
    }

    public void SetColour(ClubKitItem snapshot, int index, uint colour)
    {
        if (index is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(index));
        // FMRTE changes RGB individually and leaves the existing alpha unchanged.
        RequireCurrent(snapshot);
        byte[] after = BitConverter.GetBytes(colour);
        Write(snapshot.Address + (ulong)(8 + index * 4), after.AsSpan(0, 3));
    }

    public void SetStyle(ClubKitItem snapshot, int style)
    {
        RequireCurrent(snapshot);
        Write(snapshot.Address + 0x1C, BitConverter.GetBytes(style));
    }

    void RequireCurrent(ClubKitItem snapshot)
    {
        var current = Read(snapshot.Club, snapshot.ClubId).SingleOrDefault(x => x.Slot == snapshot.Slot);
        if (current != snapshot) throw new StaleObjectException("Kit item or containing vector changed since it was read");
    }

    void Write(ulong address, ReadOnlySpan<byte> bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException("Could not write kit item");
    }
}
