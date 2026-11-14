using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>The star: a pop-up table of the starred players (same columns and sorting as the Players table).</summary>
    void ShowFavourites()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = AppLanguage.Get("Common_Load_game_first"); return; }
        // One window only: another click brings the open one forward and shows the stars as they are now.
        if (favouritesWindow is { } open)
        {
            open.Refill();
            if (open.WindowState == Avalonia.Controls.WindowState.Minimized) open.WindowState = Avalonia.Controls.WindowState.Normal;
            open.Activate();
            return;
        }
        var players = rows.OfType<PlayerRecord>().ToList();
        if (FavoriteStore.Count == 0) status.Text = AppLanguage.Get("Ui_No_favourites_yet_Star_a_player_on_his_page");
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
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = AppLanguage.Get("Common_Load_game_first"); return; }
        try
        {
            // A player tab on screen comes first: the module bar may still point at another module (a player opened from a
            // club's squad keeps that module selected), and that module's screen would take the reload instead.
            var tab = playerTabs.FirstOrDefault(t => ReferenceEquals(t.Root, bodyHost.Content));
            if (tab?.Player is { } open)
            {
                if (backend.Players.Reload(open.Address) is not { } fresh) { status.Text = AppLanguage.Get("Ui_Player_no_longer_readable"); return; }
                fresh.GameDate = backend.GameDate;
                ReplaceRow(fresh);
                // The page shows values through the player's draft, which keeps the record it was made from; rebase it on
                // the fresh read (staged edits stay) or the page keeps showing what the game had when the tab was opened.
                if (drafts.TryGetValue(fresh.Address, out var draft)) drafts[fresh.Address] = draft.Rebase(fresh);
                tab.Player = fresh;
                selected = fresh;
                RenderEditor();
                status.Text = $"Reloaded {fresh.DisplayName}";
                return;
            }
            if (screens.Any(s => s.ReloadCurrent())) return;
            if (module is PlayersModule) { _ = LoadAsync(); return; }
            status.Text = AppLanguage.Get("Ui_Nothing_to_reload_here");
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }
}
