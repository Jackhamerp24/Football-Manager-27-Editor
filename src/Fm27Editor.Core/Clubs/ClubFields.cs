using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Object a club field path starts from: the club object, or one of its teams (reputation, tactical understanding).</summary>
public enum ClubFieldRoot { Club, Team }

public enum ClubFieldType { Byte, SByte, Int16, Int32, Bool, Date, Colour, Float }

/// <summary>
/// Every single-value field of the club screens (FMRTE 27's Information, Finances, Supporters, Tactical Understanding
/// tabs). A field is usable once the profile maps it; see docs/club-plan.md for the measured values.
/// </summary>
public enum ClubField
{
    // Information / Overview
    YearFounded, Status, ChairmanStatus, Morale,
    AverageAttendance, MinimumAttendance, MaximumAttendance, SeasonTicketHolders, SocialMediaFollowers,
    BackgroundColour, ForegroundColour, AllowCustomLogo, OwnershipType, ChairpersonTitle,
    // Information / Facilities
    Training, YouthCoaching, YouthFacilities, YouthRecruitment, YouthImportance, CorporateFacilities,
    // Finances / Overview
    Balance, TransferBudgetSeason, TransferBudgetRemaining, TransferRevenuePercent, WageBudget, WageUsed, MaxWage,
    RichBenefactor, AverageTicketPrice, StadiumRentalPerYear,
    // Finances / Transfer embargo
    EmbargoReason, EmbargoStart, EmbargoEnd, EmbargoAppealDate, EmbargoAgeRestriction,
    // Supporters
    SupportersHardcore, SupportersCore, SupportersFamily, SupportersFairWeather, SupportersCorporate, SupportersCasual,
    SupporterLoyalty, SupporterPassion, SupporterPatience, SupporterAffluence, SupporterTemperament, SupporterExpectations,
    // Team (root Team): Information / Teams, Tactical Understanding
    TeamReputation, AllowLicensedKits,
    PassingStyle, BuildUp, ChanceCreation, LineOfEngagement, PressingIntensity, PressingTrap, PositionRole,
}

/// <summary>
/// Where a field lives: from the root object, every offset but the last is a pointer to follow; the last is the
/// field. Display value = raw / Scale. Dates are int16 day of year + int16 year, given as yyyymmdd (0 = none).
/// </summary>
/// <param name="ObjectVtables">When set, the object that holds the field (after the last pointer) must have one of these
/// vtable RVAs. Small clubs have a 0x38-byte finance object where the full one is 0x8B0, so budget offsets would land in
/// the next club's object.</param>
/// <remarks>A non-zero <c>Mask</c> makes a byte field one bit: it reads 0/1 and writes keep the other bits (see the three-argument Encode).</remarks>
public sealed record ClubFieldOffset(ClubFieldRoot Root, IReadOnlyList<int> Path, ClubFieldType Type, int Scale = 1, IReadOnlyList<uint>? ObjectVtables = null, int Mask = 0)
{
    public string PathText => string.Join("->", Path.Select(p => $"0x{p:X}"));

    public static IReadOnlyList<int> ParsePath(string text) =>
        text.Split("->").Select(p => p.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(p.Trim()[2..], 16)
            : throw new FormatException($"Expected hex offsets like 0x100->0xD8, got '{text}'")).ToArray();
}

