using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Changes the two adjacent payment fields only while the club, vector and record still match the snapshot.</summary>
public sealed class ClubDebtWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Source codes directly observed in FMRTE and RAM; other codes remain unsupported.</summary>
    public static bool SupportedSource(byte value) => value is 1 or 5 or 10 or 15;

    public void SetDates(ulong club, ClubDebtEntry entry, DateOnly start, DateOnly end)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (!f.DebtDatesEditable || f.DebtEnd < 0 || f.DebtEnd > f.DebtSize - 8 || f.DebtStart != f.DebtEnd + 4
            || entry.StartDate is null || entry.EndDate is null)
            throw new InvalidOperationException("Debt dates are unavailable for this build or record");
        // These are the bounds the existing debt reader can decode, not claimed FMRTE limits.
        if (start.Year is <= 1900 or > 2300 || end.Year is <= 1900 or > 2300)
            throw new ArgumentOutOfRangeException(nameof(start), "Debt date cannot be read back by this profile");
        RequireCurrent(club, entry);
        byte[] snapshot = Convert.FromHexString(entry.Snapshot!.Bytes);
        var bytes = snapshot.AsSpan(f.DebtEnd, 8).ToArray();
        PutDate(bytes, 0, end);
        PutDate(bytes, 4, start);
        ulong address = entry.Address + (ulong)f.DebtEnd;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write debt dates at 0x{address:X}");
    }

    static void PutDate(byte[] bytes, int offset, DateOnly date)
    {
        ushort original = BitConverter.ToUInt16(bytes, offset);
        BitConverter.GetBytes((ushort)((original & 0xFE00) | date.DayOfYear)).CopyTo(bytes, offset);
        BitConverter.GetBytes((short)date.Year).CopyTo(bytes, offset + 2);
    }

    public void SetOriginalDebt(ulong club, ClubDebtEntry entry, int value)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (!f.DebtOriginalEditable || f.DebtOriginal < 0 || f.DebtOriginal > f.DebtSize - 4)
            throw new InvalidOperationException("Original debt is not verified for this build");
        RequireCurrent(club, entry);
        byte[] bytes = BitConverter.GetBytes(value);
        ulong address = entry.Address + (ulong)f.DebtOriginal;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write original debt at 0x{address:X}");
    }

    public void SetSource(ulong club, ClubDebtEntry entry, byte value)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (!f.DebtSourceEditable) throw new InvalidOperationException("Debt source is not verified for this build");
        if (f.DebtSource < 0 || f.DebtSource >= f.DebtSize || !SupportedSource(value) || !SupportedSource(entry.SourceCode))
            throw new ArgumentOutOfRangeException(nameof(value), "Debt source is not verified");
        RequireCurrent(club, entry);
        byte[] bytes = [value];
        ulong address = entry.Address + (ulong)f.DebtSource;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 1).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write debt source at 0x{address:X}");
    }

    public void SetPayments(ulong club, ClubDebtEntry entry, int repayment, int interest)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.MonthlyRepayment is not { } amount || f.MonthlyInterestRepayment is not { } interestOffset
            || amount < 0 || amount > f.DebtSize - 8 || interestOffset != amount + 4)
            throw new InvalidOperationException("Profile has no verified debt payment offsets");
        if (entry.MonthlyRepayment is null || entry.MonthlyInterestRepayment is null)
            throw new StaleObjectException("Debt snapshot is unavailable");
        RequireCurrent(club, entry);
        byte[] bytes = [.. BitConverter.GetBytes(repayment), .. BitConverter.GetBytes(interest)];
        ulong address = entry.Address + (ulong)amount;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write debt payments at 0x{address:X}");
    }

    public void SetInterestOnly(ulong club, ClubDebtEntry entry, bool value)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.DebtInterestOnly is not { } offset || offset < 0 || offset >= f.DebtSize || entry.InterestOnly is null)
            throw new InvalidOperationException("Interest-only value is unavailable");
        RequireCurrent(club, entry);
        byte[] bytes = [value ? (byte)1 : (byte)0];
        ulong address = entry.Address + (ulong)offset;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 1).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write debt interest-only flag at 0x{address:X}");
    }

    /// <summary>
    /// Drops every debt of the club by collapsing the pointer vector to empty. Records are left in memory; the game
    /// usually rebuilds the monthly repayment totals from this list.
    /// </summary>
    public void RemoveAll(ulong club)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        var debts = new ClubListReader(memory, profile, moduleBase).ReadDebts(club);
        if (debts.Count == 0) throw new StaleObjectException("The club has no debts to remove");
        if (debts[0].Snapshot is not { } snapshot || snapshot.Club != club)
            throw new StaleObjectException("Debt snapshot is unavailable");

        byte[] end = BitConverter.GetBytes(snapshot.Begin);
        ulong address = snapshot.Holder + (ulong)f.DebtsVector + 8;
        if (!memory.TryWrite(address, end) || !memory.Read(address, 8).AsSpan().SequenceEqual(end))
            throw new MemoryAccessException($"Could not empty the debt list at 0x{address:X}");
        if (new ClubListReader(memory, profile, moduleBase).ReadDebts(club).Count != 0)
            throw new MemoryAccessException("Debts are still listed after writing");
    }

    void RequireCurrent(ulong club, ClubDebtEntry entry)
    {
        if (entry.Snapshot is not { } original || original.Club != club)
            throw new StaleObjectException("Debt snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadDebts(club).FirstOrDefault(e => e.Address == entry.Address);
        if (current != entry) throw new StaleObjectException("The debt or its containing list changed since it was read");
    }
}
