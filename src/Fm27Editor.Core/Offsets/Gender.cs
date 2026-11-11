using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Offsets;

/// <summary>Men / Women classification read from mapped game-memory flags (Unknown when unmapped or unreadable).</summary>
public enum Gender : byte
{
    Unknown = 0,
    Men = 1,
    Women = 2,
}

/// <summary>Decodes club and person gender flags from mapped profile offsets.</summary>
public static class GenderReader
{
    public static Gender FromClubByte(byte value) => value == 0 ? Gender.Men : Gender.Women;

    public static Gender FromClubBytes(ReadOnlySpan<byte> clubBytes, int? genderOffset) =>
        genderOffset is { } at && (uint)at < (uint)clubBytes.Length ? FromClubByte(clubBytes[at]) : Gender.Unknown;

    public static Gender FromPersonBytes(ReadOnlySpan<byte> personBytes, RelationOffsets? relations) =>
        relations is { PersonGender: { } at, PersonGenderMask: { } mask } && mask != 0 && (uint)at < (uint)personBytes.Length
            ? (personBytes[at] & mask) != 0 ? Gender.Women : Gender.Men
            : Gender.Unknown;

    public static Gender FromPlayerBytes(ReadOnlySpan<byte> playerBytes, RelationOffsets? relations) =>
        relations is { PersonGender: { } at, PersonGenderMask: { } mask } && mask != 0
            && (uint)(relations.PlayerPerson + at) < (uint)playerBytes.Length
            ? (playerBytes[relations.PlayerPerson + at] & mask) != 0 ? Gender.Women : Gender.Men
            : Gender.Unknown;
}

/// <summary>Shared write-time gender checks for clubs, teams, and players.</summary>
public static class GenderGuard
{
    /// <summary>
    /// Rejects players flagged Female and throws <see cref="MemoryAccessException"/> when the mapped gender byte cannot be read.
    /// Does nothing when the profile does not map person gender offsets (<c>personGender</c> / <c>personGenderMask</c>).
    /// </summary>
    public static void EnsureMalePlayer(IProcessMemory memory, OffsetProfile profile, ulong playerAddress)
    {
        if (profile.Relations is not { PersonGender: { } at, PersonGenderMask: { } mask } || mask == 0) return;
        if ((memory.Read(playerAddress + (ulong)(profile.Relations.PlayerPerson + at), 1)[0] & mask) != 0)
            throw new InvalidOperationException("Only male players are supported");
    }

    /// <summary>Rejects clubs whose gender byte indicates Women's football.</summary>
    public static void EnsureMenClub(IProcessMemory memory, OffsetProfile profile, ulong clubAddress)
    {
        int gender = profile.Club?.Gender ?? throw new InvalidOperationException("No club gender offset");
        if (memory.Read(clubAddress + (ulong)gender, 1)[0] != 0)
            throw new InvalidOperationException("Only men's clubs are supported");
    }

    /// <summary>When a team points to a valid club object, rejects women's clubs.</summary>
    public static void EnsureMenClubOfTeamIfPresent(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, ulong teamAddress)
    {
        if (profile.Contract is not { } c || profile.Club is not { } k) return;
        byte[] ptr = new byte[8];
        if (teamAddress == 0 || !memory.TryRead(teamAddress + (ulong)c.TeamClub, ptr)) return;
        ulong club = BitConverter.ToUInt64(ptr);
        if (club == 0 || !memory.TryRead(club, ptr) || BitConverter.ToUInt64(ptr) != moduleBase + k.ClubVtableRva) return;
        EnsureMenClub(memory, profile, club);
    }
}
