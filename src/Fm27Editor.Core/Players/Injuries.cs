using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Player → holder (player + Pointer) whose first field is a vector of pointers to injury records. Measured by lane C2
/// and checked against FMRTE 27 on Tyrese Campbell (Concussion, 14 + 14 days) and Jay Tinsdale (torn anterior cruciate
/// ligament, 538 + 508 days). <see cref="Treatment"/> is a byte of <see cref="InjuryTreatment"/> bits.
/// </summary>
public sealed record InjuryOffsets(int Pointer, int RecordSize, int Type, int Start, int MinimumDays, int ExtraDays,
    uint TypeVtable, int TypeId, int TypeName, int Treatment)
{
    static readonly string[] Keys = ["pointer", "recordSize", "type", "start", "minimumDays", "extraDays", "typeVtable", "typeId", "typeName", "treatment"];

    internal static InjuryOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.injuries.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], (uint)v[6], v[7], v[8], v[9]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Pointer, RecordSize, Type, Start, MinimumDays, ExtraDays, TypeVtable, TypeId, TypeName, Treatment];
        w.WriteStartObject("injuries");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// FMRTE 27's four treatment boxes, checked one bit at a time (Bašić 01 Physio, Davies 02 Specialist, Eriksen 08 Isolation;
/// writing 04 showed Injection, 03 showed Physio and Specialist). The game also sets 0x10 and 0x20, which have no box.
/// </summary>
[Flags]
public enum InjuryTreatment : byte { None = 0, Physio = 0x01, Specialist = 0x02, Injection = 0x04, Isolation = 0x08 }

/// <summary>One injury as FMRTE's Injury tab shows it. <see cref="Name"/> is the game's own text (lower case).</summary>
public sealed record PlayerInjury(ulong Address, int TypeId, string? Name, DateOnly? Start, int MinimumDays, int ExtraDays,
    string Snapshot = "", ulong Holder = 0, ulong Begin = 0, ulong End = 0)
{
    public InjuryTreatment Treatment { get; init; }
}

/// <summary>A reversible removal of the injury-vector entries of one player.</summary>
public sealed record InjuryRemovalPlan(PlayerRecord Player, ulong Holder, ulong Begin, ulong End, ulong Capacity,
    byte[] Pointers, IReadOnlyList<PlayerInjury> Injuries);

