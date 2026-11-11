using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

using static Fm27Editor.Core.Tests.RegenIntakeFixtures;

/// <summary>
/// The once-only ledger and its store, against the audit's test matrix: a second scan of the same state changes
/// nothing, a reload keeps its rows by id and branch, another save or another timeline gets its own rows, and an
/// undo, a conflict and a crash in the middle all resolve without ever writing a second time.
/// </summary>
public class RegenLedgerTests
{
    static RegenLedgerEntry Record(RegenLedger ledger, string personUid, string saveId = Save, string branchId = Branch,
        int? pre = null, int? post = null) =>
        ledger.Record(saveId, branchId, personUid, Feature, EventE1.EventId, RegenStatus.LikelyIntake, pre, post);

    [Fact]
    public void T02_A_second_scan_of_the_same_state_adds_nothing()
    {
        // Audit T02: run the first scan, keep its after-scan as the next baseline, scan again with nothing new in
        // between — no rows, no writes, and the ledger still holds exactly the one row it had.
        var ledger = new RegenLedger();
        var first = RegenIntakeDiff.Compare(Baseline(), After(P200), Options(), EventE1);
        foreach (var row in first.AutoEligible) Record(ledger, row.PersonUid);
        Assert.Single(ledger.Entries);

        var second = RegenIntakeDiff.Compare(After(P200), After(P200), Options(), EventE1);
        Assert.Empty(second.Candidates);
        foreach (var row in second.AutoEligible) Record(ledger, row.PersonUid);
        Assert.Single(ledger.Entries);

        Record(ledger, "P200");   // and asking again cannot make a second row for the same player
        Assert.Single(ledger.Entries);
    }

    [Fact]
    public void T03_A_reload_that_keeps_the_uid_does_not_roll_the_player_again()
    {
        // Audit T03: the game is reloaded and every pointer moves. Identity is the id and the branch, so the row
        // that was recorded is still the row that blocks a second roll, and the scan keeps its fingerprint.
        var scan = After(P200);
        string path = NewStorePath();
        try
        {
            RegenIntakeStore.Save(path, new RegenStoreDocument([scan], [Record(new RegenLedger(), "P200")]));

            var reloaded = RegenIntakeStore.Load(path);
            var reloadedScan = Assert.Single(reloaded.Snapshots);
            Assert.Equal(scan.ScanFingerprint, reloadedScan.ScanFingerprint);

            var ledger = reloaded.ToLedger();
            Assert.True(ledger.Contains(Save, Branch, "P200", Feature));
            var again = Record(ledger, "P200");
            Assert.Equal(RegenLedgerState.Pending, again.State);
            Assert.Single(ledger.Entries);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void T04_A_reloaded_save_on_another_timeline_does_not_use_the_old_branch_ledger()
    {
        // Audit T04: the save is loaded from before the intake and the intake happens again. That is another
        // timeline, so the two scans are refused against each other and A0's row is not visible on A1.
        var ledger = new RegenLedger();
        Record(ledger, "P200");

        var redone = After(P200) with { BranchId = OtherBranch };
        var result = RegenIntakeDiff.Compare(Baseline(), redone, Options(), EventE1);

        Assert.Equal(RegenScanVerdict.Quarantined, result.Verdict);
        Assert.Empty(result.Candidates);
        Assert.True(ledger.Contains(Save, Branch, "P200", Feature));
        Assert.False(ledger.Contains(Save, OtherBranch, "P200", Feature));

        Record(ledger, "P200", branchId: OtherBranch);   // the new timeline keeps its own row
        Assert.Equal(2, ledger.Entries.Count);
    }

    [Fact]
    public void T10_The_same_uid_in_two_saves_stays_in_two_namespaces()
    {
        // Audit T10: P200 exists in save A and in save B. Two saves are two namespaces — nothing goes missing in
        // one and nothing is repeated in the other.
        var ledger = new RegenLedger();
        Record(ledger, "P200");
        Assert.True(ledger.Contains(Save, Branch, "P200", Feature));
        Assert.False(ledger.Contains(OtherSave, Branch, "P200", Feature));

        Record(ledger, "P200", saveId: OtherSave);
        Assert.Equal(2, ledger.Entries.Count);

        // Save B already had P200 before its own scan, so nothing new is reported for it there.
        var saveBBaseline = Scan([.. Baseline().Players, P200], gameDate: BeforeDate, saveId: OtherSave,
            phase: RegenPhase.BeforeIntake);
        var result = RegenIntakeDiff.Compare(saveBBaseline, saveBBaseline, Options(), EventE1);
        Assert.DoesNotContain(result.Candidates, row => row.PersonUid == "P200");
    }

    [Fact]
    public void T14_An_undo_at_the_promised_value_restores_the_old_value_and_does_not_roll_again()
    {
        // Audit T14: the game still holds exactly what the row promised, so the old value goes back, the row
        // becomes REVERTED, and nothing re-rolls the player afterwards.
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);
        ledger.MarkApplied(Save, Branch, "P200", Feature);

        var undo = ledger.TryRevert(Save, Branch, "P200", Feature, currentValue: 144);

        Assert.True(undo.Restored);
        Assert.Equal(60, undo.RestoredValue);
        Assert.Equal(RegenLedgerState.Reverted, undo.Entry.State);
        Assert.False(string.IsNullOrEmpty(undo.Detail));

        var again = Record(ledger, "P200");
        Assert.Same(undo.Entry, again);
        Assert.Single(ledger.Entries);
        Assert.Equal(RegenLedgerState.Reverted, again.State);
    }

    [Fact]
    public void T15_An_undo_when_the_value_moved_is_a_conflict_and_overwrites_nothing()
    {
        // Audit T15: the game or another process changed the value since the write. Nothing is overwritten: the
        // row becomes a conflict carrying the reason, and no old value is handed back.
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);
        ledger.MarkApplied(Save, Branch, "P200", Feature);

        var undo = ledger.TryRevert(Save, Branch, "P200", Feature, currentValue: 150);

        Assert.False(undo.Restored);
        Assert.Null(undo.RestoredValue);
        Assert.Equal(RegenLedgerState.Conflict, undo.Entry.State);
        Assert.False(string.IsNullOrEmpty(undo.Entry.ConflictReason));
        Assert.Equal(RegenLedgerState.Conflict, ledger.Find(Save, Branch, "P200", Feature)!.State);
    }

