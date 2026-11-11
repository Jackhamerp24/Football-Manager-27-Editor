using System.Globalization;

namespace Fm27Editor.Core.Players;

/// <summary>
/// What the regen-intake bridge would do, as a preview: the Golden Generation plan for the candidates the intake
/// diff allowed to act on on their own, the ones it allowed that could not be matched to a player, and how many
/// candidates were kept out. Nothing here writes to the game or to a save — the plan is what a later apply step
/// would be shown, and this feature never takes that step.
/// </summary>
public sealed record RegenGoldenGenerationPreview(
    IReadOnlyList<GoldenGenerationEntry> Entries,
    IReadOnlyList<RegenCandidate> Unmapped,
    int Eligible,
    int Excluded);

/// <summary>
/// The bridge from the post-intake diff to the detailed Golden Generation: only candidates the diff marked
/// <see cref="RegenCandidate.AutoEligible"/> (a likely intake, or a confirmed regen once FM27 itself ever produces
/// one) are mapped back to their player records and handed to <see cref="GoldenGenerationBatch.Plan"/>. A candidate
/// whose id matches no record is reported rather than dropped, and nothing is ever applied.
/// </summary>
public static class RegenGoldenGeneration
{
    public static RegenGoldenGenerationPreview Plan(
        RegenDiffResult diff,
        IReadOnlyList<PlayerRecord> players,
        GoldenGenerationBatch batch,
        GoldenGenerationRequest request,
        IGoldenGenerationFacts? facts = null,
        Random? random = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(request);

        // The ids the snapshot source wrote are the records' own ids as text; two records with one id could not be
        // told apart, so the pair is refused instead of one of them being picked by accident.
        var byUid = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        foreach (var player in players)
        {
            if (player.Id is null) continue;
            string uid = player.Id.Value.ToString(CultureInfo.InvariantCulture);
            if (!byUid.TryAdd(uid, player))
                throw new InvalidDataException($"Two player records carry the id {uid}, so neither can be told apart");
        }

        var eligible = diff.AutoEligible;
        var mapped = new List<PlayerRecord>(eligible.Count);
        var unmapped = new List<RegenCandidate>();
        foreach (var candidate in eligible)
        {
            if (byUid.TryGetValue(candidate.PersonUid, out var player)) mapped.Add(player);
            else unmapped.Add(candidate);
        }

        var entries = batch.Plan(mapped, request, facts: facts, random: random, cancellationToken: cancellationToken);
        return new(entries, unmapped, eligible.Count, diff.Candidates.Count - eligible.Count);
    }
}
