using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// The contract a staff member has with a national team, apart from the one with his club (a person can hold both). It is
/// reached person + 0xB0 → holder + 0x10, the same appointment <see cref="NationStaff"/> reads, and is an ordinary full
/// contract: Wage, Type, Started, Expires and the rolling flag sit at the same offsets as in a club contract (Tuchel, England:
/// wage 128,291, type 1, started 2025-01-01, no expiry). It is therefore read and written through the offer-contract code the
/// club staff contract uses, with the extra guard that the appointment is still this person's.
/// </summary>
public sealed class NationStaffContracts(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int HolderOffset = 0xB0, AppointmentOffset = 0x10, PersonOffset = 8;

    public OfferContract? Read(StaffRecord staff)
    {
        ulong appointment = AppointmentOf(staff);
        return appointment == 0 ? null : new FutureTransferReader(memory, profile, moduleBase).ReadContract(appointment, 0);
    }

    public void Set(StaffRecord staff, OfferContract contract, OfferContractFields fields)
    {
        if (AppointmentOf(staff) != contract.Address)
            throw new StaleObjectException($"{staff.DisplayName}'s national contract changed since it was read");
        new FutureTransferWriter(memory, profile, moduleBase).SetContract(contract, fields);
    }

    /// <summary>Renew: the expiry moves <paramref name="years"/> years past the later of today and the current expiry.</summary>
    public void Renew(StaffRecord staff, OfferContract contract, int years, DateOnly today)
    {
        if (years is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(years), "Years must be 1 to 10");
        var started = contract.Started ?? throw new InvalidOperationException("The national contract has no start date");
        var from = contract.Expires is { } expiry && expiry > today ? expiry : today;
        Set(staff, contract, Fields(contract, started, from.AddYears(years)));
    }

    /// <summary>Expire: the contract ends the day before <paramref name="today"/>.</summary>
    public void Expire(StaffRecord staff, OfferContract contract, DateOnly today)
    {
        var started = contract.Started ?? throw new InvalidOperationException("The national contract has no start date");
        Set(staff, contract, Fields(contract, started <= today ? started : today.AddYears(-1), today.AddDays(-1)));
    }

    /// <summary>Moves the expiry by whole years (−1 / +1 Year); a contract with no expiry has nothing to move.</summary>
    public void ShiftExpiry(StaffRecord staff, OfferContract contract, int years)
    {
        var started = contract.Started ?? throw new InvalidOperationException("The national contract has no start date");
        var expiry = contract.Expires ?? throw new InvalidOperationException("The national contract has no expiry to move");
        Set(staff, contract, Fields(contract, started, expiry.AddYears(years)));
    }

    static OfferContractFields Fields(OfferContract c, DateOnly started, DateOnly expires) =>
        new(c.Wage, (ContractType)c.Type, c.Rolling, started, expires);

    ulong AppointmentOf(StaffRecord staff)
    {
        ulong holder = memory.ReadUInt64(staff.Address + HolderOffset);
        if (holder == 0) return 0;
        ulong appointment = memory.ReadUInt64(holder + AppointmentOffset);
        return appointment != 0 && memory.ReadUInt64(appointment + PersonOffset) == staff.Address ? appointment : 0;
    }
}
