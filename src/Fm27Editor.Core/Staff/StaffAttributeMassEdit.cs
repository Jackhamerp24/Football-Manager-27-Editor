using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record StaffAttributeMassChange(StaffRecord Staff, StaffAttribute Field, int Before, int After, byte BeforeRaw, byte AfterRaw);

public static class StaffAttributeMassEdit
{
    public static IReadOnlyList<StaffAttributeMassChange> Plan(IEnumerable<StaffRecord> staff, StaffAttribute attribute,
        MassEditMode mode, int value, StaffWriter writer)
    {
        if (!Enum.IsDefined(attribute) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        var ids = new HashSet<int>();
        var changes = new List<StaffAttributeMassChange>();
        foreach (var person in staff)
        {
            if (person.Id <= 0 || !ids.Add(person.Id)) throw new ArgumentException("Staff must have unique positive IDs");
            byte raw = writer.ReadAttributeRaw(person, attribute);
            int before = person.Attribute(attribute);
            int after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), after, "Attribute must be between 1 and 20");
            byte newRaw = before == after ? raw : writer.EncodeAttribute(attribute, after);
            if (raw != newRaw) changes.Add(new(person, attribute, before, after, raw, newRaw));
        }
        return changes;
    }

    public static void Apply(StaffAttributeMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.BeforeRaw, change.AfterRaw, onWritten);
    public static void Undo(StaffAttributeMassChange change, StaffRepository repository, StaffWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.AfterRaw, change.BeforeRaw, onWritten);

    static void Write(StaffAttributeMassChange change, StaffRepository repository, StaffWriter writer, byte expected, byte value, Action? onWritten)
    {
        var current = repository.Reload(change.Staff.Address);
        if (current is null || current.Id != change.Staff.Id || current.StaffAddress != change.Staff.StaffAddress)
            throw new StaleObjectException("Staff member changed since the batch was prepared");
        writer.SetAttributeRaw(current, change.Field, expected, value);
        onWritten?.Invoke();
    }
}
