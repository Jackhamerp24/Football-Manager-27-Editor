using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

/// <summary>One staff member as read. Attributes are display values 1–20, indexed by <see cref="StaffAttribute"/>.</summary>
public sealed record StaffRecord
{
    /// <summary>The person part (the person-vector entry).</summary>
    public required ulong Address { get; init; }
    public required ulong StaffAddress { get; init; }
    public int Id { get; init; }
    public string? FullName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CommonName { get; init; }
    public string DisplayName => !string.IsNullOrEmpty(CommonName) ? CommonName : $"{FirstName} {LastName}".Trim();
    public DateOnly? DateOfBirth { get; init; }
    public Gender Gender { get; init; }
    public NationInfo? Nationality { get; init; }
    public ClubInfo? Club { get; init; }
    /// <summary>Team name when its classification is independently identified; otherwise null.</summary>
    public string? TeamName { get; init; }
    /// <summary>Raw job code from the job record (FMRTE Man Utd: 2 Coach, 4 Chairperson, 6 Director, 10 Director of
    /// Football, 16 Manager, 20 Assistant Manager, 26 Fitness Coach, 34 GK Coach, 60 Performance Analyst, 64 Head of
    /// Youth Development); null without a job.</summary>
    public short? JobCode { get; init; }
    public short CurrentAbility { get; init; }
    public short PotentialAbility { get; init; }
    public short HomeReputation { get; init; }
    public short CurrentReputation { get; init; }
    public short WorldReputation { get; init; }
    public required byte[] Attributes { get; init; }
    public required byte[] Personality { get; init; }
    public required StaffKind Kind { get; init; }
    /// <summary>Human managers only (1–100, indexed by <see cref="ManagerAttribute"/>); null for other staff.</summary>
    public byte[]? ManagerAttributes { get; init; }

    public int Attribute(StaffAttribute a) => Attributes[(int)a];
    /// <summary>Null when this build or staff kind has no measured offset.</summary>
    public short? Caps { get; init; }
    public short? Goals { get; init; }
    public StaffLicense? License { get; init; }
    /// <summary>Address of the 15 role ratings and their values, in <see cref="StaffRole"/> order; null when unknown.</summary>
    public ulong RolesAddress { get; init; }
    public byte[]? Roles { get; init; }
}

