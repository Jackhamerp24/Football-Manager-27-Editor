using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    Window? hookManagerWindow;

    /// <summary>One club, nation or continent the Wonderkid Academy can be limited to. Search also matches the short name.</summary>
    sealed record HookTarget(int Id, string Name, string Search)
    {
        public bool Matches(string query) =>
            PickerSearch.Hit(query, Name, Search);
    }

    /// <summary>The players of one team set to one speed; the row's box edits them all, Remove takes them out.</summary>
    sealed record SpeedGroup(string Label, double Multiplier, IReadOnlyList<PlayerRecord> Players);

    /// <summary>The hook that was active before an Add/Clear, and the lists as they were, for Undo.</summary>
    sealed record AcademySnapshot(bool Installed, GoldenGenerationScope Scope, IReadOnlyList<int> Ids,
        int MinPa, int MaxPa, int Wonderkid, Dictionary<GoldenGenerationScope, List<HookTarget>> Drafts);

    /// <summary>
    /// Hook Manager: the code hooks the editor has put into the running game (Wonderkid Academy, Development Speed) and a way
    /// to take each one out. Hooks live in the game, so they keep working with the editor closed and are gone when the game
    /// exits. The window opens at once; the hook state and the name lists are read in the background.
    /// </summary>
    void OpenHookManager()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        if (hookManagerWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var session = backend.Session;
        var academy = session.GoldenGenerationHook;
        var accel = session.AcceleratedDevelopment;
        var players = rows.OfType<PlayerRecord>().ToArray();

        var academyState = new TextBlock { FontSize = 12.5, Text = "Reading…" };
        var activeText = new TextBlock { FontSize = 12.5, Text = "Active: reading…", TextWrapping = TextWrapping.Wrap };
        NumericUpDown Num(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 110, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
        var minPa = Num(1, 200, 150);
        var maxPa = Num(1, 200, 190);
        var wonderkid = Num(0, 100, 0);
        var scopes = new[] { (GoldenGenerationScope.All, "Every regen"), (GoldenGenerationScope.Club, "Clubs"), (GoldenGenerationScope.Nation, "Nations"), (GoldenGenerationScope.Continent, "Continents") };
        var scope = new ComboBox { ItemsSource = scopes.Select(s => s.Item2).ToList(), SelectedIndex = 0, Width = 150 };
        var picker = new TextBox
        {
            Width = 300, Height = 24, MinHeight = 24, FontSize = 12.5, IsReadOnly = true,
            Watermark = "Pick a club…", Cursor = new Cursor(StandardCursorType.Hand),
        };
        var add = new Button { Content = "Add", Height = 24, MinHeight = 24, Padding = new Thickness(12, 0), FontSize = 12.5 };
        var undo = new Button { Content = "Undo", Height = 24, MinHeight = 24, Padding = new Thickness(10, 0), FontSize = 12.5, IsEnabled = false };
        var targets = new StackPanel { Spacing = 4 };
        var accelPanel = new StackPanel { Spacing = 6 };
        var accelHits = new TextBlock { FontSize = 12.5 };
        var drafts = new Dictionary<GoldenGenerationScope, List<HookTarget>>
        {
            [GoldenGenerationScope.Club] = [],
            [GoldenGenerationScope.Nation] = [],
            [GoldenGenerationScope.Continent] = [],
        };
        var history = new List<AcademySnapshot>();
        HookTarget? picked = null;
        GoldenGenerationSettings? installed = null;
        bool syncing = false, firstSync = true, scopeTouched = false;
        Window? window = null;
        Dictionary<GoldenGenerationScope, IReadOnlyList<HookTarget>> catalogues = new();

        GoldenGenerationScope CurrentScope() => scopes[Math.Max(0, scope.SelectedIndex)].Item1;
        string ScopeLabel(GoldenGenerationScope sc) => scopes.First(s => s.Item1 == sc).Item2;

        // "Man Utd (First team): 32 players", or "Kylian Mbappé — Real Madrid CF (First team)" for one player.
        List<SpeedGroup> SpeedGroups(IReadOnlyList<AcceleratedPlayer> sped)
        {
            var byAddress = players.ToDictionary(p => p.Address);
            var teamOf = new Dictionary<ulong, string>();
            foreach (var clubAddress in sped.Select(r => byAddress.GetValueOrDefault(r.Address)?.Club?.Address ?? 0).Where(a => a != 0).Distinct())
            {
                var club = clubCache?.FirstOrDefault(c => c.Address == clubAddress);
                if (club is null) continue;
                var ordered = ClubCatalog.OrderedTeams(club);
                for (int i = 0; i < ordered.Count; i++)
                    foreach (var member in session.ReadSquad(ordered[i].Address))
                        teamOf.TryAdd(member.Address, $"{club.DisplayName} ({SquadTeamLabel(ordered, i)})");
            }
            var groups = new List<SpeedGroup>();
            foreach (var g in sped.GroupBy(r => (Team: teamOf.GetValueOrDefault(r.Address) ?? byAddress.GetValueOrDefault(r.Address)?.ClubName ?? "", r.Multiplier))
                         .OrderBy(g => g.Key.Team).ThenByDescending(g => g.Key.Multiplier))
            {
                string label;
                if (g.Count() == 1)
                {
                    var one = g.First();
                    string name = byAddress.GetValueOrDefault(one.Address)?.DisplayName ?? $"player id {one.PlayerId}";
                    label = g.Key.Team.Length > 0 ? $"{name} — {g.Key.Team}" : name;
                }
                else label = $"{(g.Key.Team.Length > 0 ? g.Key.Team : "Other players")}: {g.Count()} players";
                var members = g.Select(r => byAddress.GetValueOrDefault(r.Address)).OfType<PlayerRecord>().ToList();
                groups.Add(new SpeedGroup(label, g.Key.Multiplier, members));
            }
            return groups;
        }

        string Placeholder() => CurrentScope() switch
        {
            GoldenGenerationScope.Club => "Pick a club…",
            GoldenGenerationScope.Nation => "Pick a nation…",
            GoldenGenerationScope.Continent => "Pick a continent…",
            _ => "",
        };

        void UpdateActive()
        {
            var s = installed;
            if (s is not { Enabled: true }) { activeText.Text = "Active: none"; return; }
            if (s.Scope == GoldenGenerationScope.All) { activeText.Text = "Active: every regen"; return; }
            var names = catalogues.GetValueOrDefault(s.Scope);
            var text = string.Join(", ", s.Ids.Select(id => names?.FirstOrDefault(t => t.Id == id)?.Name ?? $"id {id}"));
            activeText.Text = $"Active: {ScopeLabel(s.Scope)} — {text}";
        }

        void ShowTargets()
        {
            targets.Children.Clear();
            var sc = CurrentScope();
            if (sc == GoldenGenerationScope.All)
            {
                if (installed is { Enabled: true, Scope: GoldenGenerationScope.All })
                    targets.Children.Add(TargetRow(new HookTarget(0, "Every regen", "")));
                return;
            }
            foreach (var t in drafts[sc]) targets.Children.Add(TargetRow(t));
        }

        void UpdatePicker()
        {
            var sc = CurrentScope();
            picker.IsVisible = sc != GoldenGenerationScope.All;
            picker.Watermark = Placeholder();
            picker.Text = picked?.Name ?? "";
            picker.IsEnabled = catalogues.ContainsKey(sc) || sc == GoldenGenerationScope.All;
            add.IsEnabled = sc == GoldenGenerationScope.All || picked is not null;
        }

        Control TargetRow(HookTarget target)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var name = new TextBlock
            {
                Text = target.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0),
            };
            ToolTip.SetTip(name, target.Name);
            row.Children.Add(name);
            var clear = new Button { Content = "Clear", Height = 24, MinHeight = 24, Padding = new Thickness(10, 0), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(clear, 1);
            clear.Click += (_, _) => ClearTarget(target);
            row.Children.Add(clear);
            return row;
        }

        IReadOnlyList<(string Text, HookTarget Value)> Find(string query)
        {
            var list = catalogues.GetValueOrDefault(CurrentScope());
            if (list is null) return [];
            return list.Where(t => t.Matches(query)).Take(300).Select(t => (t.Name, t)).ToList();
        }

        async Task PickAsync()
        {
            if (window is null) return;
            var sc = CurrentScope();
            if (sc == GoldenGenerationScope.All) return;
            if (!catalogues.ContainsKey(sc)) { status.Text = "Still reading the name list…"; return; }
            var title = sc switch
            {
                GoldenGenerationScope.Club => "Pick a club",
                GoldenGenerationScope.Nation => "Pick a nation",
                _ => "Pick a continent",
            };
            if (await PickerDialog.Show(window, title, Find, "Type part of the name") is { } hit)
            {
                picked = hit;
                UpdatePicker();
            }
        }

        // Coverage, so a broader target can drop the narrower lists it already contains.
        int? NationOfClub(int clubId)
        {
            var club = clubCache?.FirstOrDefault(c => c.Id == clubId);
            if (club is null || session.Profile.Club?.Nation is not { } offset) return null;
            var bytes = new byte[8];
            if (!session.Memory.TryRead(club.Address + (ulong)offset, bytes)) return null;
            ulong address = BitConverter.ToUInt64(bytes);
            return nationCatalogue?.FirstOrDefault(n => n.Address == address)?.Id;
        }
        int? ContinentOfNation(int nationId)
        {
            var nation = nationCatalogue?.FirstOrDefault(n => n.Id == nationId);
            if (nation is null) return null;
            var continent = session.NationLinks.Continent(nation.Address);
            return continent is null ? null : IdAt(continent.Address);
        }
        int? ContinentOfClub(int clubId) => NationOfClub(clubId) is { } nation ? ContinentOfNation(nation) : null;

        Dictionary<GoldenGenerationScope, List<HookTarget>> CopyDrafts() =>
            drafts.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());

        void Remember()
        {
            history.Add(new AcademySnapshot(installed is { Enabled: true }, installed?.Scope ?? CurrentScope(), installed?.Ids.ToArray() ?? [],
                (int)(minPa.Value ?? 150), (int)(maxPa.Value ?? 190), (int)(wonderkid.Value ?? 0), CopyDrafts()));
            if (history.Count > 20) history.RemoveAt(0);
            undo.IsEnabled = true;
        }

        void ClearTarget(HookTarget target)
        {
            var sc = CurrentScope();
            if (sc == GoldenGenerationScope.All)
            {
                Remember();
                try { academy.Uninstall(); installed = null; status.Text = "Wonderkid Academy turned off."; }
                catch (Exception ex) { status.Text = $"Wonderkid Academy: {UserMessage.For(ex)}"; }
                UpdateActive();
                ShowTargets();
                UpdatePicker();
                _ = RefreshAsync();
                return;
            }
            var list = drafts[sc];
            if (list.All(c => c.Id != target.Id)) return;
            bool isActive = installed is { Enabled: true } && installed.Scope == sc;
            if (isActive) Remember();
            list.RemoveAll(c => c.Id == target.Id);
            if (picked?.Id == target.Id) picked = null;
            int lo = (int)(minPa.Value ?? 150), hi = (int)(maxPa.Value ?? 190), wk = (int)(wonderkid.Value ?? 0);
            if (isActive)
            {
                try
                {
                    if (list.Count == 0) { academy.Uninstall(); installed = null; status.Text = "Wonderkid Academy turned off."; }
                    else
                    {
                        academy.Configure(true, lo, hi, wk, sc, list.Select(c => c.Id).ToList());
                        installed = academy.Read()?.Settings;
                        status.Text = $"On: new regens from {string.Join(", ", list.Select(c => c.Name))} get PA {lo}-{hi}.";
                    }
                }
                catch (Exception ex) { status.Text = $"Wonderkid Academy: {UserMessage.For(ex)}"; }
            }
            else status.Text = $"{target.Name} removed from the {ScopeLabel(sc)} list; the active setting is unchanged.";
            UpdateActive();
            ShowTargets();
            UpdatePicker();
            _ = RefreshAsync();
        }

        Control SpeedRow(SpeedGroup group)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,72,Auto"), Margin = new Thickness(0, 0, 0, 2) };
            var label = new TextBlock
            {
                Text = group.Label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0),
            };
            ToolTip.SetTip(label, group.Label);
            var box = new TextBox
            {
                Text = group.Multiplier.ToString("0.#", CultureInfo.InvariantCulture), Width = 64,
                Height = 24, MinHeight = 24, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(box, "Any speed from 1.1x to 20x in steps of 0.1; 1 means normal speed. Press Enter to apply.");
            var remove = new Button
            {
                Content = "Remove", Height = 24, MinHeight = 24, Padding = new Thickness(8, 0), FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(box, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(label);
            grid.Children.Add(box);
            grid.Children.Add(remove);

            bool done = false;
            void Apply()
            {
                if (done) return;
                if (ParseSpeed(box.Text) is not { } multiplier || multiplier != 0 && !AcceleratedDevelopment.IsAllowedMultiplier(multiplier))
                {
                    status.Text = $"Development speed: enter {AcceleratedDevelopment.MinMultiplier.ToString(CultureInfo.InvariantCulture)} to {AcceleratedDevelopment.MaxMultiplier} with at most one decimal (1 = normal).";
                    box.Text = group.Multiplier.ToString("0.#", CultureInfo.InvariantCulture);
                    return;
                }
                if (Math.Abs(multiplier - group.Multiplier) < 1e-9) return;
                done = true;
                try
                {
                    int changed = 0;
                    foreach (var p in group.Players) if (accel.Set(p, multiplier)) changed++;
                    status.Text = $"Development speed set to {SpeedText(multiplier)} for {group.Label}.";
                }
                catch (Exception ex) { status.Text = $"Development Speed: {UserMessage.For(ex)}"; }
                _ = RefreshAsync();
            }
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(); e.Handled = true; } };
            // Posted so a click on Remove sets done before this runs and the box does not set the old speed first.
            box.LostFocus += (_, _) => Dispatcher.UIThread.Post(Apply);
            remove.Click += (_, _) =>
            {
                done = true;
                try
                {
                    foreach (var p in group.Players) accel.Set(p, 0);
                    if (accel.Read()?.Players.Count is 0) accel.Uninstall();
                    status.Text = $"Development speed removed for {group.Label}.";
                }
                catch (Exception ex) { status.Text = $"Development Speed: {UserMessage.For(ex)}"; }
                _ = RefreshAsync();
            };
            return grid;
        }

        async Task RefreshAsync()
        {
            try
            {
                var result = await Task.Run(() =>
                {
                    var a = academy.Read();
                    var d = accel.Read();
                    var g = d is null ? new List<SpeedGroup>() : SpeedGroups(d.Players);
                    return (a, d, g);
                });
                var (a, d, groups) = result;
                syncing = true;
                try
                {
                    installed = a?.Settings;
                    if (a is null) academyState.Text = "Not installed";
                    else
                    {
                        var s = a.Settings;
                        academyState.Text = $"{(s.Enabled ? "On" : "Off")} · regens seen {a.Calls} · PA raised {a.Raised} · wonderkids {a.Wonderkids}";
                        if (s.MinPa >= 1) { minPa.Value = s.MinPa; maxPa.Value = s.MaxPa; }
                        wonderkid.Value = s.WonderkidChance;
                        // Only the active scope's list is the hook; the other lists stay as the user left them.
                        if (s.Scope != GoldenGenerationScope.All)
                        {
                            var names = catalogues.GetValueOrDefault(s.Scope);
                            drafts[s.Scope] = s.Ids.Select(id => names?.FirstOrDefault(t => t.Id == id) ?? new HookTarget(id, $"id {id}", "")).ToList();
                        }
                    }
                    if (firstSync)
                    {
                        if (a is not null && !scopeTouched) scope.SelectedIndex = Math.Max(0, Array.FindIndex(scopes, x => x.Item1 == a.Settings.Scope));
                        firstSync = false;
                    }
                }
                finally { syncing = false; }
                UpdateActive();
                ShowTargets();
                UpdatePicker();

                accelPanel.Children.Clear();
                if (d is null) { accelPanel.Children.Add(new TextBlock { Text = "Not installed", FontSize = 12.5 }); accelHits.Text = ""; }
                else
                {
                    if (groups.Count == 0) accelPanel.Children.Add(new TextBlock { Text = "No players sped up", FontSize = 12.5 });
                    foreach (var g in groups) accelPanel.Children.Add(SpeedRow(g));
                    accelHits.Text = $"Gains changed {d.Hits}";
                }
            }
            catch (Exception ex) { academyState.Text = UserMessage.For(ex); }
        }

        scope.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            scopeTouched = true;
            picked = null;
            ShowTargets();
            UpdatePicker();
        };
        picker.PointerReleased += async (_, _) => await PickAsync();
        picker.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await PickAsync(); } };
        add.Click += (_, _) =>
        {
            var sc = CurrentScope();
            int lo = (int)(minPa.Value ?? 150), hi = (int)(maxPa.Value ?? 190), wk = (int)(wonderkid.Value ?? 0);
            try
            {
                if (sc == GoldenGenerationScope.All)
                {
                    Remember();
                    academy.Configure(true, lo, hi, wk, sc, []);
                    drafts[GoldenGenerationScope.Club].Clear();
                    drafts[GoldenGenerationScope.Nation].Clear();
                    drafts[GoldenGenerationScope.Continent].Clear();
                    installed = academy.Read()?.Settings;
                    status.Text = $"On: every new regen gets PA {lo}-{hi}.";
                }
                else
                {
                    if (picked is null) { status.Text = "Pick a club, nation or continent first."; return; }
                    var list = drafts[sc];
                    bool newEntry = list.All(c => c.Id != picked.Id);
                    if (newEntry && list.Count >= GoldenGenerationHook.MaxIds) { status.Text = $"At most {GoldenGenerationHook.MaxIds} entries."; return; }
                    Remember();
                    if (newEntry) list.Add(picked);
                    // A broader target makes the narrower entries it covers redundant.
                    if (sc == GoldenGenerationScope.Nation)
                        drafts[GoldenGenerationScope.Club].RemoveAll(c => NationOfClub(c.Id) == picked.Id);
                    else if (sc == GoldenGenerationScope.Continent)
                    {
                        drafts[GoldenGenerationScope.Nation].RemoveAll(n => ContinentOfNation(n.Id) == picked.Id);
                        drafts[GoldenGenerationScope.Club].RemoveAll(c => ContinentOfClub(c.Id) == picked.Id);
                    }
                    academy.Configure(true, lo, hi, wk, sc, list.Select(c => c.Id).ToList());
                    installed = academy.Read()?.Settings;
                    status.Text = $"On: new regens from {string.Join(", ", list.Select(c => c.Name))} get PA {lo}-{hi}.";
                }
            }
            catch (Exception ex) { status.Text = $"Wonderkid Academy: {UserMessage.For(ex)}"; }
            UpdateActive();
            ShowTargets();
            UpdatePicker();
            _ = RefreshAsync();
        };
        undo.Click += (_, _) =>
        {
            if (history.Count == 0) return;
            var snapshot = history[^1];
            history.RemoveAt(history.Count - 1);
            undo.IsEnabled = history.Count > 0;
            drafts = snapshot.Drafts;
            syncing = true;
            try
            {
                minPa.Value = snapshot.MinPa;
                maxPa.Value = snapshot.MaxPa;
                wonderkid.Value = snapshot.Wonderkid;
                scope.SelectedIndex = Math.Max(0, Array.FindIndex(scopes, x => x.Item1 == snapshot.Scope));
                scopeTouched = true;
            }
            finally { syncing = false; }
            try
            {
                if (snapshot.Installed)
                {
                    academy.Configure(true, snapshot.MinPa, snapshot.MaxPa, snapshot.Wonderkid, snapshot.Scope, snapshot.Ids);
                    installed = academy.Read()?.Settings;
                }
                else { academy.Uninstall(); installed = null; }
                status.Text = "Wonderkid Academy: reverted.";
            }
            catch (Exception ex) { status.Text = $"Wonderkid Academy: {UserMessage.For(ex)}"; }
            picked = null;
            UpdateActive();
            ShowTargets();
            UpdatePicker();
            _ = RefreshAsync();
        };

        void SettingsChanged(object? _, NumericUpDownValueChangedEventArgs __)
        {
            var current = installed;
            if (syncing || current is not { Enabled: true }) return;
            try
            {
                academy.Configure(true, (int)(minPa.Value ?? 150), (int)(maxPa.Value ?? 190), (int)(wonderkid.Value ?? 0), current.Scope, current.Ids);
                installed = academy.Read()?.Settings;
                status.Text = "Wonderkid Academy: settings updated.";
                UpdateActive();
            }
            catch (Exception ex) { status.Text = $"Wonderkid Academy: {UserMessage.For(ex)}"; }
        }
        minPa.ValueChanged += SettingsChanged;
        maxPa.ValueChanged += SettingsChanged;
        wonderkid.ValueChanged += SettingsChanged;

        Control Row(params Control[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var c in items) { c.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(c); }
            return row;
        }
        TextBlock Label(string text) => new() { Text = text, FontSize = 12.5 };
        TextBlock Header(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        var howTo = new Button { Content = "How to use", FontSize = 12, Height = 24, MinHeight = 24, Padding = new Thickness(10, 0), Margin = new Thickness(10, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        howTo.Click += (_, _) => { if (window is not null) WonderkidAcademyHelp.Show(window); };
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Header("Wonderkid Academy (Experimental)"), howTo } });
        stack.Children.Add(academyState);
        stack.Children.Add(activeText);
        stack.Children.Add(Row(Label("PA"), minPa, Label("to"), maxPa, Label("Wonderkid %"), wonderkid));
        stack.Children.Add(Row(Label("Applies to"), scope, picker, add, undo));
        stack.Children.Add(targets);
        stack.Children.Add(UiKit.TitleWithInfo(Header("Development Speed"),
            "Players listed here improve faster from training: 2 is twice as fast, 1 is normal. " +
            "Add a player with Set speed in his Actions panel. Works until you close the game."));
        stack.Children.Add(accelPanel);
        stack.Children.Add(accelHits);
        // Force Accept Transfer is shelved until the code patch is finished; the mechanism is hidden (see ForceAcceptTransferView.cs).
        // stack.Children.Add(BuildForceAcceptTransferSection(session));
        stack.Children.Add(UiKit.Card("Force Accept Transfer", new TextBlock { Text = "Coming Soon", FontSize = 13, Foreground = UiKit.TextMuted }));
        window = new Window
        {
            Title = "Hook Manager",
            Width = 720,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        hookManagerWindow = window;
        window.Closed += (_, _) => hookManagerWindow = null;
        window.Show(this);

        _ = Task.Run(() =>
        {
            WarmCatalogues(players);
            var nations = nationCatalogue ?? [];
            var clubs = clubCache ?? [];
            var result = new Dictionary<GoldenGenerationScope, IReadOnlyList<HookTarget>>
            {
                [GoldenGenerationScope.Club] = clubs.Where(c => c.Id is not null)
                    .Select(c => new HookTarget(c.Id!.Value, c.Name ?? c.DisplayName, $"{c.Name} {c.ShortName}"))
                    .GroupBy(t => t.Name).Select(g => g.First()).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                [GoldenGenerationScope.Nation] = nations.Where(n => n.Id is not null)
                    .Select(n => new HookTarget(n.Id!.Value, n.DisplayName, $"{n.Name} {n.ShortName}"))
                    .GroupBy(t => t.Name).Select(g => g.First()).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                [GoldenGenerationScope.Continent] = session.NationLinks.Continents(nations)
                    .Select(c => IdAt(c.Address) is { } id ? new HookTarget(id, c.Name, c.Name) : null).OfType<HookTarget>().ToList(),
            };
            return result;
        }).ContinueWith(t => Dispatcher.UIThread.Post(async () =>
        {
            if (t.Exception is { } ex) { academyState.Text = UserMessage.For(ex.InnerException ?? ex); return; }
            catalogues = t.Result;
            UpdatePicker();
            await RefreshAsync();
        }));

        int? IdAt(ulong address)
        {
            var b = new byte[4];
            return address != 0 && session.Memory.TryRead(address + 0xC, b) ? BitConverter.ToInt32(b) : null;
        }
    }
}