/// <summary>
/// Removes every injury of a player by emptying the vector (end = begin). Records are not freed: in FM27 RAM each record
/// and its holder are referenced only from that vector and player+0xF8 (checked on Tyrese Campbell), so nothing is left
/// pointing at a removed entry. Guarded: player vtable, unchanged holder and vector, read-back.
/// </summary>
public sealed class InjuryWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Capture the full vector and injury records before healing, without writing.</summary>
    public InjuryRemovalPlan? PlanRemoveAll(PlayerRecord player)
    {
        var o = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        ulong holder = memory.ReadUInt64(player.Address + (ulong)o.Pointer);
        if (holder == 0) return null;
        var header = memory.Read(holder, 24);
        ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header, 8), capacity = BitConverter.ToUInt64(header, 16);
        if (begin == 0 && end == 0 && capacity == 0 || begin != 0 && end == begin) return null;
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 8 != 0 || (end - begin) / 8 > 32)
            throw new StaleObjectException("The injury list looks invalid");
        var pointers = memory.Read(begin, (int)(end - begin));
        var injuries = new InjuryReader(memory, profile, moduleBase).Read(player);
        if (injuries.Count != pointers.Length / 8 || injuries.Where((item, i) => item.Address != BitConverter.ToUInt64(pointers, i * 8)).Any())
            throw new StaleObjectException("The injury list could not be read completely");
        var plan = new InjuryRemovalPlan(player, holder, begin, end, capacity, pointers, injuries);
        GuardRemoval(plan, o, removed: false);
        return plan;
    }

    public void ApplyRemoval(InjuryRemovalPlan plan, Action? onWritten = null) => WriteRemoval(plan, removed: false, onWritten);
    public void UndoRemoval(InjuryRemovalPlan plan, Action? onWritten = null) => WriteRemoval(plan, removed: true, onWritten);

    void WriteRemoval(InjuryRemovalPlan plan, bool removed, Action? onWritten)
    {
        var o = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        GuardRemoval(plan, o, removed);
        var next = BitConverter.GetBytes(removed ? plan.End : plan.Begin);
        if (!memory.TryWrite(plan.Holder + 8, next) || !memory.Read(plan.Holder + 8, 8).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not update the injury list");
        onWritten?.Invoke();
    }

    void GuardRemoval(InjuryRemovalPlan plan, InjuryOffsets o, bool removed)
    {
        var p = plan.Player;
        if (p.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            memory.ReadUInt64(p.Address) != moduleBase + p.VtableRva || !profile.PlayerVtableRvas.Contains(p.VtableRva) ||
            BitConverter.ToInt32(memory.Read(p.Address + (ulong)idOffset, 4)) != p.Id ||
            memory.ReadUInt64(p.Address + (ulong)o.Pointer) != plan.Holder ||
            memory.ReadUInt64(plan.Holder) != plan.Begin || memory.ReadUInt64(plan.Holder + 8) != (removed ? plan.Begin : plan.End) ||
            memory.ReadUInt64(plan.Holder + 16) != plan.Capacity ||
            !memory.Read(plan.Begin, plan.Pointers.Length).AsSpan().SequenceEqual(plan.Pointers) ||
            plan.Injuries.Any(i => !memory.Read(i.Address, o.RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(i.Snapshot))))
            throw new StaleObjectException("Injury list or player changed since the plan was prepared");
    }

    public void SetDuration(PlayerRecord player, PlayerInjury injury, int minimumDays, int extraDays)
    {
        if (minimumDays is < 0 or > ushort.MaxValue || extraDays is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(minimumDays));
        var o = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        if (o.ExtraDays != o.MinimumDays + 2 || o.MinimumDays < 0 || o.ExtraDays + 2 > o.RecordSize)
            throw new InvalidOperationException("Unsupported injury duration layout");
        Guard(player, injury, o);
        byte[] next = [..BitConverter.GetBytes((ushort)minimumDays), ..BitConverter.GetBytes((ushort)extraDays)];
        ulong at = injury.Address + (ulong)o.MinimumDays;
        if (!memory.TryWrite(at, next) || !memory.Read(at, 4).AsSpan().SequenceEqual(next))
            throw new MemoryAccessException("Could not write injury duration");
    }

    /// <summary>Sets FMRTE's four boxes; the bits the game uses without a box (0x10, 0x20) are kept.</summary>
    public void SetTreatment(PlayerRecord player, PlayerInjury injury, InjuryTreatment treatment)
    {
        const byte boxes = 0x0F;
        if (((byte)treatment & ~boxes) != 0) throw new ArgumentOutOfRangeException(nameof(treatment));
        var o = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        if (o.Treatment < 0 || o.Treatment >= o.RecordSize) throw new InvalidOperationException("Unsupported injury treatment layout");
        var before = Guard(player, injury, o);
        byte[] next = [(byte)((before[o.Treatment] & ~boxes) | (byte)treatment)];
        ulong at = injury.Address + (ulong)o.Treatment;
        if (!memory.TryWrite(at, next) || memory.Read(at, 1)[0] != next[0])
            throw new MemoryAccessException("Could not write injury treatment");
    }

    /// <summary>Player vtable, holder, vector membership and the whole record must still match the snapshot.</summary>
    byte[] Guard(PlayerRecord player, PlayerInjury injury, InjuryOffsets o)
    {
        var before = Convert.FromHexString(injury.Snapshot);
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)
            || injury.Holder == 0 || injury.Begin == 0 || injury.End <= injury.Begin
            || injury.End - injury.Begin > 32 * 8 || (injury.End - injury.Begin) % 8 != 0
            || before.Length != o.RecordSize
            || memory.ReadUInt64(player.Address + (ulong)o.Pointer) != injury.Holder
            || memory.ReadUInt64(injury.Holder) != injury.Begin || memory.ReadUInt64(injury.Holder + 8) != injury.End
            || !Enumerable.Range(0, (int)((injury.End - injury.Begin) / 8)).Any(i => memory.ReadUInt64(injury.Begin + (ulong)i * 8) == injury.Address)
            || !memory.Read(injury.Address, o.RecordSize).AsSpan().SequenceEqual(before)
            || BitConverter.ToUInt16(before, o.MinimumDays) != injury.MinimumDays || BitConverter.ToUInt16(before, o.ExtraDays) != injury.ExtraDays
            || memory.ReadUInt64(BitConverter.ToUInt64(before, o.Type)) != moduleBase + o.TypeVtable)
            throw new StaleObjectException("The injury changed since it was read");
        return before;
    }

    /// <summary>Returns how many injuries were removed (0 when the player had none).</summary>
    public int RemoveAll(PlayerRecord player)
    {
        var o = profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        ulong holder = memory.ReadUInt64(player.Address + (ulong)o.Pointer);
        if (holder == 0) return 0;
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8);
        if (begin == 0 || end == begin) return 0;
        if (end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 32) throw new StaleObjectException("The injury list looks invalid");
        var expected = BitConverter.GetBytes(end);
        if (!memory.Read(holder + 8, 8).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("The injury list changed while healing");
        var value = BitConverter.GetBytes(begin);
        if (!memory.TryWrite(holder + 8, value) || !memory.Read(holder + 8, 8).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Write at 0x{holder + 8:X} failed");
        return (int)((end - begin) / 8);
    }
}

