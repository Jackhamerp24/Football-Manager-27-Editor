using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    MassEditWindow? massEditWindow;

    internal EditorBackend Backend => backend;

    /// <summary>The rows the Players list is showing right now, filters included.</summary>
    internal IReadOnlyList<PlayerRecord> MassEditPlayers =>
        (grid.ItemsSource as System.Collections.IEnumerable)?.OfType<PlayerRecord>().ToArray() ?? [];

    internal IReadOnlyList<StaffRecord> MassEditStaff => staffScreen.Rows.Select(r => r.Staff).ToArray();

    internal IReadOnlyList<ClubEntry> MassEditClubs => clubScreen.Rows.Select(r => r.Club).ToArray();

    internal IReadOnlyList<NationRecord> MassEditNations => nationScreen.Rows.Select(r => r.Nation).ToArray();

    internal void OpenMassEdit()
    {
        if (massEditWindow is { } open) { open.Activate(); return; }
        massEditWindow = new MassEditWindow(this);
        massEditWindow.Show(this);
    }

    internal void MassEditClosed(MassEditWindow window)
    {
        if (ReferenceEquals(massEditWindow, window)) massEditWindow = null;
    }

    /// <summary>Reloads the screen the mass edit just changed, so the list shows the new values.</summary>
    internal async Task MassEditReloadAsync(string scope)
    {
        switch (scope)
        {
            case "Player": await LoadAsync(); break;
            case "Coach": await staffScreen.LoadAsync(); break;
            case "Club": await clubScreen.LoadAsync(); break;
            case "Nation": await nationScreen.LoadAsync(); break;
        }
    }
}
