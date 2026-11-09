namespace Fm27Editor.Core.Competitions;

/// <summary>
/// FMRTE 27's Substitution Rules dropdown: the byte at competition +0xFD for each option, in FMRTE's list order.
/// Measured 8/11/2026 on Premier League (ID 11): FMRTE saved each option in turn while a poller logged the byte (75 options,
/// every value distinct). 0x11 = "5 from 9" matches the earlier FMRTE write on the same competition.
/// </summary>
public static class SubstitutionRules
{
    public static readonly IReadOnlyList<(byte Value, string Label)> All =
    [
        (0xFF, "Do Not Override Sub Rules"), (0x13, "No Subs Allowed"), (0x02, "3 from 3"), (0x05, "3+Gk from 4"), (0x06, "3 from 4"),
        (0x01, "3 from 5"), (0x03, "3+Gk from 5"), (0x0D, "3 from 5 + 2 Extra Time Subs"), (0x0E, "3+Gk from 5 + 2 Extra Time Subs"),
        (0x0B, "3 from 5 + 1 Extra Time Sub"), (0x0C, "3+Gk from 5 + 1 Extra Time Sub"), (0x07, "3 from 6"), (0x0F, "3+Gk from 6"),
        (0x1B, "3 from 5+Gk"), (0x04, "3+Gk from 7"), (0x00, "3 from 7"), (0x34, "3 from 7 + 2 Extra Time Subs"),
        (0x4C, "3 from 7 (max of 3 stoppages) + 1 Extra Time Sub"), (0x16, "3+Gk from 7(home), 5(away)"), (0x2C, "3 from 7 + 1 Extra Time Sub"),
        (0x4D, "3 from 7 (max of 3 stoppages)"), (0x0A, "3 from 9"), (0x22, "3+Gk from 9"), (0x2B, "3 from 9 + 1 Extra Time Sub"),
        (0x29, "3 from 10"), (0x19, "3 from 11"), (0x08, "3 from 12"), (0x2D, "3 from 12 + 1 Extra Time Sub"), (0x1D, "4+Gk from 4"),
        (0x15, "4 from 7"), (0x1A, "4 from 9"), (0x36, "4 from 12 (max of 3 stoppages)"), (0x32, "4 from 12"), (0x17, "5 from 5"),
        (0x47, "5 from 6 (max of 3 stoppages)"), (0x48, "5 from 6"), (0x20, "5+Gk from 7"), (0x14, "5 from 7"), (0x33, "5 from 7 + 1 Extra Time Sub"),
        (0x38, "5 from 7 (max of 3 stoppages)"), (0x3E, "5 from 7 (max of 3 stoppages) + 1 Extra Time Sub"), (0x10, "5 from 8"), (0x11, "5 from 9"),
        (0x31, "5 from 9 + 1 Extra Time Sub"), (0x3A, "5 from 9 (max of 3 stoppages)"), (0x3F, "5 from 9 (max of 3 stoppages) + 1 Extra Time Sub"),
        (0x42, "5 from 10"), (0x3C, "5 from 10 (max of 3 stoppages) + 1 Extra Time Sub"), (0x44, "5 from 10 + 1 Extra Time Sub"),
        (0x3B, "5 from 10 (max of 3 stoppages)"), (0x40, "5 from 11"), (0x46, "5 from 11 + 1 Extra Time Sub"), (0x45, "5 from 11 (max of 3 stoppages)"),
        (0x3D, "5 from 12 (max of 3 stoppages)"), (0x30, "5 from 12"), (0x39, "5 from 12 plus 1 Extra Time Sub"),
        (0x43, "5 from 12 (max of 3 stoppages) + 1 Extra Time Sub"), (0x49, "5 from 15 (max of 3 stoppages)"), (0x4A, "5 from 15"),
        (0x4B, "5 from 15 (max of 3 stoppages) + 1 Extra Time Sub"), (0x1E, "6 from 7"), (0x37, "3 from 9 plus 3 Half-Time Subs"), (0x18, "6 from 9"),
        (0x2E, "6 from 11"), (0x21, "6 from 12"), (0x09, "7 from 7"), (0x2F, "7 from 9"), (0x23, "7 from 12"), (0x24, "8 from 12"), (0x25, "9 from 12"),
        (0x1C, "All from 10 (home), 12(away)"), (0x41, "10 from 11"), (0x26, "10 from 12"), (0x27, "11 from 12"), (0x28, "12 from 12"), (0x12, "15 from 15"),
    ];

    public static string? LabelOf(byte value) { foreach (var (v, l) in All) if (v == value) return l; return null; }
}
