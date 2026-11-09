using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// The club FMRTE 27's Information-page presets call "current": the club of the team the player plays for, not the club
/// of his contract. Measured on Game Pass 2493426 with Mark Shelton (contract York, on loan at Grimsby Town): Improve Team
/// Cohesion left York alone and set the Grimsby row to 100, and Trained at club wrote Grimsby as the formed-at club.
/// For most players both clubs are the same.
/// </summary>
public static class PlayingClub
{
    /// <summary>
    /// Address of the club of <paramref name="player"/>'s playing team; falls back to the contract club when the player has no
    /// valid team or the team does not lead to a club. Zero when neither is known.
    /// </summary>
    public static ulong Of(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, PlayerRecord player)
    {
        if (profile.Team is { } team && profile.Contract is { } contract && profile.Club is { } club
            && Pointer(memory, player.Address + (ulong)team.PlayerTeam) is { } playingTeam
            && Pointer(memory, playingTeam) == moduleBase + team.VtableRva
            && Pointer(memory, playingTeam + (ulong)contract.TeamClub) is { } playingClub
            && Pointer(memory, playingClub) == moduleBase + club.ClubVtableRva)
            return playingClub;
        return player.Club?.Address ?? 0;
    }

    static ulong? Pointer(IProcessMemory memory, ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return at >= 0x10000 && memory.TryRead(at, b) && BitConverter.ToUInt64(b) is var v && v != 0 ? v : null;
    }
}
