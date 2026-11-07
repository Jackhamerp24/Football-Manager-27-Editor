using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Players;

public sealed record PlayerNationalitySearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

/// <summary>Matches a nation object, optionally including verified other-nationality relations.</summary>
public sealed class PlayerNationalitySearch(PlayerStatusReader statuses)
{
    public PlayerNationalitySearchResult Find(IEnumerable<PlayerRecord> players, NationRecord nation, bool includeOther, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(nation);
        if (nation.Address == 0) throw new ArgumentException("Nation address is missing", nameof(nation));
        var matches = new List<PlayerRecord>();
        var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (player.Nationality?.Address == nation.Address) { matches.Add(player); continue; }
            if (!includeOther)
            {
                if (player.Nationality is null) unavailable.Add(player);
                continue;
            }
            var state = statuses.Read(player);
            if (state.Relationships.Any(r => r.Kind == RelationshipKind.OtherNationality && r.TargetAddress == nation.Address))
                matches.Add(player);
            else if (!state.NationalitiesComplete || player.Nationality is null)
                unavailable.Add(player);
        }
        return new(matches, unavailable);
    }
}