    [Fact]
    public void T16_A_tool_that_stopped_after_writing_reconciles_and_never_writes_twice()
    {
        // Audit T16: the write reached the game but the ledger was never committed. Reading back shows the promised
        // value is already there, so the row is marked applied and nothing is written a second time.
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);

        var afterCrash = ledger.Reconcile(Save, Branch, "P200", Feature, readback: 144);

        Assert.Equal(RegenReconcileAction.AlreadyApplied, afterCrash.Action);
        Assert.Equal(RegenLedgerState.Applied, afterCrash.Entry.State);
        Assert.Equal(1, ledger.Entries.Count);

        var secondLook = ledger.Reconcile(Save, Branch, "P200", Feature, readback: 144);
        Assert.Equal(RegenReconcileAction.AlreadyApplied, secondLook.Action);
        Assert.Equal(1, ledger.Entries.Count);
    }

    [Fact]
    public void A_readback_that_still_holds_the_original_value_says_nothing_was_written()
    {
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);

        var result = ledger.Reconcile(Save, Branch, "P200", Feature, readback: 60);

        Assert.Equal(RegenReconcileAction.NothingWritten, result.Action);
        Assert.Equal(RegenLedgerState.Pending, result.Entry.State);
    }

    [Fact]
    public void A_readback_that_matches_neither_value_is_a_conflict()
    {
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);

        var changed = ledger.Reconcile(Save, Branch, "P200", Feature, readback: 120);
        Assert.Equal(RegenReconcileAction.Conflict, changed.Action);
        Assert.Equal(RegenLedgerState.Conflict, changed.Entry.State);
        Assert.False(string.IsNullOrEmpty(changed.Entry.ConflictReason));

        var unreadable = new RegenLedger();
        Record(unreadable, "P200", pre: 60, post: 144);
        var nothingToRead = unreadable.Reconcile(Save, Branch, "P200", Feature, readback: null);
        Assert.Equal(RegenReconcileAction.Conflict, nothingToRead.Action);
        Assert.Equal(RegenLedgerState.Conflict, nothingToRead.Entry.State);
    }

    [Fact]
    public void A_row_is_verified_only_after_it_was_applied()
    {
        var ledger = new RegenLedger();
        Record(ledger, "P200", pre: 60, post: 144);
        Assert.Throws<InvalidOperationException>(() => ledger.MarkVerified(Save, Branch, "P200", Feature));

        ledger.MarkApplied(Save, Branch, "P200", Feature);
        var verified = ledger.MarkVerified(Save, Branch, "P200", Feature);

        Assert.Equal(RegenLedgerState.Verified, verified.State);
        Assert.True(verified.VerifiedAt is not null);
    }

    [Fact]
    public void A_row_recorded_without_a_promise_cannot_be_marked_applied()
    {
        var ledger = new RegenLedger();
        Record(ledger, "P200");

        Assert.Throws<InvalidOperationException>(() => ledger.MarkApplied(Save, Branch, "P200", Feature));
    }

    [Fact]
    public void The_store_round_trips_scans_and_ledger_rows()
    {
        string path = NewStorePath();
        try
        {
            var scan = After(P200);
            var entry = Record(new RegenLedger(), "P200", pre: 60, post: 144);
            RegenIntakeStore.Save(path, new RegenStoreDocument([scan], [entry]));

            var loaded = RegenIntakeStore.Load(path);
            var loadedScan = Assert.Single(loaded.Snapshots);
            Assert.Equal(Save, loadedScan.SaveId);
            Assert.Equal(Branch, loadedScan.BranchId);
            Assert.Equal(scan.Players.Count, loadedScan.Players.Count);
            Assert.Equal(P200, Assert.Single(loadedScan.Players, p => p.PersonUid == "P200"));
            Assert.Equal(scan.ScanFingerprint, loadedScan.ScanFingerprint);

            var loadedEntry = Assert.Single(loaded.Entries);
            Assert.Equal(RegenLedgerState.Pending, loadedEntry.State);
            Assert.Equal(RegenStatus.LikelyIntake, loadedEntry.Decision);
            Assert.Equal(60, loadedEntry.PreValue);
            Assert.Equal(144, loadedEntry.ExpectedPostValue);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_store_keeps_the_before_and_after_scans_of_one_branch()
    {
        // The whole feature needs both scans after a restart: saving the after scan must not overwrite the baseline.
        string path = NewStorePath();
        try
        {
            var before = Baseline();
            var after = After(P200);
            RegenIntakeStore.Save(path, new RegenStoreDocument([before], []));
            RegenIntakeStore.Save(path, new RegenStoreDocument([after], []));

            var loaded = RegenIntakeStore.Load(path);
            Assert.Equal(2, loaded.Snapshots.Count);
            var loadedBefore = Assert.IsType<RegenSnapshot>(loaded.Snapshot(Save, Branch, RegenPhase.BeforeIntake));
            var loadedAfter = Assert.IsType<RegenSnapshot>(loaded.Snapshot(Save, Branch, RegenPhase.AfterIntake));
            Assert.Equal(before.ScanFingerprint, loadedBefore.ScanFingerprint);
            Assert.Equal(after.ScanFingerprint, loadedAfter.ScanFingerprint);

            var diff = RegenIntakeDiff.Compare(loadedBefore, loadedAfter, Options(), EventE1);
            Assert.Contains(diff.Candidates, c => c.PersonUid == "P200" && c.Status == RegenStatus.LikelyIntake);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_store_file_that_cannot_be_read_is_refused_rather_than_read_as_empty()
    {
        // An empty store would look like "nobody has been processed yet" and invite a second pass, so a file that
        // is not a store is an error.
        string path = NewStorePath();
        try
        {
            File.WriteAllText(path, "{ this is not a store");
            Assert.Throws<InvalidDataException>(() => RegenIntakeStore.Load(path));

            Assert.Equal(RegenStoreDocument.Empty.Snapshots, RegenIntakeStore.Load(path + ".missing").Snapshots);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_store_file_holding_one_row_twice_is_refused()
    {
        var entry = Record(new RegenLedger(), "P200");
        var duplicated = new RegenStoreDocument([], [entry, entry with { State = RegenLedgerState.Applied }]);
        string path = NewStorePath();
        try
        {
            Assert.Throws<InvalidDataException>(() => RegenIntakeStore.Save(path, duplicated));
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