/// <summary>Reads injuries; removal is <see cref="InjuryWriter"/>.</summary>
public sealed class InjuryReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxInjuries = 32, MaxName = 200;

    public IReadOnlyList<PlayerInjury> Read(PlayerRecord player)
    {
        if (profile.Injuries is not { } o || !Q(player.Address, out ulong vt) || !profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) return [];
        if (!Q(player.Address + (ulong)o.Pointer, out ulong holder) || holder == 0) return [];
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || begin == 0) return [];
        if (end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxInjuries) return [];
        var result = new List<PlayerInjury>();
        var b = new byte[o.RecordSize];
        for (ulong p = begin; p < end; p += 8)
        {
            if (!Q(p, out ulong record) || record == 0 || !memory.TryRead(record, b)) continue;
            ulong type = BitConverter.ToUInt64(b, o.Type);
            if (!Q(type, out ulong typeVt) || typeVt != moduleBase + o.TypeVtable) continue;
            var t = new byte[Math.Max(o.TypeId + 4, o.TypeName + 8)];
            if (!memory.TryRead(type, t)) continue;
            result.Add(new(record, BitConverter.ToInt32(t, o.TypeId), StringAt(BitConverter.ToUInt64(t, o.TypeName)),
                Date(BitConverter.ToUInt16(b, o.Start), BitConverter.ToInt16(b, o.Start + 2)),
                BitConverter.ToUInt16(b, o.MinimumDays), BitConverter.ToUInt16(b, o.ExtraDays), Convert.ToHexString(b), holder, begin, end)
                { Treatment = (InjuryTreatment)(b[o.Treatment] & 0x0F) });
        }
        return result;
    }

    static DateOnly? Date(ushort raw, short year)
    {
        int day = raw & 0x1FF;
        if (year <= 1900 || year > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }

    string? StringAt(ulong p)
    {
        if (p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
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
/// Heal, as the user defined it: remove every injury, Condition (= FMRTE's Match Exercise) and Match Sharpness to 10000,
/// Jadedness to -500. Each step is guarded; a failure stops before the remaining steps.
/// </summary>
public sealed class PlayerHealer(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int FullCondition = 10000, FullSharpness = 10000, FreshJadedness = -500;

    public void Heal(PlayerRecord player)
    {
        new InjuryWriter(memory, profile, moduleBase).RemoveAll(player);
        var fields = new PlayerFieldAccess(memory, profile, moduleBase);
        foreach (var (field, value) in new[] { (PlayerField.Condition, FullCondition), (PlayerField.MatchSharpness, FullSharpness), (PlayerField.Jadedness, FreshJadedness) })
            if (fields.Read(player, field) is { } current && current != value) fields.Set(player, field, current, value);
    }
}

/// <summary>Inspire, as the user defined it: Morale to its highest value (20 on the game's 1–20 scale).</summary>
public sealed class PlayerInspirer(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int HighestMorale = 20;

    public void Inspire(PlayerRecord player)
    {
        var fields = new PlayerFieldAccess(memory, profile, moduleBase);
        long current = fields.Read(player, PlayerField.Morale) ?? throw new InvalidOperationException("Morale is not readable for this player");
        if (current != HighestMorale) fields.Set(player, PlayerField.Morale, current, HighestMorale);
    }
}

/// <summary>
/// Destroy: Morale to 0, Condition and Match Sharpness to 5000,
/// Jadedness (FM26 IGE's "Fatigue") to 1000 — the lowest condition/sharpness and the highest fatigue FM26's official
/// In-Game Editor allows. Injuries are not added. Each step is guarded; fields already at the target are not written.
/// </summary>
public sealed class PlayerDestroyer(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int LowestMorale = 0, LowestCondition = 5000, LowestSharpness = 5000, MostJaded = 1000;

    public void Destroy(PlayerRecord player)
    {
        var fields = new PlayerFieldAccess(memory, profile, moduleBase);
        foreach (var (field, value) in new[]
                 {
                     (PlayerField.Morale, LowestMorale), (PlayerField.Condition, LowestCondition),
                     (PlayerField.MatchSharpness, LowestSharpness), (PlayerField.Jadedness, MostJaded),
                 })
            if (fields.Read(player, field) is { } current && current != value) fields.Set(player, field, current, value);
    }
}
