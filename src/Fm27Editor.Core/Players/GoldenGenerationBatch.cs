using Fm27Editor.Core.History;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Settings;

namespace Fm27Editor.Core.Players;

/// <summary>Why one player was left alone, or could not be written.</summary>
public enum GoldenGenerationSkipReason
{
    None = 0,
    /// <summary>The filter ruled this player out.</summary>
    FilterMismatch,
    /// <summary>The id was already in the earlier snapshot, so this is not a new generation.</summary>
    NotANewGeneration,
    /// <summary>This feature has already rolled the player once.</summary>
    AlreadyRolled,
    TooOld,
    BornTooEarly,
    NotOnYouthContract,
    /// <summary>Data the decision needs is not readable on this build or for this player: reported, never guessed.</summary>
    PendingReview,
    /// <summary>The guarded write was refused because the game no longer holds the planned values.</summary>
    WriteFailed,
}

/// <summary>One row of the preview: what the player has, what it would get, and why it would not be touched.</summary>
public sealed record GoldenGenerationEntry(
    PlayerRecord Player, int PaBefore, int CaBefore, int PaAfter, int CaAfter,
    GoldenGenerationSkipReason Reason, string? Detail)
{
    public bool WillWrite => Reason == GoldenGenerationSkipReason.None;
    public bool IsPendingReview => Reason == GoldenGenerationSkipReason.PendingReview;
    public bool ChangesAbility => PaAfter != PaBefore || CaAfter != CaBefore;
}

/// <summary>What one Golden Generation run should do.</summary>
public sealed record GoldenGenerationRequest
{
    public required GoldenGenerationPotential Potential { get; init; }

    /// <summary>One roll in 64 ignores the window and gives its top value.</summary>
    public bool MaxPotentialChance { get; init; }

    public WonderkidChance Wonderkid { get; init; } = WonderkidChance.Off;

    /// <summary>Null means every player the other filters let through.</summary>
    public GoldenGenerationFilter? Filter { get; init; }

    public GoldenGenerationRegenOptions Regens { get; init; } = new();
}

public readonly record struct GoldenGenerationProgress(int Done, int Total, string Phase)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);
}

/// <summary>How one apply went: what was written, what was held back, and the history group the writes landed in.</summary>
public sealed record GoldenGenerationBatchResult(
    int Written, int Skipped, int PendingReview, IReadOnlyList<GoldenGenerationEntry> Failed, ChangeGroup? Group);

/// <summary>
/// The regen-focused Golden Generation: pick the new generations out of two snapshots, hold back the ones the filter,
/// the core filters or the ledger rule out, roll a potential and a wonderkid lift for the rest, then write just those
/// two abilities through the guarded player writer, all in one history group so one Undo puts the whole run back.
/// </summary>
public sealed class GoldenGenerationBatch(PlayerWriter writer, OffsetProfile profile, ChangeJournal? journal = null)
{
    const string Kind = "Player", Entity = "Golden Generation", Title = "Golden Generation";

    /// <summary>
    /// The preview. <paramref name="baselineIds"/> are the ids of the earlier snapshot: a player already in it is not a
    /// new generation. Every row comes back, written or not, with the reason it was held back.
    /// </summary>
    public IReadOnlyList<GoldenGenerationEntry> Plan(
        IReadOnlyList<PlayerRecord> current,
        GoldenGenerationRequest request,
        IReadOnlyCollection<int>? baselineIds = null,
        GoldenGenerationLedger? ledger = null,
        IGoldenGenerationFacts? facts = null,
        IProgress<GoldenGenerationProgress>? progress = null,
        Random? random = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        var window = request.Potential;
        if (window.Min < 1 || window.Max > 200 || window.Min > window.Max)
            throw new ArgumentException($"Potential {window.Min}–{window.Max} must be inside 1–200 with its minimum first");
        if (!WonderkidRoll.IsOffered(request.Wonderkid))
            throw new ArgumentException($"{request.Wonderkid} is not one of the offered wonderkid chances", nameof(request));
        var rng = random ?? Random.Shared;
        var filter = request.Filter;
        var entries = new List<GoldenGenerationEntry>(current.Count);
        for (int i = 0; i < current.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(PlanOne(current[i], request, filter, baselineIds, ledger, facts, rng));
            progress?.Report(new GoldenGenerationProgress(i + 1, current.Count, "Selecting"));
        }
        return entries;
    }