public sealed class StaffRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxPersons = 2_000_000, PersonReadSize = 0xB0, StaffReadSize = 0xE0, JobReadSize = 0x28;

    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("Profile has no staff offsets");

    public IReadOnlyList<StaffRecord> LoadAll()
    {
        var s = S;
        ulong root = s.DirectStaffVector ? moduleBase + s.PersonListRootRva : memory.ReadUInt64(moduleBase + s.PersonListRootRva);
        ulong begin = memory.ReadUInt64(root + (ulong)s.PersonListOffset), end = memory.ReadUInt64(root + (ulong)s.PersonListOffset + 8);
        if (root == 0 || begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxPersons)
            throw new MemoryAccessException($"Person list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        var raw = memory.Read(begin, (int)(end - begin));
        var reader = new PlayerReader(memory, profile) { ModuleBase = moduleBase };
        var result = new List<StaffRecord>();
        Span<byte> vtable = stackalloc byte[8];
        for (int i = 0; i < raw.Length; i += 8)
        {
            ulong person = BitConverter.ToUInt64(raw, i);
            if (s.DirectStaffVector)
            {
                if (person == 0 || !memory.TryRead(person, vtable)) continue;
                ulong vt = BitConverter.ToUInt64(vtable);
                var kind = s.Kinds.FirstOrDefault(k => moduleBase + k.StaffVtableRva == vt);
                if (kind is null) continue;
                person += (ulong)kind.StaffPart;
            }
            if (Decode(person, reader) is { } r) result.Add(r);
        }
        return result;
    }

    /// <summary>Re-reads one staff member by person-part address; null when it is no longer a staff object.</summary>
    public StaffRecord? Reload(ulong address) => Decode(address, new PlayerReader(memory, profile) { ModuleBase = moduleBase });

    StaffRecord? Decode(ulong person, PlayerReader reader)
    {
        var s = S;
        if (person == 0) return null;
        var p = new byte[PersonReadSize];
        if (!memory.TryRead(person, p)) return null;
        ulong vt = BitConverter.ToUInt64(p);
        if (s.Kinds.FirstOrDefault(k => moduleBase + k.PersonVtableRva == vt) is not { } kind) return null;
        ulong staff = person - (ulong)kind.StaffPart;
        var st = new byte[kind.Manager ? kind.StaffPart : StaffReadSize];
        if (!memory.TryRead(staff, st) || BitConverter.ToUInt64(st) != moduleBase + kind.StaffVtableRva) return null;
        byte[]? manager = kind.Manager
            ? Enum.GetValues<ManagerAttribute>().Select(a => s.ManagerAttributes.TryGetValue(a, out int o) && o < st.Length ? st[o] : (byte)0).ToArray()
            : null;

        var attrs = new byte[Enum.GetValues<StaffAttribute>().Length];
        foreach (var (a, (off, scaled)) in s.Attributes)
        {
            byte v = st[off];
            attrs[(int)a] = scaled ? (byte)Math.Round(v / (double)profile.AttributeScale, MidpointRounding.AwayFromZero) : v;
        }
        short? jobCode = null;
        ClubInfo? club = null;
        string? teamName = null;
        ulong job = BitConverter.ToUInt64(p, s.Job);
        var jb = new byte[JobReadSize];
        if (job != 0 && memory.TryRead(job, jb) && BitConverter.ToUInt64(jb, s.JobPerson) == person)
        {
            jobCode = BitConverter.ToInt16(jb, s.JobCode);
            if (profile.Contract is { } c)
            {
                ulong team = BitConverter.ToUInt64(jb, s.JobTeam);
                club = reader.ClubOfTeam(team, c);
                teamName = ReadTeamName(team, club);
            }
        }
        int day = BitConverter.ToUInt16(p, s.DateOfBirth) & 0x1FF, year = BitConverter.ToInt16(p, s.DateOfBirth + 2);
        var details = s.PersonDetails is { } d && moduleBase + d.PersonVtableRva == vt ? d : null;
        // Caps, goals, licence and the roles pointer sit past the main person read; read them only for the measured kind.
        int extraEnd = details is null ? 0 : new[] { details.Caps + 2, details.Goals + 2, details.License + 1, details.Roles + 8 }.Max();
        var extra = new byte[extraEnd];
        if (details is not null && !memory.TryRead(person, extra)) details = null;
        ulong rolesAddress = details is null ? 0 : BitConverter.ToUInt64(extra, details.Roles);
        byte[]? roles = null;
        if (rolesAddress != 0)
        {
            var r = new byte[Enum.GetValues<StaffRole>().Length];
            if (memory.TryRead(rolesAddress, r) && r.All(b => b is >= 1 and <= 20)) roles = r; else rolesAddress = 0;
        }
        return new StaffRecord
        {
            Address = person,
            StaffAddress = staff,
            Id = BitConverter.ToInt32(p, s.Id),
            FullName = reader.StringAt(BitConverter.ToUInt64(p, s.FullName)),
            FirstName = reader.EntryString(BitConverter.ToUInt64(p, s.FirstName)),
            LastName = reader.EntryString(BitConverter.ToUInt64(p, s.LastName)),
            CommonName = reader.EntryString(BitConverter.ToUInt64(p, s.CommonName)),
            DateOfBirth = year > 1900 && year < 2300 && day >= 1 && day <= (DateTime.IsLeapYear(year) ? 366 : 365)
                ? new DateOnly(year, 1, 1).AddDays(day - 1) : null,
            Gender = GenderReader.FromPersonBytes(p, profile.Relations),
            Nationality = profile.Nation is { } n ? reader.ReadNation(BitConverter.ToUInt64(p, s.Nation), n) : null,
            Club = club,
            TeamName = teamName,
            JobCode = jobCode,
            HomeReputation = BitConverter.ToInt16(st, s.HomeReputation),
            CurrentReputation = BitConverter.ToInt16(st, s.CurrentReputation),
            WorldReputation = BitConverter.ToInt16(st, s.WorldReputation),
            CurrentAbility = BitConverter.ToInt16(st, s.CurrentAbility),
            PotentialAbility = BitConverter.ToInt16(st, s.PotentialAbility),
            Attributes = attrs,
            Personality = p[s.Personality..(s.Personality + 8)],
            Kind = kind,
            ManagerAttributes = manager,
            Caps = details is null ? null : BitConverter.ToInt16(extra, details.Caps),
            Goals = details is null ? null : BitConverter.ToInt16(extra, details.Goals),
            License = details is null ? null : (StaffLicense)(sbyte)extra[details.License],
            RolesAddress = rolesAddress,
            Roles = roles,
        };
    }

    string? ReadTeamName(ulong team, ClubInfo? club)
    {
        if (team == 0 || club is null || string.IsNullOrEmpty(club.DisplayName) || profile.Team is not { } t ||
            t.Classification is not { } typeOffset || t.AgeLimit is not { } ageOffset ||
            typeOffset < 0 || typeOffset > 0x3C || ageOffset < 0 || ageOffset >= 0x40) return null;
        var bytes = new byte[0x40];
        if (!memory.TryRead(team, bytes) || BitConverter.ToUInt64(bytes) != moduleBase + t.VtableRva) return null;
        int type = BitConverter.ToInt32(bytes, typeOffset);
        byte age = bytes[ageOffset];
        return (type, age) switch
        {
            (0, 100) => club.DisplayName,
            (0x10A, 21) => $"{club.DisplayName} U21",
            (0x10C, 18) => $"{club.DisplayName} U18",
            _ => null,
        };
    }
}

