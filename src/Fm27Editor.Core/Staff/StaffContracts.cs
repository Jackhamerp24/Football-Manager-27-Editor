using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

/// <summary>
/// FMRTE 27's staff Contract tab (Type, Wages, Started, Expires, On Rolling Contract). The staff job record at person +
/// <see cref="StaffOffsets.Job"/> is an ordinary full contract (vtable +0x4B00AE8), the same object a player's contract is;
/// Mikel Arteta read £375,000, Full Time, 13/7/2026 – 30/6/2030 as FMRTE showed (2/11/2026). Staff without a club have none.
/// </summary>
public sealed class StaffContractAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("Profile has no staff offsets");

    public OfferContract? Read(StaffRecord staff)
    {
        if (!IsStaff(staff)) return null;
        ulong job = memory.ReadUInt64(staff.Address + (ulong)S.Job);
        if (job == 0) return null;
        if (profile.FutureTransfers is not null) return new FutureTransferReader(memory, profile, moduleBase).ReadContract(job, 0);
        if (profile.Contract is not { Type: { } type, Started: { } started } c ||
            S.ContractRolling is not { } rolling || S.ContractRollingMask is not { } mask) return null;
        var b = new byte[Math.Max(type + 1, Math.Max(c.ExpiryDate + 4, started + 4))];
        if (!memory.TryRead(job, b) || BitConverter.ToUInt64(b) != moduleBase + c.ContractVtableRva ||
            BitConverter.ToUInt64(b, S.JobPerson) != staff.Address) return null;
        return new OfferContract(job, 0, false, BitConverter.ToInt32(b, c.Wage), b[type], (b[rolling] & mask) != 0,
            FutureTransferReader.DateAt(b, started), FutureTransferReader.DateAt(b, c.ExpiryDate), Convert.ToHexString(b));
    }

    public void Set(StaffRecord staff, OfferContract contract, OfferContractFields fields)
    {
        if (!IsStaff(staff) || memory.ReadUInt64(staff.Address + (ulong)S.Job) != contract.Address ||
            (staff.Id > 0 && (memory.ReadUInt64(staff.Address) != moduleBase + staff.Kind.PersonVtableRva ||
            memory.ReadUInt64(staff.StaffAddress) != moduleBase + staff.Kind.StaffVtableRva ||
            staff.Address != staff.StaffAddress + (ulong)staff.Kind.StaffPart ||
            BitConverter.ToInt32(memory.Read(staff.Address + (ulong)S.Id, 4)) != staff.Id ||
            profile.Contract is not { } c || memory.ReadUInt64(contract.Address) != moduleBase + c.ContractVtableRva ||
            memory.ReadUInt64(contract.Address + (ulong)S.JobPerson) != staff.Address)))
            throw new StaleObjectException($"{staff.DisplayName}'s contract changed since it was read");
        new FutureTransferWriter(memory, profile, moduleBase).SetContract(contract, fields);
    }

    /// <summary>Writes only the wage, checking the staff, contract owner and the captured wage.</summary>
    public void SetWage(StaffRecord staff, OfferContract contract, int wage)
    {
        if (wage < 0) throw new ArgumentOutOfRangeException(nameof(wage));
        var c = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
        var snapshot = Convert.FromHexString(contract.Snapshot);
        if (staff.Id <= 0 || !S.Kinds.Contains(staff.Kind) || staff.Address != staff.StaffAddress + (ulong)staff.Kind.StaffPart ||
            memory.ReadUInt64(staff.Address) != moduleBase + staff.Kind.PersonVtableRva ||
            memory.ReadUInt64(staff.StaffAddress) != moduleBase + staff.Kind.StaffVtableRva ||
            BitConverter.ToInt32(memory.Read(staff.Address + (ulong)S.Id, 4)) != staff.Id ||
            memory.ReadUInt64(staff.Address + (ulong)S.Job) != contract.Address ||
            memory.ReadUInt64(contract.Address) != moduleBase + c.ContractVtableRva ||
            memory.ReadUInt64(contract.Address + (ulong)S.JobPerson) != staff.Address ||
            c.Wage < 0 || snapshot.Length < c.Wage + 4 || BitConverter.ToInt32(snapshot, c.Wage) != contract.Wage)
            throw new StaleObjectException($"{staff.DisplayName}'s contract changed since it was read");
        ulong address = contract.Address + (ulong)c.Wage;
        var before = BitConverter.GetBytes(contract.Wage);
        var after = BitConverter.GetBytes(wage);
        if (!memory.Read(address, 4).AsSpan().SequenceEqual(before)) throw new StaleObjectException("Wage changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException("Could not write staff wage");
    }

    bool IsStaff(StaffRecord staff)
    {
        ulong vt = memory.ReadUInt64(staff.Address);
        return S.Kinds.Any(k => vt == moduleBase + k.PersonVtableRva);
    }
}
