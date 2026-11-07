namespace Fm27Editor.Core.Players;

public readonly record struct PlayerSearchRange(long? Minimum = null, long? Maximum = null)
{
    public bool Contains(long value) => (Minimum is null || value >= Minimum) && (Maximum is null || value <= Maximum);
    public void Validate() { if (Minimum > Maximum) throw new ArgumentException("Minimum cannot exceed maximum"); }
}

public enum PlayerSnapshotField { Age, CurrentAbility, PotentialAbility, AbilityDifference, Height, HomeReputation, CurrentReputation, WorldReputation, Caps, Goals, AskingPrice, Wages, Ethnicity, TreatedAsNonEuInItaly }

public sealed record PlayerSnapshotCriteria
{
    public IReadOnlyDictionary<PlayerSnapshotField, PlayerSearchRange> Fields { get; init; } = new Dictionary<PlayerSnapshotField, PlayerSearchRange>();
    public IReadOnlyDictionary<PlayerPosition, PlayerSearchRange> Positions { get; init; } = new Dictionary<PlayerPosition, PlayerSearchRange>();
    public IReadOnlyDictionary<PlayerAttribute, PlayerSearchRange> Attributes { get; init; } = new Dictionary<PlayerAttribute, PlayerSearchRange>();
    public IReadOnlyDictionary<PersonalityAttribute, PlayerSearchRange> Personality { get; init; } = new Dictionary<PersonalityAttribute, PlayerSearchRange>();
    public bool AnyPosition { get; init; }
    public ulong RequiredPreferredMoves { get; init; }
    public bool? Wonderkid { get; init; }
}

public sealed record PlayerSnapshotSearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

public static class PlayerSnapshotSearch
{
    public static PlayerSnapshotSearchResult Find(IEnumerable<PlayerRecord> players, PlayerSnapshotCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players); ArgumentNullException.ThrowIfNull(criteria);
        foreach (var range in criteria.Fields.Values.Concat(criteria.Positions.Values).Concat(criteria.Attributes.Values).Concat(criteria.Personality.Values)) range.Validate();
        var matches = new List<PlayerRecord>(); var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool unknown = false, failed = false;
            foreach (var (field, range) in criteria.Fields)
            {
                long? value = field switch { PlayerSnapshotField.Age => player.Age, PlayerSnapshotField.CurrentAbility => player.CurrentAbility,
                    PlayerSnapshotField.PotentialAbility => player.PotentialAbility, PlayerSnapshotField.Height => player.Height,
                    PlayerSnapshotField.AbilityDifference => player.PotentialAbility - player.CurrentAbility,
                    PlayerSnapshotField.HomeReputation => player.HomeReputation, PlayerSnapshotField.CurrentReputation => player.CurrentReputation,
                    PlayerSnapshotField.WorldReputation => player.WorldReputation, PlayerSnapshotField.Caps => player.InternationalCaps,
                    PlayerSnapshotField.Goals => player.InternationalGoals, PlayerSnapshotField.AskingPrice => player.DisplayAskingPrice,
                    PlayerSnapshotField.Wages => player.Contract?.Wage, PlayerSnapshotField.Ethnicity => player.Ethnicity,
                    PlayerSnapshotField.TreatedAsNonEuInItaly => player.TreatedAsNonEuInItaly, _ => throw new ArgumentOutOfRangeException(nameof(criteria)) };
                if (value is null) unknown = true; else if (!range.Contains(value.Value)) { failed = true; break; }
            }
            if (failed) continue;
            if (criteria.Positions.Count > 0)
            {
                bool hit = criteria.AnyPosition ? false : true, missing = false;
                foreach (var (position, range) in criteria.Positions)
                {
                    int index = (int)position;
                    if (index < 0 || index >= player.Positions.Length) { missing = true; continue; }
                    bool current = range.Contains(player.Positions[index]);
                    if (criteria.AnyPosition) hit |= current; else hit &= current;
                }
                if (criteria.AnyPosition ? !hit && !missing : !hit) continue;
                if (missing && (criteria.AnyPosition ? !hit : true)) unknown = true;
            }
            foreach (var (attribute, range) in criteria.Attributes)
            {
                int index = (int)attribute;
                if (index < 0 || index >= player.RawAttributes.Length || player.AttributeScale <= 0) { unknown = true; continue; }
                if (!range.Contains(player.Attribute(attribute))) { failed = true; break; }
            }
            if (failed) continue;
            foreach (var (personality, range) in criteria.Personality)
            {
                int index = (int)personality;
                if (index < 0 || index >= player.Personality.Length) { unknown = true; continue; }
                if (!range.Contains(player.PersonalityValue(personality))) { failed = true; break; }
            }
            if (failed) continue;
            if (criteria.RequiredPreferredMoves != 0)
            {
                if (player.PreferredMoveBits is not { } bits) unknown = true;
                else if ((bits & criteria.RequiredPreferredMoves) != criteria.RequiredPreferredMoves) continue;
            }
            if (criteria.Wonderkid is { } wanted)
            {
                if (WonderkidSearch.IsWonderkid(player) is not { } isWonderkid) unknown = true;
                else if (wanted != isWonderkid) continue;
            }
            if (unknown) unavailable.Add(player); else matches.Add(player);
        }
        return new(matches, unavailable);
    }
}
