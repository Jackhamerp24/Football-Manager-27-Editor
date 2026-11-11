using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record StaffKnowledgeRow(ulong Address, string Nation, byte Value, string Snapshot, ulong Holder, ulong Begin, ulong End);

public sealed class StaffKnowledge(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int RecordSize = 40, MaxEntries = 256;
    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("Staff profile is unavailable");
    int HolderOffset => S.KnowledgeHolder ?? throw new InvalidOperationException("Staff knowledge is unavailable for this build");
    int ValueOffset => S.KnowledgeValue is >= 8 and < RecordSize ? S.KnowledgeValue.Value : throw new InvalidOperationException("Staff knowledge value offset is unavailable or invalid");

    void RequireStaff(StaffRecord staff)
    {
        if (staff.Address != staff.StaffAddress + (ulong)staff.Kind.StaffPart ||
            memory.ReadUInt64(staff.Address) != moduleBase + staff.Kind.PersonVtableRva ||
            memory.ReadUInt64(staff.StaffAddress) != moduleBase + staff.Kind.StaffVtableRva)
            throw new StaleObjectException("Staff record changed");
    }

    public IReadOnlyList<StaffKnowledgeRow> Read(StaffRecord staff, IReadOnlyList<Nations.NationRecord>? nations = null)
    {
        RequireStaff(staff);
        ulong holder = memory.ReadUInt64(staff.StaffAddress + (ulong)HolderOffset);
        if (holder == 0) return [];
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8);
        if (begin == 0 && end == 0) return [];
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxEntries)
            throw new MemoryAccessException("Invalid staff knowledge list");
        var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Nation profile is unavailable");
        // 27.0.1+2494687: rows point at national-team containers (Colin Little's Ireland row targets a
        // NATIONAL_TEAM_CONTAINER, FMRTE-verified); older builds point at national teams directly.
        // The profile selects the model: container keys mean containers, otherwise the legacy team path.
        if (nationOffsets.ContainerVtable is { } containerVtable && nationOffsets.ContainerNation is { } containerNation)
            return ReadContainers(nationOffsets, containerVtable, containerNation, holder, begin, end, nations);
        return ReadTeams(nationOffsets, holder, begin, end, nations);
    }

    IReadOnlyList<StaffKnowledgeRow> ReadContainers(NationOffsets nationOffsets, uint containerVtable, int containerNation,
        ulong holder, ulong begin, ulong end, IReadOnlyList<Nations.NationRecord>? nations)
    {
        var allNations = (nations ?? new Nations.NationRepository(memory, profile, moduleBase).LoadAll()).ToList();
        var byAddress = allNations.ToDictionary(n => n.Address);
        var womenContainers = new HashSet<ulong>();
        if (nationOffsets.WomenTeam is { } womenOffset)
            foreach (var nation in allNations)
            {
                ulong women = memory.ReadUInt64(nation.Address + (ulong)womenOffset);
                if (women != 0 && memory.ReadUInt64(women) == moduleBase + containerVtable) womenContainers.Add(women);
            }
        string ContainerName(ulong container)
        {
            ulong nat = memory.ReadUInt64(container + (ulong)containerNation);
            if (!byAddress.TryGetValue(nat, out var nation) || memory.ReadUInt64(nat) != moduleBase + nationOffsets.VtableRva)
                throw new MemoryAccessException("Unknown staff knowledge nation");
            return womenContainers.Contains(container) ? nation.DisplayName + " (Women)" : nation.DisplayName;
        }
        var rows = new List<StaffKnowledgeRow>();
        for (ulong p = begin; p < end; p += 8)
        {
            ulong record = memory.ReadUInt64(p);
            byte[] data = memory.Read(record, RecordSize);
            ulong container = BitConverter.ToUInt64(data);
            if (memory.ReadUInt64(container) != moduleBase + containerVtable)
                throw new MemoryAccessException("Unknown staff knowledge national team");
            rows.Add(new(record, ContainerName(container), data[ValueOffset], Convert.ToHexString(data), holder, begin, end));
        }
        return rows;
    }

    /// <summary>Legacy model (builds before 27.0.1): rows point at national teams, named through Rivalries.</summary>
    IReadOnlyList<StaffKnowledgeRow> ReadTeams(NationOffsets nationOffsets,
        ulong holder, ulong begin, ulong end, IReadOnlyList<Nations.NationRecord>? nations)
    {
        var nationTeam = nationOffsets.Rivalries ?? throw new InvalidOperationException("Nation team profile is unavailable");
        var names = new Dictionary<ulong, string>();
        foreach (var nation in nations ?? new Nations.NationRepository(memory, profile, moduleBase).LoadAll())
        {
            ulong team = memory.ReadUInt64(nation.Address + (ulong)nationTeam.Team);
            if (team != 0 && memory.ReadUInt64(team) == moduleBase + nationTeam.TeamVtable)
                names.TryAdd(team, nation.DisplayName);
            // Staff knowledge can name the women's team (nation + 0x110). Measured on Steam 27.0.0+2493426:
            // all 440 unmatched rows pointed here, so they are real data, not a broken record.
            if (profile.Nation?.WomenTeam is { } womenOffset)
            {
                ulong women = memory.ReadUInt64(nation.Address + (ulong)womenOffset);
                if (women != 0 && women != team && memory.ReadUInt64(women) == moduleBase + nationTeam.TeamVtable)
                    names.TryAdd(women, nation.DisplayName + " (Women)");
            }
        }
        var rows = new List<StaffKnowledgeRow>();
        for (ulong p = begin; p < end; p += 8)
        {
            ulong record = memory.ReadUInt64(p);
            byte[] data = memory.Read(record, RecordSize);
            ulong team = BitConverter.ToUInt64(data);
            if (!names.TryGetValue(team, out var name)) throw new MemoryAccessException("Unknown staff knowledge national team");
            rows.Add(new(record, name, data[ValueOffset], Convert.ToHexString(data), holder, begin, end));
        }
        return rows;
    }

    public void SetValue(StaffRecord staff, StaffKnowledgeRow row, int value)
    {
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Knowledge is 0–100");
        RequireStaff(staff);
        byte[] before = Convert.FromHexString(row.Snapshot);
        var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Nation profile is unavailable");
        ulong targetVtable = nationOffsets.ContainerVtable is { } cv ? moduleBase + cv
            : nationOffsets.Rivalries is { } rt ? moduleBase + rt.TeamVtable
            : throw new InvalidOperationException("Nation team profile is unavailable");
        if (before.Length != RecordSize || before[ValueOffset] != row.Value ||
            memory.ReadUInt64(BitConverter.ToUInt64(before)) != targetVtable ||
            memory.ReadUInt64(staff.StaffAddress + (ulong)HolderOffset) != row.Holder ||
            memory.ReadUInt64(row.Holder) != row.Begin || memory.ReadUInt64(row.Holder + 8) != row.End ||
            row.Begin == 0 || row.End < row.Begin || (row.End - row.Begin) % 8 != 0 || (row.End - row.Begin) / 8 > MaxEntries ||
            !Enumerable.Range(0, (int)((row.End - row.Begin) / 8)).Any(i => memory.ReadUInt64(row.Begin + (ulong)i * 8) == row.Address) ||
            !memory.Read(row.Address, RecordSize).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Staff knowledge changed since it was read");
        ulong address = row.Address + (ulong)ValueOffset;
        if (!memory.TryWrite(address, [(byte)value]) || memory.Read(address, 1)[0] != value)
            throw new MemoryAccessException("Could not write staff knowledge");
    }

    /// <summary>Every national team or container (the objects knowledge rows point at) by nation name: the choices for Add.</summary>
    public IReadOnlyList<(ulong Team, string Name)> Catalogue(IReadOnlyList<Nations.NationRecord> nations)
    {
        var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Nation profile is unavailable");
        if (nationOffsets.ContainerVtable is { } cv)
        {
            if (nationOffsets.Teams is not { } t)
                throw new InvalidOperationException("Nation container profile is unavailable");
            var result = new List<(ulong, string)>();
            foreach (var nation in nations)
            {
                ulong senior = memory.ReadUInt64(nation.Address + (ulong)t.Pointer);
                if (senior != 0 && memory.ReadUInt64(senior) == moduleBase + cv) result.Add((senior, nation.DisplayName));
                if (nationOffsets.WomenTeam is { } womenOffset)
                {
                    ulong women = memory.ReadUInt64(nation.Address + (ulong)womenOffset);
                    if (women != 0 && women != senior && memory.ReadUInt64(women) == moduleBase + cv)
                        result.Add((women, nation.DisplayName + " (Women)"));
                }
            }
            return result.DistinctBy(x => x.Item1).OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        }
        var nationTeam = nationOffsets.Rivalries ?? throw new InvalidOperationException("Nation team profile is unavailable");
        return nations.Select(n => (Team: memory.ReadUInt64(n.Address + (ulong)nationTeam.Team), Name: n.DisplayName))
            .Where(t => t.Team != 0 && memory.ReadUInt64(t.Team) == moduleBase + nationTeam.TeamVtable)
            .DistinctBy(t => t.Team).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds a nation like FMRTE 27's + (Simeone → Portugal, 2/11/2026): a copy of the first row's 40-byte record with the
    /// national team swapped in (FMRTE copies the selected row, which keeps the +0x20 bytes the game wrote) and the value
    /// set; its pointer is appended, and a full vector moves to a new buffer. Both live outside the game's allocator.
    /// </summary>
    public void Add(StaffRecord staff, IReadOnlyList<StaffKnowledgeRow> current, ulong team, int value, Func<int, ulong> allocate)
    {
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Knowledge is 0–100");
        if (current.Count == 0) throw new InvalidOperationException("No knowledge row to copy");
        var (holder, begin, end, cap) = Require(staff, current);
        var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Nation profile is unavailable");
        ulong targetVtable = nationOffsets.ContainerVtable is { } cv ? moduleBase + cv
            : nationOffsets.Rivalries is { } rt ? moduleBase + rt.TeamVtable
            : throw new InvalidOperationException("Nation team profile is unavailable");
        if (memory.ReadUInt64(team) != targetVtable) throw new ArgumentException("Not a national team", nameof(team));
        if (current.Any(r => BitConverter.ToUInt64(Convert.FromHexString(r.Snapshot)) == team)) throw new InvalidOperationException("That nation is already listed");
        var record = Convert.FromHexString(current[0].Snapshot);
        BitConverter.GetBytes(team).CopyTo(record, 0);
        record[ValueOffset] = (byte)value;
        ulong at = allocate(RecordSize);
        Write(at, record);
        if (end < cap)
        {
            Write(end, BitConverter.GetBytes(at));
            Write(holder + 8, BitConverter.GetBytes(end + 8));
            return;
        }
        int count = current.Count, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        Write(buffer, [.. current.SelectMany(r => BitConverter.GetBytes(r.Address)), .. BitConverter.GetBytes(at)]);
        Write(holder, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)(count + 1) * 8), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    /// <summary>Removes a row like FMRTE 27's −: later pointers move up and the end pointer moves back 8; the record stays.</summary>
    public void Remove(StaffRecord staff, IReadOnlyList<StaffKnowledgeRow> current, StaffKnowledgeRow row)
    {
        var (holder, begin, end, _) = Require(staff, current);
        int index = current.ToList().FindIndex(r => r.Address == row.Address);
        if (index < 0) throw new StaleObjectException("The row is no longer in the list");
        if (index < current.Count - 1) Write(begin + (ulong)index * 8, current.Skip(index + 1).SelectMany(r => BitConverter.GetBytes(r.Address)).ToArray());
        Write(holder + 8, BitConverter.GetBytes(end - 8));
    }

    /// <summary>The vector must hold exactly the rows read, in order, each record unchanged.</summary>
    (ulong holder, ulong begin, ulong end, ulong cap) Require(StaffRecord staff, IReadOnlyList<StaffKnowledgeRow> current)
    {
        RequireStaff(staff);
        ulong holder = memory.ReadUInt64(staff.StaffAddress + (ulong)HolderOffset);
        if (holder == 0) throw new InvalidOperationException("The staff member has no knowledge list");
        var h = memory.Read(holder, 24);
        ulong begin = BitConverter.ToUInt64(h, 0), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        if (end < begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 != (ulong)current.Count
            || current.Any(r => r.Holder != holder || r.Begin != begin || r.End != end)
            || current.Select((r, i) => memory.ReadUInt64(begin + (ulong)i * 8) != r.Address || !memory.Read(r.Address, RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(r.Snapshot))).Any(bad => bad))
            throw new StaleObjectException("Staff knowledge changed since it was read");
        return (holder, begin, end, cap);
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
