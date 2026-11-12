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
    SaveFingerprint? watchedSave;
    /// <summary>Consecutive checks that saw no save, or a different one; a single odd read while the game is processing a day is ignored.</summary>
    int emptyChecks, differentChecks;

    /// <summary>
    /// Every address in the game changes when a save is loaded, so what the editor read earlier is stale. A few player objects
    /// of the save that was read tell it from another save (see <see cref="SaveFingerprint"/>): when they are gone, the page
    /// asks the user to press Load Game.
    /// </summary>
    void StartSaveWatcher()
    {
        saveWatcher.Tick += (_, _) => CheckForNewSave();
        saveWatcher.Start();
    }

    /// <summary>The player id at <paramref name="address"/>, or null when it no longer holds a player object.</summary>
    int? PlayerIdAt(ulong address)
    {
        try
        {
            var session = backend.Session;
            var profile = session.Profile;
            if (profile.Fields.Id is not { } idOffset) return null;
            ulong vtable = session.Memory.ReadUInt64(address);
            if (!profile.PlayerVtableRvas.Any(r => vtable == session.Game.ModuleBase + r)) return null;
            return BitConverter.ToInt32(session.Memory.Read(address + (ulong)idOffset, 4));
        }
        catch (MemoryAccessException) { return null; }
    }

    /// <summary>The player pointers at the sample positions of the game's list (only those are read).</summary>
    SaveFingerprint? TakeFingerprint()
    {
        try
        {
            var session = backend.Session;
            var profile = session.Profile;
            ulong root = session.Game.ModuleBase + profile.PlayerListRootRva;
            ulong begin = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListBeginOffset);
            ulong end = session.Memory.ReadUInt64(root + (ulong)profile.PlayerListEndOffset);
            if (begin == 0 || end <= begin) return null;
            int count = (int)((end - begin) / 8);
            var list = new LazyPointerList(i => session.Memory.ReadUInt64(begin + (ulong)i * 8), count);
            return SaveFingerprint.Take(list, PlayerIdAt);
        }
        catch (MemoryAccessException) { return null; }
    }

    sealed class LazyPointerList(Func<int, ulong> at, int count) : IReadOnlyList<ulong>
    {
        public ulong this[int index] => at(index);
        public int Count => count;
        public IEnumerator<ulong> GetEnumerator() { for (int i = 0; i < count; i++) yield return at(i); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
        if (backend.State.Status != ConnectionStatus.Connected || busy) { watchedSave = null; emptyChecks = differentChecks = 0; return; }
        if (!SaveIsLoaded())
        {
            // Back at the start screen (or the game dropped its save): the player list is empty, so what the editor shows belongs
            // to a save that is gone. Seen twice in a row, so a list emptied for a moment while the game processes a day is not it.
            if (++emptyChecks >= 2 && watchedSave is not null && !backend.SaveChanged) MarkSaveChanged("The game has no save loaded");
            return;
        }
        emptyChecks = 0;
        // The game moves on while the editor is open; keep the date in the status bar current (it was only set at Load Game).
        if (!backend.SaveChanged && backend.GameDate is { } today) gameDateText.Text = today.ToString("dd MMM yyyy", Inv);
        if (backend.SaveChanged) staleHeading.Text = "A save was loaded in the game";   // the start screen was shown, a save is back
        if (noSaveAtConnect)
        {
            if (TakeFingerprint() is not { } first) return;
            noSaveAtConnect = false; watchedSave = first;
            MarkSaveChanged("A save was loaded in the game");
            return;
        }
        if (watchedSave is not { } read) { watchedSave = TakeFingerprint(); return; }
        if (backend.SaveChanged || read.StillHolds(PlayerIdAt)) { differentChecks = 0; return; }
        if (++differentChecks < 2) return;
        differentChecks = 0;
        watchedSave = TakeFingerprint();             // one reminder per change
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
        if (busy) return;
        unifiedCacheLoaded = false;
        unifiedCacheTask = null;
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
        watchedSave = null;
        await ConnectAsync();
        if (backend.State.Status == ConnectionStatus.Connected)
        {
            // Loading a save frees every object, so tabs opened against the old one would only show stale addresses.
            CloseDetailTab();
            drafts.Clear();
            playerScalarDrafts.Clear();
            clubDrafts.Clear();
            if (staleOverlay is not null) staleOverlay.IsVisible = false;
        }
    }
}

/// <summary>
/// A few players of the save the editor read, by object address and id. Playing on keeps those objects where they are: the
/// player list itself may grow, shrink (retirements) or move to a new buffer when the game adds regens, so the list's shape
/// says nothing about which save is loaded. Loading another save frees every object, so most samples stop being the same
/// player at the same address.
/// </summary>
public sealed record SaveFingerprint(IReadOnlyList<(ulong Address, int Id)> Samples)
{
    public const int Size = 8;

    /// <summary>
    /// Samples spread across the list. <paramref name="idOf"/> returns the player id at an address, or null when the address no
    /// longer holds a player object. Null when no sample could be read.
    /// </summary>
    public static SaveFingerprint? Take(IReadOnlyList<ulong> players, Func<ulong, int?> idOf)
    {
        var samples = new List<(ulong, int)>(Size);
        if (players.Count == 0) return null;
        for (int i = 0; i < Size; i++)
        {
            ulong address = players[(int)((long)i * players.Count / Size)];
            if (idOf(address) is { } id && !samples.Exists(s => s.Item1 == address)) samples.Add((address, id));
        }
        return samples.Count == 0 ? null : new(samples);
    }

    /// <summary>True while at least half of the samples are still the same player at the same address.</summary>
    public bool StillHolds(Func<ulong, int?> idOf) => Samples.Count(s => idOf(s.Address) == s.Id) * 2 >= Samples.Count;
}
