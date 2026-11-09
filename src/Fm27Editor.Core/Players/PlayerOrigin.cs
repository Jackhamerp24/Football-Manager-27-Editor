using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// City Of Birth: player + <see cref="Pointer"/> is a City*. Cities are 0x58-byte objects (vtable, index, id, name, nation)
/// listed by a pointer vector whose begin/end sit at [module + <see cref="ListRootRva"/>] (58,475 on 15/8/2026). FMRTE 27 changing Bruno
/// Fernandes' Maia to João Lisboa (MA) rewrote only that pointer (2/11/2026); Harry Kane's London matched FMRTE.
/// </summary>
public sealed record CityOffsets(int Pointer, uint ListRootRva, uint Vtable, int Id, int Name, int Nation)
{
    static readonly string[] Keys = ["pointer", "listRootRva", "vtable", "id", "name", "nation"];
    static readonly string[] FieldKeys = ["latitude", "longitude", "altitude", "inhabitants", "attraction"];

    /// <summary>Single-value fields of FMRTE 27's City page (float latitude/longitude, int16 altitude, byte range index, byte attraction); optional.</summary>
    public CityFieldOffsets? Fields { get; init; }

    /// <summary>Pointers to the Weather, Language and Local Region objects (FMRTE 27's City page); optional.</summary>
    public CityLinkOffsets? Links { get; init; }

    static readonly string[] LinkKeys = ["weather", "language", "region"];

    internal static CityOffsets Load(JsonElement e)
    {
        CityLinkOffsets? links = null;
        if (e.TryGetProperty("weather", out _))
        {
            int[] l = LinkKeys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
                ? s[2..] : throw new FormatException($"player.cities.{key}: expected hex"), 16)).ToArray();
            links = new(l[0], l[1], l[2]);
        }
        var loaded = LoadBase(e);
        return loaded with { Links = links };
    }

    static CityOffsets LoadBase(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.cities.{key}: expected hex"), 16)).ToArray();
        CityFieldOffsets? fields = null;
        if (e.TryGetProperty("latitude", out _))
        {
            int[] f = FieldKeys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
                ? s[2..] : throw new FormatException($"player.cities.{key}: expected hex"), 16)).ToArray();
            fields = new(f[0], f[1], f[2], f[3], f[4]);
        }
        return new((int)v[0], (uint)v[1], (uint)v[2], (int)v[3], (int)v[4], (int)v[5]) { Fields = fields };
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Pointer, ListRootRva, Vtable, Id, Name, Nation];
        w.WriteStartObject("cities");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        if (Fields is { } f)
        {
            int[] o = [f.Latitude, f.Longitude, f.Altitude, f.Inhabitants, f.Attraction];
            for (int i = 0; i < FieldKeys.Length; i++) w.WriteString(FieldKeys[i], $"0x{o[i]:X}");
        }
        if (Links is { } k)
        {
            int[] o = [k.Weather, k.Language, k.Region];
            for (int i = 0; i < LinkKeys.Length; i++) w.WriteString(LinkKeys[i], $"0x{o[i]:X}");
        }
        w.WriteEndObject();
    }
}

public sealed record CityFieldOffsets(int Latitude, int Longitude, int Altitude, int Inhabitants, int Attraction);

public sealed record CityLinkOffsets(int Weather, int Language, int Region);

/// <summary>The four objects a city points to. Nation is <c>cities.nation</c>; the rest are <see cref="CityLinkOffsets"/>.</summary>
public enum CityLink { Nation, Weather, Language, Region }

/// <summary>One choice for a city link: the object's address and the name the game shows.</summary>
public sealed record CityLinkTarget(ulong Address, string Name);

/// <summary>
/// A city the game knows. <see cref="Nation"/> is the nation object, for telling same-named cities apart. Inhabitants is the
/// index of FMRTE's Inhabitants Range list (0 Not Set, 1 "0 - 1000" … 15 "20000001 +").
/// </summary>
public sealed record CityRecord(ulong Address, int Id, string Name, ulong Nation,
    float Latitude = 0, float Longitude = 0, short Altitude = 0, byte Inhabitants = 0, byte Attraction = 0);
public sealed record PlayerBirthCitySearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

