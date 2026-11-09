using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

public enum CurrentLoanStatus { Present, Absent, Unavailable }

/// <summary>Read-only inspection; an unavailable structure must not be treated as no loan.</summary>
public sealed record LoanAmounts(int AgreedPrice, int BuyBackFee, int MonthlyFee, int UnusedMonthlyFee);
public sealed record CurrentLoanInspection(CurrentLoanStatus Status, OfferContract? Contract, ulong BorrowingTeamAddress, LoanAmounts? Amounts = null, DateOnly? Signed = null);

public sealed class CurrentLoanReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public CurrentLoanInspection Read(PlayerRecord player)
    {
        var missing = new CurrentLoanInspection(CurrentLoanStatus.Unavailable, null, 0);
        if (profile.FutureTransfers is not { CurrentLoanHolder: { } offset } ||
            !Q(player.Address, out var vt) || !profile.PlayerVtableRvas.Any(x => vt == moduleBase + x) ||
            !Q(player.Address + (ulong)offset, out var holder)) return missing;
        if (holder == 0) return new(CurrentLoanStatus.Absent, null, 0);
        if (!Q(holder, out var address) || address == 0) return missing;
        var contract = new FutureTransferReader(memory, profile, moduleBase).ReadContract(address, 0);
        var bytes = new byte[0xD4];
        if (contract is not { Loan: true } || !Q(address + 0x10, out var team) || team == 0 ||
            !memory.TryRead(address, bytes) || !bytes.AsSpan(0, Convert.FromHexString(contract.Snapshot).Length).SequenceEqual(Convert.FromHexString(contract.Snapshot)) ||
            !Q(player.Address + (ulong)offset, out var holderAfter) || holderAfter != holder ||
            !Q(holder, out var addressAfter) || addressAfter != address) return missing;
        int agreed = BitConverter.ToInt32(bytes, 0xC0), buyBack = BitConverter.ToInt32(bytes, 0xC8);
        return new(CurrentLoanStatus.Present, contract, team, new(agreed, buyBack,
            BitConverter.ToInt32(bytes, 0xCC), BitConverter.ToInt32(bytes, 0xD0)), FutureTransferReader.DateAt(bytes, 0x4C));
    }

    bool Q(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
}
