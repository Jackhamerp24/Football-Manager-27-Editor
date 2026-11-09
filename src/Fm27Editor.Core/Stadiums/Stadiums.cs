using System.Text.Json;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Stadiums;

/// <summary>
/// Single-value stadium fields (FMRTE 27's Information tab), profile <c>stadium.fields</c>, paths from the 0xC0-byte
/// stadium object. Measured on Old Trafford (680) and Etihad Stadium (5114325); the enums were mapped by writing a code
/// and reading FMRTE back: Environment 3 Standard / 4 High End / 5 State of the art, Owner Type 1 Club / 2 Council /
/// 3 Private, Pitch Deterioration 1 Fast / 2 Medium / 3 Slow. Pitch sizes are tenths of a metre.
/// </summary>
public enum StadiumField
{
    Capacity, SeatingCapacity, UsedCapacity, AllSeaterCapacity, ExpansionCapacity,
    BuildDate, RebuildDate, PitchLastRelaid, PitchRelayRequired,
    PitchLength, PitchWidth, MinPitchLength, MinPitchWidth, MaxPitchLength, MaxPitchWidth,
    PitchCondition, Decay, PitchRecoveryRate, PitchDeteriorationRate, Environment, OwnerType,
    /// <summary>Byte +0xAD, FMRTE's Pitch Type list order: 1 Grass … 8 Grass/Synthetic Mix.</summary>
    PitchType,
    /// <summary>Bytes +0xB1, +0xB2, +0xB3, FMRTE's Used By National (U21, U19) Team list: 0 No Matches … 6 Minor Matches and Some Medium Matches.</summary>
    UsedByNationalTeam, UsedByNationalU21Team, UsedByNationalU19Team,
}

/// <summary>Stadium checkboxes stored as bits of one byte (profile <c>stadium.flags</c>).</summary>
public enum StadiumFlag { IsCovered, HasUndersoilHeating, Extinct, NotUsedInContinentalFinal, IsTrainingGround, RetractableRoof, DigitalBoards }

/// <summary>One of the eight stands, in FMRTE's order: bottom, bottom-left, left, top-left, top, top-right, right, bottom-right.</summary>
public sealed record StadiumStand(int Index, int Capacity, bool HasRoof, bool HasSeats, bool HasTunnel, byte RawFlags);

public sealed record StadiumRecord(ulong Address, int Id, string? Name, ClubInfo? Owner, string? NearbyStadium,
    float Latitude, float Longitude, IReadOnlyList<StadiumStand> Stands, byte Flags, byte AltFlags = 0,
    short? IntLength = null, short? IntWidth = null, uint SeatColour = 0)
{
    public string DisplayName => Name ?? "";
    public int StandsTotal => Stands.Sum(s => s.Capacity);
}

