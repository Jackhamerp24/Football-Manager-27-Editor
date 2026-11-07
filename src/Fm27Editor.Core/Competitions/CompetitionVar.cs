using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Competitions;

public sealed record CompetitionVar(CompetitionRecord Competition, ulong Address, byte Type, byte TvOnly, byte Enabled, uint RawFromDate)
{
    public DateOnly? FromDate
    {
        get
        {
            int day = (int)(RawFromDate & 0x1FF), year = (short)(RawFromDate >> 16);
            if (year is < 1 or > 9999 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365) || (year == 1900 && day == 1)) return null;
            return new DateOnly(year, 1, 1).AddDays(day - 1);
        }
    }
}
public enum CompetitionVarField { Type, TvOnly, Enabled }

/// <summary>VAR settings observed on Premier League and Bundesliga on build 2493426.</summary>
public sealed class CompetitionVarAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public CompetitionVar? Read(CompetitionRecord competition)
    {
        var c = profile.Competition;
        if (c?.PrizeMoney is null || c.Id is not { } id) return null;
        try
        {
            if (memory.ReadUInt64(competition.Address) != moduleBase + c.VtableRva ||
                BitConverter.ToInt32(memory.Read(competition.Address + (ulong)id, 4)) != competition.Id) return null;
            ulong address = memory.ReadUInt64(competition.Address + 0xD0);
            if (address == 0) return null;
            byte[] values = memory.Read(address + 0x14, 3);
            if (values[1] > 1 || values[2] > 1) return null;
            return new(competition, address, values[0], values[1], values[2], BitConverter.ToUInt32(memory.Read(address + 0x10, 4)));
        }
        catch (MemoryAccessException) { return null; }
    }

    public void SetFromDate(CompetitionVar snapshot, DateOnly? value)
    {
        var current = Read(snapshot.Competition);
        if (current is null || current.Address != snapshot.Address || current.RawFromDate != snapshot.RawFromDate)
            throw new StaleObjectException("VAR date changed since it was read");
        DateOnly date = value ?? new DateOnly(1900, 1, 1);
        uint encoded = (snapshot.RawFromDate & 0xFE00u) | (uint)date.DayOfYear | ((uint)date.Year << 16);
        ulong at = snapshot.Address + 0x10;
        byte[] before = BitConverter.GetBytes(snapshot.RawFromDate), after = BitConverter.GetBytes(encoded);
        if (!memory.Read(at, 4).AsSpan().SequenceEqual(before)) throw new StaleObjectException("VAR date changed since it was read");
        if (!memory.TryWrite(at, after) || !memory.Read(at, 4).AsSpan().SequenceEqual(after)) throw new MemoryAccessException("Could not write VAR date");
    }

    public void Set(CompetitionVar snapshot, CompetitionVarField field, byte value)
    {
        if (field == CompetitionVarField.Type ? value is < 1 or > 5 : value > 1)
            throw new ArgumentOutOfRangeException(nameof(value));
        int offset = field switch { CompetitionVarField.Type => 0x14, CompetitionVarField.TvOnly => 0x15,
            CompetitionVarField.Enabled => 0x16, _ => throw new ArgumentOutOfRangeException(nameof(field)) };
        byte before = field switch { CompetitionVarField.Type => snapshot.Type, CompetitionVarField.TvOnly => snapshot.TvOnly, _ => snapshot.Enabled };
        var current = Read(snapshot.Competition);
        if (current is null || current.Address != snapshot.Address) throw new StaleObjectException("VAR settings changed since they were read");
        ulong at = snapshot.Address + (ulong)offset;
        if (memory.Read(at, 1)[0] != before) throw new StaleObjectException("VAR field changed since it was read");
        if (!memory.TryWrite(at, [value]) || memory.Read(at, 1)[0] != value) throw new MemoryAccessException("Could not write VAR field");
    }
}
