using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

public sealed record StaffTendency(ulong Holder, ulong Begin, ulong End, ulong Address, byte Type, byte Level);

public sealed class StaffTendencies(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxEntries = 256;

    /// <summary>
    /// FMRTE 27's Type list by code. Mapped on Diego Simeone (2/11/2026) by picking every entry for his first row and
    /// reading the type byte FMRTE wrote; the list runs in code order. Codes outside it (31, 96…) FMRTE shows as "Unknown (n)".
    /// </summary>
    public static readonly IReadOnlyList<(byte Code, string Name)> Types =
    [
        (1, "Sign domestic-based players"), (2, "Spend on youth signings"), (3, "Sign many youth players"), (4, "Sign youth players for first team"),
        (5, "Use budget on one player"), (6, "Has A Large Senior Squad"), (7, "Will Use Young Players In Low Priority Cup Matches"),
        (15, "Use young players in low priority cups"), (16, "Not select a substitute goalkeeper"), (17, "Will Make Early Tactical Changes"),
        (18, "Will use zonal marking for set pieces"), (19, "Fit players into preferred tactic"), (20, "Use target man"), (21, "Use counter-attacks"),
        (22, "Select domestic players for National Team"), (23, "Signs Lower League Players"), (24, "Sign lower league players"), (25, "Use loan market"),
        (27, "Hires Experienced Managers"), (33, "Hires Manager from Within Club"), (34, "Hires Young Managers"), (35, "Hires Former Players as Managers"),
        (36, "Hires Managers from same Nation as Club"), (37, "Hires Managers from same Nation as Chairman"), (38, "Rely on set pieces"),
        (39, "Hire Director of Football for first team transfers and contracts"), (40, "Hire Director of Football for youth team transfers and contracts"),
        (41, "Expect passing football"), (42, "Expect attacking football"), (48, "Gesture: Gets Crowd Going"), (49, "Gesture: Argues With Officials"),
        (50, "Gesture: Sits Quietly"), (51, "Gesture: Roams Technical Area"), (52, "Gesture: Sits In Stands"), (53, "Attire: Suit"), (54, "Attire: Tracksuit"),
        (55, "Attire: Casual"), (57, "Operate without an Assistant Manager"), (58, "Pay fees upfront"), (59, "Target high-profile signings"),
        (60, "Offer high Promotion wage percentage increases"), (61, "Offer High Promotion Wage Percentage Decreases"),
        (62, "Have a Willingness To Work In Lower Leagues"), (63, "Use non-first team goalkeeper for domestic cup games"), (64, "Is Frugal with Transfer Budget"),
        (65, "Uses Non First Team Keeper for Continental Cup Games"), (66, "Signs Players For Second Team"), (67, "Loans Out Hot Prospects"),
        (69, "Doesn't use Data Analysts"), (71, "Signs A Lot Of Youth Players"), (73, "Will sit back and defend a lead"), (75, "Allow creative freedom"),
        (76, "Will use man-marking"), (77, "Will use offside trap"), (78, "Will sit back and protect a lead"), (79, "Use substitutes"), (80, "Uses crosses"),
        (81, "Will look for underlaps"), (82, "Will look for overlaps"), (84, "Works the ball into the box"), (85, "Plays out of defence"),
        (87, "Will try to prevent crosses"), (89, "Will focus play down flanks"), (90, "Uses intense pressing"), (91, "Uses counter-pressing"),
        (92, "Sign a lot of young players to turn a profit on them"), (93, "Signs U22 Players For The First Team"), (94, "Influenced by supporters"),
    ];

    public static string TypeName(byte code) => Types.FirstOrDefault(t => t.Code == code).Name ?? $"Unknown ({code})";
    int HolderOffset => profile.Staff?.TendenciesHolder ?? throw new InvalidOperationException("Staff tendencies are unavailable for this build");
    void RequireStaff(StaffRecord s)
    {
        if (s.Address != s.StaffAddress + (ulong)s.Kind.StaffPart ||
            memory.ReadUInt64(s.Address) != moduleBase + s.Kind.PersonVtableRva ||
            memory.ReadUInt64(s.StaffAddress) != moduleBase + s.Kind.StaffVtableRva)
            throw new StaleObjectException("Staff record changed");
    }
    public IReadOnlyList<StaffTendency> Read(StaffRecord s)
    {
        RequireStaff(s);
        ulong holder = memory.ReadUInt64(s.StaffAddress + (ulong)HolderOffset);
        if (holder == 0) return [];
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8);
        if (begin == 0 && end == 0) return [];
        if (begin == 0 || end < begin || (end - begin) % 2 != 0 || (end - begin) / 2 > MaxEntries)
            throw new MemoryAccessException("Invalid staff tendencies list");
        byte[] bytes = memory.Read(begin, (int)(end - begin));
        return Enumerable.Range(0, bytes.Length / 2)
            .Select(i => new StaffTendency(holder, begin, end, begin + (ulong)(2 * i), bytes[2 * i], bytes[2 * i + 1])).ToArray();
    }
    public void SetLevel(StaffRecord s, StaffTendency row, int value)
    {
        if (value is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(value));
        Require(s, row);
        if (!memory.TryWrite(row.Address + 1, [(byte)value]) || memory.Read(row.Address + 1, 1)[0] != value)
            throw new MemoryAccessException("Could not write staff tendency");
    }

    /// <summary>Changes a row's Type like FMRTE 27 (only the type byte changes; the level stays).</summary>
    public void SetType(StaffRecord s, StaffTendency row, byte type)
    {
        if (!Types.Any(t => t.Code == type)) throw new ArgumentOutOfRangeException(nameof(type), "Not a type FMRTE offers");
        Require(s, row);
        if (!memory.TryWrite(row.Address, [type]) || memory.Read(row.Address, 1)[0] != type)
            throw new MemoryAccessException("Could not write staff tendency");
    }

    void Require(StaffRecord s, StaffTendency row)
    {
        RequireStaff(s);
        if (memory.ReadUInt64(s.StaffAddress + (ulong)HolderOffset) != row.Holder ||
            memory.ReadUInt64(row.Holder) != row.Begin || memory.ReadUInt64(row.Holder + 8) != row.End ||
            row.Begin == 0 || row.End < row.Begin || (row.End - row.Begin) % 2 != 0 || (row.End - row.Begin) / 2 > MaxEntries ||
            row.Address < row.Begin || row.Address >= row.End || (row.Address - row.Begin) % 2 != 0 ||
            !memory.Read(row.Address, 2).AsSpan().SequenceEqual(new byte[] { row.Type, row.Level }))
            throw new StaleObjectException("Staff tendency changed since it was read");
    }
}
