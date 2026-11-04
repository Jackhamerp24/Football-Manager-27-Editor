using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

/// <summary>
/// Eight formation bytes on the staff part: Preferred, Second Preferred, Defensive, Attacking, then the same four Out of
/// Possession. FMRTE 27 on Simeone (2/11/2026) wrote each of the eight rows into its own byte and nothing else; picking every
/// entry of its formation list in the Attacking row gave <see cref="Codes"/> (the list runs in code order).
/// </summary>
public sealed class StaffFormations(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public static readonly IReadOnlyList<(byte Code, string Name)> Codes =
    [
        (0x00, "Formation Unset"), (0x03, "4-4-2"), (0x04, "4-3-3 DM Wide"), (0x07, "4-4-2 Diamond Narrow"), (0x16, "4-4-2 2DM"),
        (0x1B, "4-3-2-1 DM AM Narrow"), (0x1C, "4-2-4 DM Wide"), (0x1D, "4-2-3-1 DM AM Wide"), (0x1E, "5-3-2 DM WB"),
        (0x20, "4-1-4-1 DM"), (0x21, "4-4-1-1 2DM"), (0x23, "5-2-3 DM Wide"), (0x3E, "4-2-2-2 DM AM Narrow"),
        (0x3F, "5-2-2-1 DM AM"), (0x40, "5-2-1-2 DM AM"), (0x41, "3-2-5 DM 2AM Wide"), (0x42, "5-2-1-2 DM AM Narrow"),
        (0x43, "5-2-2-1 DM AM Narrow"), (0x44, "5-3-2 DM Wide"), (0x45, "5-3-2 DM Narrow"),
    ];
    int Offset => profile.Staff?.Formations ?? throw new InvalidOperationException("Staff formations are unavailable for this build");
    void RequireStaff(StaffRecord staff)
    {
        if (staff.Address != staff.StaffAddress + (ulong)staff.Kind.StaffPart ||
            memory.ReadUInt64(staff.Address) != moduleBase + staff.Kind.PersonVtableRva ||
            memory.ReadUInt64(staff.StaffAddress) != moduleBase + staff.Kind.StaffVtableRva)
            throw new StaleObjectException("Staff record changed");
    }
    public byte[] Read(StaffRecord staff)
    {
        RequireStaff(staff);
        return memory.Read(staff.StaffAddress + (ulong)Offset, 8);
    }
    public void Save(StaffRecord staff, byte[] snapshot, byte[] values)
    {
        if (snapshot.Length != 8 || values.Length != 8) throw new ArgumentException("Expected eight formation values");
        for (int i = 0; i < 8; i++)
            if (snapshot[i] != values[i] && !Codes.Any(c => c.Code == values[i]))
                throw new ArgumentException($"Formation code {values[i]} is not one FMRTE offers");
        RequireStaff(staff);
        ulong address = staff.StaffAddress + (ulong)Offset;
        if (!memory.Read(address, 8).AsSpan().SequenceEqual(snapshot)) throw new StaleObjectException("Staff formations changed since they were read");
        if (!memory.TryWrite(address, values) || !memory.Read(address, 8).AsSpan().SequenceEqual(values))
            throw new MemoryAccessException("Could not write staff formations");
    }
}
