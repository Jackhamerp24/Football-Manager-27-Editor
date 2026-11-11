using System.Text.Json.Nodes;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Competition Details (CE table's cmpo layout, FMCET26 update 8). Enum names are transcribed from the
/// table's dropdowns (Type 0-39, Trophy 0-11, Lowest Official Category -1-5); the La Liga row of the CE
/// Details screenshot anchors the realistic case (type 1 "Domestic Division", trophy 6
/// "National Championship 2", level 6, category 6 which the dropdown does not map). All other bytes are
/// set by hand in the test.
/// </summary>
public class CompetitionDetailsTests
{
    const ulong LaLiga = 0x7000_1000, NotACompetition = 0x7000_2000;

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void Competition(ulong at, int index, int id, string threeLetter, short reputation,
        byte type, byte level, byte trophy, sbyte category, short position, short last,
        byte extra, byte seated, byte flags)
    {
        var o = _m.Map(at, 0x1B0);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Competition!.VtableRva);
        BitConverter.GetBytes(index).CopyTo(o, 8);
        BitConverter.GetBytes(id).CopyTo(o, 0xC);
        TestWorld.Set64(o, 0x40, at + 0x800); _m.MapString(at + 0x800, "Name");
        TestWorld.Set64(o, 0x48, at + 0x900); _m.MapString(at + 0x900, "Short");
        TestWorld.Set64(o, 0x50, at + 0xA00); _m.MapString(at + 0xA00, threeLetter);
        BitConverter.GetBytes(reputation).CopyTo(o, 0x188);
        BitConverter.GetBytes(reputation).CopyTo(o, 0x18A);
        o[0xF4] = type; o[0x199] = level; o[0x19C] = trophy; o[0x19E] = (byte)category;
        BitConverter.GetBytes(position).CopyTo(o, 0x17E);
        BitConverter.GetBytes(last).CopyTo(o, 0x17C);
        o[0xF6] = extra; o[0xF7] = seated; o[0x1A0] = flags;
    }

    CompetitionRecord LaLigaRecord()
    {
        Competition(LaLiga, 51, 67, "LAL", 178, 1, 6, 6, 3, -1, -1, 1, 0, 0b101);
        return new CompetitionRepository(_m, _w.Profile, TestWorld.ModuleBase).Reload(LaLiga)!;
    }

    CompetitionDetails Read(CompetitionRecord record) =>
        new CompetitionDetailsAccess(_m, _w.Profile, TestWorld.ModuleBase).Read(record)!;

    [Fact]
    public void Full_details_read_and_named()
    {
        var d = Read(LaLigaRecord());
        Assert.Equal("LAL", d.ThreeLetterName);
        Assert.Equal((byte)1, d.Type);
        Assert.Equal("Domestic Division", d.TypeName);
        Assert.Equal((byte)6, d.CompetitionLevel);
        Assert.Equal("National Championship 2", d.TrophyName);
        Assert.Equal((sbyte)3, d.LowestOfficialCategory);
        Assert.Equal("European First Group", d.LowestOfficialCategoryName);
        Assert.Equal((short)-1, d.ContinentalPosition);
        Assert.Equal((short)-1, d.LastContinentalPosition);
        Assert.Equal((byte)1, d.UsesExtraOfficials);
        Assert.Equal((byte)0, d.SeatedStadiumsOnly);
        Assert.Equal((byte)0b101, d.Flags);
    }

    [Fact]
    public void Values_outside_the_ce_tables_read_as_unknown()
    {
        // La Liga's own category 6 is outside the table's -1-5; nothing may be invented for it.
        Competition(LaLiga, 51, 67, "LAL", 178, 200, 200, 200, 6, -1, -1, 1, 0, 0);
        var d = Read(LaLigaRecord());
        Assert.Null(d.Type);
        Assert.Null(d.TypeName);
        Assert.Null(d.CompetitionLevel);
        Assert.Null(d.Trophy);
        Assert.Null(d.TrophyName);
        Assert.Null(d.LowestOfficialCategory);
        Assert.Null(d.LowestOfficialCategoryName);
        Assert.Equal("LAL", d.ThreeLetterName);
    }

    [Fact]
    public void Reputation_anchor_mismatch_returns_null()
    {
        var record = LaLigaRecord();
        Assert.True(_m.TryWrite(LaLiga + 0x188, BitConverter.GetBytes((short)179)));
        Assert.Null(new CompetitionDetailsAccess(_m, _w.Profile, TestWorld.ModuleBase).Read(record));
    }

    [Fact]
    public void Wrong_vtable_or_id_returns_null()
    {
        var record = LaLigaRecord();
        var reader = new CompetitionDetailsAccess(_m, _w.Profile, TestWorld.ModuleBase);
        Assert.True(_m.TryWrite(LaLiga, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.OtherVtable)));
        Assert.Null(reader.Read(record));
    }

    [Fact]
    public void Missing_profile_offsets_return_null()
    {
        var record = LaLigaRecord();
        var node = JsonNode.Parse(File.ReadAllText(TestWorld.ProfilePath))!.AsObject();
        foreach (var key in new[] { "threeLetterName", "type", "usesExtraOfficials", "seatedStadiumsOnly",
                     "lastContinentalPosition", "continentalPosition", "competitionLevel", "trophy",
                     "lowestOfficialCategory", "flags" })
            node["competition"]!.AsObject().Remove(key);
        var profile = OffsetProfile.Load(node.ToJsonString());
        Assert.Null(new CompetitionDetailsAccess(_m, profile, TestWorld.ModuleBase).Read(record));
    }

    [Fact]
    public void Bad_name_pointer_reads_unknown_name_and_keeps_the_rest()
    {
        Competition(LaLiga, 51, 67, "LAL", 178, 1, 6, 6, 3, -1, -1, 1, 0, 0);
        Assert.True(_m.TryWrite(LaLiga + 0x50, BitConverter.GetBytes(LaLiga + 0xB00)));
        _m.Map(LaLiga + 0xB00, 8);   // length prefix reads 0: not a name
        var d = Read(LaLigaRecord());
        Assert.Null(d.ThreeLetterName);
        Assert.Equal("Domestic Division", d.TypeName);
    }

    [Fact]
    public void Positions_out_of_range_read_unknown()
    {
        Competition(LaLiga, 51, 67, "LAL", 178, 1, 6, 6, 3, 1000, -1, 1, 0, 0);
        var d = Read(LaLigaRecord());
        Assert.Null(d.ContinentalPosition);
        Assert.Equal((short)-1, d.LastContinentalPosition);
    }

    [Theory]
    [InlineData(0, "Domestic Top Division")]
    [InlineData(21, "Club Champions Cup")]
    [InlineData(30, null)]   // the table skips 30: a value there is unmapped, not Youth Cup
    [InlineData(39, "Youth Cup")]
    [InlineData(40, null)]
    public void Type_names_follow_the_ce_table(byte value, string? expected)
    {
        Competition(LaLiga, 51, 67, "LAL", 178, value, 6, 6, 3, -1, -1, 1, 0, 0);
        Assert.Equal(expected, Read(LaLigaRecord()).TypeName);
    }

    [Theory]
    [InlineData(0, "Not set")]
    [InlineData(6, "National Championship 2")]
    [InlineData(11, "Continental Cup 1")]
    [InlineData(12, null)]
    public void Trophy_names_follow_the_ce_table(byte value, string? expected)
    {
        Competition(LaLiga, 51, 67, "LAL", 178, 1, 6, value, 3, -1, -1, 1, 0, 0);
        Assert.Equal(expected, Read(LaLigaRecord()).TrophyName);
    }

    [Theory]
    [InlineData(-1, "Not Available")]
    [InlineData(0, "None")]
    [InlineData(5, "European Third Group")]
    [InlineData(6, null)]
    [InlineData(-2, null)]
    public void Category_names_follow_the_ce_table(sbyte value, string? expected)
    {
        Competition(LaLiga, 51, 67, "LAL", 178, 1, 6, 6, value, -1, -1, 1, 0, 0);
        var d = Read(LaLigaRecord());
        // sbyte 6 / -2 fail the reader's -1-5 check; -1 and 0-5 map.
        Assert.Equal(value is >= -1 and <= 5 ? value : null, d.LowestOfficialCategory);
        Assert.Equal(expected, d.LowestOfficialCategoryName);
    }
}
