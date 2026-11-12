using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Stadiums;

/// <summary>
/// Batch edits of the stadium fields a club's ground stores (capacity, pitch size). A club is linked to its ground the
/// same way the editor's stadium screen links the other way: every stadium points at the club that owns it
/// (<c>stadium.owner</c>), so the owner pointer is what maps a club to its stadium here.
/// </summary>
public sealed record StadiumMassChange(ClubEntry Club, ulong Stadium, StadiumField Field, long Before, long After);

public static class StadiumMassEdit
{
    public static bool IsSupported(OffsetProfile profile, StadiumField field) =>
        profile.Stadium?.Fields.ContainsKey(field) == true;

    public static IReadOnlyList<StadiumMassChange> Plan(IReadOnlyList<ClubEntry> clubs, IReadOnlyList<StadiumRecord> stadiums, StadiumField field,
        MassEditMode mode, long value, long minimum, long maximum, IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
        var offsets = profile.Stadium ?? throw new InvalidOperationException("Profile has no stadium offsets");
        if (!offsets.Fields.TryGetValue(field, out var o)) throw new InvalidOperationException($"{field} is not mapped in this profile");
        var writer = new StadiumWriter(memory, profile, moduleBase);
        var changes = new List<StadiumMassChange>();
        var ids = new HashSet<int>();
        foreach (var club in clubs)
        {
            if (club.Id is not > 0 || !ids.Add(club.Id.Value)) throw new ArgumentException("Clubs must have unique positive IDs");
            var stadium = stadiums.FirstOrDefault(s => s.Owner?.Address == club.Address);
            if (stadium is null) continue;
            Require(stadium.Address, memory, profile, moduleBase);
            if (!writer.ReadFields(stadium.Address).TryGetValue(field, out long before))
            {
                // An unticked International size has no value; setting one side alone would leave half a size.
                if (field is StadiumField.InternationalPitchLength or StadiumField.InternationalPitchWidth) continue;
                throw new StaleObjectException($"{club.DisplayName}: {field} is not readable");
            }
            long after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after < minimum || after > maximum)
                throw new ArgumentOutOfRangeException(nameof(value), after, $"{field} must be between {minimum} and {maximum}");
            _ = ClubFieldAccess.Encode(after, o);
            if (after != before) changes.Add(new(club, stadium.Address, field, before, after));
        }
        return changes;
    }

    public static void Apply(StadiumMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, Action? onWritten = null) =>
        Write(change, memory, profile, moduleBase, change.Before, change.After, onWritten);

    public static void Undo(StadiumMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, Action? onWritten = null) =>
        Write(change, memory, profile, moduleBase, change.After, change.Before, onWritten);

    static void Write(StadiumMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, long expected, long value, Action? onWritten)
    {
        var offsets = profile.Stadium ?? throw new InvalidOperationException("Profile has no stadium offsets");
        if (!offsets.Fields.ContainsKey(change.Field)) throw new InvalidOperationException("This field cannot be edited as a batch");
        Require(change.Stadium, memory, profile, moduleBase);
        new StadiumWriter(memory, profile, moduleBase).SetField(change.Stadium, change.Field, expected, value);
        onWritten?.Invoke();
    }

    static void Require(ulong stadium, IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
        var offsets = profile.Stadium ?? throw new InvalidOperationException("Profile has no stadium offsets");
        if (memory.ReadUInt64(stadium) != moduleBase + offsets.VtableRva) throw new StaleObjectException("Not a stadium object");
    }
}
