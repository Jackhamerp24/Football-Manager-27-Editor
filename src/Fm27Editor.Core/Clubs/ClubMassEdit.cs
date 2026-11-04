using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

public sealed record ClubMassChange(ClubEntry Club, ClubField Field, long Before, long After, IReadOnlyList<ulong> Owners);

/// <summary>Plans reversible edits of integer fields whose stored value has no rounding or bit mask.</summary>
public static class ClubMassEdit
{
    public static bool IsSupported(OffsetProfile profile, ClubField field) =>
        profile.Club?.Fields?.TryGetValue(field, out var offset) == true && offset.Root == ClubFieldRoot.Club &&
        offset.Scale == 1 && offset.Mask == 0 && offset.Type is ClubFieldType.Byte or ClubFieldType.SByte or ClubFieldType.Int16 or ClubFieldType.Int32;

    public static IReadOnlyList<ClubMassChange> Plan(IEnumerable<ClubEntry> clubs, ClubField field, MassEditMode mode,
        long value, long minimum, long maximum, IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
        if (!IsSupported(profile, field) || !Enum.IsDefined(mode) || minimum > maximum) throw new ArgumentException("Invalid mass edit field or mode");
        var offset = profile.Club!.Fields![field];
        var access = new ClubFieldAccess(memory, profile, moduleBase);
        var ids = new HashSet<int>();
        var changes = new List<ClubMassChange>();
        foreach (var club in clubs)
        {
            if (club.Id is not > 0 || !ids.Add(club.Id.Value)) throw new ArgumentException("Clubs must have unique positive IDs");
            RequireClub(club, memory, profile, moduleBase);
            var owners = ReadOwners(club.Address, offset, memory);
            long before = access.Read(club.Address, field) ?? throw new StaleObjectException($"{club.DisplayName}: {field} is not readable");
            if (!owners.SequenceEqual(ReadOwners(club.Address, offset, memory))) throw new StaleObjectException($"{field}: pointer path changed");
            long after = mode == MassEditMode.Set ? value : checked(before + value);
            if (after < minimum || after > maximum) throw new ArgumentOutOfRangeException(nameof(value), after, $"{field} must be between {minimum} and {maximum}");
            _ = ClubFieldAccess.Encode(after, offset);
            if (after != before) changes.Add(new(club, field, before, after, owners));
        }
        return changes;
    }

    public static void Apply(ClubMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, Action? onWritten = null) =>
        Write(change, memory, profile, moduleBase, change.Before, change.After, onWritten);
    public static void Undo(ClubMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, Action? onWritten = null) =>
        Write(change, memory, profile, moduleBase, change.After, change.Before, onWritten);

    static void Write(ClubMassChange change, IProcessMemory memory, OffsetProfile profile, ulong moduleBase, long expected, long value, Action? onWritten)
    {
        if (!IsSupported(profile, change.Field)) throw new InvalidOperationException("This field cannot be edited as a batch");
        RequireClub(change.Club, memory, profile, moduleBase);
        if (!change.Owners.SequenceEqual(ReadOwners(change.Club.Address, profile.Club!.Fields![change.Field], memory)))
            throw new StaleObjectException($"{change.Field}: pointer path changed");
        new ClubFieldAccess(memory, profile, moduleBase).Set(change.Club.Address, change.Field, expected, value);
        onWritten?.Invoke();
    }

    static ulong[] ReadOwners(ulong root, ClubFieldOffset offset, IProcessMemory memory)
    {
        var owners = new List<ulong>();
        ulong current = root;
        for (int i = 0; i < offset.Path.Count - 1; i++)
        {
            current = memory.ReadUInt64(current + (ulong)offset.Path[i]);
            if (current == 0) throw new StaleObjectException($"Field path {offset.PathText} is broken");
            owners.Add(current);
        }
        return owners.ToArray();
    }

    static void RequireClub(ClubEntry club, IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
    {
        var offsets = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
        int idOffset = offsets.Id ?? throw new InvalidOperationException("Profile has no club ID");
        if (club.Id is not > 0 || memory.ReadUInt64(club.Address) != moduleBase + offsets.ClubVtableRva ||
            BitConverter.ToInt32(memory.Read(club.Address + (ulong)idOffset, 4)) != club.Id)
            throw new StaleObjectException("Club changed since the batch was prepared");
    }
}
