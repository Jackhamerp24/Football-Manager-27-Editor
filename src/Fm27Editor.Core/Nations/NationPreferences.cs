using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

public sealed record NationPreference(ulong Nation, ulong Team, ulong Begin, ulong End, ulong Address,
    ulong TargetTeam, string Target, sbyte Rating, byte MinAge, byte MaxAge, byte Knowledge, string Snapshot, ulong Capacity);

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
            if (names.TryGetValue(target, out var name))
                rows.Add(new(nation.Address, team, begin, end, p, target, name, unchecked((sbyte)b[0]), b[2], b[1], b[4], Convert.ToHexString(b), capacity));
        }
        return rows.AsReadOnly();
    }

    public void Set(NationPreference row, int rating, int minAge, int maxAge, int knowledge)
    {
        if (rating is < -128 or > 127 || minAge is < 0 or > 255 || maxAge is < 0 or > 255 || minAge > maxAge || knowledge is < 0 or > 255)
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
        if (!memory.TryWrite(row.Address, next) || !memory.Read(row.Address, next.Length).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write nation preference");
    }
}
