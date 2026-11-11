namespace Fm27Editor.Core.Players;

/// <summary>
/// The post-intake diff: two scans of the same save and branch, the ids that appeared between them, and what can be
/// said about each of them. A player that was already in the baseline is never a new generation, whatever its id or
/// age looks like, and a scan that cannot be trusted produces nothing at all.
/// </summary>
public static class RegenIntakeDiff
{
    /// <summary>
    /// Compare the scan taken before the intake with the one taken after it. <paramref name="baseline"/> is null
    /// when the tool first ran with no earlier scan kept: that is reported as an unknown baseline, not as a field
    /// of new players.
    /// </summary>
    public static RegenDiffResult Compare(
        RegenSnapshot? baseline,
        RegenSnapshot current,
        RegenIntakeOptions options,
        RegenIntakeEvent? intakeEvent = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        intakeEvent?.Validate();

        var problems = Check(current, "the after scan");
        if (problems.Count > 0) return Quarantined(problems);

        if (baseline is null)
            return new(RegenScanVerdict.UnknownBaseline,
                "No scan was kept from before the intake, so nothing can be called new. Capture a baseline before the next " +
                "intake; until then every row is for manual review only.", []);

        problems = Check(baseline, "the baseline scan");
        if (problems.Count > 0) return Quarantined(problems);
        if (!string.Equals(baseline.SaveId, current.SaveId, StringComparison.Ordinal))
            return Quarantined([$"the baseline belongs to save {baseline.SaveId} and the after scan to {current.SaveId}"]);
        if (!string.Equals(baseline.BranchId, current.BranchId, StringComparison.Ordinal))
            return Quarantined([$"the baseline belongs to timeline {baseline.BranchId} and the after scan to {current.BranchId}: " +
                "the save was reloaded or rewound, so the two scans are not the same history"]);
        if (current.GameDate < baseline.GameDate)
            return Quarantined([$"the after scan is dated {current.GameDate.ToString("O")}, before the baseline's " +
                $"{baseline.GameDate.ToString("O")}: time ran backwards"]);

        var before = baseline.Players.ToDictionary(p => p.PersonUid, StringComparer.Ordinal);
        var after = current.Players.ToDictionary(p => p.PersonUid, StringComparer.Ordinal);

        var candidates = new List<RegenCandidate>();
        foreach (var player in current.Players)
            if (!before.ContainsKey(player.PersonUid))
                candidates.Add(Classify(player, current.GameDate, options, intakeEvent));

        // A club scope is what separates a player who arrived at the club from a player who was born into the world.
        if (options.ClubUid is { } club)
        {
            var atClubBefore = baseline.Players.Where(p => p.ClubUid == club).Select(p => p.PersonUid).ToHashSet(StringComparer.Ordinal);
            foreach (var player in current.Players)
                if (player.ClubUid == club && !atClubBefore.Contains(player.PersonUid) && before.ContainsKey(player.PersonUid))
                    candidates.Add(new RegenCandidate(player.PersonUid, RegenStatus.NewToClub,
                        $"already in the database before this scan, so the move to {club} is a transfer, a loan or a " +
                        "promotion rather than a new player", player));
        }

        return new(RegenScanVerdict.Classified, candidates.Count == 0
            ? "Every player in the after scan was already in the baseline."
            : $"{candidates.Count} row(s) need a decision.", candidates);
    }

