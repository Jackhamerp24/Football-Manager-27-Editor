using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// City page (FMRTE 27). The 0x48..0x57 bytes are dumped from FM27 27.0.0 RAM (PID 21248, 3/11/2026): latitude and longitude
/// are floats, then int16 altitude, byte Inhabitants Range index, byte padding, byte Attraction. Expected values are the FMRTE
/// City screens (six decimals; a float holds about five, so the tests compare five): Manchester 53.47932 / -2.248485 / 0 / "250001 - 500000"
/// / 18, London (ON) 42.98695 / -81.24318 / 0 / "250001 - 500000" / 11, Manchester (MA) 42.57778 / -70.76945 / 30 / "5001 - 10000" / 8.
/// Attraction above 20 was refused by FMRTE's Save; 20 saved and read back as 0x14.
/// </summary>
public class CityFieldTests
{
    const ulong Manchester = 0x6000_0000, London = 0x6000_1000, ManchesterMa = 0x6000_2000, NotACity = 0x6000_3000;
    const string ManchesterTail = "D4EA55422EE70FC00000090012000000";
    const string LondonTail = "A3F22B42827CA2C2000009000B000000";
    const string ManchesterMaTail = "A54F2A42F5898DC21E00040008000000";

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void City(ulong at, int id, string name, string tail)
    {
        var o = _m.Map(at, 0x58);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Cities!.Vtable);
        BitConverter.GetBytes(id).CopyTo(o, 0xC);
        TestWorld.Set64(o, 0x18, at + 0x400);
        _m.MapString(at + 0x400, name);
        Convert.FromHexString(tail).CopyTo(o, 0x48);
    }

    CityRecord Read(ulong at)
    {
        City(Manchester, 100062, "Manchester", ManchesterTail);
        return new CityRepository(_m, _w.Profile, TestWorld.ModuleBase).Read(at)!;
    }

    CityWriter Writer() => new(_m, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Three_cities_match_FMRTE_values()
    {
        City(Manchester, 100062, "Manchester", ManchesterTail);
        City(London, 145755, "London (ON)", LondonTail);
        City(ManchesterMa, 2000073269, "Manchester (MA)", ManchesterMaTail);
        var repo = new CityRepository(_m, _w.Profile, TestWorld.ModuleBase);
        var rows = new[] { Manchester, London, ManchesterMa }.Select(a => repo.Read(a)!).ToArray();
        Assert.Equal(["Manchester", "London (ON)", "Manchester (MA)"], rows.Select(r => r.Name));
        Assert.Equal([53.47932, 42.98695, 42.57778], rows.Select(r => Math.Round((double)r.Latitude, 5)));
        Assert.Equal([-2.24849, -81.24318, -70.76945], rows.Select(r => Math.Round((double)r.Longitude, 5)));
        Assert.Equal(new short[] { 0, 0, 30 }, rows.Select(r => r.Altitude));
        Assert.Equal(new byte[] { 9, 9, 4 }, rows.Select(r => r.Inhabitants));
        Assert.Equal(new byte[] { 18, 11, 8 }, rows.Select(r => r.Attraction));
    }

    [Fact]
    public void Each_field_write_changes_only_its_own_bytes()
    {
        var city = Read(Manchester);
        var before = _m.Read(Manchester, 0x58);
        Writer().Set(city, CityField.Attraction, 20);
        var after = _m.Read(Manchester, 0x58);
        Assert.Equal(20, after[0x54]);
        after[0x54] = before[0x54];
        Assert.Equal(before, after);

        city = new CityRepository(_m, _w.Profile, TestWorld.ModuleBase).Read(Manchester)!;
        Writer().Set(city, CityField.Inhabitants, 4);
        Writer().Set(city with { Inhabitants = 4 }, CityField.Altitude, -12);
        Writer().Set(city, CityField.Latitude, 51.5);
        Writer().Set(city, CityField.Longitude, -0.12);
        var now = new CityRepository(_m, _w.Profile, TestWorld.ModuleBase).Read(Manchester)!;
        Assert.Equal((byte)4, now.Inhabitants);
        Assert.Equal((short)-12, now.Altitude);
        Assert.Equal(51.5f, now.Latitude);
        Assert.Equal(-0.12f, now.Longitude);
        Assert.Equal((byte)20, now.Attraction);
        Assert.Equal(before[..0x48], _m.Read(Manchester, 0x48));
    }

    [Fact]
    public void Guards_refuse_out_of_range_values_stale_snapshots_and_non_cities_without_writing()
    {
        var city = Read(Manchester);
        int writes = _m.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Attraction, 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Attraction, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Inhabitants, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Latitude, 90.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Longitude, -180.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Altitude, 12.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(city, CityField.Altitude, 40000));
        Assert.Equal(writes, _m.Writes);

        Assert.True(_m.TryWrite(Manchester + 0x54, [19]));   // the game (or FMRTE) changed Attraction behind our back
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().Set(city, CityField.Attraction, 5));
        Assert.Equal(writes, _m.Writes);

        _m.Map(NotACity, 0x58)[0x54] = city.Attraction; // same byte as the snapshot, so only the vtable guard can refuse
        Assert.Throws<StaleObjectException>(() => Writer().Set(city with { Address = NotACity }, CityField.Attraction, 5));
        Assert.Equal(writes, _m.Writes);
    }
}
