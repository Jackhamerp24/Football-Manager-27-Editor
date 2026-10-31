using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Club field paths on a fake club laid out like Sheffield United in FM27 27.0.0 (probe results, docs/club-plan.md):
/// club +0x100 → detail object with year founded 1889 at +0xD8 (int32) and chairman status 6916 at +0xDE (int16).
/// Team root: first team reputation 6200. Dates are int16 day of year + int16 year; 1/1/1900 means none.
/// Offsets are injected through the profile JSON, so these tests also cover the club.fields parser.
/// </summary>
public class ClubFieldAccessTests
{
    const uint ClubVt = 0x4CDA1F8, TeamVt = 0x4CE1108, OtherVt = 0x1234560;
    const ulong Club = 0x5000_0000, Detail = 0x5001_0000, Team = 0x5002_0000, Finance = 0x5003_0000;
    const string Fields = """
        "fields": {
          "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
          "ChairmanStatus": { "root": "club", "path": "0x100->0xDE", "type": "int16" },
          "AllowCustomLogo": { "root": "club", "path": "0x100->0xE0", "type": "bool" },
          "EmbargoStart": { "root": "club", "path": "0x100->0xE4", "type": "date" },
          "SupportersCore": { "root": "club", "path": "0x100->0xE8", "type": "int16", "scale": 10 },
          "AverageTicketPrice": { "root": "club", "path": "0x100->0xEC", "type": "float" },
          "WageBudget": { "root": "club", "path": "0x108->0x40", "type": "int32", "objectVtables": ["0x4CF0CC8"] },
          "TeamReputation": { "root": "team", "path": "0x20", "type": "int16" }
        },
        "transferClauses": {
        """;
    readonly FakeMemory _m = new();
    // The real profile's own club.fields block is renamed out of the way so only the test mapping is loaded.
    static string BaseJson => File.ReadAllText(TestWorld.ProfilePath).Replace("\"fields\": {", "\"unusedFields\": {");
    readonly OffsetProfile _profile = OffsetProfile.Load(BaseJson.Replace("\"transferClauses\": {", Fields));