    /// <summary>
    /// What one newly appeared player can be called. Data that is missing or unverified holds the row for review
    /// rather than being read as a negative answer, so nothing slips through as a silent miss.
    /// </summary>
    static RegenCandidate Classify(RegenObservation player, DateOnly gameDate, RegenIntakeOptions options, RegenIntakeEvent? intakeEvent)
    {
        if (player.IsPlayer is false) return Held(player, "the record is not a player");
        if (player.IsPlayer is null) return Held(player, "the object type was not verified, so it is not known to be a player");
        if (player.DateOfBirth is not { } born) return Held(player, "the birth date is not readable");
        if (string.IsNullOrWhiteSpace(player.ClubUid) || string.IsNullOrWhiteSpace(player.TeamUid))
            return Held(player, "club or team membership is not readable");
        if (player.TransferEvidence is not null)
            return Held(player, $"recorded as {player.TransferEvidence}, which is a move rather than a birth");

        int? boundBelow = options.MinAge, boundAbove = options.MaxAge;
        if (boundBelow is not null || boundAbove is not null)
        {
            int age = RegenIntakeOptions.AgeAt(born, gameDate);
            if (boundBelow is { } lo && age < lo) return Held(player, $"age {age} on {gameDate.ToString("O")} is below the configured minimum {lo}");
            if (boundAbove is { } hi && age > hi) return Held(player, $"age {age} on {gameDate.ToString("O")} is above the configured maximum {hi}");
        }

        if (intakeEvent is null) return Held(player, "no youth intake was observed for this scan");
        if (!intakeEvent.Verified) return Held(player, $"intake {intakeEvent.EventId} ({intakeEvent.Source}) has not been confirmed against the game");
        if (!string.Equals(intakeEvent.ClubUid, player.ClubUid, StringComparison.Ordinal))
            return Held(player, $"intake {intakeEvent.EventId} belongs to club {intakeEvent.ClubUid}, not to {player.ClubUid}");
        if (DaysBetween(intakeEvent.ObservedDate, gameDate) > options.IntakeWindowDays)
            return Held(player, $"intake {intakeEvent.EventId} was observed on {intakeEvent.ObservedDate.ToString("O")}, outside the " +
                $"{options.IntakeWindowDays}-day window around {gameDate.ToString("O")}");

        return new(player.PersonUid, RegenStatus.LikelyIntake,
            $"new to the database and at club {player.ClubUid} when intake {intakeEvent.EventId} was observed on " +
            $"{intakeEvent.ObservedDate.ToString("O")}", player);
    }

    static RegenCandidate Held(RegenObservation player, string detail) =>
        new(player.PersonUid, RegenStatus.PendingReview, detail, player);

    /// <summary>Whole days between two dates, whichever way round they fall.</summary>
    static int DaysBetween(DateOnly first, DateOnly second) =>
        Math.Abs((first.ToDateTime(TimeOnly.MinValue) - second.ToDateTime(TimeOnly.MinValue)).Days);

    /// <summary>
    /// Whether a scan may be compared at all. An incomplete scan cannot tell a player who was never there from a
    /// player the scanner missed, a scan of one club cannot see the rest of the database, and the same id twice
    /// means the scan does not describe one coherent world — each of those is refused rather than worked around.
    /// </summary>
    static List<string> Check(RegenSnapshot snapshot, string label)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(snapshot.SaveId)) problems.Add($"{label} has no save id");
        if (string.IsNullOrWhiteSpace(snapshot.BranchId)) problems.Add($"{label} has no timeline id");
        if (!snapshot.Complete) problems.Add($"{label} is marked incomplete, so a player it does not list may still exist");
        if (!string.Equals(snapshot.Scope, RegenScope.Global, StringComparison.Ordinal))
            problems.Add($"{label} covers {snapshot.Scope} rather than the whole database, so a player outside it may still exist");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in snapshot.Players ?? [])
        {
            if (string.IsNullOrWhiteSpace(player.PersonUid)) { problems.Add($"{label} has a record with no id"); continue; }
            if (!seen.Add(player.PersonUid))
                problems.Add($"{label} lists id {player.PersonUid} twice: the collision has to be resolved before the scan means anything");
        }
        return problems;
    }

    static RegenDiffResult Quarantined(IReadOnlyList<string> problems) =>
        new(RegenScanVerdict.Quarantined,
            "Refused, nothing was classified: " + string.Join("; ", problems) + ".", []);
}