public sealed record StadiumOffsets(
    uint ListRootRva, int ListOffset, uint VtableRva, int Id, int Name, int Owner, int Nearby, int Latitude, int Longitude,
    int Stands, int StandCount, int StandSize, int StandFlags, byte RoofBit, byte SeatsBit, byte TunnelBit,
    int Flags, IReadOnlyDictionary<StadiumFlag, byte> FlagBits, IReadOnlyDictionary<StadiumField, ClubFieldOffset> Fields)
{
    static readonly string[] Keys = ["id", "name", "owner", "nearby", "latitude", "longitude", "stands", "standCount", "standSize", "standFlags", "flags"];

    /// <summary>Second flag byte (+0xAB): Is Training Ground 0x04 and Digital Advertising Boards 0x40; -1 when the profile has none.</summary>
    public int AltFlags { get; init; } = -1;
    public IReadOnlyDictionary<StadiumFlag, byte> AltFlagBits { get; init; } = new Dictionary<StadiumFlag, byte>();

    /// <summary>Int. Length / Width (+0x9C, +0x9E, int16, 0.1 m; FFFF FFFF when FMRTE's box is unticked) and Seat Colour (+0x78, bytes B, G, R, A); -1 when unmapped.</summary>
    public int IntLength { get; init; } = -1;
    public int IntWidth { get; init; } = -1;
    public int SeatColour { get; init; } = -1;

    static int Hex(JsonElement e, string k) => e.GetProperty(k).GetString() is { } s && s.StartsWith("0x")
        ? Convert.ToInt32(s[2..], 16) : throw new FormatException($"stadium.{k}: expected hex");

    internal static StadiumOffsets Load(JsonElement e, Func<JsonElement, IReadOnlyDictionary<StadiumField, ClubFieldOffset>> fields)
    {
        var bits = e.GetProperty("standBits");
        var flagBits = e.GetProperty("flagBits").EnumerateObject().ToDictionary(p => Enum.Parse<StadiumFlag>(p.Name), p => (byte)Hex(e.GetProperty("flagBits"), p.Name));
        return new((uint)Hex(e, "listRootRva"), Hex(e, "listOffset"), (uint)Hex(e, "vtable"), Hex(e, "id"), Hex(e, "name"), Hex(e, "owner"),
            Hex(e, "nearby"), Hex(e, "latitude"), Hex(e, "longitude"), Hex(e, "stands"), Hex(e, "standCount"), Hex(e, "standSize"), Hex(e, "standFlags"),
            (byte)Hex(bits, "roof"), (byte)Hex(bits, "seats"), (byte)Hex(bits, "tunnel"), Hex(e, "flags"), flagBits,
            e.TryGetProperty("fields", out var fe) ? fields(fe) : new Dictionary<StadiumField, ClubFieldOffset>())
        {
            AltFlags = e.TryGetProperty("altFlags", out _) ? Hex(e, "altFlags") : -1,
            IntLength = e.TryGetProperty("intLength", out _) ? Hex(e, "intLength") : -1,
            IntWidth = e.TryGetProperty("intWidth", out _) ? Hex(e, "intWidth") : -1,
            SeatColour = e.TryGetProperty("seatColour", out _) ? Hex(e, "seatColour") : -1,
            AltFlagBits = e.TryGetProperty("altFlagBits", out var ab)
                ? ab.EnumerateObject().ToDictionary(p => Enum.Parse<StadiumFlag>(p.Name), p => (byte)Hex(ab, p.Name)) : new Dictionary<StadiumFlag, byte>(),
        };
    }

    internal void Write(Utf8JsonWriter w)
    {
        void H(string k, long v) => w.WriteString(k, $"0x{v:X}");
        w.WriteStartObject("stadium");
        H("listRootRva", ListRootRva); H("listOffset", ListOffset); H("vtable", VtableRva);
        int[] v = [Id, Name, Owner, Nearby, Latitude, Longitude, Stands, StandCount, StandSize, StandFlags, Flags];
        for (int i = 0; i < Keys.Length; i++) H(Keys[i], v[i]);
        w.WriteStartObject("standBits"); H("roof", RoofBit); H("seats", SeatsBit); H("tunnel", TunnelBit); w.WriteEndObject();
        w.WriteStartObject("flagBits"); foreach (var (f, b) in FlagBits.OrderBy(k => k.Key)) H(f.ToString(), b); w.WriteEndObject();
        if (IntLength >= 0) H("intLength", IntLength);
        if (IntWidth >= 0) H("intWidth", IntWidth);
        if (SeatColour >= 0) H("seatColour", SeatColour);
        if (AltFlags >= 0)
        {
            H("altFlags", AltFlags);
            w.WriteStartObject("altFlagBits"); foreach (var (f, b) in AltFlagBits.OrderBy(k => k.Key)) H(f.ToString(), b); w.WriteEndObject();
        }
        w.WriteStartObject("fields");
        foreach (var (f, o) in Fields.OrderBy(k => k.Key))
        {
            w.WriteStartObject(f.ToString());
            w.WriteString("path", o.PathText);
            w.WriteString("type", o.Type.ToString().ToLowerInvariant());
            if (o.Scale != 1) w.WriteNumber("scale", o.Scale);
            w.WriteEndObject();
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }
}

public sealed class StadiumRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxStadiums = 200_000, ReadSize = 0xC0;
    StadiumOffsets S => profile.Stadium ?? throw new InvalidOperationException("Profile has no stadium offsets");

    public IReadOnlyList<StadiumRecord> LoadAll()
    {
        var s = S;
        var reader = new PlayerReader(memory, profile) { ModuleBase = moduleBase };
        var result = new List<StadiumRecord>();
        try
        {
            ulong root = memory.ReadUInt64(moduleBase + s.ListRootRva);
            ulong begin = memory.ReadUInt64(root + (ulong)s.ListOffset), end = memory.ReadUInt64(root + (ulong)s.ListOffset + 8);
            if (root != 0 && begin != 0 && end >= begin && (end - begin) % 8 == 0 && (end - begin) / 8 <= MaxStadiums)
            {
                var raw = memory.Read(begin, (int)(end - begin));
                for (int i = 0; i < raw.Length; i += 8)
                    if (Decode(BitConverter.ToUInt64(raw, i), reader) is { } r) result.Add(r);
            }
        }
        catch (MemoryAccessException) { }
        if (result.Count > 1) return result;
        // The configured slot is stale (Game Pass moves it): look in the neighbouring slots for the real list.
        var found = PointerListFinder.Find(memory, moduleBase, (uint)s.ListRootRva, moduleBase + s.VtableRva, MaxStadiums);
        var rescued = new List<StadiumRecord>(found.Count);
        foreach (ulong address in found)
            if (Decode(address, reader) is { } r) rescued.Add(r);
        if (rescued.Count > result.Count) return rescued;
        if (result.Count == 0) throw new MemoryAccessException("Stadium list looks invalid; is a save loaded?");
        return result;
    }

    /// <summary>Where the stadium stands: the city object its location pointer (<c>stadium +0x60</c>) leads to, 0 when unset.</summary>
    public const int LocationOffset = 0x60;

    public ulong LocationPointer(ulong stadium) => memory.TryRead(stadium + LocationOffset, new byte[8].AsSpan()) ? memory.ReadUInt64(stadium + LocationOffset) : 0;

    /// <summary>The nearby-stadium pointer (<c>stadium.nearby</c>, +0x68), 0 when unset.</summary>
    public ulong NearbyPointer(ulong stadium) => profile.Stadium is { } s && memory.TryRead(stadium + (ulong)s.Nearby, new byte[8].AsSpan()) ? memory.ReadUInt64(stadium + (ulong)s.Nearby) : 0;

    public Fm27Editor.Core.Players.CityRecord? LocationOf(ulong stadium) =>
        LocationPointer(stadium) is var city and not 0 ? new Fm27Editor.Core.Players.CityRepository(memory, profile, moduleBase).Read(city) : null;
    public StadiumRecord? Reload(ulong address) => Decode(address, new PlayerReader(memory, profile) { ModuleBase = moduleBase });

    StadiumRecord? Decode(ulong address, PlayerReader reader)
    {
        var s = S;
        var b = new byte[ReadSize];
        if (address == 0 || !memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + s.VtableRva) return null;
        var stands = new List<StadiumStand>();
        ulong sp = BitConverter.ToUInt64(b, s.Stands);
        var sb = new byte[s.StandCount * s.StandSize];
        if (sp != 0 && memory.TryRead(sp, sb))
            for (int i = 0; i < s.StandCount; i++)
            {
                byte f = sb[i * s.StandSize + s.StandFlags];
                stands.Add(new(i, BitConverter.ToInt32(sb, i * s.StandSize), (f & s.RoofBit) != 0, (f & s.SeatsBit) != 0, (f & s.TunnelBit) != 0, f));
            }
        string? nearby = null;
        ulong np = BitConverter.ToUInt64(b, s.Nearby);
        var nb = new byte[ReadSize];
        if (np != 0 && memory.TryRead(np, nb) && BitConverter.ToUInt64(nb) == moduleBase + s.VtableRva) nearby = reader.StringAt(BitConverter.ToUInt64(nb, s.Name));
        ClubInfo? owner = null;
        ulong op = BitConverter.ToUInt64(b, s.Owner);
        if (op != 0 && profile.Club is { } k)
        {
            var cb = new byte[Math.Max(k.Name, k.ShortName) + 8];
            if (memory.TryRead(op, cb) && BitConverter.ToUInt64(cb) == moduleBase + k.ClubVtableRva)
                owner = new ClubInfo(op, reader.StringAt(BitConverter.ToUInt64(cb, k.Name)), reader.StringAt(BitConverter.ToUInt64(cb, k.ShortName)));
        }
        return new StadiumRecord(address, BitConverter.ToInt32(b, s.Id), reader.StringAt(BitConverter.ToUInt64(b, s.Name)), owner, nearby,
            BitConverter.ToSingle(b, s.Latitude), BitConverter.ToSingle(b, s.Longitude), stands, b[s.Flags], s.AltFlags >= 0 ? b[s.AltFlags] : (byte)0,
            s.IntLength >= 0 && BitConverter.ToInt16(b, s.IntLength) is var il and > 0 ? il : null,
            s.IntWidth >= 0 && BitConverter.ToInt16(b, s.IntWidth) is var iw and > 0 ? iw : null,
            s.SeatColour >= 0 ? BitConverter.ToUInt32(b, s.SeatColour) : 0);
    }
}

