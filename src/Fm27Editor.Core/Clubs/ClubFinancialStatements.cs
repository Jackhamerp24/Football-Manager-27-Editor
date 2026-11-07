using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>One month of FMRTE's Income Statement: expenditure block plus the income block that follows it.</summary>
public sealed record ClubFinancialPeriod(
    int Index,
    long Other, long LoanRepayment, long LeagueFines, long GroundMaintenance, long PlayerWages,
    long StaffWages, long Bonuses, long LoyaltyBonuses, long TransferExpenditure, long Vat, long NiEmployerTax,
    long GateReceipts, long OtherIncome, long SeasonTickets, long Interest, long Investments,
    long Merchandising, long PlayersSold, long Sponsorship, long MatchDayIncome, long Grants)
{
    public long TotalIncome => GateReceipts + OtherIncome + SeasonTickets + Interest + Investments
        + Merchandising + PlayersSold + Sponsorship + MatchDayIncome + Grants;

    public long TotalExpenditure => Other + LoanRepayment + LeagueFines + GroundMaintenance + PlayerWages
        + StaffWages + Bonuses + LoyaltyBonuses + TransferExpenditure + Vat + NiEmployerTax;
}

/// <summary>
/// Reads the monthly statement blocks of one club's finance object. Index 0 is the most recent month; the reader does
/// not label months, because the game date is not part of the block.
/// </summary>
public sealed class ClubFinancialStatements(IProcessMemory memory, OffsetProfile profile)
{
    const int MaxPeriods = 24;

    public IReadOnlyList<ClubFinancialPeriod> Read(ulong clubAddress)
    {
        if (profile.Club?.FinanceLists?.Statements is not { } o) return [];
        if (o.Periods is <= 0 or > MaxPeriods) return [];
        if (o.Income.Count != ClubStatementOffsets.IncomeKeys.Length
            || o.Expenditure.Count != ClubStatementOffsets.ExpenditureKeys.Length) return [];
        if (!Fits(o)) return [];

        var pointer = memory.Read(clubAddress + (ulong)o.FinancePointer, 8);
        if (pointer.Length < 8) return [];
        ulong finance = BitConverter.ToUInt64(pointer, 0);
        if (finance == 0) return [];

        var result = new List<ClubFinancialPeriod>(o.Periods);
        for (int index = 0; index < o.Periods; index++)
        {
            ulong block = finance + (ulong)(o.Base + o.Stride * index);
            if (ReadBlock(block, o) is { } period) result.Add(period with { Index = index });
        }
        // The block run is fixed length; months the game has not filled in stay at zero and are dropped.
        while (result.Count > 0 && result[^1] is { TotalIncome: 0, TotalExpenditure: 0 })
            result.RemoveAt(result.Count - 1);
        return result.AsReadOnly();
    }

    ClubFinancialPeriod? ReadBlock(ulong block, ClubStatementOffsets o)
    {
        if (!TryRead(block, o.Expenditure, out var expenditure)) return null;
        if (!TryRead(block + (ulong)o.IncomeBase, o.Income, out var income)) return null;
        return new ClubFinancialPeriod(
            0,
            expenditure[0], expenditure[1], expenditure[2], expenditure[3], expenditure[4],
            expenditure[5], expenditure[6], expenditure[7], expenditure[8], expenditure[9], expenditure[10],
            income[0], income[1], income[2], income[3], income[4], income[5], income[6], income[7], income[8], income[9]);
    }

    /// <summary>
    /// Writes one statement figure after checking the block still holds the snapshot value. Income and expenditure keys
    /// come from <see cref="ClubStatementOffsets.IncomeKeys"/> / <see cref="ClubStatementOffsets.ExpenditureKeys"/>.
    /// </summary>
    public void Set(ulong clubAddress, int periodIndex, bool income, string key, long expected, long value)
    {
        if (profile.Club?.FinanceLists?.Statements is not { } o)
            throw new InvalidOperationException("Profile has no statement offsets");
        var keys = income ? ClubStatementOffsets.IncomeKeys : ClubStatementOffsets.ExpenditureKeys;
        var offsets = income ? o.Income : o.Expenditure;
        int index = Array.IndexOf(keys, key);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(key), "Unknown statement field");
        if (periodIndex < 0 || periodIndex >= o.Periods) throw new ArgumentOutOfRangeException(nameof(periodIndex));
        if (value is < int.MinValue or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        if (!Fits(o)) throw new InvalidOperationException("Statement offsets are not usable for this build");

        var pointer = memory.Read(clubAddress + (ulong)o.FinancePointer, 8);
        if (pointer.Length < 8) throw new MemoryAccessException("Club finance pointer is unreadable");
        ulong finance = BitConverter.ToUInt64(pointer, 0);
        if (finance == 0) throw new StaleObjectException("Club finance object is gone");

        ulong address = finance + (ulong)(o.Base + o.Stride * periodIndex + (income ? o.IncomeBase : 0) + offsets[index]);
        var current = memory.Read(address, 4);
        if (current.Length < 4) throw new MemoryAccessException($"Statement field 0x{address:X} is unreadable");
        if (BitConverter.ToInt32(current, 0) != expected)
            throw new StaleObjectException("The statement figure changed since it was read");

        var bytes = BitConverter.GetBytes((int)value);
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 4).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write statement field 0x{address:X}");
    }

    bool TryRead(ulong baseAddress, IReadOnlyList<int> offsets, out long[] values)    {
        values = new long[offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
        {
            var raw = memory.Read(baseAddress + (ulong)offsets[i], 4);
            if (raw.Length < 4) return false;
            values[i] = BitConverter.ToInt32(raw, 0);
        }
        return true;
    }

    /// <summary>Every declared field must sit inside one stride, and the blocks must be inside the object's first page run.</summary>
    static bool Fits(ClubStatementOffsets o)
    {
        if (o.Base < 0 || o.IncomeBase < 0 || o.Stride <= 0 || o.IncomeBase + 4 > o.Stride) return false;
        static bool Inside(IReadOnlyList<int> offsets, ClubStatementOffsets o, int extra) =>
            offsets.All(v => v >= 0 && v + 4 <= (extra == 0 ? o.Stride : o.Stride - o.IncomeBase));
        return Inside(o.Expenditure, o, 0) && Inside(o.Income, o, o.IncomeBase);
    }
}
