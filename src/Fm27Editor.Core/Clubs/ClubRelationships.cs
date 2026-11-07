using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary><see cref="Other"/> rows are shown by FMRTE as "Unknown (type)"; only their type byte is known.</summary>
public enum ClubRelationshipKind { Rival, LikesPerson, DislikesPerson, Other }

/// <summary>
/// One row of a club's Relationships tab. <see cref="Reason"/> is the raw code: rivals 1 Local, 2 Competitive, 3 Historic;
/// liked people 0 No Reason, 1 Player, 2 Manager, 4 Chairperson, 8 Founder (3, 5, 6, 7 follow FMRTE's dropdown order).
/// <see cref="Snapshot"/> is the whole 16-byte record as read, the guard for writes.
/// </summary>
public sealed record ClubRelationship(ulong Address, ClubRelationshipKind Kind, int TypeCode, string? Name, int Value,
    int Reason, bool Permanent, string Snapshot, ulong Begin, ulong End, ulong Capacity);

/// <summary>Read-only snapshot of the vector stored inline in the club object (club + Vector).</summary>
public sealed class ClubRelationshipReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxRows = 512, MaxName = 200;
    const byte PermanentFlag = 0x4F;

    public IReadOnlyList<ClubRelationship> Read(ulong club)
    {
        if (profile.Club?.Relationships is not { } o || !IsClub(club)) return [];
        if (!Q(club + (ulong)o.Vector, out ulong begin) || !Q(club + (ulong)o.Vector + 8, out ulong end)
            || !Q(club + (ulong)o.Vector + 16, out ulong cap)) return [];
        if (begin == 0 || o.RecordSize <= 0 || end < begin || cap < end || (end - begin) % (ulong)o.RecordSize != 0
            || (cap - begin) % (ulong)o.RecordSize != 0
            || (end - begin) / (ulong)o.RecordSize > MaxRows) return [];
        var bytes = new byte[end - begin];
        if (bytes.Length > 0 && !memory.TryRead(begin, bytes)) return [];
        var rows = new List<ClubRelationship>();
        for (int i = 0; i < bytes.Length; i += o.RecordSize)
        {
            byte type = bytes[i + o.Type];
            var kind = type == o.RivalType ? ClubRelationshipKind.Rival
                : type == o.LikesPersonType ? ClubRelationshipKind.LikesPerson
                : type == o.DislikesPersonType ? ClubRelationshipKind.DislikesPerson : ClubRelationshipKind.Other;
            ulong target = BitConverter.ToUInt64(bytes, i + o.Target);
            rows.Add(new(begin + (ulong)i, kind, type, TargetName(target, bytes[i + o.Kind]), bytes[i + o.Value],
                bytes[i + o.Reason], bytes[i + o.Flags] == PermanentFlag, Convert.ToHexString(bytes, i, o.RecordSize), begin, end, cap));
        }
        return rows.AsReadOnly();
    }

    bool IsClub(ulong address) => profile.Club is { } k && Q(address, out ulong vt) && vt == moduleBase + k.ClubVtableRva;

    string? TargetName(ulong target, byte kind)
    {
        if (profile.Club?.Relationships is not { } o || target == 0) return null;
        if (kind == o.ClubKind) return ClubName(target);
        if (kind == o.TeamKind && profile.Team is { } t && profile.Contract is { } c && Q(target, out ulong tvt)
            && tvt == moduleBase + t.VtableRva && Q(target + (ulong)c.TeamClub, out ulong club)) return ClubName(club);
        return kind == o.PersonKind && profile.Relations is { } r
            ? new PlayerStatusReader(memory, profile, moduleBase).PersonName(target, r) : null;
    }

    string? ClubName(ulong club)
    {
        if (!IsClub(club) || profile.Club is not { } k) return null;
        return Text(club + (ulong)k.Name) ?? Text(club + (ulong)k.ShortName);
    }

    string? Text(ulong field)
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
/// Edits Value and Reason (and Permanent for people) of an existing row. Guarded like every writer: club vtable, vector
/// unchanged, the whole record equal to the snapshot, then write and read back. Unknown (type) rows are not editable.
/// </summary>
public sealed class ClubRelationshipWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const byte PermanentFlag = 0x4F;

    public static int ReasonCount(ClubRelationshipKind kind) => kind == ClubRelationshipKind.Rival ? 4 : 9;

    public void SetValue(ulong club, ClubRelationship row, int value) => SetDetails(club, row, null, null, value);

    /// <summary>Reason is the low byte of the int16 at +Reason (as FMRTE 27 wrote it); Permanent is +Flags 0x4F / 0x00.</summary>
    public void SetDetails(ulong club, ClubRelationship row, int? reason, bool? permanent, int? value)
    {
        if (row.Kind == ClubRelationshipKind.Other) throw new ArgumentException("Only Rival, Likes Person and Dislikes Person rows are editable", nameof(row));
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1–100");
        if (reason is { } r && (r < 0 || r >= ReasonCount(row.Kind))) throw new ArgumentOutOfRangeException(nameof(reason));
        if (permanent is not null && row.Kind == ClubRelationshipKind.Rival) throw new ArgumentException("Rival rows have no Permanent flag", nameof(permanent));
        var o = profile.Club?.Relationships ?? throw new InvalidOperationException("Profile has no club relationship offsets");
        var before = Convert.FromHexString(row.Snapshot);
        Require(club, row, o, before);
        var after = (byte[])before.Clone();
        if (reason is { } rc) { after[o.Reason] = (byte)rc; after[o.Reason + 1] = 0; }
        if (value is { } v) after[o.Value] = (byte)v;
        if (permanent is { } p) after[o.Flags] = p ? PermanentFlag : (byte)0;
        if (!memory.TryWrite(row.Address, after) || !memory.Read(row.Address, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write {after.Length} bytes at 0x{row.Address:X}");
    }

    /// <summary>Removes one row: the rows after it shift left and the vector's end shrinks by one record.</summary>
    public void Remove(ulong club, ClubRelationship row)
    {
        var o = profile.Club?.Relationships ?? throw new InvalidOperationException("Profile has no club relationship offsets");
        var before = Convert.FromHexString(row.Snapshot);
        Require(club, row, o, before);
        ulong header = club + (ulong)o.Vector;
        ulong next = row.Address + (ulong)o.RecordSize;
        if (next < row.End)
        {
            var tail = memory.Read(next, (int)(row.End - next));
            if (!memory.TryWrite(row.Address, tail) || !memory.Read(row.Address, tail.Length).AsSpan().SequenceEqual(tail))
                throw new MemoryAccessException($"Could not move the rows at 0x{row.Address:X}");
        }
        var newEnd = BitConverter.GetBytes(row.End - (ulong)o.RecordSize);
        if (!memory.TryWrite(header + 8, newEnd) || !memory.Read(header + 8, 8).AsSpan().SequenceEqual(newEnd))
            throw new MemoryAccessException($"Could not shrink the club relationship vector at 0x{header:X}");
    }

    /// <summary>
    /// Appends a Rival row into the vector's spare capacity (no allocation: throws when the vector is full), then grows
    /// the end and returns the row as read back.
    /// </summary>
    public ClubRelationship? AddRival(ulong club, ulong rivalClub, int value = 50, int reason = 2, Func<int, ulong>? allocate = null)
    {
        var o = profile.Club?.Relationships ?? throw new InvalidOperationException("Profile has no club relationship offsets");
        if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Relationship value is 1-100");
        if (reason < 0 || reason >= ReasonCount(ClubRelationshipKind.Rival)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (!IsClubObject(club) || !IsClubObject(rivalClub)) throw new ArgumentException("Both ends must be club objects");
        ulong header = club + (ulong)o.Vector;
        if (!Q(header, out ulong begin) || !Q(header + 8, out ulong end) || !Q(header + 16, out ulong cap))
            throw new StaleObjectException("The club changed since it was read");
        if (begin == 0 || end < begin || cap < end || (end - begin) % (ulong)o.RecordSize != 0)
            throw new StaleObjectException("The club relationship list looks invalid");
        var record = new byte[o.RecordSize];
        record[o.Type] = (byte)o.RivalType;
        record[o.Kind] = (byte)o.ClubKind;
        BitConverter.GetBytes(rivalClub).CopyTo(record, o.Target);
        record[o.Value] = (byte)value;
        record[o.Reason] = (byte)reason;
        record[o.Flags] = 0;
        if (end + (ulong)o.RecordSize <= cap)
        {
            if (!memory.TryWrite(end, record) || !memory.Read(end, record.Length).AsSpan().SequenceEqual(record))
                throw new MemoryAccessException($"Could not write the new club relationship at 0x{end:X}");
            var grown = BitConverter.GetBytes(end + (ulong)o.RecordSize);
            if (!memory.TryWrite(header + 8, grown) || !memory.Read(header + 8, 8).AsSpan().SequenceEqual(grown))
                throw new MemoryAccessException($"Could not grow the club relationship vector at 0x{header:X}");
            return new ClubRelationshipReader(memory, profile, moduleBase).Read(club).LastOrDefault(r => r.Address == end);
        }
        // Vector full: a buffer outside the game's allocator holds old rows + the new one (same rule as squad vectors).
        if (allocate is null) throw new StaleObjectException("The club relationship list has no spare room");
        int oldBytes = (int)(end - begin), newBytes = oldBytes + o.RecordSize * 2;
        ulong buffer = allocate(newBytes);
        if (buffer == 0) throw new MemoryAccessException("Could not allocate a club relationship buffer");
        var copy = memory.Read(begin, oldBytes);
        if (!memory.TryWrite(buffer, copy) || !memory.Read(buffer, copy.Length).AsSpan().SequenceEqual(copy))
            throw new MemoryAccessException($"Could not copy the club relationships to 0x{buffer:X}");
        if (!memory.TryWrite(buffer + (ulong)oldBytes, record)
            || !memory.Read(buffer + (ulong)oldBytes, record.Length).AsSpan().SequenceEqual(record))
            throw new MemoryAccessException($"Could not write the new club relationship at 0x{buffer + (ulong)oldBytes:X}");
        var headerBytes = new byte[24];
        BitConverter.GetBytes(buffer).CopyTo(headerBytes, 0);
        BitConverter.GetBytes(buffer + (ulong)oldBytes + (ulong)o.RecordSize).CopyTo(headerBytes, 8);
        BitConverter.GetBytes(buffer + (ulong)newBytes).CopyTo(headerBytes, 16);
        if (!memory.TryWrite(header, headerBytes) || !memory.Read(header, headerBytes.Length).AsSpan().SequenceEqual(headerBytes))
            throw new MemoryAccessException($"Could not point the club relationship vector at 0x{buffer:X}");
        return new ClubRelationshipReader(memory, profile, moduleBase).Read(club)
            .LastOrDefault(r => r.Address == buffer + (ulong)oldBytes);
    }

    bool IsClubObject(ulong address) => profile.Club is { } k && Q(address, out ulong vt) && vt == moduleBase + k.ClubVtableRva;

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }

    void Require(ulong club, ClubRelationship row, ClubRelationshipOffsets o, byte[] before)
    {
        if (profile.Club is not { } k || memory.ReadUInt64(club) != moduleBase + k.ClubVtableRva)
            throw new StaleObjectException("The club changed since it was read");
        ulong header = club + (ulong)o.Vector;
        if (before.Length != o.RecordSize || memory.ReadUInt64(header) != row.Begin || memory.ReadUInt64(header + 8) != row.End
            || memory.ReadUInt64(header + 16) != row.Capacity
            || row.Address < row.Begin || row.Address + (ulong)o.RecordSize > row.End
            || (row.Address - row.Begin) % (ulong)o.RecordSize != 0
            || !memory.Read(row.Address, o.RecordSize).AsSpan().SequenceEqual(before) || before[o.Value] != row.Value)
            throw new StaleObjectException("The relationship changed since it was read");
    }
}
