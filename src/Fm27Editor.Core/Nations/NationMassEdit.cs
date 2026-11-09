using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

public sealed record NationMassChange(NationRecord Nation, NationField Field, long Before, long After);

/// <summary>
/// Bulk edits of one nation field; every write goes through <see cref="NationFieldAccess"/>'s vtable and old-value
/// guards, so a nation that changed since the plan was made is refused instead of overwritten.
/// </summary>
public static class NationMassEdit
{
    public static IReadOnlyList<NationMassChange> Plan(IEnumerable<NationRecord> nations, NationFieldAccess access, NationField field, MassEditMode mode, long value)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        var (minimum, maximum) = NationFieldInfo.Range(field);
        var seen = new HashSet<ulong>();
        var changes = new List<NationMassChange>();
        foreach (var nation in nations)
        {
            if (nation.Address == 0 || !seen.Add(nation.Address)) throw new ArgumentException("Nations must be unique and valid");
            long before = access.Read(nation.Address, field) ?? throw new StaleObjectException($"{nation.DisplayName}: {field} is not readable");
            long after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after < minimum || after > maximum)
                throw new ArgumentOutOfRangeException(nameof(value), after, $"{field} must be between {minimum} and {maximum}");
            if (before != after) changes.Add(new(nation, field, before, after));
        }
        return changes;
    }

    public static void Apply(NationMassChange change, NationFieldAccess access) => access.Set(change.Nation.Address, change.Field, change.Before, change.After);

    public static void Undo(NationMassChange change, NationFieldAccess access) => access.Set(change.Nation.Address, change.Field, change.After, change.Before);
}
