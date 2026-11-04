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
        ulong root = memory.ReadUInt64(moduleBase + rva);
        ulong begin = memory.ReadUInt64(root + (ulong)off), end = memory.ReadUInt64(root + (ulong)off + 8);
        if (root == 0 || begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxNations)
            throw new MemoryAccessException($"Nation list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        var raw = memory.Read(begin, (int)(end - begin));
        var reader = new PlayerReader(memory, profile) { ModuleBase = moduleBase };
        var result = new List<NationRecord>();
        for (int i = 0; i < raw.Length; i += 8)
        {
            ulong p = BitConverter.ToUInt64(raw, i);
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
