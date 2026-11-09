using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Promises (FMRTE 27's Promises page). Measured on Game Pass 27.0.0+2493426, 9/11/2026, Mbappé with FMRTE's Frida-traced writes.
/// A promise is a 0x60-byte plain record (first u32 a reference count, 3 = held by three lists). It is listed in three places:
/// the promised person's vector (person + <see cref="Holder"/> → holder, holder + 0 = begin/end/cap), the manager's vector (same
/// offset on the manager's person part) and a global vector at [module + <see cref="GlobalRootRva"/>] + <see cref="GlobalVector"/>.
/// </summary>
public sealed record PromiseOffsets(int Holder, int RecordSize, int Manager, int Person, int Club, int Team, int Made, int Deadline,
    int Parameter0, int Parameter1, int Type, uint GlobalRootRva, int GlobalVector)
{
    static readonly string[] Keys = ["holder", "recordSize", "manager", "person", "club", "team", "made", "deadline", "parameter0", "parameter1", "type", "globalRootRva", "globalVector"];

    internal static PromiseOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.promises.{key}: expected hex"), 16)).ToArray();
        return new((int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (int)v[7], (int)v[8], (int)v[9], (int)v[10], (uint)v[11], (int)v[12]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Holder, RecordSize, Manager, Person, Club, Team, Made, Deadline, Parameter0, Parameter1, Type, GlobalRootRva, GlobalVector];
        w.WriteStartObject("promises");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>What a promise type asks for besides the dates.</summary>
public enum PromiseParameter
{
    None,
    /// <summary>A competition: its index (the object's +0x8, not its ID) at +0x44.</summary>
    Competition,
    /// <summary>A player or staff member: the person index at +0x44.</summary>
    Person,
    /// <summary>A playing-time role (the contract's Playing Time list): its value at +0x40.</summary>
    PlayingTime,
    /// <summary>A squad number at +0x40.</summary>
    Number,
}

public sealed record PromiseType(int Id, string Text, PromiseParameter Parameter)
{
    public override string ToString() => Text;
}

/// <summary>
/// The 68 types FMRTE 27 lists, each id read from FMRTE's own write when that line was picked on Mbappé (alphabetical by text, as
/// FMRTE shows them). Texts that FMRTE shows twice have two ids.
/// </summary>
public static class PromiseTypes
{
    public static readonly IReadOnlyList<PromiseType> All =
    [
        new(60, "Add minimum release clause", PromiseParameter.None),
        new(46, "Attempt to qualify for 'Competition'", PromiseParameter.Competition),
        new(68, "Be appointed vice-captain", PromiseParameter.None),
        new(229, "Become an established 'Competition' team", PromiseParameter.Competition),
        new(230, "Become an established 'Competition' team", PromiseParameter.Competition),
        new(228, "Become recognised as the 'best of the rest' in the 'Competition'", PromiseParameter.Competition),
        new(48, "Bring in a suitable signing to help player settle", PromiseParameter.None),
        new(45, "Challenge for 'Competition' title", PromiseParameter.Competition),
        new(217, "Challenge for the 'Competition'", PromiseParameter.Competition),
        new(216, "Club will fund a coaching course", PromiseParameter.None),
        new(54, "Do not fine this player", PromiseParameter.None),
        new(57, "Don't sell player", PromiseParameter.None),
        new(72, "Don't sign replacement player", PromiseParameter.None),
        new(55, "Drop 'Person'", PromiseParameter.Person),
        new(77, "Drop 'Person'", PromiseParameter.Person),
        new(1, "Fair increase in playing time", PromiseParameter.None),
        new(25, "Find loan move for player", PromiseParameter.None),
        new(51, "Find loan move for player to secure work permit", PromiseParameter.None),
        new(215, "Finish in the top half of the 'Competition' table", PromiseParameter.Competition),
        new(219, "Finish in the top half of the 'Competition' table", PromiseParameter.Competition),
        new(40, "Get appointed captain", PromiseParameter.None),
        new(37, "Get to 'Competition' playoffs", PromiseParameter.Competition),
        new(218, "Get to 'Competition' playoffs", PromiseParameter.Competition),
        new(36, "Get to latter stages of 'Competition'", PromiseParameter.Competition),
        new(17, "Give complete rest from matches", PromiseParameter.None),
        new(8, "Give limited rest from matches", PromiseParameter.None),
        new(86, "Help 'Person' to improve", PromiseParameter.Person),
        new(247, "Honour playing time pathway increase to 'Playing Time'", PromiseParameter.PlayingTime),
        new(5, "Increase agreed playing time to 'Playing Time'", PromiseParameter.PlayingTime),
        new(243, "Increase playing time to 'Playing Time' upon return from loan", PromiseParameter.PlayingTime),
        new(103, "Keep 'Job' in place", PromiseParameter.Person),
        new(231, "Look to become 'Competition' team after yo-yoing between divisions", PromiseParameter.Competition),
        new(232, "Look to become 'Competition' team after yo-yoing between divisions", PromiseParameter.Competition),
        new(2, "Offer a new contract", PromiseParameter.None),
        new(39, "Offer non-player role", PromiseParameter.None),
        new(78, "Play 'Person'", PromiseParameter.Person),
        new(44, "Play games from the bench", PromiseParameter.None),
        new(43, "Play in cup games", PromiseParameter.None),
        new(120, "Play loanee 'Person' more often", PromiseParameter.Person),
        new(209, "Promised to qualify for the 'Competition' this season", PromiseParameter.Competition),
        new(31, "Qualify for 'Competition'", PromiseParameter.Competition),
        new(234, "Reach the 'Competition' knockout stages", PromiseParameter.Competition),
        new(235, "Reach the 'Competition' proper", PromiseParameter.Competition),
        new(42, "Receive big pay rise", PromiseParameter.None),
        new(74, "Register in 'Competition' squad", PromiseParameter.Competition),
        new(242, "Restore agreed playing time to 'Playing Time'", PromiseParameter.PlayingTime),
        new(13, "Sell 'Person'", PromiseParameter.Person),
        new(3, "Sell player", PromiseParameter.None),
        new(63, "Sell player if not granted work permit", PromiseParameter.None),
        new(52, "Treat player more fairly", PromiseParameter.None),
        new(64, "Wait for a team to bid", PromiseParameter.None),
        new(202, "Will assign player as free kick taker", PromiseParameter.None),
        new(203, "Will assign player as penalty taker", PromiseParameter.None),
        new(11, "Will be restored to the first-team squad", PromiseParameter.None),
        new(237, "Will become a regular starter should 'Person' leave the club", PromiseParameter.Person),
        new(204, "Will give player big pay rise", PromiseParameter.None),
        new(200, "Will give player the number 'Number' shirt", PromiseParameter.Number),
        new(248, "Will guarantee future", PromiseParameter.None),
        new(207, "Will improve first team coaching staff", PromiseParameter.None),
        new(206, "Will improve training facilities", PromiseParameter.None),
        new(41, "Will not stand in the way of a big transfer", PromiseParameter.None),
        new(69, "Will receive agreed playing time on return from injury", PromiseParameter.None),
        new(33, "Win 'Competition'", PromiseParameter.Competition),
        new(34, "Win 'Competition'", PromiseParameter.Competition),
        new(73, "Win 'Competition'", PromiseParameter.Competition),
        new(30, "Win promotion to 'Competition'", PromiseParameter.Competition),
        new(211, "Win the 'Competition' next season", PromiseParameter.Competition),
        new(201, "Won't be replaced by 'Person'", PromiseParameter.Person),
    ];

    public static PromiseType? Find(int id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>One promise as read. <see cref="Snapshot"/> is the whole record, kept to guard the write.</summary>
public sealed record PromiseRecord(ulong Address, int TypeId, ulong Manager, ulong Person, ulong Club, ulong Team, DateOnly? Made, DateOnly? Deadline,
    int Parameter0, int Parameter1, string Snapshot)
{
    public PromiseType? Type => PromiseTypes.Find(TypeId);
}

/// <summary>The values the editor changes on a promise (everything else keeps its bytes).</summary>
public sealed record PromiseValues(int TypeId, DateOnly Made, DateOnly Deadline, int Parameter0, int Parameter1);

public sealed class PromiseStore(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxPromises = 50_000;

    /// <summary>A Person promise stores the person's index (the u32 at person + 8: player + 0x290, the same index Work Permit uses), not the unique ID.</summary>
    public uint PersonIndex(ulong personPart) => BitConverter.ToUInt32(memory.Read(personPart + 8, 4));

    PromiseOffsets O => profile.Promises ?? throw new InvalidOperationException("Profile has no promise offsets");

    public bool Available => profile.Promises is not null;

    /// <summary>The promises held by a player's or manager's person part, in the order the game keeps them.</summary>
    public IReadOnlyList<PromiseRecord> Read(PersonOwner owner)
    {
        if (!Available || !owner.IsValid(memory, moduleBase)) return [];
        var (_, _, begin, end, _) = Vector(HolderVector(owner.Address, owner.Shift) ?? 0);
        return ReadAll(begin, end);
    }

    IReadOnlyList<PromiseRecord> ReadAll(ulong begin, ulong end)
    {
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxPromises) return [];
        var raw = memory.Read(begin, (int)(end - begin));
        var list = new List<PromiseRecord>();
        for (int i = 0; i < raw.Length; i += 8)
            if (ReadRecord(BitConverter.ToUInt64(raw, i)) is { } record) list.Add(record);
        return list;
    }

    PromiseRecord? ReadRecord(ulong address)
    {
        var o = O;
        var b = new byte[o.RecordSize];
        if (address == 0 || !memory.TryRead(address, b)) return null;
        return new(address, BitConverter.ToUInt16(b, o.Type), BitConverter.ToUInt64(b, o.Manager), BitConverter.ToUInt64(b, o.Person),
            BitConverter.ToUInt64(b, o.Club), BitConverter.ToUInt64(b, o.Team), DateAt(b, o.Made), DateAt(b, o.Deadline),
            BitConverter.ToInt32(b, o.Parameter0), BitConverter.ToInt32(b, o.Parameter1), Convert.ToHexString(b));
    }

    /// <summary>The address of the vector header the person's holder points at (0 when the person has none).</summary>
    ulong? HolderVector(ulong person, int shift)
    {
        ulong at = person + (ulong)(O.Holder - shift);
        if (!memory.TryRead(at, new byte[8])) return null;
        ulong holder = memory.ReadUInt64(at);
        return holder == 0 ? null : holder;
    }

    (ulong vector, ulong unused, ulong begin, ulong end, ulong cap) Vector(ulong vector)
    {
        if (vector == 0 || !memory.TryRead(vector, new byte[24])) return (0, 0, 0, 0, 0);
        return (vector, 0, memory.ReadUInt64(vector), memory.ReadUInt64(vector + 8), memory.ReadUInt64(vector + 16));
    }

    ulong GlobalVectorAddress()
    {
        var o = O;
        ulong root = memory.ReadUInt64(moduleBase + o.GlobalRootRva);
        if (root == 0) throw new MemoryAccessException("The promise list is not loaded; is a save loaded?");
        return root + (ulong)o.GlobalVector;
    }

    public static DateOnly? DateAt(byte[] b, int at)
    {
        // The nine low bits are the day of the year; the game sets flag bits above them on promises it made itself (0x8AE2 is
        // day 226). The ones FMRTE writes have no flags.
        int day = BitConverter.ToUInt16(b, at) & 0x1FF, year = BitConverter.ToUInt16(b, at + 2);
        if (year is < 1900 or > 2200 || day is < 1 or > 366) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }

    static byte[] DateBytes(DateOnly d) => [.. BitConverter.GetBytes((ushort)d.DayOfYear), .. BitConverter.GetBytes((ushort)d.Year)];

    /// <summary>The same, keeping the flag bits the game put above the day of the year in the old word.</summary>
    static byte[] DateBytes(DateOnly d, byte[] before, int at) =>
        [.. BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(before, at) & 0xFE00) | d.DayOfYear)), .. BitConverter.GetBytes((ushort)d.Year)];

    /// <summary>
    /// Changes a promise like FMRTE's Save: parameters reset to -1, then the type, then the parameter the type takes. Dates are two
    /// u16 (day of year, year). Guarded: the record is still exactly what was read and still sits in the owner's vector.
    /// </summary>
    public void Update(PersonOwner owner, PromiseRecord promise, PromiseValues values)
    {
        var o = O;
        CheckOwner(owner, promise);
        // A promise of a kind FMRTE does not list (the game made it) may be saved with its own type; no other unknown type is accepted.
        var type = PromiseTypes.Find(values.TypeId)
            ?? (values.TypeId == promise.TypeId ? new PromiseType(values.TypeId, $"Unknown ({values.TypeId})", PromiseParameter.None) : throw new ArgumentOutOfRangeException(nameof(values), "Unknown promise type"));
        CheckParameter(type, values);
        if (values.Deadline < values.Made) throw new ArgumentException("The deadline is before the day it was made");
        var before = Convert.FromHexString(promise.Snapshot);
        if (!memory.Read(promise.Address, o.RecordSize).AsSpan().SequenceEqual(before)) throw new StaleObjectException("The promise changed since it was read");
        if (promise.Made != DateAt(before, o.Made) || promise.Deadline != DateAt(before, o.Deadline)) throw new StaleObjectException("The promise changed since it was read");
        var after = (byte[])before.Clone();
        BitConverter.GetBytes(values.Parameter0).CopyTo(after, o.Parameter0);
        BitConverter.GetBytes(values.Parameter1).CopyTo(after, o.Parameter1);
        BitConverter.GetBytes((ushort)values.TypeId).CopyTo(after, o.Type);
        DateBytes(values.Made, before, o.Made).CopyTo(after, o.Made);
        DateBytes(values.Deadline, before, o.Deadline).CopyTo(after, o.Deadline);
        for (int i = 0; i < after.Length;)
        {
            if (after[i] == before[i]) { i++; continue; }
            int j = i;
            while (j < after.Length && after[j] != before[j]) j++;
            Write(promise.Address + (ulong)i, after[i..j]);
            i = j;
        }
    }

    static void CheckParameter(PromiseType type, PromiseValues v)
    {
        switch (type.Parameter)
        {
            case PromiseParameter.Competition or PromiseParameter.Person when v.Parameter1 < 0:
                throw new ArgumentException($"'{type.Text}' needs a {type.Parameter.ToString().ToLowerInvariant()}");
            case PromiseParameter.PlayingTime or PromiseParameter.Number when v.Parameter0 <= 0:
                throw new ArgumentException($"'{type.Text}' needs a {(type.Parameter == PromiseParameter.Number ? "squad number" : "playing time")}");
        }
    }

    /// <summary>
    /// Adds a promise like FMRTE 27: a fresh record appended to the owner's, the manager's and the global vector; a full vector
    /// moves to a new buffer. The record and any new buffer live outside the game's allocator.
    /// </summary>
    public PromiseRecord Add(PersonOwner owner, ulong manager, ulong club, PromiseValues values, Func<int, ulong> allocate)
    {
        var o = O;
        if (!owner.IsValid(memory, moduleBase)) throw new StaleObjectException($"{owner.Name} changed since it was read");
        var type = PromiseTypes.Find(values.TypeId) ?? throw new ArgumentOutOfRangeException(nameof(values), "Unknown promise type");
        CheckParameter(type, values);
        if (values.Deadline < values.Made) throw new ArgumentException("The deadline is before the day it was made");
        if (manager == 0 || club == 0) throw new ArgumentException("A promise needs a manager and a club");
        var holders = Holders(owner, manager, allocate);
        var record = new byte[o.RecordSize];
        BitConverter.GetBytes(3).CopyTo(record, 0);           // held by three lists
        BitConverter.GetBytes(manager).CopyTo(record, o.Manager);
        BitConverter.GetBytes(PersonPart(owner)).CopyTo(record, o.Person);
        BitConverter.GetBytes(club).CopyTo(record, o.Club);
        BitConverter.GetBytes(club).CopyTo(record, o.Team);
        DateBytes(values.Made).CopyTo(record, o.Made);
        DateBytes(values.Deadline).CopyTo(record, o.Deadline);
        BitConverter.GetBytes(values.Parameter0).CopyTo(record, o.Parameter0);
        BitConverter.GetBytes(values.Parameter1).CopyTo(record, o.Parameter1);
        BitConverter.GetBytes((ushort)0x5DC).CopyTo(record, o.Type - 2);
        BitConverter.GetBytes((ushort)values.TypeId).CopyTo(record, o.Type);
        byte[] tail = [0, 0, 0, 0, 0, 5, 0, 2, 0, 1];         // bytes after the type, copied from every record FMRTE showed
        tail.CopyTo(record, o.Type + 2);
        ulong at = allocate(o.RecordSize);
        Write(at, record);
        foreach (ulong vector in holders) Append(vector, at, allocate);
        return ReadRecord(at)!;
    }

    /// <summary>Removes a promise like FMRTE 27: in each of the three vectors later pointers move up and the end pointer moves back; the record stays.</summary>
    public void Remove(PersonOwner owner, PromiseRecord promise)
    {
        CheckOwner(owner, promise);
        ulong manager = promise.Manager;
        var holders = Holders(owner, manager, null);
        foreach (ulong vector in holders)
        {
            var (_, _, begin, end, _) = Vector(vector);
            var raw = memory.Read(begin, (int)(end - begin));
            int index = -1;
            for (int i = 0; i < raw.Length; i += 8) if (BitConverter.ToUInt64(raw, i) == promise.Address) { index = i; break; }
            if (index < 0) throw new StaleObjectException("The promise is no longer in every list");
        }
        foreach (ulong vector in holders)
        {
            var (_, _, begin, end, _) = Vector(vector);
            var raw = memory.Read(begin, (int)(end - begin));
            int index = 0;
            for (int i = 0; i < raw.Length; i += 8) if (BitConverter.ToUInt64(raw, i) == promise.Address) { index = i; break; }
            if (index + 8 < raw.Length) Write(begin + (ulong)index, raw[(index + 8)..]);
            Write(vector + 8, BitConverter.GetBytes(end - 8));
        }
    }

    /// <summary>
    /// The three vector headers a promise sits in: the person's, the manager's and the global one. A person who never had a promise has no holder;
    /// with <paramref name="allocate"/> one is made like FMRTE does (24 zero bytes, its pointer stored at person + holder offset), otherwise it is an error.
    /// </summary>
    ulong[] Holders(PersonOwner owner, ulong manager, Func<int, ulong>? allocate)
    {
        ulong person = HolderOf(owner.Address, owner.Shift, owner.Name, allocate);
        ulong mgr = HolderOf(manager, profile.Relations?.PlayerPerson ?? 0x288, "The manager", allocate);
        return [person, mgr, GlobalVectorAddress()];
    }

    ulong HolderOf(ulong person, int shift, string name, Func<int, ulong>? allocate)
    {
        if (HolderVector(person, shift) is { } existing) return existing;
        if (allocate is null) throw new StaleObjectException($"{name} has no promise list");
        ulong holder = allocate(24);
        Write(holder, new byte[24]);
        Write(person + (ulong)(O.Holder - shift), BitConverter.GetBytes(holder));
        return holder;
    }

    void CheckOwner(PersonOwner owner, PromiseRecord promise)
    {
        if (!owner.IsValid(memory, moduleBase)) throw new StaleObjectException($"{owner.Name} changed since it was read");
        if (promise.Person != PersonPart(owner)) throw new StaleObjectException("The promise belongs to someone else");
        var (_, _, begin, end, _) = Vector(HolderVector(owner.Address, owner.Shift) ?? 0);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0) throw new StaleObjectException("The person's promises changed since they were read");
        var raw = memory.Read(begin, (int)(end - begin));
        bool found = false;
        for (int i = 0; i < raw.Length; i += 8) if (BitConverter.ToUInt64(raw, i) == promise.Address) found = true;
        if (!found) throw new StaleObjectException("The promise is no longer in the person's list");
    }

    /// <summary>The person part a record points at: player + 0x288 for a player, the entry itself for staff.</summary>
    ulong PersonPart(PersonOwner owner) => owner.Address + (ulong)((profile.Relations?.PlayerPerson ?? 0x288) - owner.Shift);

    void Append(ulong vector, ulong record, Func<int, ulong> allocate)
    {
        var (_, _, begin, end, cap) = Vector(vector);
        if (begin != 0 && end >= begin && end < cap && (end - begin) % 8 == 0)
        {
            Write(end, BitConverter.GetBytes(record));
            Write(vector + 8, BitConverter.GetBytes(end + 8));
            return;
        }
        int count = begin == 0 ? 0 : (int)((end - begin) / 8), capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        var existing = count == 0 ? [] : memory.Read(begin, count * 8);
        Write(buffer, [.. existing, .. BitConverter.GetBytes(record)]);
        Write(vector, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)(count + 1) * 8), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