    public ClubFieldAccessTests()
    {
        var club = _m.Map(Club, 0x110);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(club, 0x100, Detail);
        TestWorld.Set64(club, 0x108, Finance);
        var fin = _m.Map(Finance, 0x50);
        TestWorld.Set64(fin, 0, TestWorld.ModuleBase + 0x4CF0CC8);
        BitConverter.GetBytes(496589).CopyTo(fin, 0x40);
        var d = _m.Map(Detail, 0x100);
        BitConverter.GetBytes(1889).CopyTo(d, 0xD8);
        BitConverter.GetBytes((short)6916).CopyTo(d, 0xDE);
        d[0xE0] = 1;
        Convert.FromHexString("01006C07").CopyTo(d, 0xE4); // 1/1/1900
        BitConverter.GetBytes((short)400).CopyTo(d, 0xE8);  // 40 % stored ×10
        BitConverter.GetBytes(35f).CopyTo(d, 0xEC);         // ticket price is a float (C1: Sheffield 35.0)
        var t = _m.Map(Team, 0x40);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + TeamVt);
        BitConverter.GetBytes((short)6200).CopyTo(t, 0x20);
    }

    ClubFieldAccess Access() => new(_m, _profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_club_and_team_fields_through_their_paths()
    {
        var v = Access().ReadAll(Club, Team);

        Assert.Equal(1889, v[ClubField.YearFounded]);
        Assert.Equal(6916, v[ClubField.ChairmanStatus]);
        Assert.Equal(1, v[ClubField.AllowCustomLogo]);
        Assert.Equal(0, v[ClubField.EmbargoStart]);
        Assert.Equal(40, v[ClubField.SupportersCore]);
        Assert.Equal(6200, v[ClubField.TeamReputation]);
        Assert.Equal(35, v[ClubField.AverageTicketPrice]);
        Assert.False(v.ContainsKey(ClubField.Morale));
        Assert.False(Access().IsMapped(ClubField.Morale));
    }

    [Fact]
    public void Writes_go_to_the_resolved_address_with_the_field_type()
    {
        Access().Set(Club, ClubField.YearFounded, 1889, 1900);
        Access().Set(Club, ClubField.ChairmanStatus, 6916, 7000);
        Access().Set(Club, ClubField.EmbargoStart, 0, 20260801);
        Access().Set(Club, ClubField.SupportersCore, 40, 45);
        Access().Set(Team, ClubField.TeamReputation, 6200, 9000);
        Access().Set(Club, ClubField.AverageTicketPrice, 35, 40);

        // 1900 = 0x076C; 7000 = 0x1B58; 1 August 2026 is day 213 = 0xD5, 2026 = 0x07EA; 45 % ×10 = 450 = 0x01C2.
        Assert.Equal(new byte[] { 0x6C, 0x07, 0, 0 }, _m.Read(Detail + 0xD8, 4));
        Assert.Equal(new byte[] { 0x58, 0x1B }, _m.Read(Detail + 0xDE, 2));
        Assert.Equal(new byte[] { 0xD5, 0x00, 0xEA, 0x07 }, _m.Read(Detail + 0xE4, 4));
        Assert.Equal(new byte[] { 0xC2, 0x01 }, _m.Read(Detail + 0xE8, 2));
        Assert.Equal(9000, BitConverter.ToInt16(_m.Read(Team + 0x20, 2)));
        Assert.Equal(BitConverter.GetBytes(40f), _m.Read(Detail + 0xEC, 4));
        Assert.Equal(1, _m.Read(Detail + 0xE0, 1)[0]);
    }

    [Fact]
    public void Refuses_stale_values_wrong_roots_and_out_of_range()
    {
        Assert.True(_m.TryWrite(Detail + 0xD8, BitConverter.GetBytes(1890)));
        int writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Access().Set(Club, ClubField.YearFounded, 1889, 1900));
        // A team field asked of the club object, and a club whose vtable changed.
        Assert.Throws<StaleObjectException>(() => Access().Set(Club, ClubField.TeamReputation, 6200, 9000));
        Assert.True(_m.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + OtherVt)));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Access().Set(Club, ClubField.ChairmanStatus, 6916, 7000));
        Assert.Null(Access().Read(Club, ClubField.ChairmanStatus));
        Assert.Equal(writes, _m.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => Access().Set(Team, ClubField.TeamReputation, 6200, 40000));
    }

    /// <summary>Oxford-style compact finance object (vtable +0x4CF07F8): a field limited to the full object must not be read or written.</summary>
    [Fact]
    public void Field_limited_to_an_object_class_is_skipped_on_other_classes()
    {
        Assert.Equal(496589, Access().Read(Club, ClubField.WageBudget));

        Assert.True(_m.TryWrite(Finance, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CF07F8)));
        int writes = _m.Writes;
        Assert.Null(Access().Read(Club, ClubField.WageBudget));
        Assert.False(Access().ReadAll(Club, Team).ContainsKey(ClubField.WageBudget));
        Assert.Throws<StaleObjectException>(() => Access().Set(Club, ClubField.WageBudget, 496589, 1));
        Assert.Equal(writes, _m.Writes);
        Assert.Equal(496589, BitConverter.ToInt32(_m.Read(Finance + 0x40, 4)));
    }

    [Fact]
    public void Profile_round_trips_club_fields_and_rejects_unknown_names()
    {
        var copy = OffsetProfile.Load(_profile.ToJson());
        var f = copy.Club!.Fields!;
        Assert.Equal(8, f.Count);
        Assert.Equal([0x4CF0CC8u], f[ClubField.WageBudget].ObjectVtables);
        Assert.Equal([0x100, 0xD8], f[ClubField.YearFounded].Path);
        Assert.Equal(ClubFieldRoot.Team, f[ClubField.TeamReputation].Root);
        Assert.Equal(ClubFieldType.Date, f[ClubField.EmbargoStart].Type);
        Assert.Equal(10, f[ClubField.SupportersCore].Scale);

        var bad = BaseJson.Replace("\"transferClauses\": {",
            "\"fields\": { \"NotAField\": { \"root\": \"club\", \"path\": \"0x10\", \"type\": \"int32\" } }, \"transferClauses\": {");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(bad));
        var decimalPath = BaseJson.Replace("\"transferClauses\": {",
            "\"fields\": { \"Morale\": { \"root\": \"club\", \"path\": \"256->216\", \"type\": \"int32\" } }, \"transferClauses\": {");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(decimalPath));
    }
}
