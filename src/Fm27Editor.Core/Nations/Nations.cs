using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// Nation values (FMRTE 27's Information and National Teams tabs), mapped in profile <c>nation.fields</c> with paths from
/// the nation object. National-team values sit in the men's national-team object at nation + 0x108, which is laid out
/// like a club (attendances behind its +0xB0 pointer, as on clubs). Measured on Vietnam (145) and England (765).
/// </summary>
public enum NationField
{
    EconomicFactor, YearsToGainNationality, Development, GainNationalityType, DoesNotAllowDualNationality,
    FifaRankingPoints, FifaPosition, YearAffiliatedToFifa, YearAffiliatedToFederation, YearAssociationFormed, YouthRating,
    FaFinancialPower, MaxYouthAge, FaPatience, ForeignManagerLikelihood, AllowMultipleAssistantManagers,
    /// <summary>Men's team importance, 1 Very Important / 2 Important / 3 Unimportant / 4 No One Cares (FMRTE 27 showed it after writing 4 on Spain). Stored twice:
    /// FMRTE 26 writes the same code to FM26 +0x874 and +0x875; on FM27 +0x87C and +0x87D are equal on all 251 nations.</summary>
    Importance,
    AverageAttendance, MinimumAttendance, MaximumAttendance,
    // Women's national team: same layout at nation + 0x110 (England: FIFA points 2027, Youth Rating 161, attendance 60000/45000/90000).
    WomenFifaRankingPoints, WomenFifaPosition, WomenYearAffiliatedToFifa, WomenYearAffiliatedToFederation, WomenYearAssociationFormed,
    WomenYouthRating, WomenFaFinancialPower, WomenMaxYouthAge, WomenFaPatience, WomenForeignManagerLikelihood,
    WomenAverageAttendance, WomenMinimumAttendance, WomenMaximumAttendance,
}

public sealed record NationRecord(ulong Address, int? Id, string? Name, string? ShortName)
{
    public string DisplayName => Name ?? ShortName ?? "";
}

public sealed class NationRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxNations = 4096;
    NationOffsets N => profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");

    /// <summary>Every nation in the game's nation vector, in its order.</summary>
    public IReadOnlyList<NationRecord> LoadAll()
    {
        var n = N;
        if (n.ListRootRva is not { } rva || n.ListOffset is not { } off) throw new InvalidOperationException("Profile has no nation list");
        try
        {
            ulong root = memory.ReadUInt64(moduleBase + rva);
            ulong begin = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)off), end = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)off + 8);
            if (begin != 0 && end >= begin && (end - begin) % 8 == 0 && (end - begin) / 8 <= MaxNations)
            {
                var raw = memory.Read(begin, (int)(end - begin));
                var pointers = new ulong[raw.Length / 8];
                Buffer.BlockCopy(raw, 0, pointers, 0, raw.Length);
                var list = ReadPointers(pointers);
                if (list.Count > 0) return list;
            }
        }
        catch (MemoryAccessException) { }
        if (FindNearbyNationList(rva) is { Count: > 0 } discovered) return discovered;
        return LoadFromReferences();
    }

    // Some saves move the database holder between adjacent static slots and change its vector offsets.
    IReadOnlyList<NationRecord>? FindNearbyNationList(uint configuredRva)
    {
        IReadOnlyList<NationRecord>? best = null;
        var expectedVtable = moduleBase + N.VtableRva;
        var holderBytes = new byte[0x810];
        Span<byte> pointer = stackalloc byte[8];
        for (long delta = -0x20; delta <= 0x20; delta += 8)
        {
            if ((long)configuredRva + delta < 0) continue;
            if (!memory.TryRead(moduleBase + (ulong)((long)configuredRva + delta), pointer)) continue;
            ulong holder = BitConverter.ToUInt64(pointer);
            if (holder == 0 || !memory.TryRead(holder, holderBytes)) continue;
            for (int off = 0; off + 24 <= holderBytes.Length; off += 8)
            {
                ulong begin = BitConverter.ToUInt64(holderBytes, off), end = BitConverter.ToUInt64(holderBytes, off + 8), cap = BitConverter.ToUInt64(holderBytes, off + 16);
                if (begin == 0 || end <= begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxNations) continue;
                int count = (int)((end - begin) / 8);
                if (best is not null && count <= best.Count) continue;
                var raw = new byte[count * 8];
                if (!memory.TryRead(begin, raw)) continue;
                bool plausible = true;
                for (int i = 0; i < Math.Min(count, 3); i++)
                {
                    ulong obj = BitConverter.ToUInt64(raw, i * 8);
                    if (obj == 0 || !memory.TryRead(obj, pointer) || BitConverter.ToUInt64(pointer) != expectedVtable) { plausible = false; break; }
                }
                if (!plausible) continue;
                var pointers = new ulong[count];
                Buffer.BlockCopy(raw, 0, pointers, 0, raw.Length);
                var rows = ReadPointers(pointers);
                if (rows.Count == count) best = rows;
            }
        }
        return best;
    }

    // FM Studio collects nation objects from players, clubs and competitions when building its nation list.
    IReadOnlyList<NationRecord> LoadFromReferences()
    {
        var players = new PlayerRepository(memory, profile, moduleBase).LoadAll();
        var pointers = players.Where(p => p.Nationality is not null).Select(p => p.Nationality!.Address).ToHashSet();
        foreach (var club in ClubDirectory.FromPlayers(memory, players, profile, moduleBase))
            for (int offset = 0xD8; offset <= 0xF8; offset += 8)
                AddPointer(club.Address + (ulong)offset);
        if (profile.Competition is not null)
        {
            try
            {
                foreach (var competition in new Fm27Editor.Core.Competitions.CompetitionRepository(memory, profile, moduleBase).LoadAll())
                    AddPointer(competition.Address + 0x60);
            }
            catch (MemoryAccessException) { }
        }
        var result = ReadPointers(pointers);
        if (result.Count == 0) throw new MemoryAccessException("Could not find any nations from player, club or competition references");
        return result;

        void AddPointer(ulong address)
        {
            Span<byte> bytes = stackalloc byte[8];
            if (memory.TryRead(address, bytes)) pointers.Add(BitConverter.ToUInt64(bytes));
        }
    }

    IReadOnlyList<NationRecord> ReadPointers(IEnumerable<ulong> pointers)
    {
        var n = N;
        var reader = new PlayerReader(memory, profile) { ModuleBase = moduleBase };
        var result = new List<NationRecord>();
        foreach (ulong p in pointers.Distinct())
        {
            if (reader.ReadNation(p, n) is not { } info) continue;
            int? id = n.Id is { } io && memory.Read(p + (ulong)io, 4) is var b ? BitConverter.ToInt32(b) : null;
            result.Add(new NationRecord(p, id, info.Name, info.ShortName));
        }
        return result;
    }
}

