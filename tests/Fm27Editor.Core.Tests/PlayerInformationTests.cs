using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// FMRTE 27's Information tab fields. Bytes are Bruno Fernandes' (ID 43124203) as dumped on the 15/8/2026 save before any
/// edit; expected values are what FMRTE 27 showed: Ecto-Mesomorph, Declared For Nation, Non-EU in Italy and Retiring
/// unticked, Mediterranean/Hispanic, Dark Brown 2, Short, Skin Tone 6, Days in Country 2390, City Of Birth Maia.
/// Written bytes are FMRTE 27's own writes on him (2/11/2026).
/// </summary>
public class PlayerInformationTests
{
    const uint CityVt = 0x4DE29C8, NationVt = 0x4CDF658;
    const ulong Maia = 0x6000_0000, Lisboa = 0x6000_0100, NotACity = 0x6000_0200, CityList = 0x6001_0000, CityVector = 0x6002_0000;
    const ulong Portugal = 0x6100_0000, Poland = 0x6100_0100;
    readonly TestWorld _w = new();

    public PlayerInformationTests()
    {
        void Put(int offset, string hex) => Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + (ulong)offset, Convert.FromHexString(hex)));
        Put(0x230, "02");
        Put(0x26E, "53");
        Put(0x278, "80");
        Put(0x2A1, "02");
        Put(0x315, "010B0106");
        Put(0x3A8, "1D00E407");
        // Cities: header as dumped (vtable, index, id twice), name and nation pointers re-pointed at fakes.
        City(Maia, "F802000057030000570300000000000", "Maia", Portugal);
        City(Lisboa, "884B0000AD1D2201AD1D220100000000", "João Lisboa (MA)", 0x6100_0200);
        _w.Memory.Map(NotACity, 0x58);
        var list = _w.Memory.Map(CityList, 16);
        TestWorld.Set64(list, 0, Maia); TestWorld.Set64(list, 8, Lisboa);
        // module + 0x5703908 holds a pointer to the vector's begin/end (as read on the 15/8/2026 save).
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x5703908, 8), 0, CityVector);
        var vector = _w.Memory.Map(CityVector, 24);
        TestWorld.Set64(vector, 0, CityList); TestWorld.Set64(vector, 8, CityList + 16); TestWorld.Set64(vector, 16, CityList + 16);
        Put(0x308, Convert.ToHexString(BitConverter.GetBytes(Maia)));
        foreach (var n in new[] { Portugal, Poland }) TestWorld.Set64(_w.Memory.Map(n, 0x40), 0, TestWorld.ModuleBase + NationVt);
        Put(0x2F0, Convert.ToHexString(BitConverter.GetBytes(Portugal)));
    }

    void City(ulong at, string header, string name, ulong nation)
    {
        var c = _w.Memory.Map(at, 0x58);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + CityVt);
        Convert.FromHexString(header.PadRight(32, '0')).CopyTo(c, 8);
        ulong text = at + 0x10_0000;
        _w.Memory.MapString(text, name);
        TestWorld.Set64(c, 0x18, text);
        TestWorld.Set64(c, 0x20, nation);
    }

    PlayerRecord Bruno() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    PlayerFieldAccess Access() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    CityRepository Cities() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    PlayerOriginWriter Origin() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_values_FMRTE_shows()
    {
        var v = Access().ReadAll(Bruno());
        Assert.Equal(2, v[PlayerField.BodyType]);
        Assert.Equal(83, v[PlayerField.NationalityInfo]);
        Assert.Equal(0, v[PlayerField.TreatedAsNonEuInItaly]);
        Assert.Equal(0, v[PlayerField.RetiringAfterSpellAtCurrentClub]);
        Assert.Equal(1, v[PlayerField.Ethnicity]);
        Assert.Equal(11, v[PlayerField.HairColour]);
        Assert.Equal(1, v[PlayerField.HairLength]);
        Assert.Equal(6, v[PlayerField.SkinColour]);
        // FMRTE: 2390 days in country on 15/8/2026.
        Assert.Equal(20200129, v[PlayerField.ArrivedInCountry]);
        Assert.Equal(2390, new DateOnly(2026, 8, 15).DayNumber - new DateOnly(2020, 1, 29).DayNumber);
    }

    [Fact]
    public void Writes_match_FMRTEs_bytes_and_keep_the_other_bits()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        var a = Access();
        a.Set(Bruno(), PlayerField.TreatedAsNonEuInItaly, 0, 1);           // FMRTE: 80 → 88
        a.Set(Bruno(), PlayerField.RetiringAfterSpellAtCurrentClub, 0, 1); // FMRTE: 02 → 82
        a.Set(Bruno(), PlayerField.BodyType, 2, 5);                        // Endomorph
        a.Set(Bruno(), PlayerField.HairColour, 11, 13);                    // Red 2
        a.Set(Bruno(), PlayerField.HairLength, 1, 3);                      // Long
        a.Set(Bruno(), PlayerField.SkinColour, 6, 15);
        a.Set(Bruno(), PlayerField.Ethnicity, 1, 6);                       // Pacific Islander
        a.Set(Bruno(), PlayerField.NationalityInfo, 83, 81);               // Has Played For Nation
        var arrived = new DateOnly(2026, 8, 15).AddDays(-2500);            // FMRTE: 2500 days → 1C 01 E3 07
        a.Set(Bruno(), PlayerField.ArrivedInCountry, 20200129, arrived.Year * 10000 + arrived.Month * 100 + arrived.Day);

        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(0x88, after[0x278]);
        Assert.Equal(0x82, after[0x2A1]);
        Assert.Equal(5, after[0x230]);
        Assert.Equal(0x51, after[0x26E]);
        Assert.Equal(Convert.FromHexString("060D030F"), after[0x315..0x319]);
        Assert.Equal(Convert.FromHexString("1C01E307"), after[0x3A8..0x3AC]);
        foreach (var (at, length) in new[] { (0x278, 1), (0x2A1, 1), (0x230, 1), (0x26E, 1), (0x315, 4), (0x3A8, 4) })
            after.AsSpan(at, length).CopyTo(before.AsSpan(at, length));
        Assert.Equal(before, after);

        a.Set(Bruno(), PlayerField.TreatedAsNonEuInItaly, 1, 0);
        Assert.Equal(0x80, _w.Bytes(TestWorld.Mainoo + 0x278, 1)[0]);
    }

    [Fact]
    public void Flag_writes_refuse_stale_values_and_non_flags()
    {
        var snapshot = Bruno();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x278, [0x88]));
        Assert.Throws<StaleObjectException>(() => Access().Set(snapshot, PlayerField.TreatedAsNonEuInItaly, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Access().Set(snapshot, PlayerField.TreatedAsNonEuInItaly, 1, 2));
        Assert.Equal(0x88, _w.Bytes(TestWorld.Mainoo + 0x278, 1)[0]);
    }

    [Fact]
    public void Reads_the_city_list_and_the_players_city()
    {
        Assert.Equal([("Maia", 855, Portugal), ("João Lisboa (MA)", 19013037, 0x6100_0200UL)], Cities().LoadAll().Select(c => (c.Name, c.Id, c.Nation)));
        Assert.Equal("Maia", Cities().CityOf(Bruno())?.Name);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x308, BitConverter.GetBytes(NotACity)));
        Assert.Null(Cities().CityOf(Bruno()));
    }

    [Fact]
    public void BirthCitySearchMatchesCityIdAndRejectsInvalidCityObjects()
    {
        var bruno = Bruno();
        Assert.Equal(new[] { bruno.Address }, Cities().FindPlayersBornInCity([bruno], 855).Select(p => p.Address));
        Assert.Empty(Cities().FindPlayersBornInCity([bruno], 19013037));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x308, BitConverter.GetBytes(NotACity)));
        Assert.Empty(Cities().FindPlayersBornInCity([bruno], 855));
        var unknown = Cities().FindPlayersBornInCityDetailed([bruno], 855);
        Assert.Empty(unknown.Matches);
        Assert.Equal(new[] { bruno.Address }, unknown.Unavailable.Select(p => p.Address));
    }

    [Fact]
    public void AdvancedSearchCombinesBirthCityAndNationalityWithoutLosingUnknown()
    {
        var reader = new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var search = new AdvancedPlayerSearch(Cities(), new ContractSearch(new ContractWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase)),
            new PlayerNationalitySearch(reader), new PlayerContinentSearch(new NationLinks(_w.Memory, _w.Profile, TestWorld.ModuleBase)), new PlayerRelationSearch(reader));
        var player = Bruno();
        var criteria = new AdvancedPlayerCriteria { BirthCityId = 855, Nationality = new NationRecord(Portugal, 788, "Portugal", null) };
        Assert.Equal(new[] { player.Address }, search.Find([player], criteria).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], criteria with { BirthCityId = 19013037 }).Matches);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x308, BitConverter.GetBytes(NotACity)));
        var unknown = search.Find([player], criteria);
        Assert.Empty(unknown.Matches);
        Assert.Equal(new[] { player.Address }, unknown.Unavailable.Select(p => p.Address));
    }

    [Fact]
    public void City_and_nationality_swaps_rewrite_one_pointer_and_refuse_stale_or_wrong_targets()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        var lisboa = Cities().Read(Lisboa)!;
        Origin().SetCity(Bruno(), Maia, lisboa);
        Origin().SetNationality(Bruno(), Portugal, new NationRecord(Poland, 0, "Poland", null));
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(Lisboa, BitConverter.ToUInt64(after, 0x308));
        Assert.Equal(Poland, BitConverter.ToUInt64(after, 0x2F0));
        after.AsSpan(0x308, 8).CopyTo(before.AsSpan(0x308, 8)); after.AsSpan(0x2F0, 8).CopyTo(before.AsSpan(0x2F0, 8));
        Assert.Equal(before, after);

        Assert.Throws<StaleObjectException>(() => Origin().SetCity(Bruno(), Maia, lisboa));
        Assert.Throws<StaleObjectException>(() => Origin().SetCity(Bruno(), Lisboa, new CityRecord(NotACity, 1, "x", 0)));
        Assert.Throws<StaleObjectException>(() => Origin().SetNationality(Bruno(), Poland, new NationRecord(NotACity, 0, "x", null)));
        Assert.Equal(Lisboa, BitConverter.ToUInt64(_w.Bytes(TestWorld.Mainoo + 0x308, 8)));
        Assert.Equal(Poland, BitConverter.ToUInt64(_w.Bytes(TestWorld.Mainoo + 0x2F0, 8)));
    }

    [Fact]
    public void Masks_and_city_offsets_survive_a_profile_round_trip()
    {
        var copy = OffsetProfile.Load(_w.Profile.ToJson());
        Assert.Equal(0x8, copy.PlayerFields![PlayerField.TreatedAsNonEuInItaly].Mask);
        Assert.Equal(0x80, copy.PlayerFields[PlayerField.RetiringAfterSpellAtCurrentClub].Mask);
        Assert.Equal(0, copy.PlayerFields[PlayerField.BodyType].Mask);
        Assert.Equal(_w.Profile.Cities, copy.Cities);
        Assert.Equal(0xA, copy.Relations!.RetiredInternationalType);
    }
}