public sealed class CityRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxCities = 200_000, MaxName = 200;

    /// <summary>Every city in the game's list, in its order.</summary>
    public IReadOnlyList<CityRecord> LoadAll()
    {
        var o = profile.Cities ?? throw new InvalidOperationException("Profile has no city offsets");
        ulong vector = memory.ReadUInt64(moduleBase + o.ListRootRva);
        ulong begin = vector == 0 ? 0 : memory.ReadUInt64(vector), end = vector == 0 ? 0 : memory.ReadUInt64(vector + 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxCities)
            throw new MemoryAccessException($"City list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        var raw = memory.Read(begin, (int)(end - begin));
        var result = new List<CityRecord>(raw.Length / 8);
        for (int i = 0; i < raw.Length; i += 8)
            if (Read(BitConverter.ToUInt64(raw, i)) is { } city) result.Add(city);
        return result;
    }

    /// <summary>The player's City Of Birth, or null when it is unset or not a city.</summary>
    public CityRecord? CityOf(PlayerRecord player) => Read(CityPointer(player));

    /// <summary>Find players born in a specific city ID. A shared city object is decoded only once per search.</summary>
    public IReadOnlyList<PlayerRecord> FindPlayersBornInCity(IEnumerable<PlayerRecord> players, int cityId, CancellationToken cancellationToken = default)
        => FindPlayersBornInCityDetailed(players, cityId, cancellationToken).Matches;

    /// <summary>Reports absent or unreadable city pointers separately instead of asserting they do not match.</summary>
    public PlayerBirthCitySearchResult FindPlayersBornInCityDetailed(IEnumerable<PlayerRecord> players, int cityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (profile.Cities is null) throw new InvalidOperationException("Profile has no city offsets");
        if (cityId <= 0) throw new ArgumentOutOfRangeException(nameof(cityId));
        var cityIds = new Dictionary<ulong, int?>();
        var matches = new List<PlayerRecord>();
        var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong address = CityPointer(player);
            if (address == 0) { unavailable.Add(player); continue; }
            if (!cityIds.TryGetValue(address, out int? id)) cityIds[address] = id = Read(address)?.Id;
            if (id is null) unavailable.Add(player);
            else if (id == cityId) matches.Add(player);
        }
        return new(matches, unavailable);
    }

    /// <summary>The raw City Of Birth pointer (0 when unset or unreadable): the snapshot for <see cref="PlayerOriginWriter.SetCity"/>.</summary>
    public ulong CityPointer(PlayerRecord player) =>
        profile.Cities is { } o && IsPlayer(player.Address) && Q(player.Address + (ulong)o.Pointer, out ulong city) ? city : 0;

    public CityRecord? Read(ulong city)
    {
        if (profile.Cities is not { } o || city == 0) return null;
        var f = o.Fields;
        var b = new byte[Math.Max(o.Nation + 8, f is null ? 0 : Math.Max(Math.Max(f.Longitude + 4, f.Altitude + 2), Math.Max(f.Latitude + 4, Math.Max(f.Inhabitants, f.Attraction) + 1)))];
        if (!memory.TryRead(city, b) || BitConverter.ToUInt64(b, 0) != moduleBase + o.Vtable) return null;
        if (StringAt(BitConverter.ToUInt64(b, o.Name)) is not { } name) return null;
        return f is null
            ? new(city, BitConverter.ToInt32(b, o.Id), name, BitConverter.ToUInt64(b, o.Nation))
            : new(city, BitConverter.ToInt32(b, o.Id), name, BitConverter.ToUInt64(b, o.Nation), BitConverter.ToSingle(b, f.Latitude),
                BitConverter.ToSingle(b, f.Longitude), BitConverter.ToInt16(b, f.Altitude), b[f.Inhabitants], b[f.Attraction]);
    }

    bool IsPlayer(ulong address) => Q(address, out ulong vt) && profile.PlayerVtableRvas.Any(r => vt == moduleBase + r);

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

public enum CityField { Latitude, Longitude, Altitude, Inhabitants, Attraction }

