using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Player → relations holder (player + Pointer); the holder starts with a vector of inline 16-byte records (target
/// pointer, category, type, code). Measured by lane D against FMRTE 27's Relationships page on Raphael (Hibernian) and
/// Oliver Arblaster (Sheffield United): type 25 competition registration, 72 formed at club, 70 formed at country.
/// Type codes are written in hex like every other profile value.
/// </summary>
public sealed record RelationOffsets(int Pointer, int RecordSize, int Target, int Type, int Code,
    int RegistrationType, int TrainedAtClubType, int TrainedAtNationType,
    int Reason, int Flags, int LikesPersonType, int DislikesPersonType, int LikesTeamType, uint PersonVtable, uint StaffPersonVtable, int PersonName, int PersonFirstName, int PersonLastName, int PersonCommonName,
    int TargetKind, int PlayerPerson, int RetiredInternationalType, int OtherNationalityType)
{
    static readonly string[] Keys = ["pointer", "recordSize", "target", "type", "code", "registrationType", "trainedAtClubType", "trainedAtNationType",
        "reason", "flags", "likesPersonType", "dislikesPersonType", "likesTeamType", "personVtable", "staffPersonVtable", "personName", "personFirstName", "personLastName", "personCommonName", "targetKind", "playerPerson", "retiredInternationalType", "otherNationalityType"];

    internal static RelationOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.relations.{key}: expected hex"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], (uint)v[13], (uint)v[14], v[15], v[16], v[17], v[18], v[19], v[20], v[21], v[22]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Pointer, RecordSize, Target, Type, Code, RegistrationType, TrainedAtClubType, TrainedAtNationType,
            Reason, Flags, LikesPersonType, DislikesPersonType, LikesTeamType, PersonVtable, StaffPersonVtable, PersonName, PersonFirstName, PersonLastName, PersonCommonName, TargetKind, PlayerPerson, RetiredInternationalType, OtherNationalityType];
        w.WriteStartObject("relations");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>Competition registration. <see cref="Kind"/>: 0 UEFA, 8 Domestic (other codes unknown, kept raw).</summary>
public sealed record PlayerRegistration(ClubInfo? Club, byte Kind);

/// <summary>Formed at club. <see cref="Period"/>: 1 = "3 Years (15-21)", 10 = "Done" (others unknown, kept raw).</summary>
public sealed record PlayerTrainedAtClub(ClubInfo? Club, byte Period);

/// <summary>Formed at country; only period 1 ("3 Years (15-21)") has been seen.</summary>
public sealed record PlayerTrainedAtNation(NationInfo? Nation, byte Period);

/// <summary><see cref="RetiredFromInternationalFootball"/> is the Information tab's "Retired From Int. Foot." box, not a Relationships row.</summary>
public enum RelationshipKind { LikesPerson, DislikesPerson, LikesTeam, RetiredFromInternationalFootball, OtherNationality }

/// <summary>
/// One Likes Person / Dislikes Person / Likes Team row of FMRTE 27's Relationships tab. <see cref="Reason"/> is the raw code
/// (people: 2 Sibling, 5 Teammate, 6 Manager, 9 Idol; teams: 1 Supporter, 2 Player, as FMRTE showed for Mbappé).
/// <see cref="Flags"/> is 0 when FMRTE leaves Permanent unticked (Neymar) and 0x4F when ticked (Ethan Mbappé).
/// </summary>
public sealed record PlayerRelationship(ulong Address, RelationshipKind Kind, string? Name, int Value, int Reason, int Flags,
    string Snapshot, ulong Holder, ulong Begin, ulong End)
{
    public ulong TargetAddress { get; init; }
}

public sealed record PlayerStatuses(
    IReadOnlyList<PlayerRegistration> Registrations,
    IReadOnlyList<PlayerTrainedAtClub> TrainedAtClubs,
    IReadOnlyList<PlayerTrainedAtNation> TrainedAtNations)
{
    public static readonly PlayerStatuses None = new([], [], []);
    public IReadOnlyList<PlayerRelationship> Relationships { get; init; } = [];
    /// <summary>The relations vector as read (holder, begin, end), for guarded adds; 0 when there is none.</summary>
    public ulong Holder { get; init; }
    public ulong Begin { get; init; }
    public ulong End { get; init; }
    public ulong Capacity { get; init; }
    public string RecordSnapshot { get; init; } = "";
    /// <summary>True only when the relation vector and every other-nationality target were read successfully.</summary>
    public bool NationalitiesComplete { get; init; }
    public bool TrainingComplete { get; init; }
    public bool RelationshipsComplete { get; init; }
    /// <summary>The type-0xA record behind "Retired From Int. Foot.", or null when the box is unticked.</summary>
    public PlayerRelationship? InternationalRetirement { get; init; }
}

/// <summary>Read only: no writer yet (registering or marking trained needs an oracle for what the game changes).</summary>
public sealed class PlayerStatusReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxRelations = 500, MaxName = 200;

    public PlayerStatuses Read(PlayerRecord player) => Read(PersonOwner.Of(player, profile));

    /// <summary>Relations of a player or a staff member (the holder sits on the person for both).</summary>
    public PlayerStatuses Read(PersonOwner player)
    {
        if (profile.Relations is not { } o || !player.IsValid(memory, moduleBase)) return PlayerStatuses.None;
        if (!Q(player.At(o.Pointer), out ulong holder) || holder == 0) return PlayerStatuses.None;
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || !Q(holder + 16, out ulong cap)) return PlayerStatuses.None;
        if (begin == 0 || end < begin || cap < end || (end - begin) % (ulong)o.RecordSize != 0 || (end - begin) / (ulong)o.RecordSize > MaxRelations)
            return PlayerStatuses.None;
        var bytes = new byte[end - begin];
        if (bytes.Length > 0 && !memory.TryRead(begin, bytes)) return PlayerStatuses.None;

        var registrations = new List<PlayerRegistration>();
        var clubs = new List<PlayerTrainedAtClub>();
        var nations = new List<PlayerTrainedAtNation>();
        var relationships = new List<PlayerRelationship>();
        bool nationalitiesComplete = true, trainingComplete = true, relationshipsComplete = true;
        PlayerRelationship? retirement = null;
        for (int i = 0; i < bytes.Length; i += o.RecordSize)
        {
            ulong target = BitConverter.ToUInt64(bytes, i + o.Target);
            byte type = bytes[i + o.Type], code = bytes[i + o.Code];
            if (type == o.RegistrationType && TeamClub(target) is { } club) registrations.Add(new(club, code));
            else if (type == o.TrainedAtClubType) { if (Club(target) is { } c) clubs.Add(new(c, code)); else trainingComplete = false; }
            else if (type == o.TrainedAtNationType) { if (Nation(target) is { } n) nations.Add(new(n, code)); else trainingComplete = false; }
            else if ((type == o.LikesPersonType || type == o.DislikesPersonType || type == o.LikesTeamType)
                     && (type == o.LikesTeamType ? Club(target)?.DisplayName : PersonName(target, o)) is { } name)
            {
                var kind = type == o.LikesPersonType ? RelationshipKind.LikesPerson : type == o.DislikesPersonType ? RelationshipKind.DislikesPerson : RelationshipKind.LikesTeam;
                relationships.Add(new(begin + (ulong)i, kind, name, code, bytes[i + o.Reason], bytes[i + o.Flags],
                    Convert.ToHexString(bytes, i, o.RecordSize), holder, begin, end) { TargetAddress = target });
            }
            else if (type == o.LikesPersonType || type == o.DislikesPersonType || type == o.LikesTeamType) relationshipsComplete = false;
            else if (type == o.OtherNationalityType)
            {
                if (Nation(target) is { } other)
                    relationships.Add(new PlayerRelationship(begin + (ulong)i, RelationshipKind.OtherNationality, other.DisplayName, code, bytes[i + o.Reason], bytes[i + o.Flags],
                        Convert.ToHexString(bytes, i, o.RecordSize), holder, begin, end) { TargetAddress = target });
                else nationalitiesComplete = false;
            }
            else if (type == o.RetiredInternationalType && retirement is null)
                retirement = new(begin + (ulong)i, RelationshipKind.RetiredFromInternationalFootball, null, code, bytes[i + o.Reason], bytes[i + o.Flags],
                    Convert.ToHexString(bytes, i, o.RecordSize), holder, begin, end);
        }
        return new(registrations, clubs, nations) { Relationships = relationships, Holder = holder, Begin = begin, End = end, Capacity = cap, RecordSnapshot = Convert.ToHexString(bytes), InternationalRetirement = retirement, NationalitiesComplete = nationalitiesComplete, TrainingComplete = trainingComplete, RelationshipsComplete = relationshipsComplete };
    }

    /// <summary>Common name, else first + last, else full name: persons not in play keep only the name entries (+0x40 is null).</summary>
    internal string? PersonName(ulong person, RelationOffsets o)
    {
        if (!Q(person, out ulong vt) || (vt != moduleBase + o.PersonVtable && vt != moduleBase + o.StaffPersonVtable)) return null;
        string? Entry(int at) => Q(person + (ulong)at, out ulong e) && e != 0 ? StringAt(e) : null;
        if (Entry(o.PersonCommonName) is { Length: > 0 } common) return common;
        var parts = new[] { Entry(o.PersonFirstName), Entry(o.PersonLastName) }.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        return parts.Length > 0 ? string.Join(' ', parts) : StringAt(person + (ulong)o.PersonName);
    }

    ClubInfo? TeamClub(ulong team)
    {
        if (profile.Team is not { } t || profile.Contract is not { } c || !Q(team, out ulong vt) || vt != moduleBase + t.VtableRva) return null;
        return Q(team + (ulong)c.TeamClub, out ulong club) ? Club(club) : null;
    }

    ClubInfo? Club(ulong address)
    {
        if (profile.Club is not { } k || !Q(address, out ulong vt) || vt != moduleBase + k.ClubVtableRva) return null;
        return new(address, StringAt(address + (ulong)k.Name), StringAt(address + (ulong)k.ShortName));
    }

    NationInfo? Nation(ulong address)
    {
        if (profile.Nation is not { } n || !Q(address, out ulong vt) || vt != moduleBase + n.VtableRva) return null;
        return new(address, StringAt(address + (ulong)n.Name), StringAt(address + (ulong)n.ShortName));
    }

    string? StringAt(ulong field)
    {
        if (!Q(field, out ulong p) || p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int count = BitConverter.ToInt32(len);
        if (count < 0 || count > MaxName) return null;
        var b = new byte[count];
        return memory.TryRead(p + 4, b) ? Encoding.UTF8.GetString(b) : null;
    }

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>
/// Sets the Value of a Likes Person / Dislikes Person / Likes Team row (byte <see cref="RelationOffsets.Code"/>). Writing Ethan
/// Mbappé 100 → 99 and Michael Olise 90 → 89 showed in FMRTE 27 as written. Guarded: player vtable, unchanged holder and
/// vector, whole 16-byte record; read-back.
/// </summary>
public sealed class RelationshipWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetValue(PlayerRecord player, PlayerRelationship relationship, int value) => SetValue(PersonOwner.Of(player, profile), relationship, value);

    public void SetValue(PersonOwner player, PlayerRelationship relationship, int value)
    {
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1–100");
        var o = Require(player, relationship, out _);
        ulong at = relationship.Address + (ulong)o.Code;
        Write(at, [(byte)value]);
    }

    /// <summary>FMRTE 27's Reason dropdowns, by code: 12 for people (None … Youth Staff), 5 for teams (None … Backroom Staff).</summary>
    public static int ReasonCount(RelationshipKind kind) => kind == RelationshipKind.LikesTeam ? 5 : 12;

    /// <summary>
    /// Sets Reason, for people Permanent, and optionally Value, in one write. FMRTE 27 (Kane → Bukayo Saka, 2/11/2026) wrote Reason as the low byte of the
    /// int16 at +0x8 (Teammate 5, Backroom Staff 8; Tottenham Supporter 1 → Manager 3) and Permanent as +0xD 0x4F / 0x00.
    /// Teams keep their +0xD byte (0x4E and 0x4F both seen; FMRTE shows no Permanent box for them).
    /// </summary>
    public void SetDetails(PlayerRecord player, PlayerRelationship relationship, int reason, bool? permanent, int? value = null) =>
        SetDetails(PersonOwner.Of(player, profile), relationship, reason, permanent, value);

    public void SetDetails(PersonOwner player, PlayerRelationship relationship, int reason, bool? permanent, int? value = null)
    {
        if (reason < 0 || reason >= ReasonCount(relationship.Kind)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1–100");
        var o = Require(player, relationship, out var before);
        var after = (byte[])before.Clone();
        after[o.Reason] = (byte)reason; after[o.Reason + 1] = 0;
        if (value is { } v) after[o.Code] = (byte)v;
        if (relationship.Kind != RelationshipKind.LikesTeam && permanent is { } p) after[o.Flags] = p ? PermanentFlag : (byte)0;
        Write(relationship.Address, after);
    }

    /// <summary>
    /// Nationality Info codes FMRTE 27 offers for an Other Nationality row (No info, then 80–90 without 83 Declared For
    /// Nation); the code is the record's +0xD byte.
    /// </summary>
    public static readonly IReadOnlyList<int> OtherNationalityInfos = [0, 80, 81, 82, 84, 85, 86, 87, 88, 89, 90];

    /// <summary>
    /// Sets an Other Nationality row's Nationality Info (+0xD). FMRTE 27 on Mikel Arteta's Pays Basque row (2/11/2026)
    /// turned 0x50 Eligible For Nation into 0x51 Has Played For Nation and changed nothing else in the record.
    /// </summary>
    public void SetNationalityInfo(PersonOwner player, PlayerRelationship relationship, int info)
    {
        if (relationship.Kind != RelationshipKind.OtherNationality) throw new ArgumentException("Not an Other Nationality row", nameof(relationship));
        if (!OtherNationalityInfos.Contains(info)) throw new ArgumentOutOfRangeException(nameof(info));
        var o = Require(player, relationship, out var before);
        if (before[o.Type] != o.OtherNationalityType) throw new StaleObjectException("The relationship changed since it was read");
        Write(relationship.Address + (ulong)o.Flags, [(byte)info]);
    }

    const byte PermanentFlag = 0x4F, PersonTarget = 3, ClubTarget = 1;

    /// <summary>
    /// Adds a Likes/Dislikes Person or Likes Team row like FMRTE 27: a 16-byte record appended to the vector — target,
    /// int16 reason, target kind (3 person, 1 club), type, value, flags (0x4F permanent / new team, else 0), 00, FF. A full
    /// vector moves to a new buffer outside the game's allocator. <paramref name="target"/> is the person part of a
    /// player (player + <see cref="RelationOffsets.PlayerPerson"/>) or a club.
    /// </summary>
    public void Add(PlayerRecord player, PlayerStatuses current, RelationshipKind kind, ulong target, int value, int reason, bool permanent, Func<int, ulong> allocate) =>
        Add(PersonOwner.Of(player, profile), current, kind, target, value, reason, permanent, allocate);

    public void Add(PersonOwner player, PlayerStatuses current, RelationshipKind kind, ulong target, int value, int reason, bool permanent, Func<int, ulong> allocate)
    {
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1–100");
        if (reason < 0 || reason >= ReasonCount(kind)) throw new ArgumentOutOfRangeException(nameof(reason));
        var (o, records) = RequireVector(player, current);
        ulong targetVt = memory.ReadUInt64(target);
        bool person = kind != RelationshipKind.LikesTeam;
        if (person ? targetVt != moduleBase + o.PersonVtable && targetVt != moduleBase + o.StaffPersonVtable
                   : profile.Club is not { } club || targetVt != moduleBase + club.ClubVtableRva)
            throw new ArgumentException("The target is not a person / club", nameof(target));
        byte type = (byte)(kind switch { RelationshipKind.LikesPerson => o.LikesPersonType, RelationshipKind.DislikesPerson => o.DislikesPersonType, _ => o.LikesTeamType });
        for (int i = 0; i < records.Length; i += o.RecordSize)
            if (records[i + o.Type] == type && BitConverter.ToUInt64(records, i + o.Target) == target)
                throw new InvalidOperationException("That relationship already exists");
        var record = new byte[o.RecordSize];
        BitConverter.GetBytes(target).CopyTo(record, o.Target);
        record[o.Reason] = (byte)reason;
        record[o.TargetKind] = person ? PersonTarget : ClubTarget;
        record[o.Type] = type;
        record[o.Code] = (byte)value;
        record[o.Flags] = !person || permanent ? PermanentFlag : (byte)0;
        record[o.RecordSize - 1] = 0xFF;
        Append(current.Holder, records, record, allocate);
    }

    /// <summary>
    /// Ticks "Retired From Int. Foot.": appends the record all 88 game-made ones share (save of 15/8/2026): no target, type
    /// 0xA, value 100, +0xD and +0xF 0xFF. FMRTE 27 overwrote the first row instead (Bruno Fernandes, 2/11/2026), so its
    /// layout is followed but not its placement. Unticking is <see cref="Remove"/> on <see cref="PlayerStatuses.InternationalRetirement"/>.
    /// </summary>
    public void AddInternationalRetirement(PlayerRecord player, PlayerStatuses current, Func<int, ulong> allocate)
    {
        var (o, records) = RequireVector(PersonOwner.Of(player, profile), current);
        for (int i = 0; i < records.Length; i += o.RecordSize)
            if (records[i + o.Type] == o.RetiredInternationalType) throw new InvalidOperationException("The player is already retired from international football");
        var record = new byte[o.RecordSize];
        record[o.Type] = (byte)o.RetiredInternationalType;
        record[o.Code] = 100;
        record[o.Flags] = 0xFF;
        record[o.RecordSize - 1] = 0xFF;
        Append(current.Holder, records, record, allocate);
    }

    /// <summary>
    /// FMRTE's "Trained at club" preset. FMRTE 27 overwrote the club of the first formed-at-club row (Bruno Fernandes:
    /// Boavista → Man Utd, 2/11/2026), losing real history; this appends the row the game itself uses instead: club, kind 1,
    /// type 0x48, period 1 ("3 Years (15-21)", 32,151 of 42,717 game rows), +0xD 0xFF. Returns false when the player
    /// already has a row for <paramref name="club"/>.
    /// </summary>
    public bool AddTrainedAtClub(PlayerRecord player, PlayerStatuses current, ulong club, Func<int, ulong> allocate)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (profile.Club is not { } k || memory.ReadUInt64(club) != moduleBase + k.ClubVtableRva) throw new ArgumentException("Not a club", nameof(club));
        return AddUnique(PersonOwner.Of(player, profile), current, club, ClubTarget, o.TrainedAtClubType, 1, 0xFF, allocate);
    }

    /// <summary>
    /// "Trained at nation": like <see cref="AddTrainedAtClub"/> for a nation (kind 8, type 0x46, period 1; 44,923 of 49,879
    /// game rows). FMRTE 27 used the club's nation (Man Utd → England) and overwrote the first row (Portugal).
    /// </summary>
    public bool AddTrainedAtNation(PlayerRecord player, PlayerStatuses current, ulong nation, Func<int, ulong> allocate)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (profile.Nation is not { } n || memory.ReadUInt64(nation) != moduleBase + n.VtableRva) throw new ArgumentException("Not a nation", nameof(nation));
        return AddUnique(PersonOwner.Of(player, profile), current, nation, NationTarget, o.TrainedAtNationType, 1, 0xFF, allocate);
    }

    /// <summary>
    /// "Register for UEFA": FMRTE 27 appended team, kind 4, type 0x19, code 0, +0xD 0 (Bruno Fernandes, Man Utd first team),
    /// the same bytes as the game's 1,069 UEFA rows; the domestic row differs only in code 8. Returns false when the player is
    /// already registered for UEFA with <paramref name="team"/>.
    /// </summary>
    public bool AddUefaRegistration(PlayerRecord player, PlayerStatuses current, ulong team, Func<int, ulong> allocate)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (profile.Team is not { } t || memory.ReadUInt64(team) != moduleBase + t.VtableRva) throw new ArgumentException("Not a team", nameof(team));
        return AddUnique(PersonOwner.Of(player, profile), current, team, TeamTarget, o.RegistrationType, UefaRegistration, 0, allocate);
    }

    const byte NationTarget = 8, TeamTarget = 4, UefaRegistration = 0;

    /// <summary>Adds the Other Nationality record observed on Simeone and Mbappé; info is the FMRTE nationality-info code.</summary>
    public void AddOtherNationality(PersonOwner player, PlayerStatuses current, ulong nation, int info, Func<int, ulong> allocate)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (!OtherNationalityInfos.Contains(info)) throw new ArgumentOutOfRangeException(nameof(info));
        if (profile.Nation is not { } n || memory.ReadUInt64(nation) != moduleBase + n.VtableRva)
            throw new ArgumentException("Not a nation", nameof(nation));
        if (!AddUnique(player, current, nation, NationTarget, o.OtherNationalityType, 100, (byte)info, allocate))
            throw new InvalidOperationException("That nationality already exists");
    }

    bool AddUnique(PersonOwner player, PlayerStatuses current, ulong target, byte kind, int type, byte code, byte flags, Func<int, ulong> allocate)
    {
        var (o, records) = RequireVector(player, current);
        for (int i = 0; i < records.Length; i += o.RecordSize)
            if (records[i + o.Type] == type && BitConverter.ToUInt64(records, i + o.Target) == target && (type != o.RegistrationType || records[i + o.Code] == code))
                return false;
        var record = new byte[o.RecordSize];
        BitConverter.GetBytes(target).CopyTo(record, o.Target);
        record[o.TargetKind] = kind;
        record[o.Type] = (byte)type;
        record[o.Code] = code;
        record[o.Flags] = flags;
        record[o.RecordSize - 1] = 0xFF;
        Append(current.Holder, records, record, allocate);
        return true;
    }

    /// <summary>The vector must be the one read; returns its records.</summary>
    (RelationOffsets o, byte[] records) RequireVector(PersonOwner player, PlayerStatuses current)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        RequirePlayer(player, o, current.Holder);
        var header = memory.Read(current.Holder, 24);
        ulong begin = BitConverter.ToUInt64(header, 0), end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
        if (current.Holder == 0 || begin != current.Begin || end != current.End || cap != current.Capacity || begin == 0 || cap < end) throw new StaleObjectException("The player's relationships changed since they were read");
        var records = memory.Read(begin, (int)(end - begin));
        if (!records.AsSpan().SequenceEqual(Convert.FromHexString(current.RecordSnapshot))) throw new StaleObjectException("The player's relationships changed since they were read");
        return (o, records);
    }

    /// <summary>Appends in place when there is room; a full vector moves to a new buffer outside the game's allocator.</summary>
    void Append(ulong holder, byte[] records, byte[] record, Func<int, ulong> allocate)
    {
        var header = memory.Read(holder, 24);
        ulong end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
        if (end + (ulong)record.Length <= cap)
        {
            Write(end, record);
            Write(holder + 8, BitConverter.GetBytes(end + (ulong)record.Length));
            return;
        }
        int count = records.Length / record.Length, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * record.Length);
        Write(buffer, [.. records, .. record]);
        Write(holder, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)records.Length + (ulong)record.Length), .. BitConverter.GetBytes(buffer + (ulong)(capacity * record.Length))]);
    }

    /// <summary>Removes a row like FMRTE 27 (vector erase): later records move up, the end pointer moves back one record.</summary>
    public void Remove(PlayerRecord player, PlayerRelationship relationship) => Remove(PersonOwner.Of(player, profile), relationship);

    public void Remove(PersonOwner player, PlayerRelationship relationship)
    {
        var o = Require(player, relationship, out _);
        ulong next = relationship.Address + (ulong)o.RecordSize;
        if (next < relationship.End) Write(relationship.Address, memory.Read(next, (int)(relationship.End - next)));
        Write(relationship.Holder + 8, BitConverter.GetBytes(relationship.End - (ulong)o.RecordSize));
    }

    /// <summary>
    /// Removes one trained-at row (record type <paramref name="type"/>) whose target is <paramref name="target"/>; the rows
    /// after it shift left and the vector's end shrinks by one record, everything else stays byte-for-byte. Returns false
    /// when there is no such row. The Trained at club / Trained at nation presets only add rows, so this is the way back.
    /// </summary>
    public bool RemoveTrainedRow(PersonOwner player, ulong target, byte type, byte? period = null)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (!player.IsValid(memory, moduleBase)) throw new StaleObjectException($"{player.Name} changed since it was read");
        ulong holder = memory.ReadUInt64(player.At(o.Pointer));
        if (holder == 0) return false;
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8);
        if (begin == 0 || end <= begin || (end - begin) % (ulong)o.RecordSize != 0 || (end - begin) / (ulong)o.RecordSize > 512) return false;
        var snapshot = memory.Read(begin, (int)(end - begin));
        int found = -1;
        for (int i = 0; i < snapshot.Length; i += o.RecordSize)
            if (snapshot[i + o.Type] == type && BitConverter.ToUInt64(snapshot, i + o.Target) == target
                && (period is not { } wanted || snapshot[i + o.Code] == wanted)) { found = i; break; }
        if (found < 0) return false;
        if (!memory.Read(begin, snapshot.Length).AsSpan().SequenceEqual(snapshot) || memory.ReadUInt64(holder + 8) != end)
            throw new StaleObjectException("The relations changed while removing a row");
        var tail = snapshot[(found + o.RecordSize)..];
        if (tail.Length > 0 && (!memory.TryWrite(begin + (ulong)found, tail) || !memory.Read(begin + (ulong)found, tail.Length).AsSpan().SequenceEqual(tail)))
            throw new MemoryAccessException($"Write at 0x{begin + (ulong)found:X} failed");
        var newEnd = BitConverter.GetBytes(end - (ulong)o.RecordSize);
        if (!memory.TryWrite(holder + 8, newEnd) || !memory.Read(holder + 8, 8).AsSpan().SequenceEqual(newEnd))
            throw new MemoryAccessException($"Write at 0x{holder + 8:X} failed");
        return true;
    }

    /// <summary>Removes the "trained at club" row for <paramref name="club"/> (undoes the Trained at club preset).</summary>
    public bool RemoveTrainedAtClub(PlayerRecord player, ulong club) =>
        RemoveTrainedRow(PersonOwner.Of(player, profile), club, (byte)(profile.Relations?.TrainedAtClubType ?? throw new InvalidOperationException("Profile has no relation offsets")));

    /// <summary>Removes the "trained at nation" row for <paramref name="nation"/> (undoes the Trained at nation preset).</summary>
    public bool RemoveTrainedAtNation(PlayerRecord player, ulong nation) =>
        RemoveTrainedRow(PersonOwner.Of(player, profile), nation, (byte)(profile.Relations?.TrainedAtNationType ?? throw new InvalidOperationException("Profile has no relation offsets")));

    void RequirePlayer(PersonOwner player, RelationOffsets o, ulong holder)
    {
        if (!player.IsValid(memory, moduleBase) || memory.ReadUInt64(player.At(o.Pointer)) != holder)
            throw new StaleObjectException($"{player.Name} changed since it was read");
    }

    /// <summary>Player vtable, unchanged holder and vector, record inside it and byte-for-byte as read.</summary>
    RelationOffsets Require(PersonOwner player, PlayerRelationship relationship, out byte[] before)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        before = Convert.FromHexString(relationship.Snapshot);
        RequirePlayer(player, o, relationship.Holder);
        if (before.Length != o.RecordSize
            || relationship.Address < relationship.Begin || relationship.Address + (ulong)o.RecordSize > relationship.End
            || (relationship.Address - relationship.Begin) % (ulong)o.RecordSize != 0
            || memory.ReadUInt64(relationship.Holder) != relationship.Begin || memory.ReadUInt64(relationship.Holder + 8) != relationship.End
            || !memory.Read(relationship.Address, o.RecordSize).AsSpan().SequenceEqual(before) || before[o.Code] != relationship.Value)
            throw new StaleObjectException("The relationship changed since it was read");
        return o;
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
