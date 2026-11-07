namespace Fm27Editor.Core.Players;

/// <summary>A stage partitions its candidates into matches, unavailable, and all remaining nonmatches.</summary>
public sealed record PlayerSearchStage(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

public static class PlayerSearchIntersection
{
    /// <summary>AND stages, retaining unknown only when no later condition proves the player does not match.</summary>
    public static PlayerSearchStage Find(IEnumerable<PlayerRecord> players,
        IEnumerable<Func<IReadOnlyList<PlayerRecord>, CancellationToken, PlayerSearchStage>> stages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players); ArgumentNullException.ThrowIfNull(stages);
        var candidates = players.ToList();
        if (candidates.Select(p => p.Address).Distinct().Count() != candidates.Count) throw new ArgumentException("Duplicate player addresses", nameof(players));
        var unknown = new HashSet<ulong>();
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = stage(candidates, cancellationToken) ?? throw new InvalidOperationException("Search stage returned null");
            var matched = result.Matches.Select(p => p.Address).ToHashSet();
            var unavailable = result.Unavailable.Select(p => p.Address).ToHashSet();
            var valid = candidates.Select(p => p.Address).ToHashSet();
            if (matched.Overlaps(unavailable) || !matched.IsSubsetOf(valid) || !unavailable.IsSubsetOf(valid))
                throw new InvalidOperationException("Search stage returned invalid players");
            candidates.RemoveAll(p => !matched.Contains(p.Address) && !unavailable.Contains(p.Address));
            unknown.UnionWith(unavailable);
        }
        return new(candidates.Where(p => !unknown.Contains(p.Address)).ToArray(), candidates.Where(p => unknown.Contains(p.Address)).ToArray());
    }
}
