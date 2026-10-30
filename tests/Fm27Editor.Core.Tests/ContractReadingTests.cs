using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Verification;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Player → contract → team → club, laid out with the offsets measured on FM27 27.0.0
/// (contract +0x330, wage +0x20, expiry +0x48, joined +0x4C, team +0x10, club +0x30, names +0xC0/+0xC8).
/// Built on top of <see cref="TestWorld"/> without changing it, because other test projects share that file.
/// </summary>
public class ContractReadingTests
{
    const ulong Contract = 0x4000_0000, Team = 0x4000_1000, Club = 0x4000_2000;
    const uint ContractVtable = 0x4B00AE8, ClubVtable = 0x4CDA1F8;

    readonly TestWorld _w = new();

    public ContractReadingTests()
    {
        var c = _w.Memory.Map(Contract, 0x60);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        TestWorld.Set64(c, 0x10, Team);
        BitConverter.GetBytes(135000).CopyTo(c, 0x20);
        TestWorld.Set16(c, 0x48, 181); TestWorld.Set16(c, 0x4A, 2031); // 30 June 2031 is day 181
        TestWorld.Set16(c, 0x4C, 182); TestWorld.Set16(c, 0x4E, 2014); // 1 July 2014 is day 182

        var t = _w.Memory.Map(Team, 0x40);
        TestWorld.Set64(t, 0x30, Club);

        var k = _w.Memory.Map(Club, 0xD0);
        TestWorld.Set64(k, 0x00, TestWorld.ModuleBase + ClubVtable);
        TestWorld.Set64(k, 0xC0, 0x4000_3000); _w.Memory.MapString(0x4000_3000, "Manchester United");
        TestWorld.Set64(k, 0xC8, 0x4000_3100); _w.Memory.MapString(0x4000_3100, "Man Utd");

        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
    }

    PlayerRecord Mainoo(OffsetProfile? profile = null) =>
        new PlayerRepository(_w.Memory, profile ?? _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;

    [Fact]
    public void Reads_wage_expiry_joined_and_club_names()
    {
        var p = Mainoo();
        Assert.Equal(135000, p.Contract!.Wage);
        Assert.Equal(new DateOnly(2031, 6, 30), p.Contract.Expires);
        Assert.Equal(new DateOnly(2014, 7, 1), p.Contract.Joined);
        Assert.Equal("Manchester United", p.Club!.Name);
        Assert.Equal("Man Utd", p.Club.DisplayName);
    }

    [Fact]
    public void Player_without_contract_pointer_has_no_contract()
    {
        Assert.Null(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Yamal)!.Contract);
    }

    [Fact]
    public void Unknown_contract_type_is_not_decoded()
    {
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1111110)));
        Assert.Null(Mainoo().Contract);
    }

    [Fact]
    public void Club_with_unexpected_vtable_is_dropped_but_contract_kept()
    {
        Assert.True(_w.Memory.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1111110)));
        var p = Mainoo();
        Assert.Equal(135000, p.Contract!.Wage);
        Assert.Null(p.Club);
    }

    [Fact]
    public void Profile_without_contract_section_still_loads_players()
    {
        var json = File.ReadAllText(TestWorld.ProfilePath);
        int cut = json.IndexOf(",\n  \"contract\"", StringComparison.Ordinal);
        if (cut < 0) cut = json.IndexOf(",\r\n  \"contract\"", StringComparison.Ordinal);
        Assert.True(cut > 0);
        var profile = OffsetProfile.Load(json[..cut] + "\n}");

        Assert.Null(profile.Contract);
        var p = Mainoo(profile);
        Assert.Null(p.Contract);
        Assert.Equal("Kobbie Mainoo", p.DisplayName);
    }

    [Fact]
    public void Written_json_keeps_contract_and_club_offsets()
    {
        var copy = OffsetProfile.Load(_w.Profile.ToJson());
        Assert.Equivalent(_w.Profile.Contract, copy.Contract);
        Assert.Equivalent(_w.Profile.Club, copy.Club);
        Assert.Equal(0x330, copy.Contract!.PlayerContract);
    }

    [Fact]
    public void Golden_comparison_reports_a_wrong_club_and_wage()
    {
        var golden = new GoldenPlayer("Kobbie Mainoo", null, null, null, false, null, null, new Dictionary<PlayerAttribute, int>())
        {
            Club = "Man City", Wage = 100000, ContractExpires = new DateOnly(2031, 6, 30),
        };
        var diffs = GoldenPlayers.Compare(golden, Mainoo());
        Assert.Equal(2, diffs.Count);
        Assert.Contains(diffs, d => d.StartsWith("Club:"));
        Assert.Contains(diffs, d => d.StartsWith("Wage:"));
    }
}
