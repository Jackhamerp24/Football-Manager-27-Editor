using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

public sealed record NationRivalry(ulong Nation, ulong Team, ulong Begin, ulong End, ulong Address,
    ulong OpponentTeam, string Opponent, byte Value, string Snapshot, ulong Capacity);

/// <summary>Snapshots of confirmed rival-nation relationships on men's national teams.</summary>
public sealed class NationRivalries(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxEntries = 1024;
    NationRivalryOffsets? Layout => profile.Nation?.Rivalries;

    bool Vtable(ulong address, uint rva) => address != 0 && memory.TryRead(address, new byte[8])
        && memory.ReadUInt64(address) == moduleBase + rva;

    bool ValidLayout(NationRivalryOffsets l) => l.EntrySize is >= 16 and <= 256 && l.Team >= 0 && l.Vector >= 0
        && l.Type >= 8 && l.Type + 2 <= l.EntrySize && l.Value >= 8 && l.Value < l.EntrySize;

    public IReadOnlyList<NationRivalry> Read(NationRecord nation, IReadOnlyList<NationRecord> nations)
    {
        if (profile.Nation is not { } n || Layout is not { } l || !ValidLayout(l)
            || !Vtable(nation.Address, n.VtableRva)) return [];
        ulong team = memory.ReadUInt64(nation.Address + (ulong)l.Team);
        if (!Vtable(team, l.TeamVtable)) return [];
        ulong begin = memory.ReadUInt64(team + (ulong)l.Vector), end = memory.ReadUInt64(team + (ulong)l.Vector + 8),
            capacity = memory.ReadUInt64(team + (ulong)l.Vector + 16);
        if (begin == 0 || end < begin || capacity < end || (end - begin) % (ulong)l.EntrySize != 0
            || (capacity - begin) % (ulong)l.EntrySize != 0
            || (end - begin) / (ulong)l.EntrySize > MaxEntries) return [];
        var known = new Dictionary<ulong, string>();
        foreach (var other in nations)
            if (Vtable(other.Address, n.VtableRva) && memory.TryRead(other.Address + (ulong)l.Team, new byte[8]))
            {
                ulong otherTeam = memory.ReadUInt64(other.Address + (ulong)l.Team);
                if (Vtable(otherTeam, l.TeamVtable)) known.TryAdd(otherTeam, other.DisplayName);
            }
        var result = new List<NationRivalry>();
        for (ulong p = begin; p < end; p += (ulong)l.EntrySize)
        {
            var bytes = new byte[l.EntrySize];
            if (!memory.TryRead(p, bytes)) throw new MemoryAccessException("Cannot read nation relationships");
            ulong target = BitConverter.ToUInt64(bytes);
            if (BitConverter.ToUInt16(bytes, l.Type) == l.RivalType && known.TryGetValue(target, out string? name))
                result.Add(new(nation.Address, team, begin, end, p, target, name, bytes[l.Value], Convert.ToHexString(bytes), capacity));
        }
        return result.AsReadOnly();
    }

    public void SetValue(NationRivalry row, int value)
    {
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        if (profile.Nation is not { } n || Layout is not { } l || !ValidLayout(l))
            throw new InvalidOperationException("Nation rivalries are not mapped");
        var before = Convert.FromHexString(row.Snapshot);
        if (before.Length != l.EntrySize || BitConverter.ToUInt64(before) != row.OpponentTeam
            || BitConverter.ToUInt16(before, l.Type) != l.RivalType || before[l.Value] != row.Value
            || !Vtable(row.Nation, n.VtableRva) || !Vtable(row.Team, l.TeamVtable)
            || !Vtable(row.OpponentTeam, l.TeamVtable)
            || memory.ReadUInt64(row.Nation + (ulong)l.Team) != row.Team
            || memory.ReadUInt64(row.Team + (ulong)l.Vector) != row.Begin
            || memory.ReadUInt64(row.Team + (ulong)l.Vector + 8) != row.End
            || memory.ReadUInt64(row.Team + (ulong)l.Vector + 16) != row.Capacity
            || row.Address < row.Begin || row.Address >= row.End || (row.Address - row.Begin) % (ulong)l.EntrySize != 0
            || !memory.Read(row.Address, l.EntrySize).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Nation rivalry changed since it was read");
        ulong at = row.Address + (ulong)l.Value;
        byte[] next = [(byte)value];
        if (!memory.TryWrite(at, next) || !memory.Read(at, 1).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write nation rivalry");
    }
}
