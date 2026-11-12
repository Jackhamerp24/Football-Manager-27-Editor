using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Unhappiness motives: the full contract (player + contract.playerContract, contract vtable) points at
/// <see cref="Pointer"/> to a list header: int32 count at +0, then one pointer per motive from +8 (8 bytes each), each
/// to its own <see cref="RecordSize"/>-byte record. The records are separate allocations from a shared pool: the record
/// after one player's record can belong to another player. Measured on Steam 27.0.1 (game date 15/12/2026): Tahith
/// Chong's header holds 4 pointers to 0x...D38, 0x...D00, 0x...D70, 0x...D1C, and FMRTE lists the two open ones,
/// 802 with Jamal Baptiste and 902 with Matt Doherty, both from 2/12/2026; reading 4 records back to back from the
/// first pointer gave another player's 1202 and 629 instead. Measured on FM27 against FMRTE 27's Contract tab: Bruno Fernandes
/// "Unhappy at being stripped of the captaincy", Concerned, from 15/8/2026, no end date; Mauro Icardi has the same
/// motive ending on the game date (8/11/2026) and FMRTE shows him content. Players with 2-5 motives fill
/// count × 0x1C bytes exactly.
/// </summary>
public sealed record UnhappinessOffsets(int Pointer, int RecordSize, int Start, int End, int Item1, int Item2, int Motive, int Type)
{
    /// <summary>
    /// Record byte +0x1B (0, 4, 6, 0xC seen). Its meaning is unknown: an earlier reading took it for "FMRTE lists this
    /// motive", but that came from reading records back to back; with the pointer array, FMRTE lists Tahith Chong's two
    /// open motives whose byte is 0. Kept only for diagnosis; it does not decide what is shown.
    /// </summary>
    public int? Visible { get; init; }

    static readonly string[] Keys = ["pointer", "recordSize", "start", "end", "item1", "item2", "motive", "type"];

