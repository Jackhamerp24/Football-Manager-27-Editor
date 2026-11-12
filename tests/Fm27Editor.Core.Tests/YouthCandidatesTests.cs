using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Youth candidates as measured on Steam 27.0.1+2494687 (Man Utd save, 15 Dec 2026, see
/// docs/HANDOVER-YOUTH-INTAKE.md): Van Chung Nguyen (CA 72, PA 150, Handling raw 72, Aerial Reach raw 62,
/// Reflexes raw 14, Left Foot raw 20, Right Foot raw 8, GK 20 with the rest 1, 195 cm, born 26 Dec 2010),
/// Joshua Umar (76/151) and Oshane Mckogg (53/127, another club). Layout literals below are the handover's,
/// the profile is the real Steam one, so a wrong profile entry breaks these tests.
/// </summary>
public class YouthCandidatesTests
{
    // Handover literals: the candidate map is the +0x118 map of the manager, each node carries its vector at +0x28.
    const int CandidateMap = 0x118, NodeVector = 0x28, RecordSize = 0x110, PlayingDataBase = 0x148;
    const int Ca = 0xFE, Pa = 0x100, Height = 0xF4, BirthDate = 0xF0, Club = 0x78;
    const int FirstName = 0xC8, LastName = 0xD0, Nationality = 0xB0, CityOfBirth = 0xC0;

    const ulong Module = TestWorld.ModuleBase;
    const ulong Manager = 0x6000_0000, Sentinel = 0x6000_1000, Node1 = 0x6000_2000, Node2 = 0x6000_3000;
    const ulong Vec1 = 0x6000_4000, Vec2 = 0x6000_6000;
    const ulong ManUtd = 0x6000_7000, OtherClub = 0x6000_7100;
    static readonly DateOnly GameDate = new(2026, 12, 15);

    readonly FakeMemory _memory = new();
    readonly OffsetProfile _profile;
    ulong _rec1, _rec2, _rec3;

    public YouthCandidatesTests()
    {
        _profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2494687-steam.json"));
        if (_profile.YouthCandidates is not { } yc)
            throw new InvalidOperationException("The Steam profile has no youthCandidates section");
        Assert.Equal(0x4FE3058u, yc.ManagerVtable);

        // Manager object with the candidate map head.
        var m = _memory.Map(Manager, 0x140);
        TestWorld.Set64(m, 0, Module + yc.ManagerVtable);
        var sentinel = _memory.Map(Sentinel, 0x40);
        TestWorld.Set64(sentinel, 8, Node1);
        TestWorld.Set64(m, CandidateMap, Sentinel);
        BitConverter.GetBytes(2).CopyTo(m, CandidateMap + 8);

        // Two nodes: the first holds two Man Utd candidates, the second one candidate of another club.
        var n1 = _memory.Map(Node1, 0x40);
        TestWorld.Set64(n1, 8, Sentinel); TestWorld.Set64(n1, 0x10, Node2); n1[0x19] = 0;
        BitConverter.GetBytes(409).CopyTo(n1, 0x20);
        TestWorld.Set64(n1, NodeVector, Vec1); TestWorld.Set64(n1, NodeVector + 8, Vec1 + 2 * RecordSize);
        var n2 = _memory.Map(Node2, 0x40);
        TestWorld.Set64(n2, 8, Node1); n2[0x19] = 0;
        BitConverter.GetBytes(7).CopyTo(n2, 0x20);
        TestWorld.Set64(n2, NodeVector, Vec2); TestWorld.Set64(n2, NodeVector + 8, Vec2 + RecordSize);

        _memory.Map(Vec1, 2 * RecordSize);
        _memory.Map(Vec2, RecordSize);
        _rec1 = Vec1;
        _rec2 = Vec1 + (ulong)RecordSize;
        _rec3 = Vec2;
        WriteRecord(_rec1, ManUtd, "Van Chung", "Nguyen", 72, 150, 195, 360, 2010,
            attributes: new() { [PlayerAttribute.Handling] = 72, [PlayerAttribute.AerialReach] = 62, [PlayerAttribute.Reflexes] = 14,
                [PlayerAttribute.LeftFoot] = 20, [PlayerAttribute.RightFoot] = 8 },
            positions: new() { [PlayerPosition.Goalkeeper] = 20, [PlayerPosition.Striker] = 1 });
        WriteRecord(_rec2, ManUtd, "Joshua", "Umar", 76, 151, 188, 100, 2011, null, null);
        WriteRecord(_rec3, OtherClub, "Oshane", "Mckogg", 53, 127, 180, 200, 2010, null, null);
    }

    void WriteRecord(ulong at, ulong club, string first, string last, short ca, short pa, short height,
        short dayOfYear, short year, Dictionary<PlayerAttribute, byte>? attributes, Dictionary<PlayerPosition, byte>? positions)
    {
        var record = new byte[RecordSize];
        TestWorld.Set64(record, 0, Module + 0x51A1111);
        TestWorld.Set64(record, Club, club);
        TestWorld.Set64(record, Nationality, 0x6000_8000);
        TestWorld.Set64(record, CityOfBirth, 0x6000_8100);
        MapName(record, FirstName, first);
        MapName(record, LastName, last);
        BitConverter.GetBytes(ca).CopyTo(record, Ca);
        BitConverter.GetBytes(pa).CopyTo(record, Pa);
        BitConverter.GetBytes(height).CopyTo(record, Height);
        BitConverter.GetBytes(dayOfYear).CopyTo(record, BirthDate);
        BitConverter.GetBytes(year).CopyTo(record, BirthDate + 2);
        // Positions and attributes in enum order at their derived offsets, defaulting to 1.
        int posCount = Enum.GetValues<PlayerPosition>().Length;
        for (int i = 0; i < posCount; i++) record[_profile.Positions[(PlayerPosition)i] - PlayingDataBase] = 1;
        if (positions is not null)
            foreach (var (p, v) in positions) record[_profile.Positions[p] - PlayingDataBase] = v;
        int attrCount = Enum.GetValues<PlayerAttribute>().Length;
        for (int i = 0; i < attrCount; i++) record[_profile.Attributes[(PlayerAttribute)i] - PlayingDataBase] = 5;
        if (attributes is not null)
            foreach (var (a, v) in attributes) record[_profile.Attributes[a] - PlayingDataBase] = v;
        Assert.True(_memory.TryWrite(at, record));
        MapNation();
        MapCity();
    }

    ulong _names = 0x6000_9000;
    void MapName(byte[] record, int offset, string text)
    {
        ulong entry = _names; _names += 0x100;
        ulong str = _names; _names += 0x100;
        // The candidate name entry points at a length-prefixed string directly (no player-style indirection).
        TestWorld.Set64(record, offset, entry);
        _memory.MapNameEntry(entry, str, text);
    }

    bool _nationMapped;
    void MapNation()
    {
        if (_nationMapped) return;
        _nationMapped = true;
        var nat = _profile.Nation!;
        var b = _memory.Map(0x6000_8000, 0x40);
        TestWorld.Set64(b, 0, Module + nat.VtableRva);
        ulong name = 0x6000_8200;
        _memory.MapString(name, "Vietnam");
        TestWorld.Set64(b, nat.Name, name);
    }

    bool _cityMapped;
    void MapCity()
    {
        if (_cityMapped) return;
        _cityMapped = true;
        var cities = _profile.Cities!;
        var b = _memory.Map(0x6000_8100, 0x48);
        TestWorld.Set64(b, 0, Module + cities.Vtable);
        ulong name = 0x6000_8300;
        _memory.MapString(name, "Hac Thanh");
        TestWorld.Set64(b, cities.Name, name);
    }

    YouthCandidateReader Reader() => new(_memory, _profile, Module);
    YouthCandidateWriter Writer() => new(_memory, _profile, Module);

    [Fact]
    public void Steam_profile_carries_the_measured_youth_section()
    {
        var yc = _profile.YouthCandidates!;
        Assert.Equal((0x118, 0x28, 0x110, 0x148), (yc.CandidateMap, yc.NodeVector, yc.RecordSize, yc.PlayingDataBase));
        Assert.Equal((0xFE, 0x100, 0xF4, 0xF0), (yc.CurrentAbility, yc.PotentialAbility, yc.Height, yc.BirthDate));
        Assert.Equal((0x68, 0x70, 0x78, 0x80), (yc.BasedNation, yc.Team, yc.Club, yc.OtherClub));
        Assert.Equal((0xB0, 0xC0, 0xC8, 0xD0, 0xF8), (yc.Nationality, yc.CityOfBirth, yc.FirstName, yc.LastName, yc.FutureId));
        var copy = OffsetProfile.Load(_profile.ToJson());
        Assert.Equivalent(yc, copy.YouthCandidates);
    }

    [Fact]
    public void Reads_the_clubs_candidates_across_nodes_as_fmrte_shows_them()
    {
        var found = Reader().ReadForClub(Manager, ManUtd).ToList();
        Assert.Equal(2, found.Count);
        var nguyen = found.Single(c => c.LastName == "Nguyen");
        Assert.Equal("Van Chung", nguyen.FirstName);
        Assert.Equal("Van Chung Nguyen", nguyen.DisplayName);
        Assert.Equal((72, 150, 195), (nguyen.CurrentAbility, nguyen.PotentialAbility, nguyen.Height));
        Assert.Equal(new DateOnly(2010, 12, 26), nguyen.DateOfBirth);
        Assert.Equal("Vietnam", nguyen.NationalityName);
        Assert.Equal("Hac Thanh", nguyen.CityOfBirthName);
        Assert.Equal(14, nguyen.Attribute(PlayerAttribute.Handling));
        Assert.Equal(12, nguyen.Attribute(PlayerAttribute.AerialReach));
        Assert.Equal(20, nguyen.Raw(PlayerAttribute.LeftFoot));
        Assert.Equal(8, nguyen.Raw(PlayerAttribute.RightFoot));
        Assert.Equal(20, nguyen.Position(PlayerPosition.Goalkeeper));
        nguyen.GameDate = GameDate;
        Assert.Equal(15, nguyen.Age);
        var umar = found.Single(c => c.LastName == "Umar");
        Assert.Equal((76, 151), (umar.CurrentAbility, umar.PotentialAbility));
    }

    [Fact]
    public void Other_clubs_candidates_are_not_returned_for_this_club()
    {
        var found = Reader().ReadForClub(Manager, ManUtd);
        Assert.DoesNotContain(found, c => c.LastName == "Mckogg");
        Assert.Equal("Oshane Mckogg", Assert.Single(Reader().ReadAll(Manager), c => c.LastName == "Mckogg").DisplayName);
    }

    [Fact]
    public void Without_the_profile_section_the_reader_reports_no_offsets()
    {
        var oldProfile = new TestWorld().Profile;
        Assert.Null(oldProfile.YouthCandidates);
        var reader = new YouthCandidateReader(_memory, oldProfile, Module);
        Assert.False(reader.HasOffsets);
        Assert.Throws<InvalidOperationException>(() => reader.ReadAll(Manager));
    }

    [Fact]
    public void Writes_change_only_their_field_and_are_guarded()
    {
        var nguyen = Reader().ReadForClub(Manager, ManUtd).Single(c => c.LastName == "Nguyen");
        int writes = _memory.Writes;
        Writer().SetPotentialAbility(nguyen, 151);
        Assert.Equal(151, BitConverter.ToInt16(_memory.Read(_rec1 + Pa, 2)));
        Writer().SetAttribute(nguyen, PlayerAttribute.Handling, 15);
        Assert.Equal(75, _memory.Read(_rec1 + (ulong)(_profile.Attributes[PlayerAttribute.Handling] - PlayingDataBase), 1)[0]);
        Writer().SetPosition(nguyen, PlayerPosition.Striker, 20);
        Assert.Equal(20, _memory.Read(_rec1 + (ulong)(_profile.Positions[PlayerPosition.Striker] - PlayingDataBase), 1)[0]);
        Writer().SetHeight(nguyen, 196);
        Assert.Equal(196, BitConverter.ToInt16(_memory.Read(_rec1 + Height, 2)));
        Writer().SetCurrentAbility(nguyen, 73);
        Assert.Equal(73, BitConverter.ToInt16(_memory.Read(_rec1 + Ca, 2)));
        Assert.Equal(writes + 5, _memory.Writes);
        // The rest of the record is untouched.
        var fresh = Reader().ReadForClub(Manager, ManUtd).Single(c => c.LastName == "Nguyen");
        fresh.GameDate = GameDate;
        Assert.Equal(("Van Chung", 15, "Vietnam", "Hac Thanh"), (fresh.FirstName, fresh.Age, fresh.NationalityName, fresh.CityOfBirthName));
    }

    [Fact]
    public void Stale_snapshots_refuse_the_write_and_write_nothing()
    {
        var nguyen = Reader().ReadForClub(Manager, ManUtd).Single(c => c.LastName == "Nguyen");
        var writer = Writer();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetPotentialAbility(nguyen, 201));

        // The game moved the value since the snapshot.
        Assert.True(_memory.TryWrite(_rec1 + Pa, BitConverter.GetBytes((short)160)));
        int writes = _memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetPotentialAbility(nguyen, 151));
        Assert.Equal(writes, _memory.Writes);
        Assert.True(_memory.TryWrite(_rec1 + Pa, BitConverter.GetBytes((short)150)));

        // The record changed club since the snapshot.
        Assert.True(_memory.TryWrite(_rec1 + Club, BitConverter.GetBytes(OtherClub)));
        writes = _memory.Writes;   // the test's own setup writes are not the writer's
        Assert.Throws<StaleObjectException>(() => writer.SetPotentialAbility(nguyen, 151));
        Assert.Equal(writes, _memory.Writes);
        Assert.True(_memory.TryWrite(_rec1 + Club, BitConverter.GetBytes(ManUtd)));

        // The manager object is gone (a new save): the record is no longer inside any vector.
        var managerBytes = _memory.Read(Manager, 8);
        Assert.True(_memory.TryWrite(Manager, new byte[8]));
        writes = _memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetPotentialAbility(nguyen, 151));
        Assert.Equal(writes, _memory.Writes);
        Assert.True(_memory.TryWrite(Manager, managerBytes));
        Assert.Equal(151, BeginWriteAndReadBack());
    }

    int BeginWriteAndReadBack()
    {
        var nguyen = Reader().ReadForClub(Manager, ManUtd).Single(c => c.LastName == "Nguyen");
        Writer().SetPotentialAbility(nguyen, 151);
        return BitConverter.ToInt16(_memory.Read(_rec1 + Pa, 2));
    }
}

