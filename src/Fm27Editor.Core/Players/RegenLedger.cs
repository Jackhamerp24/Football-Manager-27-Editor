namespace Fm27Editor.Core.Players;

/// <summary>Where a ledger row stands. A row is recorded before anything is applied and moved on as it is confirmed.</summary>
public enum RegenLedgerState
{
    /// <summary>The row was recorded; nothing has been shown to be applied yet.</summary>
    Pending,
    /// <summary>The promised value was written and read back, or the readback showed it was already there.</summary>
    Applied,
    /// <summary>Somebody checked the applied value afterwards.</summary>
    Verified,
    /// <summary>The old value was put back at the promised one. The row stays in the ledger, so nothing re-rolls it.</summary>
    Reverted,
    /// <summary>What is in the game no longer matches what the row promised. Nothing is overwritten.</summary>
    Conflict,
}

/// <summary>
/// One once-only row: the intent to touch one player, once, on one save branch under one feature. Ids are text for
/// the same reason they are text everywhere else here — FM27's ids have not been shown to fit in 32 bits.
/// </summary>
public sealed record RegenLedgerEntry(
    string SaveId,
    string BranchId,
    string PersonUid,
    string Feature,
    string EventId,
    RegenStatus Decision,
    RegenLedgerState State,
    int? PreValue,
    int? ExpectedPostValue,
    string? ConflictReason,
    DateTimeOffset RecordedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? VerifiedAt);

/// <summary>Result of an undo: whether the old value went back, and what it was.</summary>
public sealed record RegenRevertResult(bool Restored, int? RestoredValue, RegenLedgerEntry Entry, string Detail);

/// <summary>What a readback says about a row that was left pending when the tool stopped.</summary>
public enum RegenReconcileAction
{
    /// <summary>The game still holds the original value, so nothing has been written yet.</summary>
    NothingWritten,
    /// <summary>The promised value is already in the game, so it must not be written a second time.</summary>
    AlreadyApplied,
    /// <summary>The value matches neither the original nor the promise, or it could not be read at all.</summary>
    Conflict,
}

public sealed record RegenReconcileResult(RegenReconcileAction Action, RegenLedgerEntry Entry, string Detail);

/// <summary>
/// The once-only ledger, keyed by (save, branch, person, feature). A row, once recorded, is never dropped or
/// replaced by a fresh one: a second scan of the same state finds the row that is already there, a reverted row
/// still blocks a re-roll, and a conflict stays visible instead of being quietly retried.
/// </summary>
public sealed class RegenLedger
{
    readonly List<RegenLedgerEntry> _order = [];
    readonly Dictionary<(string Save, string Branch, string Person, string Feature), int> _index = new();

    public RegenLedger(IEnumerable<RegenLedgerEntry>? entries = null)
    {
        foreach (var entry in entries ?? [])
        {
            var key = Key(entry.SaveId, entry.BranchId, entry.PersonUid, entry.Feature);
            if (_index.ContainsKey(key))
                throw new InvalidDataException($"The ledger holds {entry.PersonUid} twice on {entry.BranchId} under {entry.Feature}");
            _index[key] = _order.Count;
            _order.Add(entry);
        }
    }

    public IReadOnlyList<RegenLedgerEntry> Entries => _order;

    static (string Save, string Branch, string Person, string Feature) Key(string saveId, string branchId, string personUid, string feature) =>
        (saveId, branchId, personUid, feature);

    public RegenLedgerEntry? Find(string saveId, string branchId, string personUid, string feature) =>
        _index.TryGetValue(Key(saveId, branchId, personUid, feature), out int at) ? _order[at] : null;

    public bool Contains(string saveId, string branchId, string personUid, string feature) =>
        Find(saveId, branchId, personUid, feature) is not null;

    /// <summary>
    /// Records the intent to touch one player. If a row for that key is already there it comes back as it stands:
    /// this call is the lock, so a second run cannot roll the same player again on the same branch.
    /// </summary>
    public RegenLedgerEntry Record(
        string saveId, string branchId, string personUid, string feature, string eventId, RegenStatus decision,
        int? preValue = null, int? expectedPostValue = null, DateTimeOffset? recordedAt = null)
    {
        if (string.IsNullOrWhiteSpace(saveId)) throw new ArgumentException("A save id is required", nameof(saveId));
        if (string.IsNullOrWhiteSpace(branchId)) throw new ArgumentException("A timeline id is required", nameof(branchId));
        if (string.IsNullOrWhiteSpace(personUid)) throw new ArgumentException("A person id is required", nameof(personUid));
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("A feature name is required", nameof(feature));

        var key = Key(saveId, branchId, personUid, feature);
        if (_index.TryGetValue(key, out int at)) return _order[at];
        var entry = new RegenLedgerEntry(saveId, branchId, personUid, feature, eventId, decision,
            RegenLedgerState.Pending, preValue, expectedPostValue, null, recordedAt ?? DateTimeOffset.UtcNow, null, null);
        _index[key] = _order.Count;
        _order.Add(entry);
        return entry;
    }

    /// <summary>Marks the row applied. Both promised values have to be on the row, or there is nothing to check against later.</summary>
    public RegenLedgerEntry MarkApplied(string saveId, string branchId, string personUid, string feature, DateTimeOffset? at = null)
    {
        var entry = Require(saveId, branchId, personUid, feature);
        if (entry.State is RegenLedgerState.Applied or RegenLedgerState.Verified) return entry;
        if (entry.State is RegenLedgerState.Reverted or RegenLedgerState.Conflict)
            throw new InvalidOperationException($"{entry.PersonUid} is {entry.State}, so it cannot be marked applied");
        if (entry.PreValue is null || entry.ExpectedPostValue is null)
            throw new InvalidOperationException($"{entry.PersonUid} was recorded without the values it promised, so it cannot be marked applied");
        return Replace(entry, entry with { State = RegenLedgerState.Applied, AppliedAt = at ?? DateTimeOffset.UtcNow });
    }

