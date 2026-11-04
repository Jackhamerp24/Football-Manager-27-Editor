using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Nation Information page (FMRTE 27, 3/11/2026): Vietnam Capital Ha Noi / Continent Asia, Brazil Brasília (DF) / South America,
/// England London / Europe. The pointers sit at nation+0xE8 (City) and +0xF0 (Continent) in RAM (PID 21248); a second City and
/// Continent at +0x2E8 / +0x2F0 belong to the women's side. FMRTE changing Brazil's Continent to Asia rewrote +0xF0 only.
/// </summary>
public class NationLinksTests
{
    const ulong Vietnam = 0x9000_0000, Brazil = 0x9000_1000, England = 0x9000_2000, NotANation = 0x9000_3000;
    const ulong HaNoi = 0x9100_0000, Brasilia = 0x9100_1000, London = 0x9100_2000, WomenCity = 0x9100_3000;
    const ulong Asia = 0x9200_0000, SouthAmerica = 0x9200_1000, Europe = 0x9200_2000, NotAContinent = 0x9200_3000;

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void Nation(ulong at, ulong city, ulong continent)
    {
        var o = _m.Map(at, 0x300);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Nation!.VtableRva);
        TestWorld.Set64(o, 0xE8, city); TestWorld.Set64(o, 0xF0, continent);
        TestWorld.Set64(o, 0x2E8, WomenCity); TestWorld.Set64(o, 0x2F0, continent);
    }

    void City(ulong at, string name)
    {
        var o = _m.Map(at, 0x58);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Cities!.Vtable);
        TestWorld.Set64(o, 0x18, at + 0x400);
        _m.MapString(at + 0x400, name);
    }

    void Continent(ulong at, string name)
    {
        var o = _m.Map(at, 0x40);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.CareerPlans!.ContinentVtable);
        TestWorld.Set64(o, (int)_w.Profile.CareerPlans.ContinentName, at + 0x400);
        _m.MapString(at + 0x400, name);
    }

    NationLinks Links() => new(_m, _w.Profile, TestWorld.ModuleBase);

    void Build()
    {
        City(HaNoi, "Ha Noi"); City(Brasilia, "Brasília (DF)"); City(London, "London"); City(WomenCity, "Women city");
        Continent(Asia, "Asia"); Continent(SouthAmerica, "South America"); Continent(Europe, "Europe");
        Nation(Vietnam, HaNoi, Asia); Nation(Brazil, Brasilia, SouthAmerica); Nation(England, London, Europe);
    }

    [Fact]
    public void Capital_and_continent_match_FMRTE_on_three_nations()
    {
        Build();
        Assert.Equal(["Ha Noi", "Brasília (DF)", "London"], new[] { Vietnam, Brazil, England }.Select(n => Links().Capital(n)!.Name));
        Assert.Equal(["Asia", "South America", "Europe"], new[] { Vietnam, Brazil, England }.Select(n => Links().Continent(n)!.Name));
        var nations = new[] { Vietnam, Brazil, England }.Select(a => new NationRecord(a, null, null, null)).ToArray();
        Assert.Equal(["Asia", "Europe", "South America"], Links().Continents(nations).Select(c => c.Name));
    }

    [Fact]
    public void Stadium_matches_FMRTE_My_Dinh_for_Vietnam_and_nothing_for_Brazil()
    {
        Build();
        var s = _m.Map(0x9300_0000, 0xC0);
        TestWorld.Set64(s, 0, TestWorld.ModuleBase + _w.Profile.Stadium!.VtableRva);
        TestWorld.Set64(s, _w.Profile.Stadium.Name, 0x9300_1000);
        _m.MapString(0x9300_1000, "My Dinh");
        Assert.True(_m.TryWrite(Vietnam + 0x118, BitConverter.GetBytes(0x9300_0000UL)));
        Assert.Equal("My Dinh", Links().Stadium(Vietnam)?.Name);
        Assert.Null(Links().Stadium(Brazil));             // FMRTE's Stadium field is empty for Brazil
        Assert.Null(Links().Stadium(NotANation));
    }

    [Fact]
    public void SetContinent_rewrites_only_the_mens_pointer()
    {
        Build();
        var brazil = new NationRecord(Brazil, null, null, null);
        var before = _m.Read(Brazil, 0x300);
        ulong expected = Links().ContinentPointer(Brazil);
        Links().SetContinent(brazil, expected, Links().Continent(Vietnam)!);
        var after = _m.Read(Brazil, 0x300);
        Assert.Equal("Asia", Links().Continent(Brazil)!.Name);
        Assert.Equal(Asia, BitConverter.ToUInt64(after, 0xF0));
        Assert.Equal(SouthAmerica, BitConverter.ToUInt64(after, 0x2F0));   // the women's pointer is untouched, as with FMRTE
        Array.Copy(before, 0xF0, after, 0xF0, 8);   // restore the one pointer: nothing else may differ
        Assert.Equal(before, after);
    }

    [Fact]
    public void SetContinent_refuses_stale_pointers_non_continents_and_non_nations_without_writing()
    {
        Build();
        var brazil = new NationRecord(Brazil, null, null, null);
        ulong expected = Links().ContinentPointer(Brazil);
        var asia = Links().Continent(Vietnam)!;
        int writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Links().SetContinent(brazil, expected + 8, asia));
        _m.Map(NotAContinent, 0x40);
        Assert.Throws<StaleObjectException>(() => Links().SetContinent(brazil, expected, new ContinentRecord(NotAContinent, "x")));
        var notNation = _m.Map(NotANation, 0x300);
        TestWorld.Set64(notNation, 0xF0, expected);   // same pointer as the snapshot: only the vtable guard can refuse
        Assert.Throws<StaleObjectException>(() => Links().SetContinent(new NationRecord(NotANation, null, null, null), expected, asia));
        Assert.Equal(writes, _m.Writes);
    }
}
