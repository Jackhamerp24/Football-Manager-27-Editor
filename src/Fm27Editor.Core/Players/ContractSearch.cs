using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Players;

/// <summary>Contract filters use only values actually read; unavailable data is reported separately.</summary>
public sealed record ContractSearchCriteria(ContractType? Type = null, PlayingTime? PlayingTime = null,
    long? MinimumWage = null, long? MaximumWage = null, long? MinimumAskingPrice = null, long? MaximumAskingPrice = null,
    short? ClauseType = null, bool? HasClause = null, int? MinimumClauseValue = null, int? MaximumClauseValue = null)
{
    public IReadOnlyList<ContractClauseCriterion> Clauses { get; init; } = [];
}
public sealed record ContractClauseCriterion(short Type, bool HasClause = true, int? MinimumValue = null, int? MaximumValue = null);
public sealed record ContractSearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

public sealed class ContractSearch(ContractWriter contracts)
{
    public ContractSearchResult Find(IEnumerable<PlayerRecord> players, ContractSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.MinimumWage > criteria.MaximumWage || criteria.MinimumAskingPrice > criteria.MaximumAskingPrice || criteria.MinimumClauseValue > criteria.MaximumClauseValue)
            throw new ArgumentException("Minimum cannot exceed maximum", nameof(criteria));
        if ((criteria.HasClause is not null || criteria.MinimumClauseValue is not null || criteria.MaximumClauseValue is not null) && criteria.ClauseType is null)
            throw new ArgumentException("A clause type is required", nameof(criteria));
        if (criteria.HasClause == false && (criteria.MinimumClauseValue is not null || criteria.MaximumClauseValue is not null))
            throw new ArgumentException("A missing clause has no value", nameof(criteria));
        var clauseCriteria = criteria.Clauses.ToList();
        if (criteria.ClauseType is { } singleType) clauseCriteria.Add(new(singleType, criteria.HasClause ?? true, criteria.MinimumClauseValue, criteria.MaximumClauseValue));
        if (clauseCriteria.Any(c => c.MinimumValue > c.MaximumValue || !c.HasClause && (c.MinimumValue is not null || c.MaximumValue is not null)))
            throw new ArgumentException("Invalid clause range or presence", nameof(criteria));
        var matches = new List<PlayerRecord>();
        var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool needsPrice = criteria.MinimumAskingPrice is not null || criteria.MaximumAskingPrice is not null;
            if (needsPrice && player.DisplayAskingPrice is null) { unavailable.Add(player); continue; }
            if (needsPrice && (player.DisplayAskingPrice < criteria.MinimumAskingPrice || player.DisplayAskingPrice > criteria.MaximumAskingPrice)) continue;
            bool needsContract = criteria.Type is not null || criteria.PlayingTime is not null || criteria.MinimumWage is not null || criteria.MaximumWage is not null || clauseCriteria.Count > 0;
            if (!needsContract) { matches.Add(player); continue; }
            if (player.Contract is null) { unavailable.Add(player); continue; }
            try
            {
                var details = contracts.Read(player);
                if (details is null || (criteria.Type is not null && details.Type is null) || (criteria.PlayingTime is not null && details.PlayingTime is null))
                {
                    unavailable.Add(player);
                    continue;
                }
                if (criteria.Type is { } type && details.Type != type) continue;
                if (criteria.PlayingTime is { } playing && details.PlayingTime != playing) continue;
                if (details.Wage < criteria.MinimumWage || details.Wage > criteria.MaximumWage) continue;
                if (clauseCriteria.Count > 0)
                {
                    var terms = contracts.ReadTermsForSearch(player);
                    if (terms is null) { unavailable.Add(player); continue; }
                    bool clausesMatch = clauseCriteria.All(criterion => criterion.HasClause
                        ? terms.Any(term => term.Type == criterion.Type && term.Value >= (criterion.MinimumValue ?? int.MinValue) && term.Value <= (criterion.MaximumValue ?? int.MaxValue))
                        : !terms.Any(term => term.Type == criterion.Type));
                    if (!clausesMatch) continue;
                }
                matches.Add(player);
            }
            catch (Exception ex) when (ex is StaleObjectException or MemoryAccessException)
            {
                unavailable.Add(player);
            }
        }
        return new(matches, unavailable);
    }
}
