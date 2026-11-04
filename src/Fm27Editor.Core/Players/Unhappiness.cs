using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Unhappiness motives: the full contract (player + contract.playerContract, contract vtable) points at
/// <see cref="Pointer"/> to a list header (int32 count at +0, pointer to the records at +8); the records are
/// <see cref="RecordSize"/> bytes each, back to back. Measured on FM27 against FMRTE 27's Contract tab: Bruno Fernandes
/// "Unhappy at being stripped of the captaincy", Concerned, from 15/8/2026, no end date; Mauro Icardi has the same
/// motive ending on the game date (8/11/2026) and FMRTE shows him content. Players with 2-5 motives fill
/// count × 0x1C bytes exactly.
/// </summary>
public sealed record UnhappinessOffsets(int Pointer, int RecordSize, int Start, int End, int Item1, int Item2, int Motive, int Type)
{
    static readonly string[] Keys = ["pointer", "recordSize", "start", "end", "item1", "item2", "motive", "type"];

    internal static UnhappinessOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"contract.unhappiness.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Pointer, RecordSize, Start, End, Item1, Item2, Motive, Type];
        w.WriteStartObject("unhappiness");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// One unhappiness motive. Raw codes: <see cref="Motive"/> 600 = stripped of the captaincy; <see cref="Type"/> 1 =
/// Concerned. <see cref="End"/> null = no end date. Item IDs are -1 when unused (Bruno: 21506, not a club or player).
/// </summary>
public sealed record PlayerUnhappiness(int Index, DateOnly? Start, DateOnly? End, int Item1, int Item2, short Motive, byte Type)
{
    /// <summary>FMRTE lists a motive while it has no end date or ends after the game date (Icardi, ending today: content).</summary>
    public bool IsActive(DateOnly gameDate) => End is not { } e || e > gameDate;
}

/// <summary>One reversible removal, retaining the original and packed record bytes.</summary>
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
        Locate(player) is { } list ? Decode(memory.Read(list.Records, list.Count * list.Offsets.RecordSize), list.Offsets) : [];

    internal sealed record List(ulong Header, ulong Records, int Count, UnhappinessOffsets Offsets);

    /// <summary>Null when the player has no full contract or no list, or the header does not look like one.</summary>
    internal List? Locate(PlayerRecord player)
    {
        if (profile.Contract is not { } c || profile.Unhappiness is not { } o) return null;
        var q = new byte[8];
        if (!memory.TryRead(player.Address, q) || BitConverter.ToUInt64(q) is var vt && !profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) return null;
        if (!memory.TryRead(player.Address + (ulong)c.PlayerContract, q) || BitConverter.ToUInt64(q) is var contract && contract == 0) return null;
        if (!memory.TryRead(contract, q) || BitConverter.ToUInt64(q) != moduleBase + c.ContractVtableRva) return null;
        if (!memory.TryRead(contract + (ulong)o.Pointer, q) || BitConverter.ToUInt64(q) is var header && header == 0) return null;
        var h = new byte[16];
        if (!memory.TryRead(header, h)) return null;
        int count = BitConverter.ToInt32(h);
        ulong records = BitConverter.ToUInt64(h, 8);
        if (count < 0 || count > MaxMotives || (count > 0 && records == 0)) return null;
        return new(header, records, count, o);
    }

    internal static List<PlayerUnhappiness> Decode(byte[] raw, UnhappinessOffsets o)
    {
        var result = new List<PlayerUnhappiness>();
        for (int i = 0; i * o.RecordSize < raw.Length; i++)
        {
            int b = i * o.RecordSize;
            result.Add(new(i, Date(raw, b + o.Start), Date(raw, b + o.End), BitConverter.ToInt32(raw, b + o.Item1),
                BitConverter.ToInt32(raw, b + o.Item2), BitConverter.ToInt16(raw, b + o.Motive), raw[b + o.Type]));
        }
        return result;
    }

    static DateOnly? Date(byte[] raw, int at)
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
    /// <summary>Captures active motives and the complete original list for guarded undo.</summary>
    public UnhappinessRemovalPlan? PlanRemoveActive(PlayerRecord player, DateOnly gameDate)
    {
        var c = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        var list = new UnhappinessReader(memory, profile, moduleBase).Locate(player);
        if (list is null || list.Count == 0) return null;
        var before = memory.Read(list.Records, list.Count * list.Offsets.RecordSize);
        var kept = UnhappinessReader.Decode(before, list.Offsets).Where(m => !m.IsActive(gameDate)).ToArray();
        if (kept.Length == list.Count) return null;
        var after = (byte[])before.Clone();
        int size = list.Offsets.RecordSize;
        for (int i = 0; i < kept.Length; i++) before.AsSpan(kept[i].Index * size, size).CopyTo(after.AsSpan(i * size));
        var plan = new UnhappinessRemovalPlan(player, memory.ReadUInt64(player.Address + (ulong)c.PlayerContract),
            list.Header, list.Records, list.Count, kept.Length, before, after);
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
            memory.ReadUInt64(plan.Header + 8) != plan.Records ||
            !memory.Read(plan.Records, plan.Before.Length).AsSpan().SequenceEqual(plan.RecordsWritten ? plan.After : plan.Before))
            throw new StaleObjectException("Unhappiness list or player changed since the plan was prepared");
    }

    void WriteChecked(ulong address, byte[] value)
    {
        if (!memory.TryWrite(address, value) || !memory.Read(address, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write unhappiness data at 0x{address:X}");
    }

    /// <summary>Removes the active motives for which <paramref name="which"/> is true (all active ones when null).
    /// Returns how many were removed.</summary>
    public int RemoveActive(PlayerRecord player, DateOnly gameDate, Func<PlayerUnhappiness, bool>? which = null)
    {
        var reader = new UnhappinessReader(memory, profile, moduleBase);
        if (reader.Locate(player) is not { } list || list.Count == 0) return 0;
        var o = list.Offsets;
        var snapshot = memory.Read(list.Records, list.Count * o.RecordSize);
        var motives = UnhappinessReader.Decode(snapshot, o);
        var kept = motives.Where(m => !(m.IsActive(gameDate) && (which?.Invoke(m) ?? true))).ToList();
        int removed = motives.Count - kept.Count;
        if (removed == 0) return 0;

        if (BitConverter.ToInt32(memory.Read(list.Header, 4)) != list.Count || memory.ReadUInt64(list.Header + 8) != list.Records
            || !memory.Read(list.Records, snapshot.Length).AsSpan().SequenceEqual(snapshot))
            throw new StaleObjectException("The unhappiness list changed while removing motives");
        var packed = kept.SelectMany(m => snapshot.AsSpan(m.Index * o.RecordSize, o.RecordSize).ToArray()).ToArray();
        if (packed.Length > 0 && (!memory.TryWrite(list.Records, packed) || !memory.Read(list.Records, packed.Length).AsSpan().SequenceEqual(packed)))
            throw new MemoryAccessException($"Write at 0x{list.Records:X} failed");
        var count = BitConverter.GetBytes(kept.Count);
        if (!memory.TryWrite(list.Header, count) || !memory.Read(list.Header, 4).AsSpan().SequenceEqual(count))
            throw new MemoryAccessException($"Write at 0x{list.Header:X} failed");
        return removed;
    }
}
