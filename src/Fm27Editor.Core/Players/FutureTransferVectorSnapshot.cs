using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>A bounded snapshot of one game vector. This type prepares writes; it does not publish them.</summary>
public sealed class FutureTransferVectorSnapshot
{
    readonly ulong header, begin, end, capacity;
    readonly int itemSize;
    readonly byte[] headerBytes, items;

    FutureTransferVectorSnapshot(ulong header, ulong begin, ulong end, ulong capacity, int itemSize, byte[] headerBytes, byte[] items)
    { this.header = header; this.begin = begin; this.end = end; this.capacity = capacity; this.itemSize = itemSize; this.headerBytes = headerBytes; this.items = items; }

    public int Count => items.Length / itemSize;
    public byte[] Items => (byte[])items.Clone();

    public IReadOnlyList<PlannedWrite> Guards => header == 0 ? [] : items.Length == 0
        ? [new(header, (byte[])headerBytes.Clone(), [], "vector header")]
        : [new(header, (byte[])headerBytes.Clone(), [], "vector header"), new(begin, (byte[])items.Clone(), [], "vector items")];

    /// <summary>A vector that does not exist yet (a free agent has no contract, so no contract terms): no guards, nothing to read.</summary>
    public static FutureTransferVectorSnapshot Empty(int itemSize) => new(0, 0, 0, 0, itemSize, new byte[24], []);

    public static FutureTransferVectorSnapshot Capture(IProcessMemory memory, ulong header, int itemSize, int maxItems)
    {
        if (header == 0 || itemSize <= 0 || maxItems <= 0) throw new ArgumentOutOfRangeException(nameof(header));
        var b = memory.Read(header, 24);
        ulong begin = BitConverter.ToUInt64(b, 0), end = BitConverter.ToUInt64(b, 8), cap = BitConverter.ToUInt64(b, 16);
        if ((begin == 0 && (end != 0 || cap != 0)) || end < begin || cap < end ||
            (end - begin) % (ulong)itemSize != 0 || (cap - begin) % (ulong)itemSize != 0 ||
            (cap - begin) / (ulong)itemSize > (ulong)maxItems)
            throw new StaleObjectException("Invalid future-transfer vector");
        int length = checked((int)(end - begin));
        return new(header, begin, end, cap, itemSize, b, length == 0 ? [] : memory.Read(begin, length));
    }

    /// <summary>Checks both the header and its contents, including items whose count has not changed.</summary>
    public void Validate(IProcessMemory memory)
    {
        if (header == 0) return;
        if (!memory.Read(header, 24).AsSpan().SequenceEqual(headerBytes) ||
            (items.Length != 0 && !memory.Read(begin, items.Length).AsSpan().SequenceEqual(items)))
            throw new StaleObjectException("Future-transfer vector changed since it was read");
    }

    /// <summary>Index of the first item for which <paramref name="match"/> is true, or -1.</summary>
    public int IndexOf(Func<ReadOnlySpan<byte>, bool> match)
    {
        for (int i = 0; i < Count; i++) if (match(items.AsSpan(i * itemSize, itemSize))) return i;
        return -1;
    }

    /// <summary>Number of items for which <paramref name="match"/> is true.</summary>
    public int CountOf(Func<ReadOnlySpan<byte>, bool> match)
    {
        int n = 0;
        for (int i = 0; i < Count; i++) if (match(items.AsSpan(i * itemSize, itemSize))) n++;
        return n;
    }

    /// <summary>Prepares a guarded removal: the items after <paramref name="index"/> move down one place (the vacated last slot is cleared) and the end pointer drops.</summary>
    public IReadOnlyList<PlannedWrite> RemoveAt(IProcessMemory memory, int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        Validate(memory);
        var writes = new List<PlannedWrite>();
        int tail = (Count - index) * itemSize;
        var moved = new byte[tail];
        items.AsSpan((index + 1) * itemSize).CopyTo(moved);          // the last itemSize bytes stay zero
        writes.Add(new(begin + (ulong)(index * itemSize), items.AsSpan(index * itemSize, tail).ToArray(), moved, "future vector items"));
        var next = (byte[])headerBytes.Clone();
        BitConverter.GetBytes(end - (ulong)itemSize).CopyTo(next, 8);
        writes.Add(new(header, headerBytes, next, "future vector header"));
        return writes;
    }

    /// <summary>Prepares a guarded append. A full vector uses a fresh buffer from the supplied game allocator.</summary>
    public IReadOnlyList<PlannedWrite> Append(IProcessMemory memory, ReadOnlySpan<byte> item, Func<int, ulong> allocate)
    {
        if (item.Length != itemSize) throw new ArgumentException("Wrong vector item size", nameof(item));
        Validate(memory);
        if (end != 0 && capacity - end >= (ulong)itemSize)
        {
            var next = (byte[])headerBytes.Clone();
            BitConverter.GetBytes(end + (ulong)itemSize).CopyTo(next, 8);
            return [new(end, memory.Read(end, itemSize), item.ToArray(), "future vector item"),
                new(header, headerBytes, next, "future vector header")];
        }
        var combined = new byte[checked(items.Length + itemSize)];
        items.CopyTo(combined, 0); item.CopyTo(combined.AsSpan(items.Length));
        ulong buffer = allocate(combined.Length);
        if (buffer == 0) throw new MemoryAccessException("Future-transfer vector allocation failed");
        ulong newEnd = checked(buffer + (ulong)combined.Length);
        var newHeader = new byte[24];
        BitConverter.GetBytes(buffer).CopyTo(newHeader, 0); BitConverter.GetBytes(newEnd).CopyTo(newHeader, 8);
        BitConverter.GetBytes(newEnd).CopyTo(newHeader, 16);
        return [new(buffer, memory.Read(buffer, combined.Length), combined, "new future vector buffer"),
            new(header, headerBytes, newHeader, "future vector header")];
    }
}
