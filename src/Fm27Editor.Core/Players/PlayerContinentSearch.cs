using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Players;

public sealed record PlayerContinentSearchResult(IReadOnlyList<PlayerRecord> Matches, IReadOnlyList<PlayerRecord> Unavailable);

/// <summary>Filters the continent of the player's primary nationality, not birth or other nationalities.</summary>
public sealed class PlayerContinentSearch(NationLinks links)
{
    public PlayerContinentSearchResult Find(IEnumerable<PlayerRecord> players, ContinentRecord continent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(continent);
        if (continent.Address == 0) throw new ArgumentException("Continent address is missing", nameof(continent));
        var known = new Dictionary<ulong, ulong?>();
        var matches = new List<PlayerRecord>();
        var unavailable = new List<PlayerRecord>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (player.Nationality is not { Address: not 0 } nation) { unavailable.Add(player); continue; }
            if (!known.TryGetValue(nation.Address, out var address))
            {
                try { address = links.Continent(nation.Address)?.Address; }
                catch (MemoryAccessException) { address = null; }
                known[nation.Address] = address;
            }
            if (address is null) unavailable.Add(player);
            else if (address == continent.Address) matches.Add(player);
        }
        return new(matches, unavailable);
    }
}
