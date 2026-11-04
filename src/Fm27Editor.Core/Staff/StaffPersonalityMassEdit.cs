using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record StaffPersonalityMassChange(StaffRecord Staff, StaffPersonality Field, int Before, int After);

public static class StaffPersonalityMassEdit
{
    public static IReadOnlyList<StaffPersonalityMassChange> Plan(IEnumerable<StaffRecord> staff, StaffPersonality field, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        var ids = new HashSet<int>();
        var changes = new List<StaffPersonalityMassChange>();
        foreach (var person in staff)
        {
            if (person.Id <= 0 || !ids.Add(person.Id)) throw new ArgumentException("Staff must have unique positive IDs");
            int before = person.Personality[(int)field];
            if (before is < 1 or > 20) throw new ArgumentException($"{person.DisplayName}: original {field} cannot be restored by the writer");
            int after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), after, "Personality must be between 1 and 20");
            if (before != after) changes.Add(new(person, field, before, after));
        }
        return changes;
    }

    public static void Apply(StaffPersonalityMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.Before, change.After, onWritten);
    public static void Undo(StaffPersonalityMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.After, change.Before, onWritten);

    static void Write(StaffPersonalityMassChange change, StaffRepository repository, StaffWriter writer, int expected, int value, Action? onWritten)
    {
        var current = repository.Reload(change.Staff.Address);
        if (current is null || current.Id != change.Staff.Id || current.StaffAddress != change.Staff.StaffAddress ||
            current.Personality[(int)change.Field] != expected)
            throw new StaleObjectException("Staff member or personality changed since the batch was prepared");
        writer.SetPersonality(current, change.Field, value);
        onWritten?.Invoke();
    }
}
