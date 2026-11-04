using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public enum StaffMassField { CurrentAbility, PotentialAbility, HomeReputation, CurrentReputation, WorldReputation }
public sealed record StaffMassChange(StaffRecord Staff, StaffMassField Field, int Before, int After);

/// <summary>Plans individual staff field edits; writes and undo use the normal guarded staff writer.</summary>
public static class StaffMassEdit
{
    public static IReadOnlyList<StaffMassChange> Plan(IEnumerable<StaffRecord> staff, StaffMassField field, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        bool ability = field is StaffMassField.CurrentAbility or StaffMassField.PotentialAbility;
        int min = ability ? 1 : 0, max = ability ? 200 : 10000;
        var ids = new HashSet<int>();
        var changes = new List<StaffMassChange>();
        foreach (var person in staff)
        {
            if (person.Id <= 0 || !ids.Add(person.Id)) throw new ArgumentException("Staff must have unique positive IDs");
            int[] group = ability ? [person.CurrentAbility, person.PotentialAbility] : [person.HomeReputation, person.CurrentReputation, person.WorldReputation];
            if (group.Any(n => n < min || n > max)) throw new ArgumentException($"{person.DisplayName}: original values cannot be restored by the writer");
            int before = Read(person, field);
            int after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after < min || after > max) throw new ArgumentOutOfRangeException(nameof(value), after, $"{field} must be between {min} and {max}");
            if (before != after) changes.Add(new(person, field, before, after));
        }
        return changes;
    }

    public static void Apply(StaffMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.Before, change.After, onWritten);
    public static void Undo(StaffMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.After, change.Before, onWritten);

    static void Write(StaffMassChange change, StaffRepository repository, StaffWriter writer, int expected, int value, Action? onWritten)
    {
        var current = repository.Reload(change.Staff.Address);
        if (current is null || current.Id != change.Staff.Id || current.StaffAddress != change.Staff.StaffAddress || Read(current, change.Field) != expected)
            throw new StaleObjectException("Staff member or field changed since the batch was prepared");
        switch (change.Field)
        {
            case StaffMassField.CurrentAbility: writer.SetAbility(current, value, current.PotentialAbility); break;
            case StaffMassField.PotentialAbility: writer.SetAbility(current, current.CurrentAbility, value); break;
            case StaffMassField.HomeReputation: writer.SetReputation(current, value, current.CurrentReputation, current.WorldReputation); break;
            case StaffMassField.CurrentReputation: writer.SetReputation(current, current.HomeReputation, value, current.WorldReputation); break;
            case StaffMassField.WorldReputation: writer.SetReputation(current, current.HomeReputation, current.CurrentReputation, value); break;
            default: throw new ArgumentException("Invalid mass edit field");
        }
        onWritten?.Invoke();
    }

    static int Read(StaffRecord staff, StaffMassField field) => field switch
    {
        StaffMassField.CurrentAbility => staff.CurrentAbility,
        StaffMassField.PotentialAbility => staff.PotentialAbility,
        StaffMassField.HomeReputation => staff.HomeReputation,
        StaffMassField.CurrentReputation => staff.CurrentReputation,
        StaffMassField.WorldReputation => staff.WorldReputation,
        _ => throw new ArgumentException("Invalid mass edit field")
    };
}
