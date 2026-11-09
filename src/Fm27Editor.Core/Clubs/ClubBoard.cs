using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Clubs;

public sealed record BoardMember(StaffRecord Staff, string Role);

/// <summary>
/// The board and executive roles of a club, taken from the staff list's job codes. FMRTE 27 labels these jobs in its
/// Staff tab; the codes come from the game's job catalogue (Chairperson 4, Director 6, Managing Director 8, Director of
/// Football 10, General Manager 24, Owner 66, President 70, Technical Director 88, Under 18s Technical Director 89).
/// Reading needs no new offsets: it is the staff module filtered by club and job.
/// </summary>
public static class ClubBoard
{
    /// <summary>Board roles in display order.</summary>
    public static readonly IReadOnlyList<(short Code, string Role)> Roles =
    [
        (66, "Owner"), (70, "President"), (4, "Chairperson"), (8, "Managing Director"), (6, "Director"),
        (10, "Director of Football"), (88, "Technical Director"), (89, "Under 18s Technical Director"),
        (24, "General Manager"),
    ];

    public static bool IsBoard(short? jobCode) => jobCode is { } code && Roles.Any(r => r.Code == code);

    /// <summary>Role label, or an empty string when the job is not a board job.</summary>
    public static string RoleName(short? jobCode) =>
        jobCode is { } code ? Roles.FirstOrDefault(r => r.Code == code).Role ?? "" : "";

    /// <summary>Board members of one club, in <see cref="Roles"/> order then by name.</summary>
    public static IReadOnlyList<BoardMember> Members(IEnumerable<StaffRecord> staff, ulong clubAddress)
    {
        var order = Roles.Select((r, i) => (r.Code, Index: i)).ToDictionary(x => x.Code, x => x.Index);
        return staff
            .Where(s => s.Club?.Address == clubAddress && IsBoard(s.JobCode))
            .Select(s => new BoardMember(s, RoleName(s.JobCode)))
            .OrderBy(m => order[m.Staff.JobCode!.Value])
            .ThenBy(m => m.Staff.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
