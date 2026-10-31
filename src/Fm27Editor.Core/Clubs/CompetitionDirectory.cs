using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Competitions by the internal index that clauses store (competition object +0x8).</summary>
public static class CompetitionDirectory
{
    /// <summary>
    /// The divisions the loaded players' teams play in. Cups are not reachable this way yet, so a bonus for a cup
    /// keeps its index and no name.
    /// </summary>
    public static IReadOnlyDictionary<int, CompetitionInfo> FromPlayers(IProcessMemory memory, OffsetProfile profile, IEnumerable<PlayerRecord> players)
    {
        var map = new Dictionary<int, CompetitionInfo>();
        if (profile.Competition?.Index is not { } offset) return map;
        Span<byte> b = stackalloc byte[4];
        foreach (var division in players.Select(p => p.Division).OfType<CompetitionInfo>().DistinctBy(d => d.Address))
            if (memory.TryRead(division.Address + (ulong)offset, b))
                map.TryAdd(BitConverter.ToInt32(b), division);
        return map;
    }
}
