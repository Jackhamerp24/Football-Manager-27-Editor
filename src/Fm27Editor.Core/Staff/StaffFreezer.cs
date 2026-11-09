using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record FrozenStaffAttribute(StaffAttribute Attribute, byte Value, FreezeMode Mode);

public sealed class FrozenStaff
{
    readonly Dictionary<StaffAttribute, FrozenStaffAttribute> fields;
    public int Id { get; }
    public ulong Address { get; }
    public ulong StaffAddress { get; }
    public IReadOnlyCollection<FrozenStaffAttribute> Fields => fields.Values;

    public FrozenStaff(int id, ulong address, ulong staffAddress, IEnumerable<FrozenStaffAttribute> attributes)
    {
        if (id <= 0 || address == 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Address = address;
        StaffAddress = staffAddress;
        fields = attributes.ToDictionary(a => a.Attribute);
        if (fields.Count == 0 || fields.Values.Any(a => a.Value is < 1 or > 20 || !Enum.IsDefined(a.Attribute) || !Enum.IsDefined(a.Mode)))
            throw new ArgumentException("Invalid frozen attributes", nameof(attributes));
    }

    public static FrozenStaff Capture(StaffRecord staff, IEnumerable<(StaffAttribute Attribute, FreezeMode Mode)> attributes) =>
        new(staff.Id, staff.Address, staff.StaffAddress,
            attributes.Select(a => new FrozenStaffAttribute(a.Attribute, (byte)staff.Attribute(a.Attribute), a.Mode)));

    public static IEnumerable<(StaffAttribute Attribute, FreezeMode Mode)> AllAttributes(StaffRecord staff)
    {
        foreach (var attr in Enum.GetValues<StaffAttribute>())
        {
            if (staff.Attribute(attr) is >= 1 and <= 20)
                yield return (attr, FreezeMode.Fixed);
        }
    }

    /// <summary>A deliberate editor change becomes the new frozen value of that attribute; false when it was not frozen or is unchanged.</summary>
    public bool AcceptEdit(StaffRecord staff, StaffAttribute attribute)
    {
        if (!fields.TryGetValue(attribute, out var field)) return false;
        int value = staff.Attribute(attribute);
        if (value == field.Value || value is < 1 or > 20) return false;
        fields[attribute] = field with { Value = (byte)value };
        return true;
    }

    public int Reconcile(StaffRepository repo, StaffWriter writer)
    {
        var current = repo.Reload(Address);
        if (current is null || current.Id != Id) throw new StaleObjectException("Frozen staff changed or is no longer available");
        int writes = 0;
        foreach (var field in fields.Values.ToArray())
        {
            int value = current.Attribute(field.Attribute);
            if (field.Mode == FreezeMode.AllowIncrease && value > field.Value ||
                field.Mode == FreezeMode.AllowDecrease && value < field.Value)
            {
                fields[field.Attribute] = field with { Value = checked((byte)value) };
                continue;
            }
            if (value == field.Value) continue;
            writer.SetAttribute(current, field.Attribute, field.Value);
            writes++;
        }
        return writes;
    }
}
