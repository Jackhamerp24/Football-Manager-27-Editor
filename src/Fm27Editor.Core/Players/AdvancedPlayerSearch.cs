using System.Globalization;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Players;

public sealed record AdvancedPlayerCriteria
{
    public PlayerSnapshotCriteria? Snapshot { get; init; }
    public ContractSearchCriteria? Contract { get; init; }
    public NationRecord? Nationality { get; init; }
    public bool IncludeOtherNationalities { get; init; }
    public ContinentRecord? Continent { get; init; }
    public int? BirthCityId { get; init; }
    public PlayerRelationCriteria? Relations { get; init; }
    public string? NameQuery { get; init; }
    public ulong? ClubAddress { get; init; }
    public ulong? DivisionAddress { get; init; }
}

/// <summary>Coordinates search against snapshots and session-scoped object addresses.</summary>
public sealed class AdvancedPlayerSearch(CityRepository cities, ContractSearch contracts, PlayerNationalitySearch nationalities,
    PlayerContinentSearch continents, PlayerRelationSearch relations)
{
    public PlayerSearchStage Find(IEnumerable<PlayerRecord> players, AdvancedPlayerCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var stages = new List<Func<IReadOnlyList<PlayerRecord>, CancellationToken, PlayerSearchStage>>();
        if (!string.IsNullOrWhiteSpace(criteria.NameQuery) || criteria.ClubAddress is not null || criteria.DivisionAddress is not null)
            stages.Add((rows, ct) => FilterIdentity(rows, criteria, ct));
        if (criteria.Snapshot is { } snapshot) stages.Add((rows, ct) => { var r = PlayerSnapshotSearch.Find(rows, snapshot, ct); return new(r.Matches, r.Unavailable); });
        if (criteria.Contract is { } contract) stages.Add((rows, ct) => { var r = contracts.Find(rows, contract, ct); return new(r.Matches, r.Unavailable); });
        if (criteria.Nationality is { } nation) stages.Add((rows, ct) => { var r = nationalities.Find(rows, nation, criteria.IncludeOtherNationalities, ct); return new(r.Matches, r.Unavailable); });
        if (criteria.Continent is { } continent) stages.Add((rows, ct) => { var r = continents.Find(rows, continent, ct); return new(r.Matches, r.Unavailable); });
        if (criteria.BirthCityId is { } city) stages.Add((rows, ct) => { var r = cities.FindPlayersBornInCityDetailed(rows, city, ct); return new(r.Matches, r.Unavailable); });
        if (criteria.Relations is { } relation) stages.Add((rows, ct) => { var r = relations.Find(rows, relation, ct); return new(r.Matches, r.Unavailable); });
        return PlayerSearchIntersection.Find(players, stages, cancellationToken);
    }

    static PlayerSearchStage FilterIdentity(IReadOnlyList<PlayerRecord> players, AdvancedPlayerCriteria criteria, CancellationToken ct)
    {
        var matches = new List<PlayerRecord>(); var unavailable = new List<PlayerRecord>();
        string query = criteria.NameQuery?.Trim() ?? "";
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        foreach (var p in players)
        {
            ct.ThrowIfCancellationRequested();
            if (query.Length > 0 && compare.IndexOf(p.DisplayName, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0
                && compare.IndexOf(p.FullName ?? "", query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0
                && p.Id?.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal) != true) continue;
            if (criteria.ClubAddress is { } club && p.Club is { } c && c.Address != club) continue;
            if (criteria.DivisionAddress is { } division && p.Division is { } d && d.Address != division) continue;
            if ((criteria.ClubAddress is not null && p.Club is null) || (criteria.DivisionAddress is not null && p.Division is null)) unavailable.Add(p);
            else matches.Add(p);
        }
        return new(matches, unavailable);
    }
}