/// <summary>
/// Guarded stadium writes: the object must still carry the stadium vtable, the value must still match the snapshot,
/// and every write is read back. Stand flags keep their unknown bits.
/// </summary>
public sealed class StadiumWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    StadiumOffsets S => profile.Stadium ?? throw new InvalidOperationException("Profile has no stadium offsets");

    public IReadOnlyDictionary<StadiumField, long> ReadFields(ulong stadium)
    {
        var values = new Dictionary<StadiumField, long>();
        if (!IsStadium(stadium)) return values;
        foreach (var (f, o) in S.Fields)
        {
            var b = new byte[ClubFieldAccess.Size(o.Type)];
            if (memory.TryRead(stadium + (ulong)o.Path[^1], b)) values[f] = ClubFieldAccess.Decode(b, o);
        }
        return values;
    }

    public void SetField(ulong stadium, StadiumField field, long expected, long value)
    {
        var o = S.Fields.TryGetValue(field, out var x) ? x : throw new InvalidOperationException($"{field} is not mapped in this profile");
        Require(stadium);
        ulong at = stadium + (ulong)o.Path[^1];
        var bytes = ClubFieldAccess.Encode(value, o);
        if (ClubFieldAccess.Decode(memory.Read(at, bytes.Length), o) != expected) throw new StaleObjectException($"{field} changed since it was read");
        Write(at, bytes);
    }

    /// <summary>
    /// Owner: FMRTE 27's "Change Owner" writes only the 8-byte club pointer at <c>stadium.owner</c> (Old Trafford, 9/11/2026). The
    /// pointer must still be the one that was read and the new one must be a club object.
    /// </summary>
    public void SetOwner(StadiumRecord stadium, ulong club)
    {
        var s = S;
        Require(stadium.Address);
        if (profile.Club is not { } k || memory.ReadUInt64(club) != moduleBase + k.ClubVtableRva) throw new ArgumentException("Not a club", nameof(club));
        ulong at = stadium.Address + (ulong)s.Owner;
        if (memory.ReadUInt64(at) != (stadium.Owner?.Address ?? 0)) throw new StaleObjectException("The stadium owner changed since it was read");
        Write(at, BitConverter.GetBytes(club));
    }

    /// <summary>Latitude / Longitude: two floats at <c>stadium.latitude</c> / <c>stadium.longitude</c>, each written only when asked and only if still what was read.</summary>
    public void SetCoordinates(StadiumRecord stadium, float? latitude, float? longitude)
    {
        var s = S;
        if (latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude is -90 to 90");
        if (longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude is -180 to 180");
        Require(stadium.Address);
        if (latitude is { } lat)
        {
            ulong at = stadium.Address + (ulong)s.Latitude;
            if (!memory.Read(at, 4).AsSpan().SequenceEqual(BitConverter.GetBytes(stadium.Latitude))) throw new StaleObjectException("Latitude changed since it was read");
            Write(at, BitConverter.GetBytes(lat));
        }
        if (longitude is { } lon)
        {
            ulong at = stadium.Address + (ulong)s.Longitude;
            if (!memory.Read(at, 4).AsSpan().SequenceEqual(BitConverter.GetBytes(stadium.Longitude))) throw new StaleObjectException("Longitude changed since it was read");
            Write(at, BitConverter.GetBytes(lon));
        }
    }

    /// <summary>Nearby Stadium: "Change Nearby Stadium" writes only the 8-byte stadium pointer at <c>stadium.nearby</c> (+0x68; Old Trafford → Anfield, 9/11/2026).</summary>
    public void SetNearby(StadiumRecord stadium, ulong currentNearby, ulong nearby)
    {
        var s = S;
        Require(stadium.Address);
        if (nearby == stadium.Address || memory.ReadUInt64(nearby) != moduleBase + s.VtableRva) throw new ArgumentException("Not another stadium", nameof(nearby));
        ulong at = stadium.Address + (ulong)s.Nearby;
        if (memory.ReadUInt64(at) != currentNearby) throw new StaleObjectException("The nearby stadium changed since it was read");
        Write(at, BitConverter.GetBytes(nearby));
    }

    /// <summary>Location: "Change Stadium Location" writes only the 8-byte city pointer at <see cref="StadiumRepository.LocationOffset"/>.</summary>
    public void SetLocation(StadiumRecord stadium, ulong currentCity, ulong city)
    {
        Require(stadium.Address);
        if (profile.Cities is not { } c || memory.ReadUInt64(city) != moduleBase + c.Vtable) throw new ArgumentException("Not a city", nameof(city));
        ulong at = stadium.Address + StadiumRepository.LocationOffset;
        if (memory.ReadUInt64(at) != currentCity) throw new StaleObjectException("The stadium location changed since it was read");
        Write(at, BitConverter.GetBytes(city));
    }
    /// <summary>Seat Colour is four bytes B, G, R, A at <c>stadium.seatColour</c>; FMRTE edits it with a colour picker
    /// and wrote #FF00BEEF to Old Trafford at +0x78 (5/11/2026).</summary>
    public void SetSeatColour(StadiumRecord stadium, uint argb)
    {
        var s = S;
        if (s.SeatColour < 0) throw new InvalidOperationException("Seat colour is not mapped in this profile");
        Require(stadium.Address);
        ulong at = stadium.Address + (ulong)s.SeatColour;
        if (BitConverter.ToUInt32(memory.Read(at, 4)) != stadium.SeatColour) throw new StaleObjectException("Seat colour changed since it was read");
        Write(at, BitConverter.GetBytes(argb));
    }

    public void SetFlag(StadiumRecord stadium, StadiumFlag flag, bool on)    {
        var s = S;
        Require(stadium.Address);
        bool alt = s.AltFlagBits.TryGetValue(flag, out byte altBit);
        if (alt && s.AltFlags < 0) throw new InvalidOperationException($"{flag} is not mapped in this profile");
        ulong at = stadium.Address + (ulong)(alt ? s.AltFlags : s.Flags);
        byte now = memory.Read(at, 1)[0];
        if (now != (alt ? stadium.AltFlags : stadium.Flags)) throw new StaleObjectException("Stadium flags changed since they were read");
        byte bit = alt ? altBit : s.FlagBits[flag];
        Write(at, [(byte)(on ? now | bit : now & ~bit)]);
    }

    /// <summary>
    /// Int. Length / Width: lengths in metres (80–130 × 40–100, the Pitch ranges) or null for FMRTE's unticked box, which stores FFFF FFFF
    /// (ticking it on Wembley wrote 105 × 68 = 1050, 680 and unticking restored FFFF FFFF). Guarded: stadium vtable and both int16
    /// still equal to the snapshot.
    /// </summary>
    public void SetInternationalSize(StadiumRecord stadium, (int Length, int Width)? size)
    {
        var s = S;
        if (s.IntLength < 0 || s.IntWidth < 0) throw new InvalidOperationException("International size is not mapped in this profile");
        if (size is { } v && (v.Length is < 80 or > 130 || v.Width is < 40 or > 100))
            throw new ArgumentOutOfRangeException(nameof(size), "International length is 80–130 m and width 40–100 m");
        Require(stadium.Address);
        var lengthAt = stadium.Address + (ulong)s.IntLength;
        var widthAt = stadium.Address + (ulong)s.IntWidth;
        short Raw(short? meters) => meters ?? -1;
        if (BitConverter.ToInt16(memory.Read(lengthAt, 2)) != Raw(stadium.IntLength) || BitConverter.ToInt16(memory.Read(widthAt, 2)) != Raw(stadium.IntWidth))
            throw new StaleObjectException("The international size changed since it was read");
        Write(lengthAt, BitConverter.GetBytes(size is { } a ? (short)(a.Length * 10) : (short)-1));
        Write(widthAt, BitConverter.GetBytes(size is { } b ? (short)(b.Width * 10) : (short)-1));
    }

    public void SetStand(StadiumRecord stadium, int index, int capacity, bool roof, bool seats, bool tunnel)
    {
        var s = S;
        if (index < 0 || index >= stadium.Stands.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (capacity is < 0 or > 200_000) throw new ArgumentOutOfRangeException(nameof(capacity), "Stand capacity is 0–200000");
        Require(stadium.Address);
        ulong at = memory.ReadUInt64(stadium.Address + (ulong)s.Stands) + (ulong)(index * s.StandSize);
        var old = stadium.Stands[index];
        var now = memory.Read(at, s.StandSize);
        if (BitConverter.ToInt32(now) != old.Capacity || now[s.StandFlags] != old.RawFlags) throw new StaleObjectException("The stand changed since it was read");
        byte f = old.RawFlags;
        f = (byte)(roof ? f | s.RoofBit : f & ~s.RoofBit);
        f = (byte)(seats ? f | s.SeatsBit : f & ~s.SeatsBit);
        f = (byte)(tunnel ? f | s.TunnelBit : f & ~s.TunnelBit);
        Write(at, BitConverter.GetBytes(capacity));
        Write(at + (ulong)s.StandFlags, [f]);
    }

    bool IsStadium(ulong address) => address != 0 && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + S.VtableRva;

    void Require(ulong address)
    {
        if (!IsStadium(address)) throw new StaleObjectException($"0x{address:X} is no longer a stadium object");
    }

    void Write(ulong at, byte[] bytes)
    {
        if (!memory.TryWrite(at, bytes) || !memory.Read(at, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Write at 0x{at:X} failed");
    }
}
