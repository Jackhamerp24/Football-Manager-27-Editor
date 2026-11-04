using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Bans / unavailability: the injury holder (player + injuries.pointer) has a second vector of pointers at
/// <see cref="Vector"/>, each to a 0x20-byte record. Measured by lane C2 against FMRTE 27's Unavailability tab on Alex
/// Oxlade-Chamberlain (3 matches, all nation competitions) and Hiram Boateng (7 matches, sending off).
/// </summary>
public sealed record BanOffsets(int Vector, int RecordSize, int Item, int Scope, int Start, int Days, int Points,
    int Reason, int Matches, int Fulfilled)
{
    static readonly string[] Keys = ["vector", "recordSize", "item", "scope", "start", "days", "points", "reason", "matches", "fulfilled"];

    internal static BanOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.bans.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Vector, RecordSize, Item, Scope, Start, Days, Points, Reason, Matches, Fulfilled];
        w.WriteStartObject("bans");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// One ban as FMRTE's Unavailability tab shows it. Raw codes: <see cref="Scope"/> 10 = all nation competitions (with
/// <see cref="ItemId"/> the nation's ID); <see cref="Reason"/> -1 None, 2 Sending off. <see cref="Days"/> -1 = not a day ban.
/// </summary>
public sealed record PlayerBan(ulong Address, int ItemId, sbyte Scope, DateOnly? Start, short Days, sbyte Points,
    sbyte Reason, sbyte Matches, sbyte MatchesFulfilled, string Snapshot = "", ulong Holder = 0, ulong Begin = 0, ulong End = 0);

public sealed class BanRemovalPlan(PlayerRecord player, ulong holder, ulong begin, ulong end, ulong capacity,
    byte[] before, byte[] after, IReadOnlyList<PlayerBan> records, int kept)
{
    public PlayerRecord Player { get; } = player;
    public ulong Holder { get; } = holder;
    public ulong Begin { get; } = begin;
    public ulong End { get; } = end;
    public ulong Capacity { get; } = capacity;
    public int OldCount => Before.Length / 8;
    public int NewCount { get; } = kept;
    internal byte[] Before { get; } = before;
    internal byte[] After { get; } = after;
    internal IReadOnlyList<PlayerBan> Records { get; } = records;
    internal bool PointersWritten { get; set; }
    internal bool EndWritten { get; set; }
}

public sealed class BanReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxBans = 32;

    public IReadOnlyList<PlayerBan> Read(PlayerRecord player)
    {
        if (profile.Injuries is not { } i || profile.Bans is not { } o) return [];
        Span<byte> q = stackalloc byte[8];
        if (!memory.TryRead(player.Address, q)) return [];
        ulong vt = BitConverter.ToUInt64(q);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) return [];
        if (!memory.TryRead(player.Address + (ulong)i.Pointer, q) || BitConverter.ToUInt64(q) is var holder && holder == 0) return [];
        var header = new byte[16];
        if (!memory.TryRead(holder + (ulong)o.Vector, header)) return [];
        ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header, 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxBans) return [];
        var result = new List<PlayerBan>();
        var b = new byte[o.RecordSize];
        for (ulong p = begin; p < end; p += 8)
        {
            if (!memory.TryRead(p, q) || BitConverter.ToUInt64(q) is var record && record == 0 || !memory.TryRead(record, b)) continue;
            result.Add(new(record, BitConverter.ToInt32(b, o.Item), (sbyte)b[o.Scope],
                Date(BitConverter.ToUInt16(b, o.Start), BitConverter.ToInt16(b, o.Start + 2)), BitConverter.ToInt16(b, o.Days),
                (sbyte)b[o.Points], (sbyte)b[o.Reason], (sbyte)b[o.Matches], (sbyte)b[o.Fulfilled], Convert.ToHexString(b), holder, begin, end));
        }
        return result;
    }

    static DateOnly? Date(ushort raw, short year)
    {
        int day = raw & 0x1FF;
        if (year <= 1900 || year > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }
}

