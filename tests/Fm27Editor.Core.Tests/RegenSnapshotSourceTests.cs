using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The snapshot source against a fake game built from the 2493426 profile — the build whose team classification is
/// measured. Expected values are the layout written into the fake objects (club id 680 at club+0xC, first-team
/// classification 0 at team+0x28, youth contract type 3 at contract+0xC3), not anything the source decided.
/// </summary>
public class RegenSnapshotSourceTests
{
    const ulong ClubAt = 0x2000_1000, TeamAt = 0x2000_2000, ContractAt = 0x2000_3000, PlayerAt = 0x2000_4000;
    const int ClubId = 680, PlayerId = 424242, FirstTeamClassification = 0;

    static readonly OffsetProfile Profile =
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));

    /// <summary>
    /// One player in a fake game: vtable, id and the club/team/contract chain the source walks. Without membership
    /// the player object carries no team pointer and no contract at all.
    /// </summary>
    static (Fm27RegenSnapshotSource Source, PlayerRecord Record) Player(
        bool withMembership, int? id = PlayerId, ContractType contractType = ContractType.Youth)
    {
        var club = Profile.Club ?? throw new InvalidOperationException("The profile has no club offsets");
        var team = Profile.Team ?? throw new InvalidOperationException("The profile has no team offsets");
        var contract = Profile.Contract ?? throw new InvalidOperationException("The profile has no contract offsets");
        var memory = new FakeMemory();
        uint vtable = Profile.PlayerVtableRvas[0];

        if (withMembership)
        {
            var clubObject = memory.Map(ClubAt, 0xD0);
            TestWorld.Set64(clubObject, 0, TestWorld.ModuleBase + club.ClubVtableRva);
            BitConverter.GetBytes(ClubId).CopyTo(clubObject, club.Id!.Value);

            var teamObject = memory.Map(TeamAt, 0x40);
            TestWorld.Set64(teamObject, 0, TestWorld.ModuleBase + team.VtableRva);
            BitConverter.GetBytes(FirstTeamClassification).CopyTo(teamObject, team.Classification!.Value);
            teamObject[team.AgeLimit!.Value] = 100;   // the age byte the staff reader pairs with the classification
            TestWorld.Set64(teamObject, contract.TeamClub, ClubAt);

            var contractObject = memory.Map(ContractAt, 0x100);
            TestWorld.Set64(contractObject, 0, TestWorld.ModuleBase + contract.ContractVtableRva);
            contractObject[contract.Type!.Value] = (byte)contractType;
        }

        var playerObject = memory.Map(PlayerAt, 0x3C8);
        TestWorld.Set64(playerObject, 0, TestWorld.ModuleBase + vtable);
        if (id is { } readable) BitConverter.GetBytes(readable).CopyTo(playerObject, Profile.Fields.Id!.Value);
        if (withMembership)
        {
            TestWorld.Set64(playerObject, team.PlayerTeam, TeamAt);
            TestWorld.Set64(playerObject, contract.PlayerContract, ContractAt);
        }

        var record = new PlayerRecord
        {
            Address = PlayerAt,
            VtableRva = vtable,
            Id = id,
            FirstName = "Test",
            LastName = "Youth",
            CurrentAbility = 60,
            PotentialAbility = 90,
            HomeReputation = 0,
            CurrentReputation = 0,
            WorldReputation = 0,
            Height = 180,
            DateOfBirth = new(2010, 2, 2),
            Contract = withMembership
                ? new ContractInfo(ContractAt, 1_000, null, null, new ClubInfo(ClubAt, "Test FC", "TFC"))
                : null,
            RawAttributes = new byte[Enum.GetValues<PlayerAttribute>().Length],
            AttributeScale = 5,
            Positions = new byte[Enum.GetValues<PlayerPosition>().Length],
            Personality = new byte[Enum.GetValues<PersonalityAttribute>().Length],
        };
        return (new Fm27RegenSnapshotSource(memory, Profile, TestWorld.ModuleBase), record);
    }

    static RegenSnapshot Scan(Fm27RegenSnapshotSource source, PlayerRecord record) =>
        source.Scan([record], RegenIntakeFixtures.Save, RegenIntakeFixtures.Branch,
            RegenIntakeFixtures.IntakeDate, RegenPhase.AfterIntake);

    [Fact]
    public void The_scan_carries_the_id_birth_date_club_team_and_youth_contract()
    {
        var (source, record) = Player(withMembership: true);

        var scan = Scan(source, record);

        var view = Assert.Single(scan.Players);
        Assert.Equal("424242", view.PersonUid);                       // the id as text, never a number that could truncate
        Assert.Equal(new DateOnly(2010, 2, 2), view.DateOfBirth);
        Assert.Equal("680", view.ClubUid);                            // the club's id — not its address, not its name
        Assert.Equal("680:0", view.TeamUid);                          // club id + first-team classification
        Assert.True(view.IsPlayer);
        Assert.True(view.IsYouth);                                    // contract type 3 on this fake player
        Assert.Null(view.TransferEvidence);
        Assert.Equal("player-record", view.Provenance);
        Assert.True(scan.Complete);
        Assert.Equal(RegenScope.Global, scan.Scope);
        Assert.Equal(RegenPhase.AfterIntake, scan.Phase);
        Assert.Equal(RegenIntakeFixtures.Save, scan.SaveId);
        Assert.Equal(RegenIntakeFixtures.Branch, scan.BranchId);
    }

    [Fact]
    public void A_player_on_a_full_time_contract_is_not_marked_as_youth()
    {
        var (source, record) = Player(withMembership: true, contractType: ContractType.FullTime);

        var view = Assert.Single(Scan(source, record).Players);

        Assert.False(view.IsYouth);
        Assert.Equal("680", view.ClubUid);   // the membership still reads; only the contract type differs
    }

    [Fact]
    public void A_player_with_no_readable_club_or_team_is_held_for_review()
    {
        var (source, record) = Player(withMembership: false);
        var scan = Scan(source, record);
        var view = Assert.Single(scan.Players);
        Assert.Null(view.ClubUid);
        Assert.Null(view.TeamUid);

        // The diff must hold him rather than read the missing membership as a negative answer.
        var diff = RegenIntakeDiff.Compare(RegenIntakeFixtures.Baseline(), scan,
            RegenIntakeFixtures.Options(), RegenIntakeFixtures.EventE1);

        var row = Assert.Single(diff.Candidates, candidate => candidate.PersonUid == "424242");
        Assert.Equal(RegenStatus.PendingReview, row.Status);
        Assert.False(row.AutoEligible);
        Assert.Contains("club or team", row.Detail);
        Assert.Empty(diff.AutoEligible);
    }

    [Fact]
    public void A_record_without_a_readable_id_makes_the_scan_incomplete_and_the_diff_refuses_it()
    {
        var (source, record) = Player(withMembership: true, id: null);

        var scan = Scan(source, record);

        Assert.False(scan.Complete);   // the record was left out, so the scan cannot claim to list every player
        Assert.Empty(scan.Players);

        var diff = RegenIntakeDiff.Compare(RegenIntakeFixtures.Baseline(), scan,
            RegenIntakeFixtures.Options(), RegenIntakeFixtures.EventE1);
        Assert.Equal(RegenScanVerdict.Quarantined, diff.Verdict);
        Assert.Empty(diff.Candidates);
    }

    [Fact]
    public void The_source_refuses_a_scan_without_a_save_or_timeline_id() =>
        Assert.Throws<ArgumentException>(() => Player(withMembership: true).Source.Scan(
            [], saveId: " ", branchId: RegenIntakeFixtures.Branch, gameDate: RegenIntakeFixtures.IntakeDate));
}
