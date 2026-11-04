using System.Globalization;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>Club → Staff table, as FMRTE 27's club Staff tab shows it.</summary>
public static class ClubStaffLabels
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int? Age(DateOnly? born, DateOnly? today)
    {
        if (born is not { } b || today is not { } t) return null;
        int age = t.Year - b.Year;
        return t < b.AddYears(age) ? age - 1 : age;
    }

    public static ClubListTableModel BuildTable(IEnumerable<StaffRecord> staff, DateOnly? today) => new(
        ClubSection.Staff,
        ClubFieldInfo.SectionTitle(ClubSection.Staff),
        HasReader: true,
        UsesTeamRoot: false,
        [new("Job", 1.3, IsStar: true), new("Name", 1.5, IsStar: true), new("Age", 44, RightAlign: true),
         new("Team", 1.3, IsStar: true), new("Nation", 1.2, IsStar: true), new("Current Ability", 110, RightAlign: true), new("PA", 50, RightAlign: true)],
        staff.Select(s => new ClubListRowModel(
            [new(s.JobCode is { } j ? CareerPlanLabels.Name(CareerPlanLabels.Jobs, j) : ""), new(s.DisplayName),
             new(Age(s.DateOfBirth, today)?.ToString(Inv) ?? "", RightAlign: true), new(s.TeamName ?? ""), new(s.Nationality?.DisplayName ?? ""),
             new(s.CurrentAbility.ToString(Inv), RightAlign: true), new(s.PotentialAbility.ToString(Inv), RightAlign: true)],
            [])).ToArray());
}