/// <summary>
/// Remove bans, as the user defined it: match bans only (red/yellow card bans: <see cref="PlayerBan.Matches"/> above 0 and
/// no day count). Day and global bans (Mykhaylo Mudryk: 595 days, scope 25) are kept. The kept pointers are moved to the
/// front and the vector end is pulled back; removed records are not freed (each is referenced only from this vector).
/// Guarded: player vtable, unchanged pointer array, read-back.
/// </summary>
public sealed class BanWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public BanRemovalPlan? PlanRemoveMatchBans(PlayerRecord player)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        ulong holder = memory.ReadUInt64(player.Address + (ulong)i.Pointer);
        if (holder == 0) return null;
        ulong header = holder + (ulong)o.Vector;
        var vector = memory.Read(header, 24);
        ulong begin = BitConverter.ToUInt64(vector), end = BitConverter.ToUInt64(vector, 8), cap = BitConverter.ToUInt64(vector, 16);
        if (begin == 0 && end == 0 && cap == 0 || begin != 0 && end == begin) return null;
        if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 > 32)
            throw new StaleObjectException("The ban list looks invalid");
        var before = memory.Read(begin, (int)(end - begin));
        var records = new BanReader(memory, profile, moduleBase).Read(player);
        if (records.Count != before.Length / 8 || records.Where((b, index) => b.Address != BitConverter.ToUInt64(before, index * 8)).Any())
            throw new StaleObjectException("The ban list could not be read completely");
        var kept = records.Where(b => !IsMatchBan(b)).ToArray();
        if (kept.Length == records.Count) return null;
        var after = (byte[])before.Clone();
        for (int index = 0; index < kept.Length; index++) BitConverter.GetBytes(kept[index].Address).CopyTo(after, index * 8);
        var plan = new BanRemovalPlan(player, holder, begin, end, cap, before, after, records, kept.Length);
        GuardRemoval(plan, i, o);
        return plan;
    }

    public void ApplyRemoval(BanRemovalPlan plan, Action? onChanged = null)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        if (plan.PointersWritten || plan.EndWritten) throw new InvalidOperationException("Removal has already started");
        GuardRemoval(plan, i, o);
        bool recorded = false;
        void Changed() { if (!recorded) { recorded = true; onChanged?.Invoke(); } }
        if (!plan.Before.AsSpan().SequenceEqual(plan.After))
        {
            Write(plan.Begin, plan.After);
            plan.PointersWritten = true;
            Changed();
        }
        GuardRemoval(plan, i, o);
        Write(plan.Holder + (ulong)o.Vector + 8, BitConverter.GetBytes(plan.Begin + (ulong)plan.NewCount * 8));
        plan.EndWritten = true;
        Changed();
    }

    public void UndoRemoval(BanRemovalPlan plan, Action? onRestored = null)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        if (!plan.PointersWritten && !plan.EndWritten) throw new InvalidOperationException("Removal has not been applied");
        GuardRemoval(plan, i, o);
        if (plan.PointersWritten) { Write(plan.Begin, plan.Before); plan.PointersWritten = false; }
        GuardRemoval(plan, i, o);
        if (plan.EndWritten) { Write(plan.Holder + (ulong)o.Vector + 8, BitConverter.GetBytes(plan.End)); plan.EndWritten = false; }
        onRestored?.Invoke();
    }

    void GuardRemoval(BanRemovalPlan plan, InjuryOffsets i, BanOffsets o)
    {
        var p = plan.Player;
        ulong header = plan.Holder + (ulong)o.Vector;
        if (p.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(p.Address) != moduleBase + p.VtableRva || !profile.PlayerVtableRvas.Contains(p.VtableRva) ||
            BitConverter.ToInt32(memory.Read(p.Address + (ulong)idOffset, 4)) != p.Id ||
            memory.ReadUInt64(p.Address + (ulong)i.Pointer) != plan.Holder ||
            memory.ReadUInt64(header) != plan.Begin || memory.ReadUInt64(header + 8) != (plan.EndWritten ? plan.Begin + (ulong)plan.NewCount * 8 : plan.End) ||
            memory.ReadUInt64(header + 16) != plan.Capacity ||
            !memory.Read(plan.Begin, plan.Before.Length).AsSpan().SequenceEqual(plan.PointersWritten ? plan.After : plan.Before) ||
            plan.Records.Any(b => !memory.Read(b.Address, o.RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(b.Snapshot))))
            throw new StaleObjectException("Ban list or player changed since the plan was prepared");
    }

    public void SetMatches(PlayerRecord player, PlayerBan ban, int matches, int fulfilled)
    {
        if (matches is < 1 or > 127 || fulfilled < 0 || fulfilled > matches) throw new ArgumentOutOfRangeException(nameof(matches));
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        if (o.Fulfilled != o.Matches + 1 || o.Matches < 0 || o.Fulfilled >= o.RecordSize) throw new InvalidOperationException("Unsupported match ban layout");
        var before = Convert.FromHexString(ban.Snapshot);
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r) || !IsMatchBan(ban)
            || before.Length != o.RecordSize || ban.Holder == 0 || ban.Begin == 0 || ban.End <= ban.Begin
            || ban.End - ban.Begin > 32 * 8 || (ban.End - ban.Begin) % 8 != 0
            || memory.ReadUInt64(player.Address + (ulong)i.Pointer) != ban.Holder
            || memory.ReadUInt64(ban.Holder + (ulong)o.Vector) != ban.Begin || memory.ReadUInt64(ban.Holder + (ulong)o.Vector + 8) != ban.End
            || !Enumerable.Range(0, (int)((ban.End - ban.Begin) / 8)).Any(k => memory.ReadUInt64(ban.Begin + (ulong)k * 8) == ban.Address)
            || !memory.Read(ban.Address, o.RecordSize).AsSpan().SequenceEqual(before)
            || (sbyte)before[o.Matches] != ban.Matches || (sbyte)before[o.Fulfilled] != ban.MatchesFulfilled)
            throw new StaleObjectException("The match ban changed since it was read");
        byte[] next = [(byte)matches, (byte)fulfilled];
        ulong at = ban.Address + (ulong)o.Matches;
        if (!memory.TryWrite(at, next) || !memory.Read(at, 2).AsSpan().SequenceEqual(next)) throw new MemoryAccessException("Could not write match ban");
    }

    public static bool IsMatchBan(PlayerBan ban) => ban.Matches > 0 && ban.Days < 0;

    /// <summary>
    /// FMRTE 27's new-ban record (Kobbie Mainoo, 2/11/2026): item −1, +0x4 FF, scope 25, start 25/11/2026 with no flag bits,
    /// +0x14 0x000A, days −1, points 2, reason 2, +0x1B FF, 3 matches. Fields named in <see cref="BanOffsets"/> are
    /// overwritten; the other bytes stay as FMRTE wrote them (+0x14 is 0x0A or 0x05 in game records, meaning unknown).
    /// </summary>
    const string NewBanTemplate =
        "FFFFFFFFFF1900000000000000000000" +
        "4901EA070A00FFFF020002FF03000000";

    /// <summary>The field values of a ban, as FMRTE's Unavailability card edits them.</summary>
    public sealed record BanFields(int Scope, int Item, int Reason, int Points, DateOnly Start, int Days, int Matches, int Fulfilled);

    /// <summary>
    /// Writes Scope, Item, Reason, Number of points, Start Date, Number of Days and the two match counts in one guarded
    /// write. FMRTE 27 wrote these at the profile offsets (2/11/2026, Mainoo): scope codes 1 Competition, 5 Domestic Cup,
    /// 6 Domestic League, 15 All continent competitions, 20 All international matches, 25 Global Ban, 26 … 39; reason
    /// 0–5 in dropdown order, None −1; the start date's day-of-year without flag bits.
    /// </summary>
    public void SetFields(PlayerRecord player, PlayerBan ban, BanFields fields)
    {
        var o = Require(player, ban, out var before);
        var after = Fill((byte[])before.Clone(), o, fields);
        Write(ban.Address, after);
    }

    /// <summary>
    /// Adds a ban like FMRTE 27: a new 0x20-byte record (<see cref="NewBanTemplate"/> with the given fields) and its pointer
    /// appended to the holder's ban vector; a full vector moves to a new buffer. Both live outside the game's allocator.
    /// The player must already have the injury/ban holder (FMRTE creates one, but an injury added under a holder it made
    /// had no effect in game, so that path is not copied).
    /// </summary>
    public void Add(PlayerRecord player, IReadOnlyList<PlayerBan> current, BanFields fields, Func<int, ulong> allocate)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) throw new StaleObjectException("The player changed since it was read");
        ulong holder = memory.ReadUInt64(player.Address + (ulong)i.Pointer);
        if (holder == 0) throw new InvalidOperationException("This player has no injury/ban record holder yet");
        ulong header = holder + (ulong)o.Vector;
        var h = memory.Read(header, 24);
        ulong begin = BitConverter.ToUInt64(h, 0), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        if (end < begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 != (ulong)current.Count
            || current.Select((b, k) => memory.ReadUInt64(begin + (ulong)k * 8) != b.Address || !memory.Read(b.Address, o.RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(b.Snapshot))).Any(bad => bad))
            throw new StaleObjectException("The player's bans changed since they were read");
        var template = Convert.FromHexString(NewBanTemplate);
        if (template.Length != o.RecordSize) throw new InvalidOperationException("Unsupported ban layout");
        var record = Fill(template, o, fields);
        ulong at = allocate(o.RecordSize);
        Write(at, record);
        if (begin != 0 && end < cap)
        {
            Write(end, BitConverter.GetBytes(at));
            Write(header + 8, BitConverter.GetBytes(end + 8));
            return;
        }
        int count = current.Count, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        Write(buffer, [.. current.SelectMany(b => BitConverter.GetBytes(b.Address)), .. BitConverter.GetBytes(at)]);
        Write(header, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)(count + 1) * 8), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    /// <summary>Removes one ban like FMRTE 27's −: later pointers move up, the end pointer moves back 8; the record stays.</summary>
    public void Remove(PlayerRecord player, PlayerBan ban)
    {
        var o = Require(player, ban, out _);
        int count = (int)((ban.End - ban.Begin) / 8);
        var pointers = memory.Read(ban.Begin, count * 8);
        int index = Enumerable.Range(0, count).First(k => BitConverter.ToUInt64(pointers, k * 8) == ban.Address);
        if (index < count - 1) Write(ban.Begin + (ulong)index * 8, pointers[((index + 1) * 8)..]);
        Write(ban.Holder + (ulong)o.Vector + 8, BitConverter.GetBytes(ban.End - 8));
    }

    static byte[] Fill(byte[] b, BanOffsets o, BanFields f)
    {
        if (f.Scope is < 0 or > 127 || f.Reason is < -1 or > 127 || f.Points is < 0 or > 127 || f.Days is < -1 or > short.MaxValue
            || f.Matches is < 0 or > 127 || f.Fulfilled < 0 || f.Fulfilled > Math.Max(f.Matches, 0) || f.Start.Year is < 1901 or > 2300)
            throw new ArgumentOutOfRangeException(nameof(f));
        BitConverter.GetBytes(f.Item).CopyTo(b, o.Item);
        b[o.Scope] = (byte)f.Scope;
        // Low 9 bits are the day of year; the bits above are flags the game sets (0x1A on Oxlade) and are kept.
        BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(b, o.Start) & ~0x1FF) | f.Start.DayOfYear)).CopyTo(b, o.Start);
        BitConverter.GetBytes((short)f.Start.Year).CopyTo(b, o.Start + 2);
        BitConverter.GetBytes((short)f.Days).CopyTo(b, o.Days);
        b[o.Points] = (byte)f.Points;
        b[o.Reason] = (byte)(sbyte)f.Reason;
        b[o.Matches] = (byte)f.Matches;
        b[o.Fulfilled] = (byte)f.Fulfilled;
        return b;
    }

    /// <summary>Player vtable, unchanged holder and vector, ban still listed and byte-for-byte as read.</summary>
    BanOffsets Require(PlayerRecord player, PlayerBan ban, out byte[] before)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        before = Convert.FromHexString(ban.Snapshot);
        ulong vt = memory.ReadUInt64(player.Address);
        var b = ban;
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)
            || before.Length != o.RecordSize || b.Holder == 0 || b.Begin == 0 || b.End <= b.Begin || b.End - b.Begin > 32 * 8 || (b.End - b.Begin) % 8 != 0
            || memory.ReadUInt64(player.Address + (ulong)i.Pointer) != b.Holder
            || memory.ReadUInt64(b.Holder + (ulong)o.Vector) != b.Begin || memory.ReadUInt64(b.Holder + (ulong)o.Vector + 8) != b.End
            || !Enumerable.Range(0, (int)((b.End - b.Begin) / 8)).Any(k => memory.ReadUInt64(b.Begin + (ulong)k * 8) == b.Address)
            || !memory.Read(b.Address, o.RecordSize).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The ban changed since it was read");
        return o;
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }

    /// <summary>Returns how many match bans were removed.</summary>
    public int RemoveMatchBans(PlayerRecord player)
    {
        var i = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        var o = profile.Bans ?? throw new InvalidOperationException("Profile has no ban offsets");
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        ulong holder = memory.ReadUInt64(player.Address + (ulong)i.Pointer);
        if (holder == 0) return 0;
        ulong header = holder + (ulong)o.Vector;
        ulong begin = memory.ReadUInt64(header), end = memory.ReadUInt64(header + 8);
        if (begin == 0 || end == begin) return 0;
        if (end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 32) throw new StaleObjectException("The ban list looks invalid");

        var snapshot = memory.Read(begin, (int)(end - begin));
        var bans = new BanReader(memory, profile, moduleBase).Read(player).ToDictionary(b => b.Address);
        var kept = new List<ulong>();
        for (int k = 0; k < snapshot.Length; k += 8)
        {
            ulong record = BitConverter.ToUInt64(snapshot, k);
            if (!(bans.TryGetValue(record, out var ban) && IsMatchBan(ban))) kept.Add(record);
        }
        int removed = snapshot.Length / 8 - kept.Count;
        if (removed == 0) return 0;

        if (!memory.Read(begin, snapshot.Length).AsSpan().SequenceEqual(snapshot) || memory.ReadUInt64(header + 8) != end)
            throw new StaleObjectException("The ban list changed while removing bans");
        var packed = kept.SelectMany(BitConverter.GetBytes).ToArray();
        if (packed.Length > 0 && (!memory.TryWrite(begin, packed) || !memory.Read(begin, packed.Length).AsSpan().SequenceEqual(packed)))
            throw new MemoryAccessException($"Write at 0x{begin:X} failed");
        var newEnd = BitConverter.GetBytes(begin + (ulong)packed.Length);
        if (!memory.TryWrite(header + 8, newEnd) || !memory.Read(header + 8, 8).AsSpan().SequenceEqual(newEnd))
            throw new MemoryAccessException($"Write at 0x{header + 8:X} failed");
        return removed;
    }
}
