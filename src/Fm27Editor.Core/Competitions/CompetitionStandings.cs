using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Competitions;

/// <summary>One current standings row. Addresses are valid only for the current save.</summary>
public sealed record CompetitionStanding(int Position, ulong Address, ulong Team, ulong Club, int ClubId,
    short GoalsFor, short GoalsAgainst, short Points, byte Played, byte Won, byte Drawn, byte Lost);

public enum CompetitionStandingField { Played, Won, Drawn, Lost, GoalsFor, GoalsAgainst, Points }

/// <summary>Read the standings vector measured in a one-stage league. Other layouts return null.</summary>
public sealed class CompetitionStandingsAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetPoints(CompetitionRecord competition, CompetitionStanding snapshot, short value) =>
        Set(competition, snapshot, CompetitionStandingField.Points, value);

    public static int Value(CompetitionStanding row, CompetitionStandingField field) => field switch
    {
        CompetitionStandingField.Played => row.Played, CompetitionStandingField.Won => row.Won,
        CompetitionStandingField.Drawn => row.Drawn, CompetitionStandingField.Lost => row.Lost,
        CompetitionStandingField.GoalsFor => row.GoalsFor & 0xFF, CompetitionStandingField.GoalsAgainst => row.GoalsAgainst & 0xFF,
        CompetitionStandingField.Points => row.Points, _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    public void Set(CompetitionRecord competition, CompetitionStanding snapshot, CompetitionStandingField field, int value)
    {
        if (field == CompetitionStandingField.Points ? value is < short.MinValue or > short.MaxValue : value is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        int offset = field switch
        {
            CompetitionStandingField.Played => 0xF, CompetitionStandingField.Won => 0x10,
            CompetitionStandingField.Drawn => 0x11, CompetitionStandingField.Lost => 0x12,
            CompetitionStandingField.GoalsFor => 0x8, CompetitionStandingField.GoalsAgainst => 0xA,
            CompetitionStandingField.Points => 0xC, _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var rows = Read(competition);
        if (rows is null || snapshot.Position < 1 || snapshot.Position > rows.Count)
            throw new StaleObjectException("Standings changed since they were read");
        var current = rows[snapshot.Position - 1];
        if (current.Address != snapshot.Address || current.Team != snapshot.Team || current.Club != snapshot.Club ||
            current.ClubId != snapshot.ClubId || Value(current, field) != Value(snapshot, field))
            throw new StaleObjectException("Standing row changed since it was read");
        byte[] before = field == CompetitionStandingField.Points ? BitConverter.GetBytes((short)Value(snapshot, field)) : [(byte)Value(snapshot, field)];
        byte[] after = field == CompetitionStandingField.Points ? BitConverter.GetBytes((short)value) : [(byte)value];
        ulong address = snapshot.Address + (ulong)offset;
        if (!memory.Read(address, before.Length).AsSpan().SequenceEqual(before)) throw new StaleObjectException("Standing field changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException("Could not write standings field");
    }

    public IReadOnlyList<CompetitionStanding>? Read(CompetitionRecord competition)
    {
        if (profile.Competition?.PrizeMoney is not { } layout || profile.Team is not { } teamOffsets ||
            profile.Contract is not { } contract || profile.Club is not { Id: { } clubId } clubOffsets) return null;
        try
        {
            // This validates competition, holder, single stage, ownership and vtables.
            var prize = new CompetitionPrizeMoneyAccess(memory, profile, moduleBase).Read(competition);
            if (prize is null) return null;
            ulong begin = memory.ReadUInt64(prize.Stage + 0xC0), end = memory.ReadUInt64(prize.Stage + 0xC8);
            if (begin == 0 && end == 0) return [];
            if (begin == 0 || end < begin || (end - begin) % 8 != 0 || end - begin > 2048) return null;
            var rows = new List<CompetitionStanding>();
            for (ulong p = begin; p < end; p += 8)
            {
                ulong row = memory.ReadUInt64(p);
                ulong team = memory.ReadUInt64(row + 0x88);
                if (memory.ReadUInt64(team) != moduleBase + teamOffsets.VtableRva) return null;
                ulong club = memory.ReadUInt64(team + (ulong)contract.TeamClub);
                if (memory.ReadUInt64(club) != moduleBase + clubOffsets.ClubVtableRva) return null;
                int id = BitConverter.ToInt32(memory.Read(club + (ulong)clubId, 4));
                var values = memory.Read(row + 8, 11);
                rows.Add(new(rows.Count + 1, row, team, club, id, BitConverter.ToInt16(values, 0),
                    BitConverter.ToInt16(values, 2), BitConverter.ToInt16(values, 4), values[7], values[8], values[9], values[10]));
            }
            if (rows.Select(row => row.ClubId).Distinct().Count() != rows.Count) return null;
            return rows.AsReadOnly();
        }
        catch (MemoryAccessException) { return null; }
    }
}
