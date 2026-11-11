using System.Collections.Immutable;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Players;

public enum GoldenGenerationFilterKind { Club, Competition, Nation, Continent, Uid }

/// <summary>How one filter ruled on a player: it matched, it did not, or the data to decide is not there.</summary>
public enum GoldenGenerationMatch { Yes, No, Unknown }

/// <summary>
/// One row of the filter. A club, a competition or a nation is named by address or by name, a nation also by its id,
/// a continent only by name, and a uid only by the player's own id.
/// </summary>
public sealed record GoldenGenerationFilterItem(GoldenGenerationFilterKind Kind, int? Id = null, ulong? Address = null, string? Name = null);

/// <summary>
/// The rows a Golden Generation run is restricted to. Each row stands on its own: the rows of one category are OR-ed,
/// the categories are AND-ed, and a uid is only a category of its own — it never overrides the club, competition,
/// nation or continent rows next to it.
/// </summary>
public sealed class GoldenGenerationFilter
{
    public ImmutableArray<GoldenGenerationFilterItem> Items { get; }

    /// <summary>An empty filter lets every player through.</summary>
    public static GoldenGenerationFilter None { get; } = new([]);

    public GoldenGenerationFilter(IEnumerable<GoldenGenerationFilterItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items.ToImmutableArray();
        foreach (var item in Items)
            if (!Usable(item)) throw new ArgumentException($"A {item.Kind} filter row needs " + Requirement(item.Kind));
    }

    static bool Usable(GoldenGenerationFilterItem item) => item.Kind switch
    {
        GoldenGenerationFilterKind.Uid => item.Id is > 0 && item.Address is null && item.Name is null,
        GoldenGenerationFilterKind.Continent => item.Name is not null && item.Id is null && item.Address is null,
        GoldenGenerationFilterKind.Club or GoldenGenerationFilterKind.Competition => item.Address is not null || item.Name is not null,
        GoldenGenerationFilterKind.Nation => item.Id is not null || item.Address is not null || item.Name is not null,
        _ => false,
    };

    static string Requirement(GoldenGenerationFilterKind kind) => kind switch
    {
        GoldenGenerationFilterKind.Uid => "a positive player id",
        GoldenGenerationFilterKind.Continent => "a name",
        GoldenGenerationFilterKind.Club or GoldenGenerationFilterKind.Competition => "an address or a name",
        _ => "an id, an address or a name",
    };

    /// <summary>
    /// Every category must say yes for the player to be planned. A category that cannot be decided because the player
    /// exposes no such data makes the whole result unknown rather than a no, so the run reports it instead of dropping
    /// the player silently.
    /// </summary>
    public GoldenGenerationMatch Evaluate(PlayerRecord player, IGoldenGenerationFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (Items.IsEmpty) return GoldenGenerationMatch.Yes;
        var decision = GoldenGenerationMatch.Yes;
        foreach (var category in Items.GroupBy(item => item.Kind))
        {
            var best = GoldenGenerationMatch.No;
            foreach (var item in category)
            {
                var one = Row(player, item, facts);
                if (one == GoldenGenerationMatch.Yes) { best = GoldenGenerationMatch.Yes; break; }
                if (one == GoldenGenerationMatch.Unknown) best = GoldenGenerationMatch.Unknown;
            }
            if (best == GoldenGenerationMatch.No) return GoldenGenerationMatch.No;
            if (best == GoldenGenerationMatch.Unknown) decision = GoldenGenerationMatch.Unknown;
        }
        return decision;
    }

    static GoldenGenerationMatch Row(PlayerRecord player, GoldenGenerationFilterItem item, IGoldenGenerationFacts? facts) =>
        item.Kind switch
        {
            GoldenGenerationFilterKind.Uid => player.Id is not { } id ? GoldenGenerationMatch.Unknown : Compare(id == item.Id),
            GoldenGenerationFilterKind.Club => player.Club is not { } club ? GoldenGenerationMatch.Unknown
                : Compare(Says(item, null, club.Address, club.DisplayName)),
            GoldenGenerationFilterKind.Competition => player.Division is not { } division ? GoldenGenerationMatch.Unknown
                : Compare(Says(item, null, division.Address, division.DisplayName)),
            GoldenGenerationFilterKind.Nation => player.Nationality is not { } nation ? GoldenGenerationMatch.Unknown
                : Compare(Says(item, nation.Id, nation.Address, nation.DisplayName)),
            GoldenGenerationFilterKind.Continent => facts?.Continent(player) is not { } continent ? GoldenGenerationMatch.Unknown
                : Compare(NameHits(item.Name, continent)),
            _ => GoldenGenerationMatch.Unknown,
        };

    /// <summary>Which identity the row carries decides what it is compared against: id, then address, then name.</summary>
    static bool? Says(GoldenGenerationFilterItem item, int? id, ulong address, string name)
    {
        if (item.Id is { } wanted) return id is null ? (bool?)null : id == wanted;
        if (item.Address is { } at) return address == 0 ? (bool?)null : address == at;
        return NameHits(item.Name, name);
    }

    static bool? NameHits(string? wanted, string actual) =>
        string.IsNullOrWhiteSpace(wanted) || string.IsNullOrWhiteSpace(actual) ? (bool?)null
        : string.Equals(wanted.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    static GoldenGenerationMatch Compare(bool? hit) =>
        hit is null ? GoldenGenerationMatch.Unknown : hit.Value ? GoldenGenerationMatch.Yes : GoldenGenerationMatch.No;
}

/// <summary>
/// The facts a player snapshot does not carry. Every method returns null when the fact could not be read — for this
/// build, or for this player — and the plan turns that into a pending review instead of a guess.
/// </summary>
public interface IGoldenGenerationFacts
{
    /// <summary>Continent of the player's primary nationality, or null when it is not readable.</summary>
    string? Continent(PlayerRecord player);

    /// <summary>Whether the player is on a youth contract, or null when the contract type is not readable.</summary>
    bool? YouthContract(PlayerRecord player);
}

/// <summary>Reads those facts from the game through the profile's own offsets.</summary>
public sealed class GameGoldenGenerationFacts(NationLinks links, ContractWriter contracts) : IGoldenGenerationFacts
{
    public string? Continent(PlayerRecord player)
    {
        if (player.Nationality is not { Address: not 0 } nation) return null;
        try { return links.Continent(nation.Address)?.Name; }
        catch (MemoryAccessException) { return null; }
    }

    public bool? YouthContract(PlayerRecord player)
    {
        if (player.Contract is null) return null;
        try { return contracts.Read(player)?.Type is { } type ? type == ContractType.Youth : null; }
        catch (Exception ex) when (ex is StaleObjectException or MemoryAccessException or InvalidOperationException) { return null; }
    }
}
