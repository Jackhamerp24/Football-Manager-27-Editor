using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The club FMRTE 27's presets act on. Game Pass 2493426, 8/11/2026: Mark Shelton (ID 215501) has a York contract and plays
/// for Grimsby Town; FMRTE's Improve Team Cohesion set the Grimsby row (100) and its Trained at club wrote Grimsby (period 1)
/// over his Burton row. The editor used the contract club (York), where he has no cohesion row, so the preset did nothing.
/// </summary>
public class PlayingClubTests
{
    const ulong Team = 0x7600_0000, GrimsbyClub = 0x7610_0000, YorkClub = 0x7620_0000, NotAClub = 0x7630_0000;

    readonly TestWorld _w = new();

    PlayerRecord Mainoo(ulong contractClub) => new()
    {
        Address = TestWorld.Mainoo, VtableRva = TestWorld.PlayerVtable, CurrentAbility = 153, PotentialAbility = 177,
        HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 184,
        RawAttributes = new byte[256], AttributeScale = 5, Positions = new byte[14], Personality = new byte[8],
        Contract = new ContractInfo(0, 0, null, null, new ClubInfo(contractClub, "York", null)),
    };

    void MapClub(ulong at) => TestWorld.Set64(_w.Memory.Map(at, 0x40), 0, TestWorld.ModuleBase + _w.Profile.Club!.ClubVtableRva);

    void PlayingTeam(ulong team, ulong club, uint teamVtable)
    {
        var t = _w.Memory.Map(team, 0x100);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + teamVtable);
        TestWorld.Set64(t, _w.Profile.Contract!.TeamClub, club);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + (ulong)_w.Profile.Team!.PlayerTeam, BitConverter.GetBytes(team)));
    }

    ulong Of(PlayerRecord player) => PlayingClub.Of(_w.Memory, _w.Profile, TestWorld.ModuleBase, player);

    [Fact]
    public void A_loanee_acts_on_the_club_he_plays_for_not_the_contract_club()
    {
        MapClub(GrimsbyClub); MapClub(YorkClub);
        PlayingTeam(Team, GrimsbyClub, _w.Profile.Team!.VtableRva);
        Assert.Equal(GrimsbyClub, Of(Mainoo(YorkClub)));
    }

    [Fact]
    public void Without_a_valid_team_the_contract_club_is_used()
    {
        MapClub(YorkClub);
        Assert.Equal(YorkClub, Of(Mainoo(YorkClub)));   // Mainoo's team pointer is empty in the fixture
    }

    [Fact]
    public void A_team_that_does_not_lead_to_a_club_falls_back_to_the_contract_club()
    {
        MapClub(YorkClub);
        _w.Memory.Map(NotAClub, 0x40);   // zero bytes: not a club vtable
        PlayingTeam(Team, NotAClub, _w.Profile.Team!.VtableRva);
        Assert.Equal(YorkClub, Of(Mainoo(YorkClub)));
    }

    [Fact]
    public void An_object_that_is_not_a_team_is_ignored()
    {
        MapClub(GrimsbyClub); MapClub(YorkClub);
        PlayingTeam(Team, GrimsbyClub, TestWorld.OtherVtable);
        Assert.Equal(YorkClub, Of(Mainoo(YorkClub)));
    }
}
