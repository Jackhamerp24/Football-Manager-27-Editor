using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Transfer;

public sealed record ReleaseGuard(ulong Address, byte[] Bytes, string What);
public sealed record ManagedClubSnapshot(int ManagerId, ulong Person, ulong Contract, ulong Club);
public sealed record ReleasePlan(PlayerRecord Player, ulong Club, ulong Contract, IReadOnlyList<ManagedClubSnapshot> Managers,
    IReadOnlyList<ulong> Teams, IReadOnlyList<ReleaseGuard> Guards, IReadOnlyList<PlannedWrite> Writes, int Squads, int Captaincies);

/// <summary>Plans, applies and undoes release of an ordinary contracted player from a men's club.</summary>
public sealed class ReleaseService(IProcessMemory memory, OffsetProfile profile, ulong moduleBase,
    Func<IReadOnlyList<StaffRecord>> readStaff)
{
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("No contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("No team offsets");
    ClubOffsets K => profile.Club ?? throw new InvalidOperationException("No club offsets");
    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("No staff offsets");

    public ReleasePlan Plan(PlayerRecord player)
    {
        var inspection = new ReleaseInspector(memory, profile, moduleBase).Inspect(player);
        var managers = ReadManagers();
        if (managers.Any(m => m.Club == inspection.Club))
            throw new InvalidOperationException("A human manager controls this club");
        int gender = K.Gender ?? throw new InvalidOperationException("No club gender offset");
        if (memory.Read(inspection.Club + (ulong)gender, 1)[0] != 0)
            throw new InvalidOperationException("Only men's clubs are supported");
        var guards = new List<ReleaseGuard>();
        var writes = new List<PlannedWrite>();
        byte[] Capture(ulong address, int size, string what)
        {
            var bytes = memory.Read(address, size);
            guards.Add(new(address, bytes, what));
            return bytes;
        }
        var club = Capture(inspection.Club, 0xC0, "club");
        int teamsOffset = K.Lists?.Teams ?? throw new InvalidOperationException("No club teams offset");
        int personOffset = profile.Relations?.PlayerPerson ?? throw new InvalidOperationException("No player person offset");
        int captain = T.Captain ?? throw new InvalidOperationException("No captain offset");
        int vice = T.ViceCaptain ?? throw new InvalidOperationException("No vice captain offset");
        Capture(player.Address, 8, "player vtable");
        Capture(player.Address + (ulong)(profile.Fields.Id ?? throw new InvalidOperationException("No player id offset")), 4, "player id");
        Capture(player.Address + (ulong)C.PlayerContract, 8, "player contract");
        Capture(player.Address + (ulong)T.PlayerTeam, 8, "playing team");
        Capture(inspection.Contract, Math.Max((C.Job ?? throw new InvalidOperationException("No contract job offset")) + 1,
            (C.SeniorTeam ?? throw new InvalidOperationException("No contract senior team offset")) + 8), "contract");
        ulong[] ReadVector(ulong address, int max, string what, out ulong begin, out ulong end)
        {
            var header = Capture(address, 24, what + " header");
            begin = BitConverter.ToUInt64(header, 0); end = BitConverter.ToUInt64(header, 8);
            ulong cap = BitConverter.ToUInt64(header, 16);
            if (begin == 0 && end == 0 && cap == 0) return [];
            if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0 || (cap - begin) / 8 > (ulong)max)
                throw new InvalidOperationException($"Invalid {what} vector");
            var bytes = Capture(begin, checked((int)(end - begin)), what + " entries");
            return Enumerable.Range(0, bytes.Length / 8).Select(i => BitConverter.ToUInt64(bytes, i * 8)).ToArray();
        }
        var teams = ReadVector(inspection.Club + (ulong)teamsOffset, 128, "teams", out _, out _);
        if (!teams.SequenceEqual(inspection.Teams.Select(t => t.Address))) throw new StaleObjectException("Club teams changed");
        int squads = 0, captaincies = 0;
        ulong person = player.Address + (ulong)personOffset;
        foreach (var team in inspection.Teams)
        {
            var teamBytes = Capture(team.Address, Math.Max(vice + 8, captain + 8), "team");
            if (BitConverter.ToUInt64(teamBytes, 0) != moduleBase + T.VtableRva ||
                BitConverter.ToUInt64(teamBytes, C.TeamClub) != inspection.Club)
                throw new StaleObjectException("Team changed");
            foreach (int offset in new[] { captain, vice })
                if (BitConverter.ToUInt64(teamBytes, offset) == person)
                {
                    writes.Add(new(team.Address + (ulong)offset, BitConverter.GetBytes(person), new byte[8], "captaincy"));
                    captaincies++;
                }
            var members = ReadVector(team.Address + (ulong)T.Squad, 512, "squad", out ulong begin, out ulong end);
            int[] indices = Enumerable.Range(0, members.Length).Where(i => members[i] == player.Address).ToArray();
            if (indices.Length != team.PlayerOccurrences) throw new StaleObjectException("Squad changed");
            if (indices.Length == 0) continue;
            if (indices.Length != 1) throw new InvalidOperationException("Player occurs more than once in squad");
            int index = indices[0], last = members.Length - 1;
            if (index < last)
                writes.Add(new(begin + (ulong)index * 8,
                    members[index..last].SelectMany(BitConverter.GetBytes).ToArray(),
                    members[(index + 1)..].SelectMany(BitConverter.GetBytes).ToArray(), "squad shift"));
            writes.Add(new(team.Address + (ulong)T.Squad + 8, BitConverter.GetBytes(end), BitConverter.GetBytes(end - 8), "squad end"));
            writes.Add(new(end - 8, BitConverter.GetBytes(members[last]), new byte[8], "squad last slot"));
            squads++;
        }
        writes.Add(new(player.Address + (ulong)T.PlayerTeam, BitConverter.GetBytes(inspection.PlayingTeam), new byte[8], "playing team"));
        writes.Add(new(player.Address + (ulong)C.PlayerContract, BitConverter.GetBytes(inspection.Contract), new byte[8], "player contract"));
        return new(player, inspection.Club, inspection.Contract, managers, teams, guards, writes, squads, captaincies);
    }

    public void Apply(ReleasePlan plan) => Change(plan, undo: false);
    public void Undo(ReleasePlan plan) => Change(plan, undo: true);

    void Change(ReleasePlan plan, bool undo)
    {
        ValidatePlayer(plan.Player);
        if (!ReadManagers().SequenceEqual(plan.Managers)) throw new StaleObjectException("Human managers changed; nothing written");
        foreach (var guard in plan.Guards)
        {
            var expected = (byte[])guard.Bytes.Clone();
            if (undo) foreach (var write in plan.Writes) Overlay(expected, guard.Address, write.Address, write.Value);
            if (!memory.Read(guard.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{guard.What} changed; nothing written");
        }
        foreach (var write in plan.Writes)
        {
            var expected = undo ? write.Value : write.Expected;
            if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{write.What} changed; nothing written");
        }
        var writes = undo ? plan.Writes.Reverse() : plan.Writes;
        foreach (var write in writes)
        {
            ValidatePlayer(plan.Player);
            if (memory.ReadUInt64(plan.Contract) != moduleBase + C.ContractVtableRva ||
                memory.ReadUInt64(plan.Club) != moduleBase + K.ClubVtableRva)
                throw new StaleObjectException("Contract or club changed while writing");
            foreach (ulong team in plan.Teams)
                if (memory.ReadUInt64(team) != moduleBase + T.VtableRva || memory.ReadUInt64(team + (ulong)C.TeamClub) != plan.Club)
                    throw new StaleObjectException("Team changed while writing");
            byte[] expected = undo ? write.Value : write.Expected, value = undo ? write.Expected : write.Value;
            if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{write.What} changed while writing");
            if (!memory.TryWrite(write.Address, value) || !memory.Read(write.Address, value.Length).AsSpan().SequenceEqual(value))
                throw new MemoryAccessException($"Write failed: {write.What}");
        }
    }

    IReadOnlyList<ManagedClubSnapshot> ReadManagers()
    {
        var people = readStaff().Where(s => s.Kind.Manager && s.DateOfBirth is not null).OrderBy(s => s.Id).ToArray();
        if (people.Length == 0 || people.Any(s => s.Id <= 0) || people.Select(s => s.Id).Distinct().Count() != people.Length)
            throw new InvalidOperationException("Cannot identify the save's human managers");
        var managers = new List<ManagedClubSnapshot>();
        foreach (var manager in people)
        {
            if (!S.Kinds.Contains(manager.Kind) || memory.ReadUInt64(manager.Address) != moduleBase + manager.Kind.PersonVtableRva ||
                memory.ReadUInt64(manager.StaffAddress) != moduleBase + manager.Kind.StaffVtableRva ||
                BitConverter.ToInt32(memory.Read(manager.Address + (ulong)S.Id, 4)) != manager.Id)
                throw new StaleObjectException("Human manager changed");
            ulong contract = memory.ReadUInt64(manager.Address + (ulong)S.Job), club = 0;
            if (contract != 0)
            {
                int owner = C.Owner ?? throw new InvalidOperationException("No contract owner offset");
                if (memory.ReadUInt64(contract) != moduleBase + C.ContractVtableRva || memory.ReadUInt64(contract + (ulong)owner) != manager.Address)
                    throw new StaleObjectException("Human manager contract is not valid");
                ulong team = memory.ReadUInt64(contract + (ulong)C.Team);
                if (team == 0 || memory.ReadUInt64(team) != moduleBase + T.VtableRva) throw new StaleObjectException("Human manager team is not valid");
                club = memory.ReadUInt64(team + (ulong)C.TeamClub);
                if (club == 0 || memory.ReadUInt64(club) != moduleBase + K.ClubVtableRva) throw new StaleObjectException("Human manager club is not valid");
            }
            managers.Add(new(manager.Id, manager.Address, contract, club));
        }
        return managers;
    }

    void ValidatePlayer(PlayerRecord player)
    {
        if (player.Id is not > 0 || profile.Fields.Id is not { } id || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)id, 4)) != player.Id)
            throw new StaleObjectException("Player identity changed");
    }

    static void Overlay(byte[] target, ulong address, ulong sourceAddress, byte[] source)
    {
        ulong start = Math.Max(address, sourceAddress), end = Math.Min(address + (ulong)target.Length, sourceAddress + (ulong)source.Length);
        if (start < end) Array.Copy(source, (long)(start - sourceAddress), target, (long)(start - address), (long)(end - start));
    }
}
