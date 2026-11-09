using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly DispatcherTimer saveWatcher = new() { Interval = TimeSpan.FromSeconds(3) };
    PlayerListMark? watchedPlayerList;

    /// <summary>
    /// Every address in the game changes when a save is loaded, so what the editor read earlier is stale. The player list's
    /// start pointer tells a new save from the one read: when it changes, a small window asks the user to press Load Game.
    /// </summary>
    void StartSaveWatcher()
    {
        saveWatcher.Tick += (_, _) => CheckForNewSave();
        saveWatcher.Start();
    }

    PlayerListMark? ReadPlayerListMark()
    {
        try
        {
            var session = backend.Session;
            var profile = session.Profile;
            ulong root = session.Game.ModuleBase + profile.PlayerListRootRva;
            ulong begin = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListBeginOffset);
            ulong end = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListEndOffset);
            return begin == 0 ? null : new PlayerListMark(begin, end, session.Memory.ReadUInt64(begin));
        }
        catch { return null; }
    }

    Border? staleOverlay;
    readonly TextBlock staleHeading = new() { FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    /// <summary>The editor connected to a game that had no save loaded yet; the first save to appear needs a Load Game as well.</summary>
    bool noSaveAtConnect;

    /// <summary>
    /// Covers the page below the tabs once the game holds another save. The tabs stay, but nothing can be opened or edited
    /// until Load Game reads the new save, so a stale address can never be written.
    /// </summary>
    Control BuildStaleOverlay()
    {
        var load = new Button { Content = "Load Game", Classes = { "accent" }, Width = 130, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        load.Click += async (_, _) => await ReloadGameAsync();
        staleOverlay = new Border
        {
            Background = UiKit.WindowBg, IsVisible = false,
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 16, Children = { staleHeading, load } },
        };
        return staleOverlay;
    }

    /// <summary>True once the game's player list holds players; false in the main menu or before a save is loaded.</summary>
    bool SaveIsLoaded()
    {
        try
        {
            var session = backend.Session;
            var profile = session.Profile;
            ulong root = session.Game.ModuleBase + profile.PlayerListRootRva;
            ulong begin = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListBeginOffset);
            ulong end = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListEndOffset);
            return begin != 0 && end > begin;
        }
        catch { return false; }
    }

    void CheckForNewSave()
    {
        if (backend.State.Status != ConnectionStatus.Connected || busy) { watchedPlayerList = null; return; }
        if (!SaveIsLoaded())
        {
            // Back at the start screen (or the game dropped its save): the player list is empty, so what the editor shows belongs
            // to a save that is gone. Watching only the list pointer misses this, the game reuses the same list buffer.
            if (watchedPlayerList is not null && !backend.SaveChanged) MarkSaveChanged("The game has no save loaded");
            return;
        }
        if (ReadPlayerListMark() is not { } mark) return;
        if (backend.SaveChanged) staleHeading.Text = "A save was loaded in the game";   // the start screen was shown, a save is back
        if (noSaveAtConnect)
        {
            noSaveAtConnect = false; watchedPlayerList = mark;
            MarkSaveChanged("A save was loaded in the game");
            return;
        }
        if (watchedPlayerList is not { } before) { watchedPlayerList = mark; return; }
        if (!mark.IsAnotherSaveThan(before)) { watchedPlayerList = mark; return; }
        watchedPlayerList = mark;                    // one reminder per change
        MarkSaveChanged("A different save was loaded in the game");
    }

    void MarkSaveChanged(string heading)
    {
        backend.SaveChanged = true;
        staleHeading.Text = heading;
        if (staleOverlay is not null) staleOverlay.IsVisible = true;
        favouritesWindow?.Close();
        status.Text = "Press Load Game to read the new save";
    }

    /// <summary>A small modeless window with a message and buttons, in the same style as the other prompts.</summary>
    Window ShowNotice(string title, string message, params (string Label, bool Accent, Func<Task>? Click)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, Topmost = true, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 16,
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, row },
            },
        };
        foreach (var (label, accent, click) in buttons)
        {
            var button = new Button { Content = label, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (accent) button.Classes.Add("accent");
            button.Click += async (_, _) => { dialog.Close(); if (click is not null) await click(); };
            row.Children.Add(button);
        }
        dialog.Show(this);
        return dialog;
    }

    /// <summary>The game is running a supported build but no save is loaded (or not saved yet), so there is nothing to read.</summary>
    void ShowNoSaveNotice()
    {
        noSaveAtConnect = true;
        string build = backend.Session.Profile.Build;
        status.Text = "No save is loaded in the game yet";
        ShowNotice("Save not loaded",
            $"This game version ({build}) is supported, but no save is loaded yet. Load your save in the game, save it without exiting, then press Load Game again.",
            ("OK", true, null));
    }

    /// <summary>What the Load Game button does: forget every cached list and read the game again.</summary>
    async Task ReloadGameAsync()
    {
        unifiedCacheLoaded = false;
        unifiedClubs = [];
        unifiedNations = [];
        unifiedCompetitions = [];
        unifiedStaff = [];
        unifiedStadiums = [];
        unifiedAwards = [];
        unifiedReferees = [];
        personNames = null;
        unifiedCities = [];
        WorkPermitTable.Forget();
        status.Text = "Loading game…";
        watchedPlayerList = null;
        await ConnectAsync();
        if (backend.State.Status == ConnectionStatus.Connected && staleOverlay is not null) staleOverlay.IsVisible = false;
        _ = Task.Run(() => EnsureUnifiedCache());
    }
}

/// <summary>
/// What the game's player list looks like at one moment. A different save shows as another list start or another first player;
/// a list that only grew (new players generated while playing) is still the same save, one that shrank is not. The start
/// alone is not enough: the game may refill the same list buffer when another save is loaded.
/// </summary>
public readonly record struct PlayerListMark(ulong Begin, ulong End, ulong First)
{
    public bool IsAnotherSaveThan(PlayerListMark before) => Begin != before.Begin || First != before.First || End < before.End;
}
