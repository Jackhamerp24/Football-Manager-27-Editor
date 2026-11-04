using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

/// <summary>Read-only examination of the club links a release would need to remove.</summary>
public sealed record ReleaseTeam(ulong Address, int SquadCount, int PlayerOccurrences, bool Captain, bool ViceCaptain);
public sealed record ReleaseInspection(PlayerRecord Player, ulong Club, ulong PlayingTeam, ulong Contract,
    IReadOnlyList<ReleaseTeam> Teams);

/// <summary>
/// Examines all teams in the employer club's own vector, including teams not reachable from loaded players.
/// This is not a release plan and does not determine whether releasing the player is safe.
/// </summary>
public sealed class ReleaseInspector(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public ReleaseInspection Inspect(PlayerRecord player)
    {
        var c = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
        var t = profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
        var clubOffsets = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
        int teamsOffset = clubOffsets.Lists?.Teams ?? throw new InvalidOperationException("Profile has no club teams offset");
        int personOffset = profile.Relations?.PlayerPerson ?? throw new InvalidOperationException("Profile has no player person offset");
        int seniorOffset = c.SeniorTeam ?? throw new InvalidOperationException("Profile has no contract senior team offset");
        int ownerOffset = c.Owner ?? throw new InvalidOperationException("Profile has no contract owner offset");
        int jobOffset = c.Job ?? throw new InvalidOperationException("Profile has no contract job offset");
        int captainOffset = t.Captain ?? throw new InvalidOperationException("Profile has no captain offset");
        int viceOffset = t.ViceCaptain ?? throw new InvalidOperationException("Profile has no vice captain offset");
        if (player.Id is not > 0 || profile.Fields.Id is not { } idOffset ||
            !profile.PlayerVtableRvas.Contains(player.VtableRva) || memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)idOffset, 4)) != player.Id)
            throw new StaleObjectException("Player identity changed since it was read");
        var contract = player.Contract ?? throw new InvalidOperationException("Player has no readable contract");
        if (memory.ReadUInt64(player.Address + (ulong)c.PlayerContract) != contract.Address ||
            memory.ReadUInt64(contract.Address) != moduleBase + c.ContractVtableRva)
            throw new StaleObjectException("Player contract changed since it was read");
        ulong person = player.Address + (ulong)personOffset;
        ulong employer = memory.ReadUInt64(contract.Address + (ulong)c.Team);
        if (employer == 0 || memory.ReadUInt64(contract.Address + (ulong)seniorOffset) != employer ||
            memory.ReadUInt64(contract.Address + (ulong)ownerOffset) != person || memory.Read(contract.Address + (ulong)jobOffset, 1)[0] != 1)
            throw new InvalidOperationException("Not a plain player contract with one employer team");
        ulong playing = memory.ReadUInt64(player.Address + (ulong)t.PlayerTeam);
        if (playing == 0 || memory.ReadUInt64(playing) != moduleBase + t.VtableRva ||
            memory.ReadUInt64(employer) != moduleBase + t.VtableRva)
            throw new StaleObjectException("Playing or employer team is not valid");
        ulong club = memory.ReadUInt64(employer + (ulong)c.TeamClub);
        if (club == 0 || memory.ReadUInt64(club) != moduleBase + clubOffsets.ClubVtableRva ||
            memory.ReadUInt64(playing + (ulong)c.TeamClub) != club)
            throw new InvalidOperationException("Playing team and employer are not in the same club");
        ulong[] teams = ReadVector(club + (ulong)teamsOffset, 128, allowEmpty: false);
        if (teams.Distinct().Count() != teams.Length || !teams.Contains(playing) || !teams.Contains(employer))
            throw new InvalidOperationException("Club teams do not include the playing and employer teams exactly once");
        var result = new List<ReleaseTeam>(teams.Length);
        foreach (ulong team in teams)
        {
            if (team == 0 || memory.ReadUInt64(team) != moduleBase + t.VtableRva || memory.ReadUInt64(team + (ulong)c.TeamClub) != club)
                throw new StaleObjectException("Club team changed or is not valid");
            var squad = ReadVector(team + (ulong)t.Squad, 512, allowEmpty: true);
            int occurrences = squad.Count(p => p == player.Address);
            if (occurrences > 1) throw new InvalidOperationException("Player occurs more than once in a squad");
            result.Add(new(team, squad.Length, occurrences,
                memory.ReadUInt64(team + (ulong)captainOffset) == person,
                memory.ReadUInt64(team + (ulong)viceOffset) == person));
        }
        if (result.Single(s => s.Address == playing).PlayerOccurrences != 1)
            throw new InvalidOperationException("Player is not in the playing team's squad");
        return new(player, club, playing, contract.Address, result);
    }

    ulong[] ReadVector(ulong address, int max, bool allowEmpty)
    {
        var header = memory.Read(address, 24);
        ulong begin = BitConverter.ToUInt64(header, 0), end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
        if (allowEmpty && begin == 0 && end == 0 && cap == 0) return [];
        if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0 ||
            (cap - begin) / 8 > (ulong)max)
            throw new InvalidDataException("Team or squad vector is invalid");
        var bytes = memory.Read(begin, checked((int)(end - begin)));
        if (!memory.Read(address, 24).AsSpan().SequenceEqual(header)) throw new StaleObjectException("Vector changed while reading");
        return Enumerable.Range(0, bytes.Length / 8).Select(i => BitConverter.ToUInt64(bytes, i * 8)).ToArray();
    }
}
