using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

public enum CaptainRole { Captain, ViceCaptain }

/// <summary>Person pointer currently stored in one of the team's two captaincy slots.</summary>
public sealed record CaptainPlan(PlayerRecord Player, ulong Team, ulong Club, CaptainRole Role, ulong Address,
    ulong Person, ulong Previous, byte[] Snapshot);

/// <summary>
/// Sets the captain or vice-captain pointer of the team the player is currently in. The pointer stores a
/// person block (player + 0x288), as FMLibre's squad code describes and ReleaseService already clears.
/// </summary>
public sealed class TeamCaptainService(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    RelationOffsets R => profile.Relations ?? throw new InvalidOperationException("Profile has no player relations offsets");
    ClubOffsets K => profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");

    /// <param name="target">Person to store; null means the player himself (used by CLI undo to put the old person back).</param>
    public CaptainPlan Plan(PlayerRecord player, CaptainRole role, ulong? target = null)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        int offset = Offset(role);
        ValidatePlayer(player);
        ulong team = memory.ReadUInt64(player.Address + (ulong)T.PlayerTeam);
        if (team == 0 || memory.ReadUInt64(team) != moduleBase + T.VtableRva)
            throw new StaleObjectException("Player's team is not valid");
        ulong club = memory.ReadUInt64(team + (ulong)C.TeamClub);
        if (club == 0 || memory.ReadUInt64(club) != moduleBase + K.ClubVtableRva)
            throw new StaleObjectException("Player's club is not valid");
        ulong person = player.Address + (ulong)R.PlayerPerson;
        if (memory.ReadUInt64(person) != moduleBase + R.PersonVtable)
            throw new StaleObjectException("Player's person block is not valid");
        var squad = Squad(team);
        if (squad.Count(p => p == player.Address) != 1)
            throw new InvalidOperationException("Player is not in the team's squad exactly once");
        ulong address = team + (ulong)offset;
        byte[] snapshot = memory.Read(address, 8);
        ulong previous = BitConverter.ToUInt64(snapshot, 0);
        ulong value = target ?? person;
        if (previous == value) throw new InvalidOperationException($"Player already holds the value for {Label(role)}");
        return new(player, team, club, role, address, value, previous, snapshot);
    }

    public void Apply(CaptainPlan plan) => Write(plan, plan.Previous, plan.Person);

    public void Undo(CaptainPlan plan) => Write(plan, plan.Person, plan.Previous);

    public static string Label(CaptainRole role) => role == CaptainRole.Captain ? "captain" : "vice captain";

    int Offset(CaptainRole role)
    {
        int? configured = role == CaptainRole.Captain ? T.Captain : T.ViceCaptain;
        return configured ?? throw new InvalidOperationException($"Profile has no {Label(role)} offset");
    }

    void Write(CaptainPlan plan, ulong expected, ulong value)
    {
        ValidatePlayer(plan.Player);
        if (plan.Address != plan.Team + (ulong)Offset(plan.Role) ||
            memory.ReadUInt64(plan.Team) != moduleBase + T.VtableRva ||
            memory.ReadUInt64(plan.Team + (ulong)C.TeamClub) != plan.Club ||
            memory.ReadUInt64(plan.Club) != moduleBase + K.ClubVtableRva)
            throw new StaleObjectException("Captain's team or club changed");
        if (memory.ReadUInt64(plan.Person) != moduleBase + R.PersonVtable)
            throw new StaleObjectException("Player's person block changed");
        if (memory.ReadUInt64(plan.Address) != expected)
            throw new StaleObjectException($"{Label(plan.Role)} changed since the plan was prepared");
        byte[] bytes = BitConverter.GetBytes(value);
        if (!memory.TryWrite(plan.Address, bytes) || memory.Read(plan.Address, 8).AsSpan().SequenceEqual(bytes) == false)
            throw new MemoryAccessException($"Could not write {Label(plan.Role)} at 0x{plan.Address:X}");
    }

    void ValidatePlayer(PlayerRecord player)
    {
        if (player.Id is not > 0 || profile.Fields.Id is not { } id || !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)id, 4)) != player.Id)
            throw new StaleObjectException("Player identity changed since it was read");
    }

    IReadOnlyList<ulong> Squad(ulong team)
    {
        ulong address = team + (ulong)T.Squad;
        byte[] header = memory.Read(address, 24);
        ulong begin = BitConverter.ToUInt64(header, 0), end = BitConverter.ToUInt64(header, 8), capacity = BitConverter.ToUInt64(header, 16);
        if (begin == 0 && end == 0 && capacity == 0) return [];
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 8 != 0 || (capacity - begin) % 8 != 0 ||
            (capacity - begin) / 8 > 512)
            throw new StaleObjectException("Squad vector is invalid");
        byte[] bytes = memory.Read(begin, checked((int)(end - begin)));
        if (!memory.Read(address, 24).AsSpan().SequenceEqual(header)) throw new StaleObjectException("Squad changed while reading");
        return Enumerable.Range(0, bytes.Length / 8).Select(i => BitConverter.ToUInt64(bytes, i * 8)).ToArray();
    }
}
