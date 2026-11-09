using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>What a relation record points at (byte +0xA of the record): nothing, a club, a person, a team or a nation.</summary>
public enum RelationTargetKind : byte { None = 0, Club = 1, Person = 3, Team = 4, Nation = 8 }

/// <summary>
/// One of the types in FMRTE 27's Relationship Type dropdown, with the bytes FMRTE (or the game) writes for a new row.
/// <see cref="Plus8"/> is the int16 at +0x8, <see cref="Value"/> the byte at +0xC, <see cref="Flags"/> +0xD, <see cref="Extra"/> +0xE.
/// For Other Nation Caps (0x68) and Other Nation Youth Caps (0x6B), +0xC is Caps and +0xD is Goals; FMRTE clamps both to 127.
/// </summary>
public sealed record RelationType(byte Code, string Name, RelationTargetKind Target, int Plus8 = 0, byte Value = 0, byte Flags = 0, byte Extra = 0);

/// <summary>
/// The 21 relation types of FMRTE 27's dropdown, matched to the record's type byte by reading the same players in FMRTE and in RAM
/// (10/11/2026; Haaland, Heaton, Bounou, Ocampos, Bachmann, Segovia...) and by adding each type in FMRTE with a Frida write trace.
/// Other codes exist in the game; FMRTE shows them as "Unknown (N)" with N the decimal code, and so does <see cref="NameOf"/>.
/// </summary>
public static class RelationCatalog
{
    public static readonly IReadOnlyList<RelationType> All =
    [
        new(0x01, "Likes Person", RelationTargetKind.Person, Value: 50),
        new(0x02, "Dislikes Person", RelationTargetKind.Person, Value: 50),
        new(0x03, "Likes Team", RelationTargetKind.Club, Value: 50, Flags: 0x4F),
        new(0x04, "Dislikes Team", RelationTargetKind.Club, Value: 50, Flags: 0x4F),
        new(0x09, "Other Nationality", RelationTargetKind.Nation, Value: 100, Flags: 0x50),
        new(0x0A, "International Retirement", RelationTargetKind.None, Value: 100, Flags: 0xFF),
        new(0x19, "Competition Registration", RelationTargetKind.Team),
        new(0x1B, "Competition Unregistration", RelationTargetKind.Team),
        new(0x39, "Invited to represent nation", RelationTargetKind.Nation, Plus8: 0x13, Flags: 0xFF),
        new(0x43, "Played UEFA Game", RelationTargetKind.Team, Value: 8, Flags: 0x50),
        new(0x46, "Formed at Country", RelationTargetKind.Nation, Value: 1, Flags: 0xFF),
        new(0x47, "Training Happiness", RelationTargetKind.None, Extra: 3),
        new(0x48, "Formed at Club", RelationTargetKind.Club, Value: 1, Flags: 0xFF),
        new(0x4A, "Developed at Country", RelationTargetKind.Club, Value: 0x16, Flags: 0xFF),
        new(0x51, "International Withdrawal", RelationTargetKind.None, Value: 0x32, Flags: 0x50),
        new(0x68, "Other Nation Caps", RelationTargetKind.Nation),
        new(0x6A, "Games Played", RelationTargetKind.Club, Flags: 0x1A, Extra: 9),
        new(0x6B, "Other Nation Youth Caps", RelationTargetKind.Nation),
        new(0x6D, "Settled Status", RelationTargetKind.Nation, Flags: 0xFF),
        new(0x75, "Cannot Apply for Work Permit", RelationTargetKind.Team, Plus8: 1, Flags: 3),
        new(0x7C, "Games Played In Division Level", RelationTargetKind.Nation, Flags: 0xFF),
    ];

    public static RelationType? Find(int code) => All.FirstOrDefault(t => t.Code == code);

    /// <summary>FMRTE's name for a type byte, "Unknown (N)" (decimal) for the codes it does not name.</summary>
    public static string NameOf(int code) => Find(code)?.Name ?? $"Unknown ({code})";

    /// <summary>Types that point at a person or at a team of a club use the 1–100 range of FMRTE's Value box.</summary>
    public static bool HasLikeValue(int code) => code is 0x01 or 0x02 or 0x03 or 0x04;

    /// <summary>Other Nation Caps / Youth Caps: +0xC is Caps, +0xD is Goals, both 0–127 (FMRTE clamped 300 to 127).</summary>
    public static bool HasCapsAndGoals(int code) => code is 0x68 or 0x6B;

    /// <summary>The Competition Registration codes FMRTE offers: 0 UEFA, 8 Domestic.</summary>
    public static readonly IReadOnlyList<(byte Code, string Name)> RegistrationCodes = [(0, "UEFA"), (8, "Domestic")];

    /// <summary>The Period column FMRTE shows for Formed at Club / Country rows (byte +0xC).</summary>
    public static string PeriodName(int value) => value switch { 1 => "3 Years (15-21)", 2 => "3 Years (0-21)", 10 => "Done", _ => $"Period {value}" };
}

/// <summary>One 16-byte row of a person's relation vector, whatever its type, as read.</summary>
public sealed record RelationEntry(ulong Address, byte Type, RelationTargetKind TargetKind, ulong Target, string? TargetName,
    int Plus8, byte Value, byte Flags, byte Extra, string Snapshot, ulong Holder, ulong Begin, ulong End);