    /// <summary>Marks a checked applied row as verified. Checking is what makes it verified; nothing here checks on its own.</summary>
    public RegenLedgerEntry MarkVerified(string saveId, string branchId, string personUid, string feature, DateTimeOffset? at = null)
    {
        var entry = Require(saveId, branchId, personUid, feature);
        if (entry.State is RegenLedgerState.Verified) return entry;
        if (entry.State is not RegenLedgerState.Applied)
            throw new InvalidOperationException($"{entry.PersonUid} is {entry.State}, so only an applied row can be verified");
        return Replace(entry, entry with { State = RegenLedgerState.Verified, VerifiedAt = at ?? DateTimeOffset.UtcNow });
    }

    /// <summary>
    /// Puts the old value back, but only while the game still holds exactly what the row promised. Anything else —
    /// another process, the game itself, or a value that was never promised — becomes a conflict and overwrites
    /// nothing. A reverted row stays in the ledger, so nothing re-rolls the player afterwards.
    /// </summary>
    public RegenRevertResult TryRevert(string saveId, string branchId, string personUid, string feature, int currentValue, DateTimeOffset? at = null)
    {
        var entry = Require(saveId, branchId, personUid, feature);
        switch (entry.State)
        {
            case RegenLedgerState.Reverted:
                return new(false, null, entry, "the row is already reverted, so nothing is written again");
            case RegenLedgerState.Pending:
                return new(false, null, entry, "nothing was applied to this row, so there is nothing to put back");
            case RegenLedgerState.Conflict:
                return new(false, null, entry, entry.ConflictReason ?? "the row is already in conflict");
        }
        if (entry.ExpectedPostValue is not { } expected || entry.PreValue is not { } original)
        {
            string reason = "the row never recorded the value it promised, so the current one cannot be checked";
            return new(false, null, Replace(entry, entry with { State = RegenLedgerState.Conflict, ConflictReason = reason }), reason);
        }
        if (currentValue != expected)
        {
            string reason = $"the game holds {currentValue}, not the {expected} this row promised, so something else has changed it";
            return new(false, null, Replace(entry, entry with { State = RegenLedgerState.Conflict, ConflictReason = reason }), reason);
        }
        var reverted = Replace(entry, entry with { State = RegenLedgerState.Reverted, ConflictReason = null });
        return new(true, original, reverted,
            $"the game still held the promised {expected}, so {original} was put back and this player will not be rolled again on this branch");
    }

    /// <summary>
    /// Reads the ledger back after a stop in the middle. A row whose promised value is already in the game is
    /// marked applied and must not be written again; a row the game never received stays pending; anything else is
    /// a conflict. Nothing is ever applied twice on the strength of this call.
    /// </summary>
    public RegenReconcileResult Reconcile(string saveId, string branchId, string personUid, string feature, int? readback)
    {
        var entry = Require(saveId, branchId, personUid, feature);
        if (entry.State is RegenLedgerState.Applied or RegenLedgerState.Verified)
            return new(RegenReconcileAction.AlreadyApplied, entry, "the row is already marked applied, so nothing is written again");
        if (entry.State is RegenLedgerState.Reverted or RegenLedgerState.Conflict)
            return new(RegenReconcileAction.Conflict, entry, $"the row is {entry.State}, so it needs a look before anything else happens");
        if (entry.ExpectedPostValue is null || entry.PreValue is null)
        {
            string missing = "the row never recorded the values it promised, so nothing can be confirmed";
            return new(RegenReconcileAction.Conflict, Replace(entry, entry with { State = RegenLedgerState.Conflict, ConflictReason = missing }), missing);
        }
        if (readback is null)
        {
            string unreadable = "the current value could not be read back, so nothing can be confirmed";
            return new(RegenReconcileAction.Conflict, Replace(entry, entry with { State = RegenLedgerState.Conflict, ConflictReason = unreadable }), unreadable);
        }
        if (readback == entry.ExpectedPostValue)
        {
            var applied = Replace(entry, entry with { State = RegenLedgerState.Applied, AppliedAt = entry.AppliedAt ?? DateTimeOffset.UtcNow });
            return new(RegenReconcileAction.AlreadyApplied, applied,
                $"the game already holds the promised {readback}, so the row is marked applied and nothing is written again");
        }
        if (readback == entry.PreValue)
            return new(RegenReconcileAction.NothingWritten, entry, $"the game still holds the original {readback}, so nothing has been written yet");

        string conflict = $"the game holds {readback}, neither the original {entry.PreValue} nor the promised {entry.ExpectedPostValue}";
        return new(RegenReconcileAction.Conflict, Replace(entry, entry with { State = RegenLedgerState.Conflict, ConflictReason = conflict }), conflict);
    }

    RegenLedgerEntry Require(string saveId, string branchId, string personUid, string feature) =>
        Find(saveId, branchId, personUid, feature)
        ?? throw new InvalidOperationException($"{personUid} on {branchId} under {feature} is not in the ledger");

    RegenLedgerEntry Replace(RegenLedgerEntry entry, RegenLedgerEntry updated)
    {
        _order[_index[Key(entry.SaveId, entry.BranchId, entry.PersonUid, entry.Feature)]] = updated;
        return updated;
    }
}
