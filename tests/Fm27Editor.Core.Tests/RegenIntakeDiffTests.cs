using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

using static Fm27Editor.Core.Tests.RegenIntakeFixtures;

/// <summary>
/// The post-intake diff against the audit's fixtures. Every expected result is taken from the audit's test matrix
/// (T01–T20), which is a product requirement, not a restatement of this code: new-to-database, new-to-club and
/// held-for-review have to stay three different things, and anything unreadable has to fail closed.
/// </summary>
public class RegenIntakeDiffTests
{
    [Fact]
    public void T01_A_new_youth_at_the_clubs_intake_is_a_likely_intake()
    {
        // Audit T01: S1 = S0 ∪ {P200}; P200 is at the youth of club X on intake E1, has a valid birth date and no
        // transfer behind it → LIKELY_INTAKE, and only that status may ever be acted on without asking.
        var result = RegenIntakeDiff.Compare(Baseline(), After(P200), Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Classified, result.Verdict);
        var row = Assert.Single(result.Candidates);
        Assert.Equal("P200", row.PersonUid);
        Assert.Equal(RegenStatus.LikelyIntake, row.Status);
        Assert.True(row.AutoEligible);
        Assert.False(string.IsNullOrEmpty(row.Detail));
        Assert.Same(row, Assert.Single(result.AutoEligible));
    }

    [Fact]
    public void T05_A_player_who_moved_into_the_club_is_new_to_the_club_not_new_to_the_database()
    {
        // Audit T05: P102 moves from club Y to the youth of club X and was already in the baseline, so it is a
        // move, not a newly generated player, and nothing may be written for it.
        var after = Snapshot(P100, P101, P102 with { ClubUid = ClubX, TeamUid = TeamXYouth });

        var result = RegenIntakeDiff.Compare(Baseline(), after, Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Classified, result.Verdict);
        var row = Assert.Single(result.Candidates);
        Assert.Equal("P102", row.PersonUid);
        Assert.Equal(RegenStatus.NewToClub, row.Status);
        Assert.False(row.AutoEligible);
        Assert.Empty(result.AutoEligible);
    }

    [Fact]
    public void T06_A_player_already_at_the_club_before_the_baseline_is_not_an_intake()
    {
        // Audit T06: P101 was at club X before the baseline and is still there — an old player, not a new intake.
        var result = RegenIntakeDiff.Compare(Baseline(), Baseline(), Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Classified, result.Verdict);
        Assert.Empty(result.Candidates);
        Assert.DoesNotContain(result.Candidates, row => row.PersonUid == "P101");
    }

    [Fact]
    public void T07_A_new_player_without_a_confirmed_intake_is_held_for_review()
    {
        // Audit T07: P201 is new in the global database but arrived in the senior squad and nothing confirmed an
        // intake for it → PENDING_REVIEW, with nothing automatic.
        var unconfirmed = EventE1 with { Verified = false };

        var result = RegenIntakeDiff.Compare(Baseline(), After(P201), Options(), unconfirmed);

        Assert.Equal(RegenScanVerdict.Classified, result.Verdict);
        var row = Assert.Single(result.Candidates);
        Assert.Equal(RegenStatus.PendingReview, row.Status);
        Assert.False(row.AutoEligible);
        Assert.Empty(result.AutoEligible);
        Assert.Contains("confirmed", row.Detail);
    }

    [Fact]
    public void T08_A_player_promoted_to_the_first_team_after_the_intake_is_not_dropped()
    {
        // Audit T08: a new youth that went straight up to the senior squad must not be dropped while a native,
        // verified intake event holds for it. The squad alone is a weak signal and never decides either way.
        var promoted = P200 with { TeamUid = TeamXFirst, IsYouth = false };

        var result = RegenIntakeDiff.Compare(Baseline(), After(promoted), Options(), EventE1);

        var row = Assert.Single(result.Candidates);
        Assert.Equal("P200", row.PersonUid);
        Assert.Equal(RegenStatus.LikelyIntake, row.Status);
        Assert.True(row.AutoEligible);
    }

    [Fact]
    public void T09_The_same_id_twice_in_one_scan_quarantines_the_scan()
    {
        // Audit T09: P200 turns up as two different records in one scan. The scan describes no single world, so it
        // is refused and no record is picked over the other.
        var result = RegenIntakeDiff.Compare(Baseline(), After(P200, P200), Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Quarantined, result.Verdict);
        Assert.True(result.Quarantined);
        Assert.Empty(result.Candidates);
        Assert.Contains("twice", result.Detail);
    }

    [Fact]
    public void T11_An_incomplete_scan_quarantines_instead_of_classifying()
    {
        // Audit T11: a scan that missed a team or the hidden players is marked incomplete, and an incomplete scan
        // cannot tell a player who was never there from one it did not see — so nothing is called new.
        var result = RegenIntakeDiff.Compare(Baseline(), After(P200) with { Complete = false }, Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Quarantined, result.Verdict);
        Assert.Empty(result.Candidates);
        Assert.Contains("incomplete", result.Detail);
    }