/// <summary>Reads and writes mapped nation fields; writes check the nation vtable and the caller's snapshot, then read back.</summary>
public sealed class NationFieldAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    IReadOnlyDictionary<NationField, ClubFieldOffset> Map => profile.Nation?.Fields ?? new Dictionary<NationField, ClubFieldOffset>();

    public IReadOnlyDictionary<NationField, long> ReadAll(ulong nation)
    {
        var values = new Dictionary<NationField, long>();
        foreach (var field in Map.Keys)
            if (Read(nation, field) is { } v) values[field] = v;
        return values;
    }

    public long? Read(ulong nation, NationField field)
    {
        if (!Map.TryGetValue(field, out var o) || !IsNation(nation) || Resolve(nation, o) is not { } at) return null;
        var b = new byte[ClubFieldAccess.Size(o.Type)];
        if (!memory.TryRead(at, b)) return null;
        return field == NationField.DoesNotAllowDualNationality ? (b[0] & 0x02) != 0 ? 1 : 0 : ClubFieldAccess.Decode(b, o);
    }

    public void Set(ulong nation, NationField field, long expected, long value)
    {
        var o = Map.TryGetValue(field, out var x) ? x : throw new InvalidOperationException($"{field} is not mapped in this profile");
        if (!IsNation(nation)) throw new StaleObjectException($"0x{nation:X} is no longer a nation object");
        if (field == NationField.DoesNotAllowDualNationality && value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value));
        var bytes = ClubFieldAccess.Encode(value, o);
        ulong at = Resolve(nation, o) ?? throw new StaleObjectException($"{field}: pointer path {o.PathText} is broken");
        var current = memory.Read(at, bytes.Length);
        long actual = field == NationField.DoesNotAllowDualNationality ? (current[0] & 0x02) != 0 ? 1 : 0 : ClubFieldAccess.Decode(current, o);
        if (actual != expected) throw new StaleObjectException($"{field} changed since it was read");
        if (field == NationField.DoesNotAllowDualNationality) bytes[0] = (byte)((current[0] & ~0x02) | (value == 1 ? 0x02 : 0));
        if (field == NationField.Importance)
        {
            // The copy right after the code must match too; both are written together, as FMRTE 26 does on FM26.
            if (value is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(value), "Importance is 1–4");
            if (memory.Read(at + 1, 1)[0] != current[0]) throw new StaleObjectException("Importance copies differ; not written");
            bytes = [(byte)value, (byte)value];
        }
        if (!memory.TryWrite(at, bytes) || !memory.Read(at, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Write of {field} at 0x{at:X} failed");
    }

    bool IsNation(ulong address) =>
        address != 0 && profile.Nation is { } n && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + n.VtableRva;

    ulong? Resolve(ulong root, ClubFieldOffset o)
    {
        ulong at = root;
        Span<byte> b = stackalloc byte[8];
        for (int i = 0; i < o.Path.Count - 1; i++)
        {
            if (!memory.TryRead(at + (ulong)o.Path[i], b)) return null;
            at = BitConverter.ToUInt64(b);
            if (at == 0) return null;
        }
        return at + (ulong)o.Path[^1];
    }
}
