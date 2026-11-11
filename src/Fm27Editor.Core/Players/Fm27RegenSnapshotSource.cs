using System.Globalization;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Turns the players the editor has already read into the intake scan's view of them: the player's id as text, his
/// birth date, the id of the club he plays for and of that club's team, and whether his contract is a youth one.
/// Club and team are named by the game's own ids, never by an address (which moves on every load) or a name (which
/// two clubs can share). A field this build was never measured for, or one that does not read, comes back null —
/// the diff then holds the player for review instead of anyone guessing.
/// </summary>
public sealed class Fm27RegenSnapshotSource(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    readonly ContractWriter _contracts = new(memory, profile, moduleBase);

    /// <summary>
    /// One sweep of <paramref name="players"/> as a scan. <paramref name="complete"/> is the caller's claim that the
    /// list covers the whole database; on top of that this method marks the scan incomplete when a record could not
    /// be given an id, because a record left out for want of an id is one the diff would otherwise never see.
    /// </summary>
    public RegenSnapshot Scan(
        IReadOnlyList<PlayerRecord> players,
        string saveId,
        string branchId,
        DateOnly gameDate,
        string phase = RegenPhase.Unknown,
        bool complete = true)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (string.IsNullOrWhiteSpace(saveId)) throw new ArgumentException("A save id is required", nameof(saveId));
        if (string.IsNullOrWhiteSpace(branchId)) throw new ArgumentException("A timeline id is required", nameof(branchId));

        var observations = new List<RegenObservation>(players.Count);
        bool completeScan = complete;
        foreach (var player in players)
        {
            // Without an id the record cannot be told apart from any other, so it is left out and the scan is marked
            // incomplete: the diff refuses an incomplete scan rather than silently comparing half a world.
            if (player.Id is not { } id) { completeScan = false; continue; }
            observations.Add(new RegenObservation(
                PersonUid: id.ToString(CultureInfo.InvariantCulture),
                DateOfBirth: player.DateOfBirth,
                ClubUid: ClubUid(player),
                TeamUid: TeamUid(player),
                IsPlayer: true,
                IsYouth: YouthContract(player),
                Provenance: "player-record"));
        }
        return new RegenSnapshot(saveId, branchId, gameDate, completeScan, observations, RegenScope.Global, phase);
    }

    /// <summary>The playing club's id as the game UI shows it, or null when no club id could be read for this build.</summary>
    string? ClubUid(PlayerRecord player)
    {
        if (profile.Club is not { Id: { } idOffset }) return null;
        ulong club = PlayingClub.Of(memory, profile, moduleBase, player);
        if (club == 0) return null;
        Span<byte> raw = stackalloc byte[4];
        return memory.TryRead(club + (ulong)idOffset, raw)
            ? BitConverter.ToInt32(raw).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// The team as its club's id plus the team's own classification (first team, U21, U18...): two values the game
    /// keeps in the objects themselves, so the pair is the same on every load. Null when this build's profile has no
    /// measured classification offset, when the team or its club does not read, or when the club has no id offset —
    /// a team named by anything less would risk pointing at a different team after a reload.
    /// </summary>
    string? TeamUid(PlayerRecord player)
    {
        if (profile.Team is not { } team || profile.Contract is not { } contract
            || profile.Club is not { Id: { } clubIdOffset } club || team.Classification is not { } classOffset)
            return null;

        Span<byte> qword = stackalloc byte[8];
        if (!memory.TryRead(player.Address + (ulong)team.PlayerTeam, qword)) return null;
        ulong teamAt = BitConverter.ToUInt64(qword);
        if (teamAt == 0 || !memory.TryRead(teamAt, qword) || BitConverter.ToUInt64(qword) != moduleBase + team.VtableRva)
            return null;

        // The classification sits inside the first 0x30 bytes of the team object, as the staff reader reads it too.
        Span<byte> head = stackalloc byte[0x30];
        if (classOffset < 0 || classOffset + 4 > head.Length || !memory.TryRead(teamAt, head)) return null;
        int classification = BitConverter.ToInt32(head[classOffset..]);

        if (!memory.TryRead(teamAt + (ulong)contract.TeamClub, qword)) return null;
        ulong clubAt = BitConverter.ToUInt64(qword);
        if (clubAt == 0 || !memory.TryRead(clubAt, qword) || BitConverter.ToUInt64(qword) != moduleBase + club.ClubVtableRva)
            return null;
        Span<byte> clubId = stackalloc byte[4];
        if (!memory.TryRead(clubAt + (ulong)clubIdOffset, clubId)) return null;
        return string.Create(CultureInfo.InvariantCulture, $"{BitConverter.ToInt32(clubId)}:{classification}");
    }

    /// <summary>Whether the player's contract is a youth one; null when it cannot be read for this player or build.</summary>
    bool? YouthContract(PlayerRecord player)
    {
        if (player.Contract is null) return null;
        try { return _contracts.Read(player)?.Type is { } type ? type == ContractType.Youth : null; }
        catch (Exception ex) when (ex is StaleObjectException or MemoryAccessException or InvalidOperationException)
        { return null; }
    }
}
