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

    internal static CityOffsets Load(JsonElement e)
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
        w.WriteEndObject();
    }
}

public sealed record CityFieldOffsets(int Latitude, int Longitude, int Altitude, int Inhabitants, int Attraction);

/// <summary>
/// A city the game knows. <see cref="Nation"/> is the nation object, for telling same-named cities apart. Inhabitants is the
/// index of FMRTE's Inhabitants Range list (0 Not Set, 1 "0 - 1000" … 15 "20000001 +").
/// </summary>
public sealed record CityRecord(ulong Address, int Id, string Name, ulong Nation,
    float Latitude = 0, float Longitude = 0, short Altitude = 0, byte Inhabitants = 0, byte Attraction = 0);

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