/// <summary>
/// Edits the single-value fields of a city (FMRTE 27's City page). Guarded: city vtable, the field still holds the value the
/// snapshot carried, then write and read back. Attraction 0–20 is FMRTE's own limit (21 and 25 were refused on Save, 20 saved);
/// Inhabitants is the Inhabitants Range index 0–15.
/// </summary>
public sealed class CityWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int MaxAttraction = 20, MaxInhabitants = 15;

    public void Set(CityRecord city, CityField field, double value)
    {
        var o = profile.Cities ?? throw new InvalidOperationException("Profile has no city offsets");
        var f = o.Fields ?? throw new InvalidOperationException("Profile has no city field offsets");
        if (memory.ReadUInt64(city.Address) != moduleBase + o.Vtable) throw new StaleObjectException("The city object changed since it was read");
        (int at, byte[] before, byte[] after) = field switch
        {
            CityField.Latitude => (f.Latitude, BitConverter.GetBytes(city.Latitude),
                value is >= -90 and <= 90 ? BitConverter.GetBytes((float)value) : throw new ArgumentOutOfRangeException(nameof(value), "Latitude is -90 to 90")),
            CityField.Longitude => (f.Longitude, BitConverter.GetBytes(city.Longitude),
                value is >= -180 and <= 180 ? BitConverter.GetBytes((float)value) : throw new ArgumentOutOfRangeException(nameof(value), "Longitude is -180 to 180")),
            CityField.Altitude => (f.Altitude, BitConverter.GetBytes(city.Altitude),
                value is >= short.MinValue and <= short.MaxValue && value == Math.Truncate(value) ? BitConverter.GetBytes((short)value) : throw new ArgumentOutOfRangeException(nameof(value), "Altitude is a whole number of metres")),
            CityField.Inhabitants => (f.Inhabitants, new[] { city.Inhabitants },
                value is >= 0 and <= MaxInhabitants && value == Math.Truncate(value) ? new[] { (byte)value } : throw new ArgumentOutOfRangeException(nameof(value), "Inhabitants range is 0–15")),
            _ => (f.Attraction, new[] { city.Attraction },
                value is >= 0 and <= MaxAttraction && value == Math.Truncate(value) ? new[] { (byte)value } : throw new ArgumentOutOfRangeException(nameof(value), "Attraction is 0–20")),
        };
        ulong address = city.Address + (ulong)at;
        if (!memory.Read(address, before.Length).AsSpan().SequenceEqual(before)) throw new StaleObjectException($"{field} changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write {after.Length} byte(s) at 0x{address:X}");
    }
}

