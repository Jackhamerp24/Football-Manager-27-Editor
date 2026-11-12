using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Profile <c>youthCandidates</c>: the manager holding the pre-intake candidate records and the field offsets
/// inside one candidate record. See docs/HANDOVER-YOUTH-INTAKE.md for what was measured and where.
/// </summary>
public sealed record YouthCandidateOffsets(
    uint ManagerVtable,
    int CandidateMap,
    int NodeVector,
    int RecordSize,
    int PlayingDataBase,
    int CurrentAbility,
    int PotentialAbility,
    int Height,
    int BirthDate,
    int BasedNation,
    int Team,
    int Club,
    int OtherClub,
    int Nationality,
    int CityOfBirth,
    int FirstName,
    int LastName,
    int FutureId)
{
    static readonly string[] Keys =
    [
        "managerVtable", "candidateMap", "nodeVector", "recordSize", "playingDataBase",
        "currentAbility", "potentialAbility", "height", "birthDate",
        "basedNation", "team", "club", "otherClub", "nationality", "cityOfBirth",
        "firstName", "lastName", "futureId",
    ];

    internal static YouthCandidateOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"youthCandidates.{key}: expected hex"), 16)).ToArray();
        return new YouthCandidateOffsets((uint)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5],
            (int)v[6], (int)v[7], (int)v[8], (int)v[9], (int)v[10], (int)v[11], (int)v[12], (int)v[13],
            (int)v[14], (int)v[15], (int)v[16], (int)v[17])
        {
            ManagerRootRva = e.TryGetProperty("managerRootRva", out var root) && root.GetString() is { } r && r.StartsWith("0x")
                ? Convert.ToUInt32(r[2..], 16) : null,
        };
    }

    /// <summary>
    /// Module slot holding the manager pointer: the game stores the new manager there right after building it
    /// (Steam 27.0.1: <c>mov [rip+…], rsi</c> at RVA 0xA62C7D, slot RVA 0x5813050). Null: find the manager by scanning.
    /// </summary>
    public uint? ManagerRootRva { get; init; }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [ManagerVtable, CandidateMap, NodeVector, RecordSize, PlayingDataBase, CurrentAbility,
            PotentialAbility, Height, BirthDate, BasedNation, Team, Club, OtherClub, Nationality, CityOfBirth,
            FirstName, LastName, FutureId];
        w.WriteStartObject("youthCandidates");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        if (ManagerRootRva is { } rootRva) w.WriteString("managerRootRva", $"0x{rootRva:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// One youth candidate before intake day: a snapshot of the game's candidate record, with an id-less identity
/// (address, name and birth date). The future player id the game holds is unverified, so it is not exposed.
/// </summary>
public sealed class YouthCandidate
{
    public required ulong Address { get; init; }
    public required ulong ManagerAddress { get; init; }
    public required ulong RecordVtable { get; init; }
    public required ulong ClubAddress { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public string? NationalityName { get; init; }
    public ulong NationalityAddress { get; init; }
    public string? CityOfBirthName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public DateOnly? GameDate { get; set; }
    public required short CurrentAbility { get; init; }
    public required short PotentialAbility { get; init; }
    public required short Height { get; init; }

    /// <summary>Raw bytes on the game's 1-100 scale, indexed by <see cref="PlayerAttribute"/>.</summary>
    public required byte[] RawAttributes { get; init; }
    public required int AttributeScale { get; init; }
    /// <summary>Indexed by <see cref="PlayerPosition"/>, 1-20.</summary>
    public required byte[] Positions { get; init; }

    public int? Age => GameDate is { } d && DateOfBirth is { } born ? PlayerAge(born, d) : null;

    public byte Raw(PlayerAttribute a) => RawAttributes[(int)a];
    public int Attribute(PlayerAttribute a) => AttributeScaling.ToDisplay(RawAttributes[(int)a], AttributeScale);
    public int Position(PlayerPosition p) => Positions[(int)p];
    public string PositionSummary => PlayerPositionSummary.Format(Positions);

    static int PlayerAge(DateOnly born, DateOnly on)
    {
        int age = on.Year - born.Year;
        return on < born.AddYears(age) ? age - 1 : age;
    }
}

/// <summary>
/// Youth candidates: one manager object (found by scanning writable memory for its vtable, once per session)
/// holds the candidate map; each node carries a vector of fixed-size candidate records. Records are filtered
/// by the club pointer, so only that club's candidates are decoded.
/// </summary>
public sealed class YouthCandidateReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    // MSVC std::map tree node: left, parent, right, then the isnil flag; the key follows at +0x20.
    const int Left = 0, Parent = 8, Right = 0x10, IsNil = 0x19, Key = 0x20;
    const int MaxNameLength = 256, MaxNodes = 100_000, MaxRecords = 10_000;

    ulong? _manager;

    public bool HasOffsets => profile.YouthCandidates is not null;

    /// <summary>True once the one-off manager scan has run.</summary>
    public bool HasCachedManager => _manager is not null;

    /// <summary>The manager object, scanned once per session. Exactly one hit is expected.</summary>
    public ulong FindManager()
    {
        if (_manager is { } known) return known;
        var o = profile.YouthCandidates ?? throw new InvalidOperationException("Profile has no youthCandidates offsets");
        if (o.ManagerRootRva is { } rva)
        {
            var slot = new byte[8];
            if (memory.TryRead(moduleBase + rva, slot) && BitConverter.ToUInt64(slot) is var fromRoot && fromRoot != 0)
            {
                var vt = new byte[8];
                if (memory.TryRead(fromRoot, vt) && BitConverter.ToUInt64(vt) == moduleBase + o.ManagerVtable && LooksLikeManager(fromRoot, o))
                    return (_manager = fromRoot).Value;
            }
        }
        var found = new List<ulong>();
        if (memory is WindowsProcessMemory windows)
        {
            ulong want = moduleBase + o.ManagerVtable;
            var buffer = new byte[1 << 22];
            foreach (var region in windows.EnumerateWritableRegions())
            {
                if (region.IsModuleData) continue;
                for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)buffer.Length)
                {
                    int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
                    if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
                    for (int i = 0; i + 8 <= length; i += 8)
                    {
                        if (BitConverter.ToUInt64(buffer, i) != want) continue;
                        ulong candidate = region.Start + offset + (ulong)i;
                        // Stop at the first object that is really the manager (its candidate map has a sentinel head):
                        // the manager is made when the game starts, so it sits early in memory and the scan ends soon,
                        // instead of reading the whole heap to prove it is the only one.
                        if (LooksLikeManager(candidate, o)) { found.Add(candidate); break; }
                    }
                    if (found.Count > 0) break;
                }
                if (found.Count > 0) break;
            }
        }
        if (found.Count == 0) throw new InvalidOperationException("The youth candidate manager was not found; the save may be outside the candidate window");
        if (found.Count > 1) throw new InvalidOperationException("The youth candidate manager is not unique");
        return (_manager = found[0]).Value;
    }

    /// <summary>The candidate map's head node is an MSVC sentinel: its isnil byte is 1.</summary>
    bool LooksLikeManager(ulong address, YouthCandidateOffsets o)
    {
        var q = new byte[8];
        if (!memory.TryRead(address + (ulong)o.CandidateMap, q)) return false;
        ulong head = BitConverter.ToUInt64(q);
        var nil = new byte[1];
        return head != 0 && memory.TryRead(head + IsNil, nil) && nil[0] == 1;
    }

    /// <summary>Every candidate of every club in the manager.</summary>
    public IReadOnlyList<YouthCandidate> ReadAll() => Read(FindManager(), null);

    /// <summary>The club's own candidates.</summary>
    public IReadOnlyList<YouthCandidate> ReadForClub(ulong clubAddress) => Read(FindManager(), clubAddress);

    /// <summary>Every candidate in the manager at <paramref name="managerAddress"/> (tests and session tools).</summary>
    public IReadOnlyList<YouthCandidate> ReadAll(ulong managerAddress) => Read(managerAddress, null);

    /// <summary>The club's candidates in the manager at <paramref name="managerAddress"/> (tests and session tools).</summary>
    public IReadOnlyList<YouthCandidate> ReadForClub(ulong managerAddress, ulong clubAddress) => Read(managerAddress, clubAddress);

    /// <summary>Whether the record still sits in one of the manager's candidate vectors.</summary>
    internal bool ContainsAddress(ulong managerAddress, ulong recordAddress)
    {
        var o = profile.YouthCandidates ?? throw new InvalidOperationException("Profile has no youthCandidates offsets");
        if (!TryQ(managerAddress, out ulong vt) || vt != moduleBase + o.ManagerVtable) return false;
        foreach (var (begin, end) in Vectors(managerAddress, o))
            if (recordAddress >= begin && recordAddress + (ulong)o.RecordSize <= end
                && (recordAddress - begin) % (ulong)o.RecordSize == 0)
                return true;
        return false;
    }

    IReadOnlyList<YouthCandidate> Read(ulong managerAddress, ulong? clubAddress)
    {
        var o = profile.YouthCandidates ?? throw new InvalidOperationException("Profile has no youthCandidates offsets");
        if (!TryQ(managerAddress, out ulong vt) || vt != moduleBase + o.ManagerVtable)
            throw new StaleObjectException($"0x{managerAddress:X} is no longer the youth candidate manager");
        CheckDerived(o);
        var result = new List<YouthCandidate>();
        var record = new byte[o.RecordSize];
        foreach (var (begin, end) in Vectors(managerAddress, o))
        {
            for (ulong at = begin; at + (ulong)o.RecordSize <= end; at += (ulong)o.RecordSize)
            {
                if (!memory.TryRead(at, record)) continue;
                // The club pointer decides before any name is decoded, so other clubs cost one pointer read.
                ulong club = U64(record, o.Club);
                if (clubAddress is { } want && club != want) continue;
                if (Decode(managerAddress, at, record, club, o) is { } candidate) result.Add(candidate);
            }
        }
        return result;
    }

    /// <summary>Every (begin, end) candidate vector in the manager's candidate map.</summary>
    IEnumerable<(ulong Begin, ulong End)> Vectors(ulong managerAddress, YouthCandidateOffsets o)
    {
        if (!TryQ(managerAddress + (ulong)o.CandidateMap, out ulong head) || head == 0) yield break;
        if (!TryQ(head + Parent, out ulong root) || root == 0 || root == head) yield break;
        // In-order walk from the root; the sentinel head itself is never a candidate node.
        var stack = new Stack<ulong>();
        var flag = new byte[1];
        ulong node = root;
        int seen = 0;
        while ((stack.Count > 0 || node != 0) && seen <= MaxNodes)
        {
            while (node != 0 && seen <= MaxNodes)
            {
                if (!TryQ(node + Left, out ulong left)) break;
                stack.Push(node);
                node = left;
                seen++;
            }
            if (stack.Count == 0) yield break;
            node = stack.Pop();
            if (memory.TryRead(node + IsNil, flag) && flag[0] == 0)
            {
                if (TryQ(node + (ulong)o.NodeVector, out ulong begin) && TryQ(node + (ulong)o.NodeVector + 8, out ulong end)
                    && end >= begin && (end - begin) / (ulong)o.RecordSize <= MaxRecords)
                    yield return (begin, end);
            }
            if (!TryQ(node + Right, out ulong right)) yield break;
            node = right;
        }
    }

    YouthCandidate? Decode(ulong managerAddress, ulong address, byte[] record, ulong club, YouthCandidateOffsets o)
    {
        string? first = NameAt(U64(record, o.FirstName));
        string? last = NameAt(U64(record, o.LastName));
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)) return null;
        var positions = new byte[Enum.GetValues<PlayerPosition>().Length];
        foreach (var (p, off) in profile.Positions) positions[(int)p] = record[off - o.PlayingDataBase];
        var attributes = new byte[Enum.GetValues<PlayerAttribute>().Length];
        foreach (var (a, off) in profile.Attributes) attributes[(int)a] = record[off - o.PlayingDataBase];
        ulong nation = U64(record, o.Nationality);
        return new YouthCandidate
        {
            Address = address,
            ManagerAddress = managerAddress,
            RecordVtable = U64(record, 0),
            ClubAddress = club,
            FirstName = first ?? "",
            LastName = last ?? "",
            NationalityAddress = nation,
            NationalityName = NationName(nation),
            CityOfBirthName = CityName(U64(record, o.CityOfBirth)),
            DateOfBirth = Date(I16(record, o.BirthDate), I16(record, o.BirthDate + 2)),
            CurrentAbility = I16(record, o.CurrentAbility),
            PotentialAbility = I16(record, o.PotentialAbility),
            Height = I16(record, o.Height),
            RawAttributes = attributes,
            AttributeScale = profile.AttributeScale,
            Positions = positions,
        };
    }

    /// <summary>The name entry points at a length-prefixed string directly; a player-style indirection is the fallback.</summary>
    string? NameAt(ulong entry)
    {
        if (entry == 0) return null;
        return StringAt(entry) ?? (TryQ(entry, out ulong inner) && inner != entry ? StringAt(inner) : null);
    }

    string? NationName(ulong address)
    {
        if (address == 0 || profile.Nation is not { } nat) return null;
        int size = Math.Max(0x38, Math.Max(nat.Name, nat.ShortName) + 8);
        var b = new byte[size];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + nat.VtableRva) return null;
        return StringAt(BitConverter.ToUInt64(b, nat.Name)) ?? StringAt(BitConverter.ToUInt64(b, nat.ShortName));
    }

    string? CityName(ulong address)
    {
        if (address == 0 || profile.Cities is not { } cities) return null;
        int size = Math.Max(0x40, cities.Name + 8);
        var b = new byte[size];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + cities.Vtable) return null;
        return StringAt(BitConverter.ToUInt64(b, cities.Name));
    }

    string? StringAt(ulong p)
    {
        if (p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxNameLength) return null;
        if (n == 0) return "";
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    static DateOnly? Date(short dayOfYear, short year)
    {
        if (year < 1800 || year > 2300 || dayOfYear < 1 || dayOfYear > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(dayOfYear - 1);
        return d.Year == year ? d : null;
    }

    /// <summary>Every position and attribute the profile maps must land inside the candidate record.</summary>
    void CheckDerived(YouthCandidateOffsets o)
    {
        foreach (var (_, off) in profile.Positions)
            if (off - o.PlayingDataBase < 0 || off - o.PlayingDataBase >= o.RecordSize)
                throw new InvalidOperationException("A profile position offset falls outside the candidate record");
        foreach (var (_, off) in profile.Attributes)
            if (off - o.PlayingDataBase < 0 || off - o.PlayingDataBase >= o.RecordSize)
                throw new InvalidOperationException("A profile attribute offset falls outside the candidate record");
    }

    static ulong U64(byte[] b, int off) => BitConverter.ToUInt64(b, off);
    static short I16(byte[] b, int off) => BitConverter.ToInt16(b, off);

    bool TryQ(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>
/// Writes youth candidate fields. Every write re-checks that the manager is still the manager, that the record
/// still sits in one of its vectors with the same vtable and club, and that the field still holds the snapshot
/// value, then reads back. Any mismatch throws and writes nothing further.
/// </summary>
public sealed class YouthCandidateWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetAttribute(YouthCandidate candidate, PlayerAttribute attribute, int display)
    {
        RequireRange(display, 1, 20, attribute.ToString());
        var o = Offsets();
        WriteGuarded(candidate, profile.Attributes[attribute] - o.PlayingDataBase,
            [candidate.Raw(attribute)], [AttributeScaling.ToRaw(display, profile.AttributeScale)]);
    }

    public void SetPosition(YouthCandidate candidate, PlayerPosition position, int value)
    {
        RequireRange(value, 1, 20, position.ToString());
        var o = Offsets();
        WriteGuarded(candidate, profile.Positions[position] - o.PlayingDataBase,
            [candidate.Positions[(int)position]], [(byte)value]);
    }

    public void SetCurrentAbility(YouthCandidate candidate, int value)
    {
        RequireRange(value, 1, 200, "Current ability");
        var o = Offsets();
        WriteGuarded(candidate, o.CurrentAbility, Int16(candidate.CurrentAbility), Int16((short)value));
    }

    public void SetPotentialAbility(YouthCandidate candidate, int value)
    {
        RequireRange(value, 1, 200, "Potential ability");
        var o = Offsets();
        WriteGuarded(candidate, o.PotentialAbility, Int16(candidate.PotentialAbility), Int16((short)value));
    }

    /// <summary>Height in centimetres.</summary>
    public void SetHeight(YouthCandidate candidate, int centimetres)
    {
        RequireRange(centimetres, 100, 250, "Height");
        var o = Offsets();
        WriteGuarded(candidate, o.Height, Int16(candidate.Height), Int16((short)centimetres));
    }

    YouthCandidateOffsets Offsets() =>
        profile.YouthCandidates ?? throw new InvalidOperationException("Profile has no youthCandidates offsets");

    static byte[] Int16(short v) => BitConverter.GetBytes(v);

    static void RequireRange(int value, int min, int max, string field)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(field, value, $"{field} must be {min}-{max}");
    }

    void WriteGuarded(YouthCandidate candidate, int offset, byte[] expected, byte[] value)
    {
        var o = Offsets();
        var reader = new YouthCandidateReader(memory, profile, moduleBase);
        ulong manager = candidate.ManagerAddress;
        if (manager == 0 || !reader.ContainsAddress(manager, candidate.Address))
            throw new StaleObjectException($"0x{candidate.Address:X} is no longer a youth candidate");
        Span<byte> head = stackalloc byte[8];
        if (!memory.TryRead(candidate.Address, head) || BitConverter.ToUInt64(head) != candidate.RecordVtable)
            throw new StaleObjectException($"0x{candidate.Address:X} changed since it was read");
        Span<byte> club = stackalloc byte[8];
        if (!memory.TryRead(candidate.Address + (ulong)o.Club, club) || BitConverter.ToUInt64(club) != candidate.ClubAddress)
            throw new StaleObjectException($"The candidate at 0x{candidate.Address:X} changed club since it was read");
        ulong at = candidate.Address + (ulong)offset;
        var current = new byte[expected.Length];
        if (!memory.TryRead(at, current) || !current.AsSpan().SequenceEqual(expected))
            throw new StaleObjectException($"Value at 0x{at:X} changed since it was read");
        if (!memory.TryWrite(at, value))
            throw new MemoryAccessException($"Write at 0x{at:X} failed");
        if (!memory.Read(at, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Write at 0x{at:X} did not stick");
    }
}