/// <summary>Reads and writes mapped club fields. Writes check the root vtable, re-walk the path, compare with the caller's snapshot and read back.</summary>
public sealed class ClubFieldAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    IReadOnlyDictionary<ClubField, ClubFieldOffset> Map => profile.Club?.Fields ?? new Dictionary<ClubField, ClubFieldOffset>();

    public bool IsMapped(ClubField field) => Map.ContainsKey(field);

    /// <summary>Mapped fields of the club and of <paramref name="team"/> (usually its first team). Unreadable fields are left out.</summary>
    public IReadOnlyDictionary<ClubField, long> ReadAll(ulong club, ulong team)
    {
        var values = new Dictionary<ClubField, long>();
        foreach (var (field, offset) in Map)
            if (Read(offset.Root == ClubFieldRoot.Club ? club : team, field) is { } v) values[field] = v;
        return values;
    }

    public long? Read(ulong root, ClubField field)
    {
        if (!Map.TryGetValue(field, out var o) || !RootOk(root, o.Root) || Resolve(root, o) is not { } at) return null;
        var b = new byte[Size(o.Type)];
        return memory.TryRead(at, b) ? Decode(b, o) : null;
    }

    public void Set(ulong root, ClubField field, long expected, long value)
    {
        var o = Map.TryGetValue(field, out var x) ? x : throw new InvalidOperationException($"{field} is not mapped in this profile");
        if (!RootOk(root, o.Root)) throw new StaleObjectException($"0x{root:X} is no longer a {o.Root.ToString().ToLowerInvariant()} object");
        var bytes = Encode(value, o);
        ulong at = Resolve(root, o) ?? throw new StaleObjectException($"{field}: this club does not store it, or pointer path {o.PathText} is broken");
        var current = memory.Read(at, bytes.Length);
        if (Decode(current, o) != expected) throw new StaleObjectException($"{field} changed since it was read");
        if (!memory.TryWrite(at, bytes) || !memory.Read(at, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Write of {field} at 0x{at:X} failed");
    }

    /// <summary>Address the field is stored at for <paramref name="root"/> (a club or team object), null when it is not mapped or the path is broken.</summary>
    public ulong? AddressOf(ulong root, ClubField field) =>
        Map.TryGetValue(field, out var o) && RootOk(root, o.Root) ? Resolve(root, o) : null;

    bool RootOk(ulong root, ClubFieldRoot kind)
    {
        uint? rva = kind == ClubFieldRoot.Club ? profile.Club?.ClubVtableRva : profile.Team?.VtableRva;
        Span<byte> b = stackalloc byte[8];
        return root != 0 && rva is { } r && memory.TryRead(root, b) && BitConverter.ToUInt64(b) == moduleBase + r;
    }

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
        if (o.ObjectVtables is { Count: > 0 } allowed)
        {
            if (!memory.TryRead(at, b)) return null;
            ulong vt = BitConverter.ToUInt64(b);
            if (!allowed.Any(r => vt == moduleBase + r)) return null;
        }
        return at + (ulong)o.Path[^1];
    }

    internal static int Size(ClubFieldType t) => t switch
    {
        ClubFieldType.Byte or ClubFieldType.SByte or ClubFieldType.Bool => 1,
        ClubFieldType.Int16 => 2,
        _ => 4,
    };

    internal static long Decode(byte[] b, ClubFieldOffset o)
    {
        if (o.Mask != 0) return (b[0] & o.Mask) != 0 ? 1 : 0;
        long raw = o.Type switch
        {
            ClubFieldType.Byte or ClubFieldType.Bool => b[0],
            ClubFieldType.SByte => (sbyte)b[0],
            ClubFieldType.Int16 => BitConverter.ToInt16(b),
            ClubFieldType.Date => DateValue(BitConverter.ToInt16(b), BitConverter.ToInt16(b, 2)),
            ClubFieldType.Colour => BitConverter.ToUInt32(b),
            ClubFieldType.Float => (long)Math.Round(BitConverter.ToSingle(b)),
            _ => BitConverter.ToInt32(b),
        };
        return o.Type is ClubFieldType.Date or ClubFieldType.Colour or ClubFieldType.Bool ? raw : raw / o.Scale;
    }

    /// <summary>Like <see cref="Encode(long, ClubFieldOffset)"/>; a masked field sets or clears its bit in <paramref name="current"/>.</summary>
    internal static byte[] Encode(long value, ClubFieldOffset o, byte[] current)
    {
        if (o.Mask == 0) return Encode(value, o);
        if (value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value), value, "A flag is 0 or 1");
        return [(byte)(value == 1 ? current[0] | o.Mask : current[0] & ~o.Mask)];
    }

    internal static byte[] Encode(long value, ClubFieldOffset o)
    {
        if (o.Mask != 0) throw new InvalidOperationException("A masked field needs the current byte to encode");
        long raw = o.Type is ClubFieldType.Date or ClubFieldType.Colour or ClubFieldType.Bool ? value : value * o.Scale;
        void Range(long min, long max) { if (raw < min || raw > max) throw new ArgumentOutOfRangeException(nameof(value), value, $"Out of range for {o.Type}"); }
        switch (o.Type)
        {
            case ClubFieldType.Bool: Range(0, 1); return [(byte)raw];
            case ClubFieldType.Byte: Range(0, 255); return [(byte)raw];
            case ClubFieldType.SByte: Range(-128, 127); return [(byte)(sbyte)raw];
            case ClubFieldType.Int16: Range(short.MinValue, short.MaxValue); return BitConverter.GetBytes((short)raw);
            case ClubFieldType.Colour: Range(0, uint.MaxValue); return BitConverter.GetBytes((uint)raw);
            case ClubFieldType.Float: Range(-16_777_216, 16_777_216); return BitConverter.GetBytes((float)raw);
            case ClubFieldType.Date:
                var d = raw == 0 ? new DateOnly(1900, 1, 1) : new DateOnly((int)(raw / 10000), (int)(raw / 100 % 100), (int)(raw % 100));
                return [.. BitConverter.GetBytes((short)d.DayOfYear), .. BitConverter.GetBytes((short)d.Year)];
            default: Range(int.MinValue, int.MaxValue); return BitConverter.GetBytes((int)raw);
        }
    }

    /// <summary>yyyymmdd; 0 for 1/1/1900 (the game's "no date") or anything unreadable.</summary>
    static long DateValue(short dayOfYear, short year)
    {
        if (year <= 1900 || year > 2300 || dayOfYear < 1 || dayOfYear > 366) return 0;
        var d = new DateOnly(year, 1, 1).AddDays(dayOfYear - 1);
        return d.Year == year ? d.Year * 10000 + d.Month * 100 + d.Day : 0;
    }
}
