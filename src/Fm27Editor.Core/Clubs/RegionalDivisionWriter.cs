using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Edits one regional division row of a club: the competition (both id copies), the level and the year. Every write first
/// checks that the record still holds exactly the bytes read, then writes and reads back.
/// </summary>
public sealed class RegionalDivisionWriter(IProcessMemory memory, OffsetProfile profile)
{
    public void Set(ClubRegionalDivisionEntry entry, int competitionId, byte level, short year)
    {
        var l = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club list offsets");
        if (l.RegionalSize < ClubRegionalDivisionEntry.YearOffset + 2 || entry.Bytes.Length == 0)
            throw new InvalidOperationException("Regional division layout is unavailable for this build");
        if (competitionId <= 0) throw new ArgumentOutOfRangeException(nameof(competitionId), "Choose a competition");
        var current = memory.Read(entry.Address, l.RegionalSize);
        if (Convert.ToHexString(current) != entry.Bytes) throw new StaleObjectException("The regional division changed since it was read; reload");
        var updated = (byte[])current.Clone();
        BitConverter.GetBytes(competitionId).CopyTo(updated, ClubRegionalDivisionEntry.CompetitionOffset);
        BitConverter.GetBytes(competitionId).CopyTo(updated, ClubRegionalDivisionEntry.SecondCompetitionOffset);
        BitConverter.GetBytes(year).CopyTo(updated, ClubRegionalDivisionEntry.YearOffset);
        updated[l.Level] = level;
        if (!memory.TryWrite(entry.Address, updated) || !memory.Read(entry.Address, updated.Length).AsSpan().SequenceEqual(updated))
            throw new MemoryAccessException($"Could not write the regional division at 0x{entry.Address:X}");
    }
}