public class YouthIntakeWindowsTests
{
    [Fact]
    public void England_window_matches_the_guide()
    {
        Assert.Equal(new YouthIntakeWindow(3, 14, 3, 31), YouthIntakeWindows.UsualFor("England"));
    }

    [Fact]
    public void Aliases_resolve_game_nation_names()
    {
        Assert.Equal(YouthIntakeWindows.UsualFor("N. Ireland"), YouthIntakeWindows.UsualFor("Northern Ireland"));
        Assert.Equal(YouthIntakeWindows.UsualFor("Czech Rep."), YouthIntakeWindows.UsualFor("Czech Republic"));
        Assert.Equal(YouthIntakeWindows.UsualFor("U.S.A"), YouthIntakeWindows.UsualFor("USA"));
    }

    [Fact]
    public void Unknown_nations_have_no_usual_window()
    {
        Assert.Null(YouthIntakeWindows.UsualFor(null));
        Assert.Null(YouthIntakeWindows.UsualFor(""));
        Assert.Null(YouthIntakeWindows.UsualFor("Atlantis"));
    }

    [Fact]
    public void Every_listed_window_is_a_real_calendar_span()
    {
        int count = 0;
        foreach (string nation in new[] { "England", "France", "Germany", "Spain", "Italy", "Brazil", "Japan",
            "Norway", "Mexico", "Sweden", "Australia", "Canada", "India", "South Korea", "Paraguay", "Uruguay" })
        {
            var w = Assert.IsType<YouthIntakeWindow>(YouthIntakeWindows.UsualFor(nation));
            _ = new DateOnly(2024, w.StartMonth, w.StartDay);
            _ = new DateOnly(2024, w.EndMonth, w.EndDay);
            count++;
        }
        Assert.True(count >= 16, "The guide table should cover well beyond the spot-checked nations");
    }
}