    internal static UnhappinessOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"contract.unhappiness.{key}: expected hex offset"), 16)).ToArray();
        int? visible = e.TryGetProperty("visible", out var vis) && vis.GetString() is { } vs && vs.StartsWith("0x") ? Convert.ToInt32(vs[2..], 16) : null;
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]) { Visible = visible };
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Pointer, RecordSize, Start, End, Item1, Item2, Motive, Type];
        w.WriteStartObject("unhappiness");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        if (Visible is { } visible) w.WriteString("visible", $"0x{visible:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// One unhappiness motive. Raw codes: <see cref="Motive"/> 600 = stripped of the captaincy; <see cref="Type"/> 1 =
/// Concerned. <see cref="End"/> null = no end date. Item IDs are -1 when unused (Bruno: 21506, not a club or player).
/// </summary>
public sealed record PlayerUnhappiness(int Index, DateOnly? Start, DateOnly? End, int Item1, int Item2, short Motive, byte Type, byte? Visible = null)
{
    /// <summary>
    /// FMRTE lists a motive while it has no end date or ends after the game date (Icardi, ending today: content).
    /// Checked on Steam 27.0.1, 15/12/2026: Tahith Chong (2 open of 4, both listed), Kylian Mbappe (2, both ended, none listed).
    /// </summary>
    public bool IsActive(DateOnly gameDate) => End is not { } e || e > gameDate;
}

/// <summary>One reversible removal: the header's pointer array before and after (records themselves are not touched).</summary>
public sealed class UnhappinessRemovalPlan(PlayerRecord player, ulong contract, ulong header, ulong records, int oldCount,
    int newCount, byte[] before, byte[] after)
{
    public PlayerRecord Player { get; } = player;
    public ulong Contract { get; } = contract;
    public ulong Header { get; } = header;
    public ulong Records { get; } = records;
    public int OldCount { get; } = oldCount;
    public int NewCount { get; } = newCount;
    internal byte[] Before { get; } = before;
    internal byte[] After { get; } = after;
    internal bool RecordsWritten { get; set; }
    internal bool CountWritten { get; set; }
}

public sealed class UnhappinessReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    internal const int MaxMotives = 32;

    /// <summary>Every motive in the list, ended ones included; filter with <see cref="PlayerUnhappiness.IsActive"/>.</summary>
    public IReadOnlyList<PlayerUnhappiness> Read(PlayerRecord player) =>
        Locate(player) is { } list ? Decode(ReadRecords(list), list.Offsets) : [];

    /// <summary>Each record's raw bytes, for diagnosis (calibrate dump); same order as <see cref="Read"/>.</summary>
    public IReadOnlyList<byte[]> ReadRaw(PlayerRecord player) =>
        Locate(player) is { } list ? list.Pointers.Select(r => memory.Read(r, list.Offsets.RecordSize)).ToList() : [];

    /// <param name="Pointers">One record address per motive, in list order.</param>
    internal sealed record List(ulong Header, IReadOnlyList<ulong> Pointers, UnhappinessOffsets Offsets)
    {
        public int Count => Pointers.Count;
        /// <summary>Address of the pointer array (header + 8).</summary>
        public ulong Slots => Header + 8;
        public byte[] SlotBytes() => [.. Pointers.SelectMany(BitConverter.GetBytes)];
    }

    /// <summary>All records of a list, concatenated in list order.</summary>
    internal byte[] ReadRecords(List list) => [.. list.Pointers.SelectMany(r => memory.Read(r, list.Offsets.RecordSize))];

    /// <summary>Null when the player has no full contract or no list, or the header does not look like one.</summary>
    internal List? Locate(PlayerRecord player)
    {
        if (profile.Contract is not { } c || profile.Unhappiness is not { } o) return null;
        var q = new byte[8];
        if (!memory.TryRead(player.Address, q) || BitConverter.ToUInt64(q) is var vt && !profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) return null;
        if (!memory.TryRead(player.Address + (ulong)c.PlayerContract, q) || BitConverter.ToUInt64(q) is var contract && contract == 0) return null;
        if (!memory.TryRead(contract, q) || BitConverter.ToUInt64(q) != moduleBase + c.ContractVtableRva) return null;
        if (!memory.TryRead(contract + (ulong)o.Pointer, q) || BitConverter.ToUInt64(q) is var header && header == 0) return null;
        var h = new byte[4];
        if (!memory.TryRead(header, h)) return null;
        int count = BitConverter.ToInt32(h);
        if (count < 0 || count > MaxMotives) return null;
        var slots = new byte[count * 8];
        if (count > 0 && !memory.TryRead(header + 8, slots)) return null;
        var pointers = Enumerable.Range(0, count).Select(i => BitConverter.ToUInt64(slots, i * 8)).ToList();
        if (pointers.Any(r => r == 0 || !memory.TryRead(r, new byte[o.RecordSize]))) return null;
        return new(header, pointers, o);
    }

    internal static List<PlayerUnhappiness> Decode(byte[] raw, UnhappinessOffsets o)
    {
        var result = new List<PlayerUnhappiness>();
        for (int i = 0; i * o.RecordSize < raw.Length; i++)
        {
            int b = i * o.RecordSize;
            result.Add(new(i, Date(raw, b + o.Start), Date(raw, b + o.End), BitConverter.ToInt32(raw, b + o.Item1),
                BitConverter.ToInt32(raw, b + o.Item2), BitConverter.ToInt16(raw, b + o.Motive), raw[b + o.Type],
                o.Visible is { } v && v < o.RecordSize ? raw[b + v] : null));
        }
        return result;
    }

    internal static DateOnly? Date(byte[] raw, int at)
    {
        int day = BitConverter.ToUInt16(raw, at) & 0x1FF, year = BitConverter.ToInt16(raw, at + 2);
        if (year <= 1900 || year > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }
}

/// <summary>
/// Removes active motives (FMRTE's Remove / Remove All on the Contract tab): the kept records are moved to the front and
/// the count is lowered; the buffer itself stays. Ended motives are kept. Guarded: player and contract vtables,
/// unchanged header and records, read-back.
/// </summary>
public sealed class UnhappinessWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int DayMask = 0x1FF;

    /// <summary>Captures active motives and the complete original list for guarded undo.</summary>
    public UnhappinessRemovalPlan? PlanRemoveActive(PlayerRecord player, DateOnly gameDate)
    {
        var c = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        var reader = new UnhappinessReader(memory, profile, moduleBase);
        var list = reader.Locate(player);
        if (list is null || list.Count == 0) return null;
        var kept = UnhappinessReader.Decode(reader.ReadRecords(list), list.Offsets).Where(m => !m.IsActive(gameDate)).ToArray();
        if (kept.Length == list.Count) return null;
        var before = list.SlotBytes();
        var after = (byte[])before.Clone();
        for (int i = 0; i < kept.Length; i++) BitConverter.GetBytes(list.Pointers[kept[i].Index]).CopyTo(after, i * 8);
        var plan = new UnhappinessRemovalPlan(player, memory.ReadUInt64(player.Address + (ulong)c.PlayerContract),
            list.Header, list.Slots, list.Count, kept.Length, before, after);
        GuardPlan(plan);
        return plan;
    }

    /// <summary>Reports the first successful write so a partially applied plan remains undoable.</summary>
    public void ApplyRemoval(UnhappinessRemovalPlan plan, Action? onChanged = null)
    {
        if (plan.RecordsWritten || plan.CountWritten) throw new InvalidOperationException("Removal has already started");
        GuardPlan(plan);
        bool recorded = false;
        void Changed() { if (!recorded) { recorded = true; onChanged?.Invoke(); } }
        if (!plan.Before.AsSpan().SequenceEqual(plan.After))
        {
            WriteChecked(plan.Records, plan.After);
            plan.RecordsWritten = true;
            Changed();
        }
        GuardPlan(plan);
        WriteChecked(plan.Header, BitConverter.GetBytes(plan.NewCount));
        plan.CountWritten = true;
        Changed();
    }

    /// <summary>Restores an applied or partially applied plan only while its data remains unchanged.</summary>
    public void UndoRemoval(UnhappinessRemovalPlan plan, Action? onRestored = null)
    {
        if (!plan.RecordsWritten && !plan.CountWritten) throw new InvalidOperationException("Removal has not been applied");
        GuardPlan(plan);
        if (plan.RecordsWritten)
        {
            WriteChecked(plan.Records, plan.Before);
            plan.RecordsWritten = false;
        }
        GuardPlan(plan);
        if (plan.CountWritten)
        {
            WriteChecked(plan.Header, BitConverter.GetBytes(plan.OldCount));
            plan.CountWritten = false;
        }
        onRestored?.Invoke();
    }

    /// <summary>What one motive row should hold after an edit; End null means no end date.</summary>
    public sealed record MotiveValues(short Motive, byte Type, DateOnly Start, DateOnly? End, int Item1 = -1);

    /// <summary>Changes one motive in place (motive, type, dates, person), keeping its other bytes. Guarded and read back.</summary>
    public void Edit(PlayerRecord player, PlayerUnhappiness row, MotiveValues values)
    {
        var (list, raw) = CurrentList(player);
        if (row.Index < 0 || row.Index >= list.Count) throw new StaleObjectException("Unhappiness list changed since it was read");
        var o = list.Offsets;
        var current = UnhappinessReader.Decode(raw, o)[row.Index];
        if (current != row) throw new StaleObjectException("Unhappiness row changed since it was read");
        var record = raw.AsSpan(row.Index * o.RecordSize, o.RecordSize).ToArray();
        Fill(record, o, values);
        WriteChecked(list.Pointers[row.Index], record);
    }

    /// <summary>
    /// Adds a motive. The header has no known spare room, so a new record and a new header (count + one more pointer)
    /// are written to blocks from <paramref name="allocate"/>, and the contract's pointer is switched to the new header;
    /// the old header and records stay as they are. The new record copies the first existing record (or zeros, with
    /// item2 -1) for the bytes whose meaning is unknown.
    /// </summary>
    public void Add(PlayerRecord player, MotiveValues values, Func<int, ulong> allocate)
    {
        var (list, raw) = CurrentList(player);
        var o = list.Offsets;
        if (list.Count >= UnhappinessReader.MaxMotives) throw new InvalidOperationException("The player has too many motives");
        var record = list.Count > 0 ? raw.AsSpan(0, o.RecordSize).ToArray() : new byte[o.RecordSize];
        if (list.Count == 0)
        {
            BitConverter.GetBytes(-1).CopyTo(record, o.Item2);
        }
        Fill(record, o, values);
        ulong recordBlock = allocate(o.RecordSize);
        WriteChecked(recordBlock, record);
        byte[] header = [.. BitConverter.GetBytes((long)(list.Count + 1)), .. list.SlotBytes(), .. BitConverter.GetBytes(recordBlock)];
        ulong headerBlock = allocate(header.Length);
        WriteChecked(headerBlock, header);
        var c = profile.Contract!;
        ulong slot = memory.ReadUInt64(player.Address + (ulong)c.PlayerContract) + (ulong)o.Pointer;
        if (memory.ReadUInt64(slot) != list.Header || BitConverter.ToInt32(memory.Read(list.Header, 4)) != list.Count ||
            !memory.Read(list.Slots, list.Count * 8).AsSpan().SequenceEqual(list.SlotBytes()))
            throw new StaleObjectException("Unhappiness list changed while adding");
        WriteChecked(slot, BitConverter.GetBytes(headerBlock));
    }

    static void Fill(byte[] record, UnhappinessOffsets o, MotiveValues values)
    {
        BitConverter.GetBytes(values.Motive).CopyTo(record, o.Motive);
        record[o.Type] = values.Type;
        PutDate(record, o.Start, values.Start);
        if (values.End is { } end) PutDate(record, o.End, end);
        else { BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(record, o.End) & ~DayMask) | 1)).CopyTo(record, o.End); BitConverter.GetBytes((short)1900).CopyTo(record, o.End + 2); }
        BitConverter.GetBytes(values.Item1).CopyTo(record, o.Item1);
    }

    static void PutDate(byte[] record, int at, DateOnly date)
    {
        if (date.Year is <= 1900 or > 2300) throw new ArgumentOutOfRangeException(nameof(date), "Date out of range");
        BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(record, at) & ~DayMask) | date.DayOfYear)).CopyTo(record, at);
        BitConverter.GetBytes((short)date.Year).CopyTo(record, at + 2);
    }

    (UnhappinessReader.List List, byte[] Raw) CurrentList(PlayerRecord player)
    {
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        var list = new UnhappinessReader(memory, profile, moduleBase).Locate(player)
            ?? throw new InvalidOperationException("The player has no contract with an unhappiness list");
        return (list, new UnhappinessReader(memory, profile, moduleBase).ReadRecords(list));
    }

    void GuardPlan(UnhappinessRemovalPlan plan)
    {
        var c = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
        var o = profile.Unhappiness ?? throw new InvalidOperationException("Profile has no unhappiness offsets");
        var p = plan.Player;
        if (p.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(p.Address) != moduleBase + p.VtableRva || !profile.PlayerVtableRvas.Contains(p.VtableRva) ||
            BitConverter.ToInt32(memory.Read(p.Address + (ulong)idOffset, 4)) != p.Id ||
            memory.ReadUInt64(p.Address + (ulong)c.PlayerContract) != plan.Contract ||
            memory.ReadUInt64(plan.Contract) != moduleBase + c.ContractVtableRva ||
            memory.ReadUInt64(plan.Contract + (ulong)o.Pointer) != plan.Header ||
            BitConverter.ToInt32(memory.Read(plan.Header, 4)) != (plan.CountWritten ? plan.NewCount : plan.OldCount) ||
            !memory.Read(plan.Records, plan.Before.Length).AsSpan().SequenceEqual(plan.RecordsWritten ? plan.After : plan.Before))
            throw new StaleObjectException("Unhappiness list or player changed since the plan was prepared");
    }

    void WriteChecked(ulong address, byte[] value)
    {
        if (!memory.TryWrite(address, value) || !memory.Read(address, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write unhappiness data at 0x{address:X}");
    }

    /// <summary>
    /// FMRTE's "end the motive": writes <paramref name="gameDate"/> into End of every record, ended ones included.
    /// Records and the count stay as they are, and the flag bits above the 9-bit day are kept, the way contract dates
    /// are written. Measured on Baptiste 28129876 (ended 18/10/2026) and Cooper 29170995 (ended 27/7/2026 and
    /// 18/10/2026): after FMRTE 27's Inspire both read 25/11/2026.
    /// A motive whose Start is after the game date keeps its End: writing the game date there would make End earlier
    /// than Start, which the game never produces. Returns how many records changed.
    /// </summary>
    public int EndMotives(PlayerRecord player, DateOnly gameDate)
    {
        if (gameDate.Year is < 1901 or > 2300) throw new ArgumentOutOfRangeException(nameof(gameDate), "Year is out of range");
        var o = profile.Unhappiness ?? throw new InvalidOperationException("Profile has no unhappiness offsets");
        var reader = new UnhappinessReader(memory, profile, moduleBase);
        if (reader.Locate(player) is not { Count: > 0 } list) return 0;

        var before = reader.ReadRecords(list);
        var changed = new List<(int At, byte[] Value)>();
        for (int i = 0; i < list.Count; i++)
        {
            int at = i * o.RecordSize + o.End;
            if (UnhappinessReader.Date(before, i * o.RecordSize + o.Start) is { } start && start > gameDate) continue;
            // Day word: 9 low bits are the day of the year, the flags above them must survive (contract dates keep them too).
            ushort day = (ushort)((BitConverter.ToUInt16(before, at) & ~DayMask) | (gameDate.DayOfYear & DayMask));
            byte[] value = [.. BitConverter.GetBytes(day), .. BitConverter.GetBytes((short)gameDate.Year)];
            if (!before.AsSpan(at, 4).SequenceEqual(value)) changed.Add((at, value));
        }
        if (changed.Count == 0) return 0;

        if (BitConverter.ToInt32(memory.Read(list.Header, 4)) != list.Count
            || !memory.Read(list.Slots, list.Count * 8).AsSpan().SequenceEqual(list.SlotBytes())
            || !reader.ReadRecords(list).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The unhappiness list changed while ending the motives");
        foreach (var (at, value) in changed) WriteChecked(list.Pointers[at / o.RecordSize] + (ulong)(at % o.RecordSize), value);
        return changed.Count;
    }

    /// <summary>Removes the active motives for which <paramref name="which"/> is true (all active ones when null).
    /// Returns how many were removed.</summary>
    public int RemoveActive(PlayerRecord player, DateOnly gameDate, Func<PlayerUnhappiness, bool>? which = null)
    {
        var reader = new UnhappinessReader(memory, profile, moduleBase);
        if (reader.Locate(player) is not { } list || list.Count == 0) return 0;
        var o = list.Offsets;
        var snapshot = reader.ReadRecords(list);
        var motives = UnhappinessReader.Decode(snapshot, o);
        var kept = motives.Where(m => !(m.IsActive(gameDate) && (which?.Invoke(m) ?? true))).ToList();
        int removed = motives.Count - kept.Count;
        if (removed == 0) return 0;

        if (BitConverter.ToInt32(memory.Read(list.Header, 4)) != list.Count
            || !memory.Read(list.Slots, list.Count * 8).AsSpan().SequenceEqual(list.SlotBytes())
            || !reader.ReadRecords(list).AsSpan().SequenceEqual(snapshot))
            throw new StaleObjectException("The unhappiness list changed while removing motives");
        // Only the pointer array is compacted; each record stays where it is (the pool holds other players' records too).
        byte[] packed = [.. kept.SelectMany(m => BitConverter.GetBytes(list.Pointers[m.Index]))];
        if (packed.Length > 0 && (!memory.TryWrite(list.Slots, packed) || !memory.Read(list.Slots, packed.Length).AsSpan().SequenceEqual(packed)))
            throw new MemoryAccessException($"Write at 0x{list.Slots:X} failed");
        var count = BitConverter.GetBytes(kept.Count);
        if (!memory.TryWrite(list.Header, count) || !memory.Read(list.Header, 4).AsSpan().SequenceEqual(count))
            throw new MemoryAccessException($"Write at 0x{list.Header:X} failed");
        return removed;
    }
}
