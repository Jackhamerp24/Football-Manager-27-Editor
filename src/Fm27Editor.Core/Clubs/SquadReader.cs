using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Players of one team (squad vector at team + profile team.squad), freshly read, in squad order.</summary>
public sealed class SquadReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxSquad = 200;

    /// <summary>Empty when the address is no longer a team or the vector looks invalid; non-player entries are skipped.</summary>
    public IReadOnlyList<PlayerRecord> Read(ulong teamAddress)
    {
        if (profile.Team is not { } t || teamAddress == 0) return [];
        var head = new byte[8];
        if (!memory.TryRead(teamAddress, head) || BitConverter.ToUInt64(head) != moduleBase + t.VtableRva) return [];
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
}
