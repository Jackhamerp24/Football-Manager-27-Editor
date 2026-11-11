using System.Globalization;

namespace Fm27Editor.App;

/// <summary>Match both the current-session address and the unique ID, never a possibly duplicated name.</summary>
public static class StaffNavigation
{
    public static StaffRow? Find(IEnumerable<StaffRow> rows, ulong address, int id) =>
        rows.FirstOrDefault(row => row.Staff.Address == address && row.Staff.Id == id);
}

public static class StaffFilter
{
    public static IReadOnlyList<StaffRow> Apply(IEnumerable<StaffRow> rows, string foldedName, string? club, string? job,
        Fm27Editor.Core.Offsets.Gender? gender = null, bool excludeWomenClubs = false) =>
        rows.Where(row =>
            (gender is null || row.Staff.Gender == gender) &&
            (!excludeWomenClubs || row.Staff.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women) &&
            (foldedName.Length == 0
                || row.SearchName.Contains(foldedName, StringComparison.Ordinal)
                || row.Staff.Id.ToString(CultureInfo.InvariantCulture).Contains(foldedName, StringComparison.Ordinal)) &&
            (club is null || string.Equals(row.Club, club, StringComparison.OrdinalIgnoreCase)) &&
            (job is null || string.Equals(row.Job, job, StringComparison.OrdinalIgnoreCase))).ToArray();
}
