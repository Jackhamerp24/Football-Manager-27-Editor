using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record StaffWageChange(StaffRecord Staff, ulong Contract, int Before, int After);
public sealed record StaffWagePlan(IReadOnlyList<StaffWageChange> Changes, int Unavailable);

public static class StaffWageMassEdit
{
    public static StaffWagePlan Plan(IEnumerable<StaffRecord> staff, StaffContractAccess contracts, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit mode");
        if (mode == MassEditMode.Set && value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Wage cannot be negative");
        var changes = new List<StaffWageChange>();
        var ids = new HashSet<int>();
        int unavailable = 0;
        foreach (var person in staff)
        {
            if (person.Id <= 0 || !ids.Add(person.Id)) throw new ArgumentException("Staff must have unique positive IDs");
            var contract = contracts.Read(person);
            if (contract is null) { unavailable++; continue; }
            int before = contract.Wage;
            int after = mode == MassEditMode.Set ? value : checked(before + value);
            if (before < 0 || after < 0) throw new ArgumentOutOfRangeException(nameof(value), after, "Wage cannot be negative");
            if (before != after) changes.Add(new(person, contract.Address, before, after));
        }
        return new(changes, unavailable);
    }

    public static void Apply(StaffWageChange change, StaffRepository repository, StaffContractAccess contracts, Action? onWritten = null) =>
        Write(change, repository, contracts, change.Before, change.After, onWritten);
    public static void Undo(StaffWageChange change, StaffRepository repository, StaffContractAccess contracts, Action? onWritten = null) =>
        Write(change, repository, contracts, change.After, change.Before, onWritten);

    static void Write(StaffWageChange change, StaffRepository repository, StaffContractAccess contracts, int expected, int value, Action? onWritten)
    {
        var person = repository.Reload(change.Staff.Address);
        if (person is null || person.Id != change.Staff.Id || person.StaffAddress != change.Staff.StaffAddress)
            throw new StaleObjectException("Staff identity changed");
        var contract = contracts.Read(person);
        if (contract is null || contract.Address != change.Contract || contract.Wage != expected)
            throw new StaleObjectException("Staff contract or wage changed since preview");
        contracts.SetWage(person, contract, value);
        onWritten?.Invoke();
    }
}
