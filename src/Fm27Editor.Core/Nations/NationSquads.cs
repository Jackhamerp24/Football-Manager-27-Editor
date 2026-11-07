using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

public sealed record NationSquadRow(PlayerRecord Player, long? Condition, long? AskingPrice);

/// <summary>
/// FMRTE 27's nation Squads page: every national team of a nation (senior, U21, U20, U19, U18 …) with its squad vector.
/// Measured on England 765: the senior team object holds the same squad vector at +0x38 as club teams, but the national
/// team vtable is nation.teams.teamVtable, so SquadReader (club vtable) does not apply.
/// </summary>
public sealed class NationSquads(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxSquad = 200;

    public IReadOnlyList<PlayerRecord> Read(ulong teamAddress)
    {
        if (profile.Team is not { } t || profile.Nation?.Teams is not { } nt || teamAddress == 0) return [];
        var head = new byte[8];
        if (!memory.TryRead(teamAddress, head) || BitConverter.ToUInt64(head) != moduleBase + nt.TeamVtable) return [];
        var vector = new byte[16];
        if (!memory.TryRead(teamAddress + (ulong)t.Squad, vector)) return [];
        ulong begin = BitConverter.ToUInt64(vector), end = BitConverter.ToUInt64(vector, 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxSquad) return [];
        var pointers = new byte[end - begin];
        if (pointers.Length > 0 && !memory.TryRead(begin, pointers)) return [];
        var repository = new PlayerRepository(memory, profile, moduleBase);
        var squad = new List<PlayerRecord>();
        for (int i = 0; i < pointers.Length; i += 8)
            if (repository.Reload(BitConverter.ToUInt64(pointers, i)) is { } p) squad.Add(p);
        return squad;
    }

    /// <summary>All national teams of a nation with their squads, in the game's order (senior first).</summary>
    public IReadOnlyList<(NationTeamRecord Team, IReadOnlyList<PlayerRecord> Squad)> ReadNation(ulong nation)
    {
        var result = new List<(NationTeamRecord, IReadOnlyList<PlayerRecord>)>();
        foreach (var team in new NationTeamReader(memory, profile, moduleBase).Read(nation))
            result.Add((team, Read(team.Address)));
        return result;
    }

    /// <summary>Squad rows with independently mapped player condition and asking price.</summary>
    public IReadOnlyList<NationSquadRow> ReadDetails(ulong teamAddress)
    {
        var fields = new PlayerFieldAccess(memory, profile, moduleBase);
        return Read(teamAddress).Select(player => new NationSquadRow(player,
            fields.Read(player, PlayerField.Condition), fields.Read(player, PlayerField.AskingPrice))).ToArray();
    }
}