    GoldenGenerationEntry PlanOne(PlayerRecord player, GoldenGenerationRequest request, GoldenGenerationFilter? filter,
        IReadOnlyCollection<int>? baselineIds, GoldenGenerationLedger? ledger, IGoldenGenerationFacts? facts, Random rng)
    {
        // Without an id nothing below can be keyed, so the row is reported rather than guessed at.
        if (profile.Fields.Id is null) return Held(player, GoldenGenerationSkipReason.PendingReview, "This build's profile has no player id offset");
        if (player.Id is not > 0) return Held(player, GoldenGenerationSkipReason.PendingReview, "The player has no readable id");

        var match = filter?.Evaluate(player, facts) ?? GoldenGenerationMatch.Yes;
        if (match == GoldenGenerationMatch.No) return Held(player, GoldenGenerationSkipReason.FilterMismatch, "The filter does not match this player");
        if (baselineIds is not null && baselineIds.Contains(player.Id.Value))
            return Held(player, GoldenGenerationSkipReason.NotANewGeneration, "The id was already in the earlier snapshot");
        if (ledger?.Contains(player.Id.Value) == true)
            return Held(player, GoldenGenerationSkipReason.AlreadyRolled, "This run has already rolled the player");

        var regens = request.Regens;
        if (regens.MaxAge is { } maxAge)
        {
            if (player.Age is not { } age) return Held(player, GoldenGenerationSkipReason.PendingReview, "The game date is not readable, so the age is unknown");
            if (age > maxAge) return Held(player, GoldenGenerationSkipReason.TooOld, $"Age {age} is above the maximum {maxAge}");
        }
        if (regens.BornAfter is { } bornAfter)
        {
            if (player.DateOfBirth is not { } born) return Held(player, GoldenGenerationSkipReason.PendingReview, "The birth date is not readable");
            if (born < bornAfter) return Held(player, GoldenGenerationSkipReason.BornTooEarly, $"Born {DateSetting.Format(born)}, before {DateSetting.Format(bornAfter)}");
        }
        if (regens.RequireYouthContract is { } wantYouth)
        {
            if (facts is null) return Held(player, GoldenGenerationSkipReason.PendingReview, "No source of contract facts was given");
            if (facts.YouthContract(player) is not { } isYouth)
                return Held(player, GoldenGenerationSkipReason.PendingReview, "The contract type is not readable");
            if (isYouth != wantYouth)
                return Held(player, GoldenGenerationSkipReason.NotOnYouthContract, isYouth ? "The player is on a youth contract" : "The player is not on a youth contract");
        }
        if (match == GoldenGenerationMatch.Unknown)
            return Held(player, GoldenGenerationSkipReason.PendingReview, "The filter needs data this player does not expose");

        int paBefore = player.PotentialAbility, caBefore = player.CurrentAbility;
        int pa = GoldenGenerationRoll.Potential(request.Potential, request.MaxPotentialChance, rng);
        // A potential below the current ability is not a valid player state. The old one-player roll lowers CA to fit;
        // the batch must not lower CA behind the user's back, so report the row instead.
        if (pa < caBefore)
            return Held(player, GoldenGenerationSkipReason.PendingReview,
                $"Rolled potential {pa} is below current ability {caBefore}; the window does not fit this player");
        return new(player, paBefore, caBefore, pa, WonderkidRoll.CurrentAfter(pa, caBefore, request.Wonderkid, rng), GoldenGenerationSkipReason.None, null);
    }

    static GoldenGenerationEntry Held(PlayerRecord player, GoldenGenerationSkipReason reason, string detail) =>
        new(player, player.PotentialAbility, player.CurrentAbility, player.PotentialAbility, player.CurrentAbility, reason, detail);

