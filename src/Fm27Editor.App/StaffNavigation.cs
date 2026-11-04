namespace Fm27Editor.App;

/// <summary>Match both the current-session address and the unique ID, never a possibly duplicated name.</summary>
public static class StaffNavigation
{
    public static MainWindow.StaffRow? Find(IEnumerable<MainWindow.StaffRow> rows, ulong address, int id) =>
        rows.FirstOrDefault(row => row.Staff.Address == address && row.Staff.Id == id);
}

public static class StaffFilter
{
    public static IReadOnlyList<MainWindow.StaffRow> Apply(IEnumerable<MainWindow.StaffRow> rows, string foldedName, string? club, string? job) =>
        rows.Where(row => (foldedName.Length == 0 || row.SearchName.Contains(foldedName, StringComparison.Ordinal)) &&
            (club is null || string.Equals(row.Club, club, StringComparison.OrdinalIgnoreCase)) &&
            (job is null || string.Equals(row.Job, job, StringComparison.OrdinalIgnoreCase))).ToArray();
}
