using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

using Regen = Fm27Editor.Core.Tests.RegenIntakeFixtures;
using Golden = Fm27Editor.Core.Tests.GoldenGenerationFixtures;

/// <summary>
/// The bridge from the post-intake diff to the Golden Generation preview: a likely intake reaches the plan, a row
/// held for review and a player who merely moved to the club do not, and a candidate that matches no record is
/// reported rather than dropped. Expected outcomes come from the audit's status matrix; the plan's numbers are
/// worked by hand from the band and the stubbed roll.
/// </summary>
public class RegenGoldenGenerationTests
{
    readonly GoldenGenerationBatch _batch;

    public RegenGoldenGenerationTests()
    {
        var world = new TestWorld();
        // Plan only reads; the writer is what the batch is built from and never touches the fake game here.
        var writer = new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase);
        _batch = new GoldenGenerationBatch(writer, world.Profile);
    }

    /// <summary>The 170–200 band, so a stubbed roll of 180 is inside it.</summary>
    static GoldenGenerationRequest Request() => new() { Potential = GoldenGenerationBands.Band(16) };

    [Fact]
    public void P200_a_likely_intake_reaches_the_plan()
    {
        // Audit P200: the new youth of club X. The snapshot source keys a player by his id as text, so the fixture's
        // P200 travels here under the id his record carries (200).
        var p200 = Regen.P200 with { PersonUid = "200" };
        var diff = RegenIntakeDiff.Compare(Regen.Baseline(), Regen.After(p200), Regen.Options(), Regen.EventE1);
        Assert.Equal(RegenStatus.LikelyIntake, Assert.Single(diff.Candidates).Status);

        var record = Golden.Player(200, born: Regen.P200.DateOfBirth, gameDate: Regen.IntakeDate, ca: 60, pa: 90);
        var preview = RegenGoldenGeneration.Plan(diff, [record], _batch, Request(), random: new StubRandom(180));

        var entry = Assert.Single(preview.Entries);
        Assert.Same(record, entry.Player);
        Assert.True(entry.WillWrite);
        // Hand-calculated: the band 170–200 answers 180, and with the wonderkid chance off CA 60 stays as it was.
        Assert.Equal((90, 60, 180, 60), (entry.PaBefore, entry.CaBefore, entry.PaAfter, entry.CaAfter));
        Assert.Empty(preview.Unmapped);
        Assert.Equal(1, preview.Eligible);
        Assert.Equal(0, preview.Excluded);
    }

    [Fact]
    public void P201_a_player_the_intake_could_not_confirm_is_not_in_the_plan()
    {
        // Audit P201 / T07: a new player with nothing behind the arrival. Without a confirmed intake the diff holds
        // him for review, and the bridge never hands a held row to the plan — his record is present all the same.
        var p201 = Regen.P201 with { PersonUid = "201" };
        var unconfirmed = Regen.EventE1 with { Verified = false };
        var diff = RegenIntakeDiff.Compare(Regen.Baseline(), Regen.After(p201), Regen.Options(), unconfirmed);
        Assert.Equal(RegenStatus.PendingReview, Assert.Single(diff.Candidates).Status);

        var record = Golden.Player(201, born: Regen.P201.DateOfBirth, gameDate: Regen.IntakeDate, ca: 60, pa: 90);
        var preview = RegenGoldenGeneration.Plan(diff, [record], _batch, Request(), random: new StubRandom(180));

        Assert.Empty(preview.Entries);
        Assert.Empty(preview.Unmapped);
        Assert.Equal(0, preview.Eligible);
        Assert.Equal(1, preview.Excluded);
    }

    [Fact]
    public void A_player_who_moved_to_the_club_is_not_in_the_plan()
    {
        // Audit T05: P102 was already in the baseline and is now at club X — a transfer, not an intake. His record
        // is handed to the bridge as well, and it must still stay out of the plan.
        var moved = Regen.Snapshot(Regen.P100, Regen.P101,
            Regen.P102 with { ClubUid = Regen.ClubX, TeamUid = Regen.TeamXYouth });
        var diff = RegenIntakeDiff.Compare(Regen.Baseline(), moved, Regen.Options(), Regen.EventE1);
        Assert.Equal(RegenStatus.NewToClub, Assert.Single(diff.Candidates).Status);

        var record = Golden.Player(102, born: Regen.P102.DateOfBirth, gameDate: Regen.IntakeDate, ca: 60, pa: 90);
        var preview = RegenGoldenGeneration.Plan(diff, [record], _batch, Request(), random: new StubRandom(180));

        Assert.Empty(preview.Entries);
        Assert.Empty(preview.Unmapped);
        Assert.Equal(0, preview.Eligible);
        Assert.Equal(1, preview.Excluded);
    }

    [Fact]
    public void An_eligible_candidate_with_no_matching_record_is_reported_rather_than_dropped()
    {
        var p200 = Regen.P200 with { PersonUid = "200" };
        var diff = RegenIntakeDiff.Compare(Regen.Baseline(), Regen.After(p200), Regen.Options(), Regen.EventE1);

        var preview = RegenGoldenGeneration.Plan(diff, [], _batch, Request(), random: new StubRandom(180));

        Assert.Empty(preview.Entries);
        var unmapped = Assert.Single(preview.Unmapped);
        Assert.Equal("200", unmapped.PersonUid);
        Assert.Equal(RegenStatus.LikelyIntake, unmapped.Status);
        Assert.Equal(1, preview.Eligible);
    }

    [Fact]
    public void Two_records_carrying_one_id_are_refused_instead_of_one_being_picked()
    {
        var p200 = Regen.P200 with { PersonUid = "200" };
        var diff = RegenIntakeDiff.Compare(Regen.Baseline(), Regen.After(p200), Regen.Options(), Regen.EventE1);
        var first = Golden.Player(200, born: Regen.P200.DateOfBirth, gameDate: Regen.IntakeDate);
        var second = Golden.Player(200, born: Regen.P200.DateOfBirth, gameDate: Regen.IntakeDate, address: 0x2000_9000);

        Assert.Throws<InvalidDataException>(() =>
            RegenGoldenGeneration.Plan(diff, [first, second], _batch, Request(), random: new StubRandom(180)));
    }
}