/// <summary>A row to add: a catalog type, its target and the bytes the user chose.</summary>
public sealed record RelationDraft(RelationType Type, ulong Target, int Plus8, byte Value, byte Flags);

public sealed partial class RelationshipWriter
{
    /// <summary>
    /// Adds a row of any catalog type, the way FMRTE writes it (Frida trace on Haaland, 10/11/2026): target, int16 at +8, target kind,
    /// type, value, flags, extra, 0xFF. The target is checked against its kind; a full vector moves to a new buffer outside the game's allocator.
    /// </summary>
    public void AddEntry(PersonOwner player, PlayerStatuses current, RelationDraft draft, Func<int, ulong> allocate)
    {
        var (o, records) = RequireVector(player, current);
        var type = draft.Type;
        if (type.Target == RelationTargetKind.None ? draft.Target != 0 : draft.Target == 0 || !IsTarget(type.Target, draft.Target, o))
            throw new ArgumentException($"The target is not valid for {type.Name}", nameof(draft));
        if (CatalogHasLikeValue(type) && draft.Value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(draft), "Relationship value is 1–100");
        if (RelationCatalog.HasCapsAndGoals(type.Code) && (draft.Value > 127 || draft.Flags > 127))
            throw new ArgumentOutOfRangeException(nameof(draft), "Caps and Goals are 0–127");
        if (draft.Plus8 is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(draft));
        for (int i = 0; i < records.Length; i += o.RecordSize)
            if (records[i + o.Type] == type.Code && BitConverter.ToUInt64(records, i + o.Target) == draft.Target && records[i + o.Code] == draft.Value)
                throw new InvalidOperationException("That relationship already exists");
        var record = new byte[o.RecordSize];
        BitConverter.GetBytes(draft.Target).CopyTo(record, o.Target);
        BitConverter.GetBytes((ushort)draft.Plus8).CopyTo(record, o.Reason);
        record[o.TargetKind] = (byte)type.Target;
        record[o.Type] = type.Code;
        record[o.Code] = draft.Value;
        record[o.Flags] = draft.Flags;
        record[o.Flags + 1] = type.Extra;
        record[o.RecordSize - 1] = 0xFF;
        Append(current.Holder, records, record, allocate);
    }

    static bool CatalogHasLikeValue(RelationType type) => RelationCatalog.HasLikeValue(type.Code);

    bool IsTarget(RelationTargetKind kind, ulong target, RelationOffsets o)
    {
        ulong vt = memory.ReadUInt64(target);
        return kind switch
        {
            RelationTargetKind.Person => vt == moduleBase + o.PersonVtable || vt == moduleBase + o.StaffPersonVtable,
            RelationTargetKind.Club => profile.Club is { } club && vt == moduleBase + club.ClubVtableRva,
            RelationTargetKind.Team => profile.Team is { } team && vt == moduleBase + team.VtableRva,
            RelationTargetKind.Nation => profile.Nation is { } nation && vt == moduleBase + nation.VtableRva,
            _ => false,
        };
    }

    /// <summary>Changes the int16 at +8 (Reason), the value byte and the flags byte of one row in a single write; the rest of the record stays as read.</summary>
    public void UpdateEntry(PersonOwner player, RelationEntry entry, int plus8, byte value, byte flags)
    {
        var (o, before) = RequireEntry(player, entry);
        var type = RelationCatalog.Find(entry.Type);
        if (type is not null && RelationCatalog.HasLikeValue(type.Code) && value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1–100");
        if (type is not null && RelationCatalog.HasCapsAndGoals(type.Code) && (value > 127 || flags > 127))
            throw new ArgumentOutOfRangeException(nameof(value), "Caps and Goals are 0–127");
        if (plus8 is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(plus8));
        var after = (byte[])before.Clone();
        BitConverter.GetBytes((ushort)plus8).CopyTo(after, o.Reason);
        after[o.Code] = value;
        after[o.Flags] = flags;
        if (!after.AsSpan().SequenceEqual(before)) WriteBytes(entry.Address, after);
    }

    /// <summary>Removes one row like FMRTE 27 (vector erase): later rows move up and the end pointer moves back one record.</summary>
    public void RemoveEntry(PersonOwner player, RelationEntry entry)
    {
        var (o, _) = RequireEntry(player, entry);
        ulong next = entry.Address + (ulong)o.RecordSize;
        if (next < entry.End) WriteBytes(entry.Address, memory.Read(next, (int)(entry.End - next)));
        WriteBytes(entry.Holder + 8, BitConverter.GetBytes(entry.End - (ulong)o.RecordSize));
    }

    (RelationOffsets o, byte[] before) RequireEntry(PersonOwner player, RelationEntry entry)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        var before = Convert.FromHexString(entry.Snapshot);
        RequirePlayer(player, o, entry.Holder);
        if (before.Length != o.RecordSize
            || entry.Address < entry.Begin || entry.Address + (ulong)o.RecordSize > entry.End
            || (entry.Address - entry.Begin) % (ulong)o.RecordSize != 0
            || memory.ReadUInt64(entry.Holder) != entry.Begin || memory.ReadUInt64(entry.Holder + 8) != entry.End
            || !memory.Read(entry.Address, o.RecordSize).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The relationship changed since it was read");
        return (o, before);
    }

    void WriteBytes(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