/// <summary>
/// Guarded staff writes: the person part and the staff part must still carry their vtables, the field must still hold
/// the snapshot value, and the write is read back.
/// </summary>
public sealed class StaffWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("Profile has no staff offsets");

    /// <summary>Read the exact stored byte for reversible batch edits, checking identity and the decoded snapshot.</summary>
    public byte ReadAttributeRaw(StaffRecord staff, StaffAttribute attribute)
    {
        RequireStaff(staff);
        var (offset, scaled) = S.Attributes[attribute];
        byte raw = memory.Read(staff.StaffAddress + (ulong)offset, 1)[0];
        int scale = scaled ? profile.AttributeScale : 1;
        if (scale <= 0 || raw < 1 || raw > 20 * scale ||
            (scaled ? (int)Math.Round(raw / (double)scale, MidpointRounding.AwayFromZero) : raw) != staff.Attribute(attribute))
            throw new StaleObjectException($"{attribute} changed or has an invalid stored value");
        return raw;
    }

    public byte EncodeAttribute(StaffAttribute attribute, int value)
    {
        if (value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value));
        int scale = S.Attributes[attribute].Scaled ? profile.AttributeScale : 1;
        if (scale <= 0 || 20 * scale > byte.MaxValue) throw new InvalidOperationException("Invalid staff attribute scale");
        return checked((byte)(value * scale));
    }

    /// <summary>Write an exact raw byte using the captured raw snapshot and normal identity guard.</summary>
    public void SetAttributeRaw(StaffRecord staff, StaffAttribute attribute, byte expected, byte value)
    {
        var (offset, scaled) = S.Attributes[attribute];
        int scale = scaled ? profile.AttributeScale : 1;
        if (scale <= 0 || value < 1 || value > 20 * scale) throw new ArgumentOutOfRangeException(nameof(value));
        Write(staff, staff.StaffAddress + (ulong)offset, [expected], [value]);
    }

    public void SetAttribute(StaffRecord staff, StaffAttribute attribute, int value)
    {
        if (value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), "Staff attributes are 1–20");
        var (off, scaled) = S.Attributes[attribute];
        int k = scaled ? profile.AttributeScale : 1;
        byte old = memory.Read(staff.StaffAddress + (ulong)off, 1)[0];
        if ((scaled ? (int)Math.Round(old / (double)k, MidpointRounding.AwayFromZero) : old) != staff.Attribute(attribute))
            throw new StaleObjectException($"{attribute} changed since it was read");
        Write(staff, staff.StaffAddress + (ulong)off, [old], [(byte)(value * k)]);
    }

    public void SetPersonality(StaffRecord staff, StaffPersonality trait, int value)
    {
        if (value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), "Personality values are 1–20");
        Write(staff, staff.Address + (ulong)(S.Personality + (int)trait), [staff.Personality[(int)trait]], [(byte)value]);
    }

    public void SetAbility(StaffRecord staff, int currentAbility, int potentialAbility)
    {
        if (currentAbility is < 1 or > 200 || potentialAbility is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(currentAbility), "CA and PA are 1–200");
        Write(staff, staff.StaffAddress + (ulong)S.CurrentAbility,
            [.. BitConverter.GetBytes(staff.CurrentAbility), .. BitConverter.GetBytes(staff.PotentialAbility)],
            [.. BitConverter.GetBytes((short)currentAbility), .. BitConverter.GetBytes((short)potentialAbility)]);
    }

    public void SetReputation(StaffRecord staff, int home, int current, int world)
    {
        if (new[] { home, current, world }.Any(r => r is < 0 or > 10000)) throw new ArgumentOutOfRangeException(nameof(home), "Reputation is 0–10000");
        Write(staff, staff.StaffAddress + (ulong)S.HomeReputation,
            [.. BitConverter.GetBytes(staff.HomeReputation), .. BitConverter.GetBytes(staff.CurrentReputation), .. BitConverter.GetBytes(staff.WorldReputation)],
            [.. BitConverter.GetBytes((short)home), .. BitConverter.GetBytes((short)current), .. BitConverter.GetBytes((short)world)]);
    }

    /// <summary>Human managers only; the value is stored as is (1–100).</summary>
    public void SetManagerAttribute(StaffRecord staff, ManagerAttribute attribute, int value)
    {
        if (staff.ManagerAttributes is not { } current || !S.ManagerAttributes.TryGetValue(attribute, out int off))
            throw new InvalidOperationException($"{staff.DisplayName} is not a human manager");
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Manager attributes are 1–100");
        Write(staff, staff.StaffAddress + (ulong)off, [current[(int)attribute]], [(byte)value]);
    }

    StaffPersonDetails Details(StaffRecord staff) =>
        S.PersonDetails is { } d && staff.Caps is not null ? d : throw new InvalidOperationException("Caps, goals and licence are not measured for this staff kind on this build");

    public void SetCaps(StaffRecord staff, int value)
    {
        if (value is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        Write(staff, staff.Address + (ulong)Details(staff).Caps, BitConverter.GetBytes(staff.Caps!.Value), BitConverter.GetBytes((short)value));
    }

    public void SetGoals(StaffRecord staff, int value)
    {
        if (value is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        Write(staff, staff.Address + (ulong)Details(staff).Goals, BitConverter.GetBytes(staff.Goals!.Value), BitConverter.GetBytes((short)value));
    }

    public void SetLicense(StaffRecord staff, StaffLicense value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Write(staff, staff.Address + (ulong)Details(staff).License, [(byte)(sbyte)staff.License!.Value], [(byte)(sbyte)value]);
    }

    public void SetRole(StaffRecord staff, StaffRole role, int value)
    {
        if (value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value));
        if (staff.Roles is not { } roles || staff.RolesAddress == 0) throw new InvalidOperationException("Role ratings are not readable for this staff member");
        Details(staff);
        if (memory.ReadUInt64(staff.Address + (ulong)S.PersonDetails!.Roles) != staff.RolesAddress)
            throw new StaleObjectException("Role ratings moved since they were read");
        Write(staff, staff.RolesAddress + (ulong)(int)role, [roles[(int)role]], [(byte)value]);
    }

    /// <summary>Date of birth, day of year in the low 9 bits (the bits above are kept) and the year after it.</summary>
    public void SetDateOfBirth(StaffRecord staff, DateOnly date)
    {
        if (date.Year is <= 1900 or >= 2300) throw new ArgumentOutOfRangeException(nameof(date));
        ulong at = staff.Address + (ulong)S.DateOfBirth;
        var before = memory.Read(at, 4);
        ushort word = (ushort)((BitConverter.ToUInt16(before) & ~0x1FF) | date.DayOfYear);
        byte[] after = [.. BitConverter.GetBytes(word), .. BitConverter.GetBytes((short)date.Year)];
        if (staff.DateOfBirth is not { } known || (BitConverter.ToUInt16(before) & 0x1FF) != known.DayOfYear || BitConverter.ToInt16(before, 2) != known.Year)
            throw new StaleObjectException("Date of birth changed since it was read");
        Write(staff, at, before, after);
    }

    /// <summary>Points the staff member at another nation object (the same pointer the editor reads the nationality from).</summary>
    public void SetNationality(StaffRecord staff, ulong nation)
    {
        if (nation == 0) throw new ArgumentOutOfRangeException(nameof(nation));
        var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");
        if (memory.ReadUInt64(nation) != moduleBase + nationOffsets.VtableRva) throw new StaleObjectException("The nation object changed since it was read");
        ulong at = staff.Address + (ulong)S.Nation;
        var before = memory.Read(at, 8);
        if (staff.Nationality is { } n && BitConverter.ToUInt64(before) != n.Address)
            throw new StaleObjectException("Nationality changed since it was read");
        Write(staff, at, before, BitConverter.GetBytes(nation));
    }

    void Write(StaffRecord staff, ulong address, byte[] expected, byte[] value)
    {
        RequireStaff(staff);
        if (!memory.Read(address, expected.Length).AsSpan().SequenceEqual(expected))
            throw new StaleObjectException($"Value at 0x{address:X} changed since it was read");
        if (!memory.TryWrite(address, value) || !memory.Read(address, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Write at 0x{address:X} failed");
    }

    void RequireStaff(StaffRecord staff)
    {
        var k = staff.Kind;
        if (memory.ReadUInt64(staff.Address) != moduleBase + k.PersonVtableRva || memory.ReadUInt64(staff.StaffAddress) != moduleBase + k.StaffVtableRva)
            throw new StaleObjectException($"0x{staff.Address:X} is no longer a staff object");
        if (staff.Id > 0 && (staff.Address != staff.StaffAddress + (ulong)k.StaffPart ||
            BitConverter.ToInt32(memory.Read(staff.Address + (ulong)S.Id, 4)) != staff.Id))
            throw new StaleObjectException("Staff identity changed since it was read");
    }
}
