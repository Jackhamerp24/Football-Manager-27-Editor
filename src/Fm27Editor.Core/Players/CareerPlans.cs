using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Career Plans: player + <see cref="Container"/> → container whose first field is a vector of pointers to 16-byte plans
/// (item*, condition, scope, job, item type, int32 always 2 in game rows). Item types: 3 club, 5 continent, 9 nation (the
/// item is the nation's <c>+0x108</c> football object), 25 competition. Measured on Bruno Fernandes against FMRTE 27
/// (2/11/2026) and over all 944 game plans on the 15/8/2026 save.
/// </summary>
public sealed record CareerPlanOffsets(int Container, int RecordSize, int Item, int Condition, int Scope, int Job, int ItemType, int Source,
    uint ContinentVtable, int ContinentName, int NationContinent)
{
    static readonly string[] Keys = ["container", "recordSize", "item", "condition", "scope", "job", "itemType", "source", "continentVtable", "continentName", "nationContinent"];

    internal static CareerPlanOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.careerPlans.{key}: expected hex"), 16)).ToArray();
        return new((int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (int)v[7], (uint)v[8], (int)v[9], (int)v[10]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Container, RecordSize, Item, Condition, Scope, Job, ItemType, Source, ContinentVtable, ContinentName, NationContinent];
        w.WriteStartObject("careerPlans");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

public enum CareerItemType : byte { Club = 3, Continent = 5, Nation = 9, Competition = 25 }

/// <summary>Something a plan can name: a club, continent, nation (its football object) or competition.</summary>
public sealed record CareerItem(ulong Address, CareerItemType Type, string Name);

/// <summary>One row of FMRTE's Career Plans list. Codes are the game's (FMRTE 26 enums, confirmed by FMRTE 27 writes).</summary>
public sealed record CareerPlan(ulong Address, ulong ItemAddress, int Condition, int Scope, int Job, int ItemType, string? ItemName,
    string Snapshot, ulong Begin, ulong End);

public sealed record CareerPlanFields(int Condition, int Scope, int Job, CareerItem Item);

/// <summary>List and owning vector snapshot, retained even when the vector contains no plans.</summary>
sealed class CareerPlanSnapshotList(ulong owner, int shift, ulong container, ulong begin, ulong end, ulong capacity,
    IReadOnlyList<CareerPlan> plans) : IReadOnlyList<CareerPlan>
{
    readonly CareerPlan[] _plans = plans.ToArray();
    public ulong Owner { get; } = owner;
    public int Shift { get; } = shift;
    public ulong Container { get; } = container;
    public ulong Begin { get; } = begin;
    public ulong End { get; } = end;
    public ulong Capacity { get; } = capacity;
    public int Count => _plans.Length;
    public CareerPlan this[int index] => _plans[index];
    public IEnumerator<CareerPlan> GetEnumerator() => ((IEnumerable<CareerPlan>)_plans).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class CareerPlanReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxPlans = 50, MaxName = 200;

    /// <summary>The player's plans, or null when the player has no readable plan container (149 players on 15/8/2026).</summary>
    public IReadOnlyList<CareerPlan>? Read(PlayerRecord player) => Read(PersonOwner.Of(player, profile));

    /// <summary>A player's or staff member's plans: the container sits on the person (player + 0x350 = person + 0xC8).</summary>
    public IReadOnlyList<CareerPlan>? Read(PersonOwner owner)
    {
        if (profile.CareerPlans is not { } o || !owner.IsValid(memory, moduleBase)) return null;
        if (!Q(owner.At(o.Container), out ulong container) || container == 0) return null;
        if (!Q(container, out ulong begin) || !Q(container + 8, out ulong end) || !Q(container + 16, out ulong capacity)
            || begin == 0 || end < begin || capacity < end || (end - begin) % 8 != 0 || (capacity - begin) % 8 != 0
            || (end - begin) / 8 > MaxPlans) return null;
        var result = new List<CareerPlan>();
        var b = new byte[o.RecordSize];
        for (ulong p = begin; p < end; p += 8)
        {
            if (!Q(p, out ulong plan) || plan == 0 || !memory.TryRead(plan, b)) return null;
            ulong item = BitConverter.ToUInt64(b, o.Item);
            result.Add(new(plan, item, b[o.Condition], b[o.Scope], b[o.Job], b[o.ItemType], Item(item)?.Name, Convert.ToHexString(b), begin, end));
        }
        return new CareerPlanSnapshotList(owner.Address, owner.Shift, container, begin, end, capacity, result);
    }

    /// <summary>The kind of object at <paramref name="address"/> by vtable, named or not; null for anything else.</summary>
    public CareerItemType? KindOf(ulong address)
    {
        if (!Q(address, out ulong vt) || vt == 0) return null;
        if (profile.Club is { } k && vt == moduleBase + k.ClubVtableRva) return CareerItemType.Club;
        if (profile.Competition is { } c && vt == moduleBase + c.VtableRva) return CareerItemType.Competition;
        if (profile.CareerPlans is { } o && vt == moduleBase + o.ContinentVtable) return CareerItemType.Continent;
        if (profile.Nation?.Rivalries is { } r && vt == moduleBase + r.TeamVtable) return CareerItemType.Nation;
        return null;
    }

    /// <summary>The item at <paramref name="address"/>, named, or null when it is none of the four kinds.</summary>
    public CareerItem? Item(ulong address)
    {
        if (!Q(address, out ulong vt) || vt == 0) return null;
        if (profile.Club is { } k && vt == moduleBase + k.ClubVtableRva)
            return Named(address, CareerItemType.Club, StringAt(address + (ulong)k.Name) ?? StringAt(address + (ulong)k.ShortName));
        if (profile.Competition is { } c && vt == moduleBase + c.VtableRva)
            return Named(address, CareerItemType.Competition, StringAt(address + (ulong)c.Name) ?? StringAt(address + (ulong)c.ShortName));
        if (profile.CareerPlans is { } o && vt == moduleBase + o.ContinentVtable)
            return Named(address, CareerItemType.Continent, StringAt(address + (ulong)o.ContinentName));
        if (profile.Nation?.Rivalries is { } r && vt == moduleBase + r.TeamVtable)
            return Named(address, CareerItemType.Nation, NationOf(address));
        return null;
    }

    /// <summary>
    /// Plan targets for the picker: every nation's football object and its continent, plus the given clubs and
    /// competitions with the names the caller already holds (so no per-item memory reads for 13,000 clubs/competitions).
    /// </summary>
    public IReadOnlyList<CareerItem> Catalogue(IEnumerable<(ulong Address, string Name)> clubs,
        IEnumerable<(ulong Address, string Name)> competitions,
        IReadOnlyList<Nations.NationRecord>? nations = null)
    {
        var items = new List<CareerItem>();
        var o = profile.CareerPlans ?? throw new InvalidOperationException("Profile has no career plan offsets");
        _nationsByTeam ??= [];
        foreach (var n in nations ?? new Nations.NationRepository(memory, profile, moduleBase).LoadAll())
        {
            if (profile.Nation?.Rivalries is { } r && Q(n.Address + (ulong)r.Team, out ulong team) && team != 0 && n.DisplayName.Length > 0)
            {
                _nationsByTeam.TryAdd(team, n.DisplayName);
                items.Add(new(team, CareerItemType.Nation, n.DisplayName));
            }
            if (Q(n.Address + (ulong)o.NationContinent, out ulong continent) && Item(continent) is { } c) items.Add(c);
        }
        items.AddRange(clubs.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => new CareerItem(c.Address, CareerItemType.Club, c.Name)));
        items.AddRange(competitions.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => new CareerItem(c.Address, CareerItemType.Competition, c.Name)));
        return items.DistinctBy(i => i.Address).OrderBy(i => i.Type).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    Dictionary<ulong, string>? _nationsByTeam;

    string? NationOf(ulong team)
    {
        if (_nationsByTeam is null)
        {
            _nationsByTeam = [];
            // An unreadable nation list leaves nation items unnamed rather than failing the whole read.
            try
            {
                if (profile.Nation?.Rivalries is { } r)
                    foreach (var n in new Nations.NationRepository(memory, profile, moduleBase).LoadAll())
                        if (Q(n.Address + (ulong)r.Team, out ulong t) && t != 0) _nationsByTeam.TryAdd(t, n.DisplayName);
            }
            catch (MemoryAccessException) { }
        }
        return _nationsByTeam.GetValueOrDefault(team);
    }

    static CareerItem? Named(ulong address, CareerItemType type, string? name) => name is null ? null : new(address, type, name);


    string? StringAt(ulong field)
    {
        if (!Q(field, out ulong p) || p == 0) return null;
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
/// Edits Career Plans like FMRTE 27: fields in place; + allocates a 16-byte plan and appends its pointer (a full vector
/// moves to a new buffer); − moves later pointers up and the end back. New plans carry the game's 2 at +0xC (FMRTE wrote 0).
/// Guarded: player vtable, unchanged vector, membership, whole record, item kind; read-back.
/// </summary>
public sealed class CareerPlanWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>FMRTE 27's Condition list (codes 1–6); 12 game rows use 7, which FMRTE does not list.</summary>
    public const int MaxCondition = 6;
    const int GameSource = 2;

    public void Set(PlayerRecord player, IReadOnlyList<CareerPlan> current, CareerPlan plan, CareerPlanFields fields) =>
        Set(PersonOwner.Of(player, profile), current, plan, fields);

    public void Set(PersonOwner player, IReadOnlyList<CareerPlan> current, CareerPlan plan, CareerPlanFields fields)
    {
        var (o, _, _, _) = Require(player, current, only: plan);
        if (!current.Contains(plan)) throw new StaleObjectException("The plan is no longer in the list");
        Write(plan.Address, Record(o, fields, BitConverter.ToInt32(Convert.FromHexString(plan.Snapshot), o.Source)));
    }

    public void Add(PlayerRecord player, IReadOnlyList<CareerPlan> current, CareerPlanFields fields, Func<int, ulong> allocate) =>
        Add(PersonOwner.Of(player, profile), current, fields, allocate);

    public void Add(PersonOwner player, IReadOnlyList<CareerPlan> current, CareerPlanFields fields, Func<int, ulong> allocate)
    {
        var (o, container, end, cap) = Require(player, current);
        var record = Record(o, fields, GameSource);
        ulong at = allocate(o.RecordSize);
        Write(at, record);
        if (end != 0 && end < cap)
        {
            Write(end, BitConverter.GetBytes(at));
            Write(container + 8, BitConverter.GetBytes(end + 8));
            return;
        }
        int count = current.Count, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        Write(buffer, [.. current.SelectMany(p => BitConverter.GetBytes(p.Address)), .. BitConverter.GetBytes(at)]);
        Write(container, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)(count + 1) * 8), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    public void Remove(PlayerRecord player, IReadOnlyList<CareerPlan> current, CareerPlan plan) =>
        Remove(PersonOwner.Of(player, profile), current, plan);

    public void Remove(PersonOwner player, IReadOnlyList<CareerPlan> current, CareerPlan plan)
    {
        var (_, container, end, _) = Require(player, current);
        int index = current.ToList().IndexOf(plan);
        if (index < 0) throw new StaleObjectException("The plan is no longer in the list");
        ulong begin = current[0].Begin;
        if (index < current.Count - 1) Write(begin + (ulong)index * 8, current.Skip(index + 1).SelectMany(p => BitConverter.GetBytes(p.Address)).ToArray());
        Write(container + 8, BitConverter.GetBytes(end - 8));
    }

    byte[] Record(CareerPlanOffsets o, CareerPlanFields f, int source)
    {
        if (f.Condition is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(f), "Condition is 1–7");
        if (f.Scope is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(f), "Scope is 1 (Club) or 2 (International)");
        if (f.Job is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(f), "Job is a byte code");
        if (new CareerPlanReader(memory, profile, moduleBase).KindOf(f.Item.Address) != f.Item.Type)
            throw new StaleObjectException($"{f.Item.Name} is not a {f.Item.Type} any more");
        var r = new byte[o.RecordSize];
        BitConverter.GetBytes(f.Item.Address).CopyTo(r, o.Item);
        r[o.Condition] = (byte)f.Condition; r[o.Scope] = (byte)f.Scope; r[o.Job] = (byte)f.Job; r[o.ItemType] = (byte)f.Item.Type;
        BitConverter.GetBytes(source).CopyTo(r, o.Source);
        return r;
    }

    /// <summary>The container's vector must hold exactly the plans read, in order, each unchanged (or just <paramref name="only"/>, so one Save can set several rows).</summary>
    (CareerPlanOffsets o, ulong container, ulong end, ulong cap) Require(PersonOwner player, IReadOnlyList<CareerPlan> current, CareerPlan? only = null)
    {
        var o = profile.CareerPlans ?? throw new InvalidOperationException("Profile has no career plan offsets");
        if (!player.IsValid(memory, moduleBase)) throw new StaleObjectException($"{player.Name} changed since it was read");
        ulong container = memory.ReadUInt64(player.At(o.Container));
        if (container == 0) throw new InvalidOperationException("The player has no career plan container");
        var h = memory.Read(container, 24);
        ulong begin = BitConverter.ToUInt64(h, 0), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        string? why = null;
        if (current is not CareerPlanSnapshotList snapshot) why = "the list was not read by the editor";
        else if (snapshot.Owner != player.Address || snapshot.Shift != player.Shift) why = "another player";
        else if (snapshot.Container != container) why = "the container moved";
        else if (snapshot.Begin != begin || snapshot.End != end || snapshot.Capacity != cap) why = "the vector changed";
        else if (end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0) why = "the vector is not valid";
        else if ((end - begin) / 8 != (ulong)current.Count || (current.Count > 0 && (current[0].Begin != begin || current[0].End != end))) why = "the number of plans changed";
        else if (current.Select((p, i) => memory.ReadUInt64(begin + (ulong)i * 8) != p.Address).Any(bad => bad)) why = "a plan moved";
        else if (current.Any(p => (only is null || p == only) && !memory.Read(p.Address, o.RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(p.Snapshot)))) why = "a plan's bytes changed";
        if (why is not null) throw new StaleObjectException($"The player's career plans changed since they were read ({why})");
        return (o, container, end, cap);
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
