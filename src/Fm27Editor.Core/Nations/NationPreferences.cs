using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

public sealed record NationPreference(ulong Nation, ulong Team, ulong Begin, ulong End, ulong Address,
    ulong TargetTeam, string? Target, sbyte Rating, byte MinAge, byte MaxAge, byte Knowledge, string Snapshot, ulong Capacity);

/// <summary>Confirmed nation entries in the men's national-team transfer preferences.</summary>
public sealed class NationPreferences(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    bool Vtable(ulong address, uint rva) => address != 0 && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + rva;
    bool Valid(NationPreferenceOffsets l) => l.Team >= 0 && l.Vector >= 0 && l.EntrySize == 16;

    public IReadOnlyList<NationPreference> Read(NationRecord nation, IReadOnlyList<NationRecord> nations)
    {
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l) || !Vtable(nation.Address, n.VtableRva)) return [];
        ulong team = memory.ReadUInt64(nation.Address + (ulong)l.Team);
        if (!Vtable(team, l.TeamVtable)) return [];
        ulong begin = memory.ReadUInt64(team + (ulong)l.Vector), end = memory.ReadUInt64(team + (ulong)l.Vector + 8),
            capacity = memory.ReadUInt64(team + (ulong)l.Vector + 16);
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 16 != 0
            || (capacity - begin) % 16 != 0 || (end - begin) / 16 > 1024) return [];
        var names = new Dictionary<ulong, string>();
        foreach (var other in nations)
            if (Vtable(other.Address, n.VtableRva) && memory.TryRead(other.Address + (ulong)l.Team, new byte[8]))
            {
                ulong target = memory.ReadUInt64(other.Address + (ulong)l.Team);
                if (Vtable(target, l.TeamVtable)) names.TryAdd(target, other.DisplayName);
            }
        var rows = new List<NationPreference>();
        for (ulong p = begin; p < end; p += 16)
        {
            var b = memory.Read(p, 16);
            ulong target = BitConverter.ToUInt64(b, 8);
            names.TryGetValue(target, out var name);
            rows.Add(new(nation.Address, team, begin, end, p, target, name, unchecked((sbyte)b[0]), b[2], b[1], b[4], Convert.ToHexString(b), capacity));
        }
        return rows.AsReadOnly();
    }

    public void Set(NationPreference row, int rating, int minAge, int maxAge, int knowledge)
    {
        if (minAge > maxAge)
            throw new ArgumentOutOfRangeException(nameof(rating));
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l)) throw new InvalidOperationException("Nation preferences are not mapped");
        var before = Convert.FromHexString(row.Snapshot);
        if (before.Length != 16 || BitConverter.ToUInt64(before, 8) != row.TargetTeam
            || unchecked((sbyte)before[0]) != row.Rating || before[1] != row.MaxAge || before[2] != row.MinAge || before[4] != row.Knowledge
            || !Vtable(row.Nation, n.VtableRva) || !Vtable(row.Team, l.TeamVtable) || !Vtable(row.TargetTeam, l.TeamVtable)
            || memory.ReadUInt64(row.Nation + (ulong)l.Team) != row.Team || memory.ReadUInt64(row.Team + (ulong)l.Vector) != row.Begin
            || memory.ReadUInt64(row.Team + (ulong)l.Vector + 8) != row.End || memory.ReadUInt64(row.Team + (ulong)l.Vector + 16) != row.Capacity
            || row.Address < row.Begin || row.Address >= row.End
            || (row.Address - row.Begin) % 16 != 0 || !memory.Read(row.Address, 16).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Nation preference changed since it was read");
        byte[] next = [unchecked((byte)(sbyte)rating), (byte)maxAge, (byte)minAge, before[3], (byte)knowledge];
        using var scope = Fm27Editor.Core.History.ChangeContext.Describe(new Fm27Editor.Core.History.ChangeLabel($"Transfer Preference: {row.Target}", Kind: Fm27Editor.Core.History.ChangeValueKind.Signed, PartNames: ["Rating", "Max Age", "Min Age", "", "Knowledge"]));
        if (!memory.TryWrite(row.Address, next) || !memory.Read(row.Address, next.Length).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write nation preference");
    }

    /// <summary>
    /// Continents rows of the same transfer-preferences team: vector at team+0x108 (measured on Spain 796 and Portugal 788),
    /// 16 bytes each, the continent pointer at +8 and the same byte layout as the nations rows.
    /// </summary>
    public const int ContinentsVector = 0x108;

    public IReadOnlyList<NationPreference> ReadContinents(NationRecord nation, IReadOnlyList<ContinentRecord> continents)
    {
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l) || !Vtable(nation.Address, n.VtableRva)) return [];
        ulong team = memory.ReadUInt64(nation.Address + (ulong)l.Team);
        if (!Vtable(team, l.TeamVtable)) return [];
        ulong begin = memory.ReadUInt64(team + ContinentsVector), end = memory.ReadUInt64(team + ContinentsVector + 8),
            capacity = memory.ReadUInt64(team + ContinentsVector + 16);
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 16 != 0
            || (capacity - begin) % 16 != 0 || (end - begin) / 16 > 64) return [];
        var names = new Dictionary<ulong, string>();
        foreach (var continent in continents) names.TryAdd(continent.Address, continent.Name);
        var rows = new List<NationPreference>();
        for (ulong p = begin; p < end; p += 16)
        {
            var b = memory.Read(p, 16);
            ulong target = BitConverter.ToUInt64(b, 8);
            names.TryGetValue(target, out var name);
            rows.Add(new(nation.Address, team, begin, end, p, target, name, unchecked((sbyte)b[0]), b[2], b[1], b[4], Convert.ToHexString(b), capacity));
        }
        return rows.AsReadOnly();
    }

    public void SetContinent(NationPreference row, int rating, int minAge, int maxAge, int knowledge)
    {
        if (minAge > maxAge)
            throw new ArgumentOutOfRangeException(nameof(rating));
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l)) throw new InvalidOperationException("Nation preferences are not mapped");
        var before = Convert.FromHexString(row.Snapshot);
        if (before.Length != 16 || BitConverter.ToUInt64(before, 8) != row.TargetTeam
            || unchecked((sbyte)before[0]) != row.Rating || before[1] != row.MaxAge || before[2] != row.MinAge || before[4] != row.Knowledge
            || !Vtable(row.Nation, n.VtableRva) || !Vtable(row.Team, l.TeamVtable)
            || memory.ReadUInt64(row.Nation + (ulong)l.Team) != row.Team || memory.ReadUInt64(row.Team + ContinentsVector) != row.Begin
            || memory.ReadUInt64(row.Team + ContinentsVector + 8) != row.End || memory.ReadUInt64(row.Team + ContinentsVector + 16) != row.Capacity
            || row.Address < row.Begin || row.Address >= row.End
            || (row.Address - row.Begin) % 16 != 0 || !memory.Read(row.Address, 16).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Nation continent preference changed since it was read");
        byte[] next = [unchecked((byte)(sbyte)rating), (byte)maxAge, (byte)minAge, before[3], (byte)knowledge];
        using var scope = Fm27Editor.Core.History.ChangeContext.Describe(new Fm27Editor.Core.History.ChangeLabel($"Transfer Preference: {row.Target}", Kind: Fm27Editor.Core.History.ChangeValueKind.Signed, PartNames: ["Rating", "Max Age", "Min Age", "", "Knowledge"]));
        if (!memory.TryWrite(row.Address, next) || !memory.Read(row.Address, next.Length).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write nation continent preference");
    }

    /// <summary>
    /// The pointer a row must store for a nation: the nation's preference team (nation + Team offset), the same object the
    /// rows are matched against when reading. A nation object pointer is NOT a valid target.
    /// </summary>
    public ulong TargetFor(NationRecord nation)
    {
        if (profile.Nation is not { } n || n.Preferences is not { } l) throw new InvalidOperationException("Nation preferences are not mapped");
        if (!Vtable(nation.Address, n.VtableRva)) throw new StaleObjectException($"{nation.DisplayName} changed since it was read");
        ulong team = memory.ReadUInt64(nation.Address + (ulong)l.Team);
        if (!Vtable(team, l.TeamVtable)) throw new StaleObjectException($"{nation.DisplayName} has no preference team");
        return team;
    }

    /// <summary>
    /// Changes which nation/continent a row points at (FMRTE's target dropdown). The guard rechecks the nation, the team,
    /// the vector at <paramref name="vector"/> and the whole row against the snapshot before writing the pointer.
    /// </summary>
    public void SetTarget(NationPreference row, int vector, ulong target)
    {
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l))
            throw new InvalidOperationException("Nation preferences are not mapped");
        if (target == 0) throw new ArgumentException("target is required", nameof(target));
        var before = Convert.FromHexString(row.Snapshot);
        if (before.Length != 16 || BitConverter.ToUInt64(before, 8) != row.TargetTeam
            || !Vtable(row.Nation, n.VtableRva) || !Vtable(row.Team, l.TeamVtable)
            || memory.ReadUInt64(row.Nation + (ulong)l.Team) != row.Team
            || memory.ReadUInt64(row.Team + (ulong)vector) != row.Begin
            || memory.ReadUInt64(row.Team + (ulong)vector + 8) != row.End
            || memory.ReadUInt64(row.Team + (ulong)vector + 16) != row.Capacity
            || row.Address < row.Begin || row.Address >= row.End || (row.Address - row.Begin) % 16 != 0
            || !memory.Read(row.Address, 16).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Nation preference changed since it was read");
        var bytes = BitConverter.GetBytes(target);
        if (!memory.TryWrite(row.Address + 8, bytes) || !memory.Read(row.Address + 8, 8).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write the preference target at 0x{row.Address + 8:X}");
    }

    /// <summary>
    /// Appends a row (16 bytes: rating, max age, min age, marker, knowledge, then the target pointer) into spare capacity;
    /// when the vector is full a new buffer is allocated and the header repointed, like the other vector writers.
    /// </summary>
    public void Add(ulong team, int vector, ulong target, sbyte rating, byte minAge, byte maxAge, byte knowledge,
        Func<int, ulong>? allocate)
    {
        if (profile.Nation?.Preferences is not { } l || !Valid(l)) throw new InvalidOperationException("Nation preferences are not mapped");
        if (!Vtable(team, l.TeamVtable)) throw new StaleObjectException("The national team changed since it was read");
        if (minAge > maxAge)
            throw new ArgumentOutOfRangeException(nameof(rating));
        ulong header = team + (ulong)vector;
        ulong begin = memory.ReadUInt64(header), end = memory.ReadUInt64(header + 8), capacity = memory.ReadUInt64(header + 16);
        bool emptyVector = begin == 0 && end == 0 && capacity == 0;
        if (!emptyVector && (begin == 0 || end < begin || capacity < end || (end - begin) % 16 != 0 || (capacity - begin) % 16 != 0))
            throw new StaleObjectException("The preference vector changed since it was read");
        var record = new byte[16];
        record[0] = unchecked((byte)rating);
        record[1] = maxAge;
        record[2] = minAge;
        record[4] = knowledge;
        BitConverter.GetBytes(target).CopyTo(record, 8);
        if (emptyVector)
        {
            if (allocate is null) throw new StaleObjectException("No allocator is available to add a preference row");
            ulong fresh = allocate(32);
            if (fresh == 0) throw new MemoryAccessException("Could not allocate a preference buffer");
            if (!memory.TryWrite(fresh, record) || !memory.Read(fresh, 16).AsSpan().SequenceEqual(record))
                throw new MemoryAccessException($"Could not write the new preference at 0x{fresh:X}");
            var freshHead = new byte[24];
            BitConverter.GetBytes(fresh).CopyTo(freshHead, 0);
            BitConverter.GetBytes(fresh + 16).CopyTo(freshHead, 8);
            BitConverter.GetBytes(fresh + 32).CopyTo(freshHead, 16);
            if (!memory.TryWrite(header, freshHead) || !memory.Read(header, freshHead.Length).AsSpan().SequenceEqual(freshHead))
                throw new MemoryAccessException($"Could not point the preference vector at 0x{fresh:X}");
            return;
        }
        if (end + 16 <= capacity)
        {
            if (!memory.TryWrite(end, record) || !memory.Read(end, 16).AsSpan().SequenceEqual(record))
                throw new MemoryAccessException($"Could not write the new preference at 0x{end:X}");
            WriteHeader(header + 8, end + 16);
            return;
        }
        if (allocate is null) throw new StaleObjectException("No allocator is available to add a preference row");
        int oldBytes = (int)(end - begin), newBytes = oldBytes + 32;
        ulong buffer = allocate(newBytes);
        if (buffer == 0) throw new MemoryAccessException("Could not allocate a preference buffer");
        var copy = memory.Read(begin, oldBytes);
        if (!memory.TryWrite(buffer, copy) || !memory.Read(buffer, copy.Length).AsSpan().SequenceEqual(copy))
            throw new MemoryAccessException($"Could not copy the preferences to 0x{buffer:X}");
        if (!memory.TryWrite(buffer + (ulong)oldBytes, record) || !memory.Read(buffer + (ulong)oldBytes, 16).AsSpan().SequenceEqual(record))
            throw new MemoryAccessException($"Could not write the new preference at 0x{buffer + (ulong)oldBytes:X}");
        var head = new byte[24];
        BitConverter.GetBytes(buffer).CopyTo(head, 0);
        BitConverter.GetBytes(buffer + (ulong)oldBytes + 16).CopyTo(head, 8);
        BitConverter.GetBytes(buffer + (ulong)newBytes).CopyTo(head, 16);
        if (!memory.TryWrite(header, head) || !memory.Read(header, head.Length).AsSpan().SequenceEqual(head))
            throw new MemoryAccessException($"Could not point the preference vector at 0x{buffer:X}");
    }

    /// <summary>Removes one row: the rows after it shift left and the end shrinks by one entry.</summary>
    public void Remove(NationPreference row, int vector)
    {
        if (profile.Nation is not { } n || n.Preferences is not { } l || !Valid(l))
            throw new InvalidOperationException("Nation preferences are not mapped");
        var before = Convert.FromHexString(row.Snapshot);
        if (before.Length != 16 || !Vtable(row.Nation, n.VtableRva) || !Vtable(row.Team, l.TeamVtable)
            || memory.ReadUInt64(row.Nation + (ulong)l.Team) != row.Team
            || memory.ReadUInt64(row.Team + (ulong)vector) != row.Begin
            || memory.ReadUInt64(row.Team + (ulong)vector + 8) != row.End
            || memory.ReadUInt64(row.Team + (ulong)vector + 16) != row.Capacity
            || row.Address < row.Begin || row.Address >= row.End || (row.Address - row.Begin) % 16 != 0
            || !memory.Read(row.Address, 16).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Nation preference changed since it was read");
        ulong next = row.Address + 16;
        if (next < row.End)
        {
            var tail = memory.Read(next, (int)(row.End - next));
            if (!memory.TryWrite(row.Address, tail) || !memory.Read(row.Address, tail.Length).AsSpan().SequenceEqual(tail))
                throw new MemoryAccessException($"Could not move the preferences at 0x{row.Address:X}");
        }
        WriteHeader(row.Team + (ulong)vector + 8, row.End - 16);
    }

    void WriteHeader(ulong address, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 8).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not update the preference vector at 0x{address:X}");
    }
}
