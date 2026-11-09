using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>One Advanced Search relationship condition: relation type code, optional target and Value range.</summary>
public sealed record RelationshipCriterion(byte Type, ulong Target, string TargetName, int MinValue, int MaxValue);

/// <summary>Matches players whose relation vector has a row of the requested type (and target, when given) with Value in range.</summary>
public static class RelationFilter
{
    public static IReadOnlyList<PlayerRecord> Find(IEnumerable<PlayerRecord> players, IReadOnlyList<RelationshipCriterion> criteria, PlayerStatusReader reader)
    {
        if (criteria.Count == 0) return players.ToArray();
        var result = new List<PlayerRecord>();
        foreach (var player in players)
        {
            var state = reader.Read(player);
            bool all = true;
            foreach (var c in criteria)
            {
                bool row = false;
                foreach (var entry in state.Entries)
                {
                    if (entry.Type != c.Type) continue;
                    if (c.Target != 0 && entry.Target != c.Target) continue;
                    if (entry.Value < c.MinValue || entry.Value > c.MaxValue) continue;
                    row = true;
                    break;
                }
                if (!row) { all = false; break; }
            }
            if (all) result.Add(player);
        }
        return result;
    }
}
