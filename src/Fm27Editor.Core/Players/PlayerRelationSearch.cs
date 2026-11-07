namespace Fm27Editor.Core.Players;

/// <summary>All targets are addresses from the current game session, never persistent IDs.</summary>
public sealed record PlayerRelationCriteria(ulong? TrainedAtClub = null, ulong? TrainedAtNation = null,
    RelationshipKind? Relationship = null, ulong? RelationshipTarget = null, int? MinimumValue = null, int? MaximumValue = null);
public sealed record PlayerRelationSearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

public sealed class PlayerRelationSearch(PlayerStatusReader reader)
{
    public PlayerRelationSearchResult Find(IEnumerable<PlayerRecord> players, PlayerRelationCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.TrainedAtClub == 0 || criteria.TrainedAtNation == 0 || criteria.RelationshipTarget == 0 || criteria.MinimumValue > criteria.MaximumValue)
            throw new ArgumentException("Invalid relation criterion", nameof(criteria));
        if (criteria.Relationship is not null and not (RelationshipKind.LikesPerson or RelationshipKind.DislikesPerson or RelationshipKind.LikesTeam))
            throw new ArgumentException("Unsupported relationship", nameof(criteria));
        if (criteria.Relationship is null && (criteria.RelationshipTarget is not null || criteria.MinimumValue is not null || criteria.MaximumValue is not null))
            throw new ArgumentException("Relationship kind is required", nameof(criteria));
        var matches = new List<PlayerRecord>();
        var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = reader.Read(player);
            bool trainingMatch = (criteria.TrainedAtClub is not { } club || state.TrainedAtClubs.Any(x => x.Club?.Address == club))
                && (criteria.TrainedAtNation is not { } nation || state.TrainedAtNations.Any(x => x.Nation?.Address == nation));
            bool relationMatch = criteria.Relationship is not { } kind || state.Relationships.Any(x => x.Kind == kind
                && (criteria.RelationshipTarget is null || x.TargetAddress == criteria.RelationshipTarget)
                && (criteria.MinimumValue is null || x.Value >= criteria.MinimumValue)
                && (criteria.MaximumValue is null || x.Value <= criteria.MaximumValue));
            if ((!trainingMatch && state.TrainingComplete) || (!relationMatch && state.RelationshipsComplete)) continue;
            if (!trainingMatch || !relationMatch) { unavailable.Add(player); continue; }
            matches.Add(player);
        }
        return new(matches, unavailable);
    }
}
