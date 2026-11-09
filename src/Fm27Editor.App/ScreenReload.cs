using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>The star: a pop-up table of the starred players (same columns and sorting as the Players table).</summary>
    void ShowFavourites()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        // One window only: another click brings the open one forward and shows the stars as they are now.
        if (favouritesWindow is { } open)
        {
            open.Refill();
            if (open.WindowState == Avalonia.Controls.WindowState.Minimized) open.WindowState = Avalonia.Controls.WindowState.Normal;
            open.Activate();
            return;
        }
        var players = rows.OfType<PlayerRecord>().ToList();
        if (FavoriteStore.Count == 0) status.Text = "No favourites yet. Star a player on his page.";
        var window = new FavoritesWindow(players, PickPlayer);
        favouritesWindow = window;
        window.Closed += (_, _) => favouritesWindow = null;
        window.Show(this);
    }

    FavoritesWindow? favouritesWindow;

    /// <summary>
    /// Reload (Ctrl+R): reads the data of the tab on screen again from the game without loading the whole save again: the open
    /// player, the open club, staff member, nation, stadium, city..., or the list of the module showing. Unsaved edits stay.
    /// </summary>
    void ReloadCurrentScreen()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        try
        {
            if (screens.Any(s => s.ReloadCurrent())) return;
            var tab = playerTabs.FirstOrDefault(t => ReferenceEquals(t.Root, bodyHost.Content));
            if (tab?.Player is { } open)
            {
                if (backend.Players.Reload(open.Address) is not { } fresh) { status.Text = "Player no longer readable"; return; }
                fresh.GameDate = backend.GameDate;
                ReplaceRow(fresh);
                selected = fresh;
                RenderEditor();
                status.Text = $"Reloaded {fresh.DisplayName}";
                return;
            }
            if (module is PlayersModule) { _ = LoadAsync(); return; }
            status.Text = "Nothing to reload here";
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }
}