    [Fact]
    public void T12_An_intake_date_without_any_new_player_reports_nothing()
    {
        // Audit T12: the intake day shows up and nothing new appears. No player is invented and no existing one is
        // relabelled as a regen to fill the gap.
        var result = RegenIntakeDiff.Compare(Baseline(), Baseline(), Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Classified, result.Verdict);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.AutoEligible);
    }

    [Fact]
    public void T13_A_transfer_that_lands_on_intake_day_is_excluded_by_the_baseline()
    {
        // Audit T13: a player born in the youth age range moves into club X's youth on the intake day. The
        // baseline says it is not new, and a new arrival that carries a move record is held back even though the
        // dates would otherwise fit.
        var moved = Snapshot(P100, P101, P102 with { ClubUid = ClubX, TeamUid = TeamXYouth });
        var arrived = new RegenObservation("P202", DateOfBirth: new(2010, 3, 3), ClubUid: ClubX, TeamUid: TeamXYouth,
            IsPlayer: true, IsYouth: true, TransferEvidence: "signed from club Y");

        var byBaseline = RegenIntakeDiff.Compare(Baseline(), moved, Options(), EventE1);
        var movedRow = Assert.Single(byBaseline.Candidates);
        Assert.Equal("P102", movedRow.PersonUid);
        Assert.Equal(RegenStatus.NewToClub, movedRow.Status);
        Assert.False(movedRow.AutoEligible);

        var byHistory = RegenIntakeDiff.Compare(Baseline(), After(arrived), Options(), EventE1);
        var arrivedRow = Assert.Single(byHistory.Candidates, row => row.PersonUid == "P202");
        Assert.Equal(RegenStatus.PendingReview, arrivedRow.Status);
        Assert.Contains("signed from club Y", arrivedRow.Detail);
        Assert.Empty(byHistory.AutoEligible);
    }

    [Fact]
    public void T17_The_uid_threshold_is_not_used_to_spot_an_intake()
    {
        // Audit T17: the CE threshold 2002068000 proves nothing. A player already in the baseline with that id is
        // not new whatever the number says, the same id appearing out of nowhere is new by the diff and gains no
        // standing from its value, and a new player with a small id is a candidate all the same.
        const string threshold = "2002068000";
        var withoutThreshold = Scan([.. Baseline().Players], gameDate: BeforeDate, phase: RegenPhase.BeforeIntake);
        var withThreshold = Scan(
            [.. Baseline().Players, Person(threshold, ClubX, TeamXYouth, new(2009, 12, 31), youth: true)],
            gameDate: IntakeDate, phase: RegenPhase.AfterIntake);

        var alreadyThere = RegenIntakeDiff.Compare(withThreshold, withThreshold, Options(), EventE1);
        Assert.DoesNotContain(alreadyThere.Candidates, row => row.PersonUid == threshold);

        var appeared = RegenIntakeDiff.Compare(withoutThreshold, withThreshold, Options(), EventE1);
        var appearedRow = Assert.Single(appeared.Candidates, row => row.PersonUid == threshold);
        Assert.Equal(RegenStatus.LikelyIntake, appearedRow.Status);

        // and with nothing confirmed behind the scan the id is only held for review, high or low
        var unconfirmed = RegenIntakeDiff.Compare(withoutThreshold, withThreshold, Options(), null);
        var heldRow = Assert.Single(unconfirmed.Candidates, row => row.PersonUid == threshold);
        Assert.Equal(RegenStatus.PendingReview, heldRow.Status);

        var smallId = RegenIntakeDiff.Compare(Baseline(),
            After(Person("7", ClubX, TeamXYouth, new(2010, 8, 8), youth: true)), Options(), EventE1);
        Assert.Contains(smallId.Candidates, row => row.PersonUid == "7" && row.Status == RegenStatus.LikelyIntake);
    }

    [Fact]
    public void T18_No_baseline_means_unknown_baseline_and_no_candidates()
    {
        // Audit T18: the tool first runs after the intake. Nothing may be called new, and no youth is taken for a
        // regen on that strength — the rows are for manual review only.
        var result = RegenIntakeDiff.Compare(null, After(P200), Options(), EventE1);

        Assert.Equal(RegenScanVerdict.UnknownBaseline, result.Verdict);
        Assert.False(result.Quarantined);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.AutoEligible);
        Assert.Contains("baseline", result.Detail);
    }

    [Fact]
    public void T19_An_uid_above_32_bits_and_an_unverified_object_type_fail_closed()
    {
        // Audit T19: FM27's ids have not been shown to fit in 32 bits, so an id is text and is never parsed into a
        // number — 4294967296 must not collapse onto the id 0 that is also in the baseline. A record whose object
        // type was never verified is held for review rather than read as a player, so nothing is edited.
        const string wide = "4294967296";
        var baseline = Snapshot(P100, P101, P102, Person("0", ClubY, TeamYYouth, new(2009, 1, 1), youth: true));
        var unverified = new RegenObservation(wide, DateOfBirth: new(2010, 5, 5), ClubUid: ClubX, TeamUid: TeamXYouth,
            IsPlayer: null, IsYouth: true);

        var result = RegenIntakeDiff.Compare(baseline, baseline with { Players = [.. baseline.Players, unverified] },
            Options(), EventE1);

        var row = Assert.Single(result.Candidates);
        Assert.Equal(wide, row.PersonUid);
        Assert.Equal(RegenStatus.PendingReview, row.Status);
        Assert.False(row.AutoEligible);
        Assert.Contains("object type", row.Detail);
    }

    [Fact]
    public void T20_Missing_birth_date_or_club_holds_the_player_for_review()
    {
        // Audit T20: a new player without a readable birth date, or without club and team membership, is held for
        // review and written nowhere. Missing data is never read as a negative answer.
        var noBirth = new RegenObservation("P210", ClubUid: ClubX, TeamUid: TeamXYouth, IsPlayer: true, IsYouth: true);
        var noClub = new RegenObservation("P211", DateOfBirth: new(2010, 7, 7), IsPlayer: true, IsYouth: true);

        var result = RegenIntakeDiff.Compare(Baseline(), After(noBirth, noClub), Options(), EventE1);

        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, row => Assert.Equal(RegenStatus.PendingReview, row.Status));
        Assert.Empty(result.AutoEligible);
        Assert.Contains(result.Candidates, row => row.PersonUid == "P210" && row.Detail.Contains("birth date"));
        Assert.Contains(result.Candidates, row => row.PersonUid == "P211" && row.Detail.Contains("club or team"));
    }

    [Fact]
    public void A_scan_of_something_less_than_the_whole_database_is_refused()
    {
        // A scan of one club cannot see the players outside it, so it can never say who is new to the database.
        var result = RegenIntakeDiff.Compare(Baseline(), After(P200) with { Scope = "one club" }, Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Quarantined, result.Verdict);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void A_baseline_from_another_save_or_timeline_or_a_later_date_is_refused()
    {
        var baseline = Baseline();

        var otherSave = RegenIntakeDiff.Compare(baseline, After(P200) with { SaveId = OtherSave }, Options(), EventE1);
        Assert.Equal(RegenScanVerdict.Quarantined, otherSave.Verdict);
        Assert.Empty(otherSave.Candidates);

        var otherTimeline = RegenIntakeDiff.Compare(baseline, After(P200) with { BranchId = OtherBranch }, Options(), EventE1);
        Assert.Equal(RegenScanVerdict.Quarantined, otherTimeline.Verdict);
        Assert.Empty(otherTimeline.Candidates);

        var backwards = RegenIntakeDiff.Compare(baseline, Baseline() with { GameDate = BeforeDate.AddDays(-3) },
            Options(), EventE1);
        Assert.Equal(RegenScanVerdict.Quarantined, backwards.Verdict);
        Assert.Empty(backwards.Candidates);
    }

    [Fact]
    public void A_player_outside_the_configured_age_window_is_held_for_review()
    {
        var tooOld = Person("P220", ClubX, TeamXYouth, new(1980, 1, 1), youth: true);

        var result = RegenIntakeDiff.Compare(Baseline(), After(tooOld), Options(), EventE1);

        var row = Assert.Single(result.Candidates);
        Assert.Equal(RegenStatus.PendingReview, row.Status);
        Assert.False(row.AutoEligible);
        Assert.Contains("age", row.Detail);
    }

    [Fact]
    public void A_backwards_age_range_or_a_negative_window_is_refused() =>
        Assert.Throws<ArgumentException>(() => (Options() with { MinAge = 20, MaxAge = 15 }).Validate());

    [Fact]
    public void A_negative_intake_window_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegenIntakeOptions(IntakeWindowDays: -1).Validate());

    [Fact]
    public void Two_scans_of_the_same_state_share_a_fingerprint_and_a_new_player_changes_it()
    {
        var first = After(P200);
        var reloaded = After(P200);   // a reload moves every pointer; nothing here stores one

        Assert.Equal(first.ScanFingerprint, reloaded.ScanFingerprint);
        Assert.Equal(first.ScanFingerprint, (first with { Players = [.. first.Players.Reverse()] }).ScanFingerprint);
        Assert.NotEqual(first.ScanFingerprint, After(P200, P201).ScanFingerprint);
    }
}