    /// <summary>
    /// Writes the planned rows. A player the game changed since the preview is reported and skipped; any other failure
    /// stops the batch there. The whole run is one history group, and the ledger only records what was really written.
    /// </summary>
    public GoldenGenerationBatchResult Apply(
        IReadOnlyList<GoldenGenerationEntry> plan,
        GoldenGenerationLedger? ledger = null,
        IProgress<GoldenGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var targets = plan.Where(entry => entry.WillWrite).ToList();
        foreach (var entry in targets)
            if (entry.PaAfter is < 1 or > 200 || entry.ChangesAbility && entry.CaAfter is < 1 or > 200)
                throw new ArgumentException($"{entry.Player.DisplayName}: the planned abilities must be inside 1–200");

        int skipped = plan.Count(entry => entry.Reason is not (GoldenGenerationSkipReason.None
            or GoldenGenerationSkipReason.PendingReview or GoldenGenerationSkipReason.WriteFailed));
        int pending = plan.Count(entry => entry.IsPendingReview);
        var failed = new List<GoldenGenerationEntry>();
        int written = 0, attempted = 0;
        var before = Snapshot();
        IDisposable? scope = journal?.Begin(Kind, Entity, Title, Labels(plan));
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = targets[i];
                bool changes = entry.ChangesAbility;
                if (!changes)
                {
                    skipped++;
                    progress?.Report(new GoldenGenerationProgress(i + 1, targets.Count, "Writing"));
                    continue;
                }
                attempted++;
                try
                {
                    // PA first: raising CA before the new PA is in place can transiently leave CA above PA.
                    if (entry.PaAfter != entry.PaBefore) writer.SetPotentialAbility(entry.Player, entry.PaAfter);
                    if (entry.CaAfter != entry.CaBefore) writer.SetCurrentAbility(entry.Player, entry.CaAfter);
                }
                catch (StaleObjectException ex)
                {
                    failed.Add(entry with { Reason = GoldenGenerationSkipReason.WriteFailed, Detail = ex.Message });
                    progress?.Report(new GoldenGenerationProgress(i + 1, targets.Count, "Writing"));
                    continue;
                }
                if (entry.Player.Id is { } id) ledger?.Add(id);
                written++;
                progress?.Report(new GoldenGenerationProgress(i + 1, targets.Count, "Writing"));
            }
        }
        finally { scope?.Dispose(); }
        return new(written, skipped, pending, failed, Group(before, attempted));
    }

    /// <summary>How much history there is before the run, so the group it lands in can be recognised afterwards.</summary>
    (int Groups, int LastEntries) Snapshot() => journal is null ? (0, 0)
        : (journal.Groups.Count, journal.Groups.Count > 0 ? journal.Groups[^1].Entries.Count : 0);

    /// <summary>The group the run's writes landed in: a new one, or the older one it joined.</summary>
    ChangeGroup? Group((int Groups, int LastEntries) before, int attempted)
    {
        if (journal is null || attempted == 0) return null;
        var groups = journal.Groups;
        if (groups.Count > before.Groups) return groups[^1];
        if (groups.Count == before.Groups && groups.Count > 0 && groups[^1].Entries.Count > before.LastEntries) return groups[^1];
        return null;
    }

    /// <summary>Names the two abilities in the history, from the profile's own offsets rather than a fixed address.</summary>
    Func<ulong, int, ChangeLabel?>? Labels(IReadOnlyList<GoldenGenerationEntry> plan)
    {
        if (journal is null) return null;
        var byAddress = new Dictionary<ulong, ChangeLabel>();
        foreach (var entry in plan)
        {
            if (entry.CaAfter != entry.CaBefore) byAddress[entry.Player.Address + (ulong)profile.Fields.CurrentAbility] = new("Current Ability");
            if (entry.PaAfter != entry.PaBefore) byAddress[entry.Player.Address + (ulong)profile.Fields.PotentialAbility] = new("Potential Ability");
        }
        return (address, _) => byAddress.GetValueOrDefault(address);
    }
}