/// <summary>
/// A city's Nation, Weather, Language and Local Region are four 8-byte pointers (city +0x20, +0x28, +0x30, +0x38); FMRTE 27 changing
/// Manchester's four values on 9/11/2026 wrote exactly those four pointers. Weather, Language and Region objects carry their name
/// (length-prefixed string) at +0x18. The choices are the distinct objects the loaded cities already point to, so the catalogue
/// needs no heap scan. Set is guarded: city vtable, the pointer still the one read, the target of the same object type as the
/// current one (nation: the nation vtable) with a readable name; then write and read back.
/// </summary>
public sealed class CityLinks(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int NameOffset = 0x18, MaxName = 200;

    public bool Available => profile.Cities?.Links is not null;

    public int OffsetOf(CityLink link)
    {
        var o = profile.Cities ?? throw new InvalidOperationException("Profile has no city offsets");
        var l = o.Links ?? throw new InvalidOperationException("Profile has no city link offsets");
        return link switch { CityLink.Nation => o.Nation, CityLink.Weather => l.Weather, CityLink.Language => l.Language, _ => l.Region };
    }

    /// <summary>The raw pointer a city holds now (0 when unset or unreadable): the snapshot for <see cref="Set"/>.</summary>
    public ulong Pointer(CityRecord city, CityLink link) => Available && Q(city.Address + (ulong)OffsetOf(link), out ulong p) ? p : 0;

    /// <summary>The name the game shows for a Weather, Language or Region object; null when the object is unreadable.</summary>
    public string? NameOf(ulong target)
    {
        if (!Q(target + NameOffset, out ulong name) || name == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(name, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n <= 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(name + 4, bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }

    /// <summary>Every Weather, Language or Region object some city points to, by name.</summary>
    public IReadOnlyList<CityLinkTarget> Catalogue(IEnumerable<CityRecord> cities, CityLink link)
    {
        if (link == CityLink.Nation || !Available) return [];
        var seen = new Dictionary<ulong, ulong>();           // pointer -> vtable
        foreach (var city in cities)
        {
            ulong p = Pointer(city, link);
            if (p != 0 && !seen.ContainsKey(p)) seen[p] = Q(p, out ulong vt) ? vt : 0;
        }
        ulong common = seen.Values.Where(v => v != 0).GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return seen.Where(kv => kv.Value == common && common != 0)
            .Select(kv => NameOf(kv.Key) is { } name ? new CityLinkTarget(kv.Key, name) : null).OfType<CityLinkTarget>()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <param name="chosenFrom">The catalogue the target was picked from. Needed when the city holds no object for this link now (0): there is no
    /// current object to compare kinds with, so the target must be one the catalogue lists.</param>
    public void Set(CityRecord city, CityLink link, ulong expected, ulong target, IReadOnlyList<CityLinkTarget>? chosenFrom = null)
    {
        var o = profile.Cities ?? throw new InvalidOperationException("Profile has no city offsets");
        int offset = OffsetOf(link);
        if (memory.ReadUInt64(city.Address) != moduleBase + o.Vtable) throw new StaleObjectException("The city object changed since it was read");
        ulong at = city.Address + (ulong)offset;
        if (memory.ReadUInt64(at) != expected) throw new StaleObjectException($"{link} changed since it was read");
        if (target == 0) throw new ArgumentException("A city needs a target", nameof(target));
        ulong targetVt = memory.ReadUInt64(target);
        if (link == CityLink.Nation)
        {
            var n = profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");
            if (targetVt != moduleBase + n.VtableRva) throw new StaleObjectException("The nation object changed since it was read");
        }
        else
        {
            bool sameKind = expected != 0 ? targetVt == memory.ReadUInt64(expected) : chosenFrom is not null && chosenFrom.Any(t => t.Address == target);
            if (!sameKind) throw new StaleObjectException($"The {link} object is not the same kind as the current one");
            if (link == CityLink.Language && profile.Languages is { } lang && targetVt != moduleBase + lang.LanguageVtable)
                throw new StaleObjectException("The language object changed since it was read");
            if (NameOf(target) is null) throw new StaleObjectException($"The {link} object has no readable name");
        }
        if (!memory.TryWrite(at, BitConverter.GetBytes(target)) || memory.ReadUInt64(at) != target)
            throw new MemoryAccessException($"Could not write 0x{at:X}");
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
/// Swaps the City Of Birth or Nationality pointer, as FMRTE 27 did on Bruno Fernandes (Maia → João Lisboa, Portugal →
/// Poland: one pointer each, Nationality Info untouched). Guarded: player vtable, pointer still the one read, target vtable;
/// read-back.
/// </summary>
public sealed class PlayerOriginWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetCity(PlayerRecord player, ulong expected, CityRecord city)
    {
        var o = profile.Cities ?? throw new InvalidOperationException("Profile has no city offsets");
        if (memory.ReadUInt64(city.Address) != moduleBase + o.Vtable) throw new StaleObjectException("The city object changed since it was read");
        Swap(player, o.Pointer, expected, city.Address);
    }

    public void SetNationality(PlayerRecord player, ulong expected, NationRecord nation)
    {
        var n = profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");
        if (memory.ReadUInt64(nation.Address) != moduleBase + n.VtableRva) throw new StaleObjectException("The nation object changed since it was read");
        Swap(player, n.PlayerNation, expected, nation.Address);
    }

    void Swap(PlayerRecord player, int offset, ulong expected, ulong target)
    {
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) throw new StaleObjectException("The player changed since it was read");
        ulong at = player.Address + (ulong)offset;
        if (memory.ReadUInt64(at) != expected) throw new StaleObjectException("The value changed since it was read");
        var data = BitConverter.GetBytes(target);
        if (!memory.TryWrite(at, data) || memory.ReadUInt64(at) != target) throw new MemoryAccessException($"Could not write 0x{at:X}");
    }
}
