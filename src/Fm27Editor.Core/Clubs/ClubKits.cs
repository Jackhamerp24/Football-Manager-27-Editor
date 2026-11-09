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
    public static readonly string[] ColourNames = ["Foreground", "Background", "Outline", "Number Colour", "Number Outline Colour"];

    /// <summary>What FMRTE calls this item ("Home Shirt", "GK Away Shorts"), or null when it is not one of the editable parts.</summary>
    public static string? Name(IReadOnlyList<ClubKitItem> items, ClubKitItem item)
    {
        static string Kit(int k) => k switch { 0 => "Home", 1 => "Away", _ => "Third" };
        string? part = (item.Type, item.Flags) switch
        {
            (3, 0x100) => "Shirt", (2, 0) => "GK Shirt", (2, 0x100) => "Text", (1 or 0x21, 0) => "Icon", (4, 0 or 1) => "Shorts", (5, 0 or 1) => "Socks", _ => null,
        };
        if (part is not null && item.Kit <= 2) return part.StartsWith("GK ", StringComparison.Ordinal) ? $"GK {Kit(item.Kit)} {part[3..]}" : $"{Kit(item.Kit)} {part}";
        for (byte k = 0; k < 3; k++)
        {
            if (GoalkeeperIcon(items, k) is { } icon && icon.Slot == item.Slot) return $"GK {Kit(k)} Icon";
            foreach (var (type, name) in new[] { ((byte)4, "Shorts"), ((byte)5, "Socks") })
                if (GoalkeeperPart(items, k, type) is { } p && p.Slot == item.Slot) return $"GK {Kit(k)} {name}";
        }
        return null;
    }

    /// <summary>Announces "Home Shirt Background" for the colour about to be written.</summary>
    public static IDisposable Announce(IReadOnlyList<ClubKitItem> items, ClubKitItem item, string what, Fm27Editor.Core.History.ChangeValueKind kind = Fm27Editor.Core.History.ChangeValueKind.Number) =>
        Fm27Editor.Core.History.ChangeContext.Describe(new Fm27Editor.Core.History.ChangeLabel($"{Name(items, item) ?? "Kit item"} {what}", Kind: kind));

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
        // The object layout is shared by Game Pass and Steam (read back on both); the vtable and id checks below refuse anything else.
        if (c?.Id is not { } idOffset || c.TransferClauses is not { } layout) return [];
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
        using var scope = ClubKitMapping.Announce(Read(snapshot.Club, snapshot.ClubId), snapshot, ClubKitMapping.ColourNames[index], Fm27Editor.Core.History.ChangeValueKind.Colour);
        Write(snapshot.Address + (ulong)(8 + index * 4), after.AsSpan(0, 3));
    }

    public void SetStyle(ClubKitItem snapshot, int style)
    {
        RequireCurrent(snapshot);
        using var scope = ClubKitMapping.Announce(Read(snapshot.Club, snapshot.ClubId), snapshot, "Style");
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
