using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record NationNavItem
    {
        public string Title => "Nations";
        public override string ToString() => Title;
    }

    public sealed record NationRow(NationRecord Nation, string Name, int? Id, long? Fifa, long? Youth, long? Economy);

    readonly NationNavItem nationNavItem = new();
    readonly Grid nationViewRoot = new() { IsVisible = false };
    readonly TextBox nationSearch = new() { Watermark = "Search nation..." };
    readonly DataGrid nationGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 29, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly StackPanel nationEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, Dictionary<NationField, string>> nationDrafts = [];
    readonly Dictionary<ulong, NationRivalryDraft> nationRivalryDrafts = [];
    readonly Dictionary<ulong, NationPreferenceDraft> nationPreferenceDrafts = [];
    /// <summary>Pending Continent per nation: the address of the chosen continent object.</summary>
    readonly Dictionary<ulong, ulong> nationContinentDrafts = [];
    /// <summary>Pending national-team Reputation per nation: team address to the typed text.</summary>
    readonly Dictionary<ulong, Dictionary<ulong, string>> nationTeamDrafts = [];
    IReadOnlyList<NationRow> nationRows = [];
    NationRecord? selectedNation;
    bool nationActive;

    void RegisterNationView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(nationViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, nationNavItem];

        nationGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(NationRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        foreach (var (h, p) in new[] { ("ID", nameof(NationRow.Id)), ("FIFA", nameof(NationRow.Fifa)), ("Youth", nameof(NationRow.Youth)), ("Economy", nameof(NationRow.Economy)) })
            nationGrid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = new DataGridLength(84) });
        nationSearch.TextChanged += (_, _) => FilterNations();
        nationGrid.SelectionChanged += (_, _) => { selectedNation = (nationGrid.SelectedItem as NationRow)?.Nation; RenderNationEditor(); };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = nationSearch };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(nationGrid);
        nationViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(560)) { MinWidth = 420 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 420 },
        };
        Grid.SetColumn(left, 0);
        nationViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        nationViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = nationEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        nationViewRoot.Children.Add(scroll);

        // Registered after Clubs and Staff: those handlers show the players screen when neither is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toNations = ReferenceEquals(modules.SelectedItem, nationNavItem);
            if (toNations == nationActive) return;
            nationActive = toNations;
            nationViewRoot.IsVisible = toNations;
            if (toNations) playersSplit.IsVisible = false;
            else if (!clubsActive && !staffActive) playersSplit.IsVisible = true;
            if (toNations && nationRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadNationsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            nationRows = [];
            nationDrafts.Clear();
            nationRivalryDrafts.Clear();
            nationPreferenceDrafts.Clear();
            nationContinentDrafts.Clear();
            nationTeamDrafts.Clear();
            selectedNation = null;
            nationGrid.ItemsSource = null;
            nationEditor.Children.Clear();
            if (nationActive && backend.State.Status == ConnectionStatus.Connected) await LoadNationsAsync();
        });
    }

    async Task LoadNationsAsync()
    {
        try
        {
            var session = backend.Session;
            nationRows = await Task.Run(() =>
            {
                var f = session.NationFields;
                return session.Nations.LoadAll().Select(n =>
                {
                    var v = f.ReadAll(n.Address);
                    return new NationRow(n, n.DisplayName, n.Id, v.TryGetValue(NationField.FifaPosition, out var a) ? a : null,
                        v.TryGetValue(NationField.YouthRating, out var y) ? y : null, v.TryGetValue(NationField.EconomicFactor, out var e) ? e : null);
                }).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            });
            FilterNations();
            status.Text = $"{nationRows.Count:N0} nations";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterNations()
    {
        string q = Fold(nationSearch.Text ?? "");
        nationGrid.ItemsSource = q.Length == 0 ? nationRows : nationRows.Where(r => Fold(r.Name).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderNationEditor()
    {
        nationEditor.Children.Clear();
        if (selectedNation is not { } n) return;
        if (!nationDrafts.TryGetValue(n.Address, out var draft)) nationDrafts[n.Address] = draft = [];
        if (!nationRivalryDrafts.TryGetValue(n.Address, out var rivalryDraft)) nationRivalryDrafts[n.Address] = rivalryDraft = new();
        if (!nationPreferenceDrafts.TryGetValue(n.Address, out var preferenceDraft)) nationPreferenceDrafts[n.Address] = preferenceDraft = new();
        if (!nationTeamDrafts.TryGetValue(n.Address, out var teamDraft)) nationTeamDrafts[n.Address] = teamDraft = [];
        var values = backend.Session.NationFields.ReadAll(n.Address);
        Button? save = null, discard = null;
        bool Dirty() => draft.Count > 0 || rivalryDraft.Count > 0 || preferenceDraft.Count > 0 || nationContinentDrafts.ContainsKey(n.Address) || teamDraft.Count > 0;
        void Changed() { if (save is not null) save.IsEnabled = discard!.IsEnabled = Dirty(); }

        Control Value(NationField f, long original)
        {
            if (f is NationField.AllowMultipleAssistantManagers or NationField.DoesNotAllowDualNationality)
            {
                var check = new CheckBox { IsChecked = (draft.TryGetValue(f, out var d) ? long.Parse(d) : original) != 0 };
                check.IsCheckedChanged += (_, _) =>
                {
                    long value = check.IsChecked == true ? 1 : 0;
                    if (value == original) draft.Remove(f); else draft[f] = value.ToString(Inv);
                    Changed();
                };
                return check;
            }
            if (f is NationField.Development or NationField.GainNationalityType or NationField.Importance)
            {
                Func<long, string> name = f switch
                {
                    NationField.Development => NationLabels.DevelopmentName,
                    NationField.Importance => NationLabels.ImportanceName,
                    _ => NationLabels.GainNationalityTypeName,
                };
                long[] codes = f == NationField.Importance ? [1, 2, 3, 4] : [1, 2];
                var combo = new ComboBox { ItemsSource = codes.Select(name).ToArray(), MinWidth = 140 };
                combo.SelectedItem = name(draft.TryGetValue(f, out var d) ? long.Parse(d) : original);
                combo.SelectionChanged += (_, _) =>
                {
                    long code = combo.SelectedIndex + 1;
                    if (code == original) draft.Remove(f); else draft[f] = code.ToString(Inv);
                    Changed();
                };
                return combo;
            }
            string text = original.ToString(Inv);
            var box = new TextBox
            {
                Text = draft.GetValueOrDefault(f, text), Width = 84, MinHeight = 24, Padding = new Thickness(4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) => { if (box.Text == text) draft.Remove(f); else draft[f] = box.Text ?? ""; Changed(); };
            return box;
        }

        nationEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = n.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold },
                new TextBlock { Text = n.Id?.ToString(Inv) ?? "", FontSize = 12.5, Foreground = Brush("#94A3B8") },
            }
        });
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var col = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(col[0], 0); Grid.SetColumn(col[1], 2);
        columns.Children.Add(col[0]); columns.Children.Add(col[1]);
        int[] placement = [0, 1, 0, 1, 0];
        for (int g = 0; g < NationLabels.Groups.Length; g++)
        {
            var (title, items) = NationLabels.Groups[g];
            var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var (f, label) in items.Where(i => values.ContainsKey(i.Field)))
            {
                int r = rows.RowDefinitions.Count; rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = new TextBlock { Text = label, FontSize = 12.5, Foreground = Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, r); rows.Children.Add(l);
                var c = Value(f, values[f]); Grid.SetRow(c, r); Grid.SetColumn(c, 1); rows.Children.Add(c);
            }
            col[placement[g]].Children.Add(new Border
            {
                Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel
                {
                    Children =
                    {
                        new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                            Child = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } },
                        rows,
                    }
                }
            });
        }
        var links = backend.Session.NationLinks;
        ulong continentNow = links.ContinentPointer(n.Address);
        var continents = links.Continents(nationRows.Select(r => r.Nation));
        var location = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
        void LocationRow(string label, Control value)
        {
            int r = location.RowDefinitions.Count; location.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new TextBlock { Text = label, FontSize = 12.5, Foreground = Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(l, r); location.Children.Add(l);
            Grid.SetRow(value, r); Grid.SetColumn(value, 1); location.Children.Add(value);
        }
        if (links.Capital(n.Address) is { } capital)
            LocationRow("Capital", new TextBlock { Text = capital.Name, FontSize = 12.5, Foreground = Brush("#F1F5F9"), VerticalAlignment = VerticalAlignment.Center });
        if (links.Stadium(n.Address) is { } stadium)
            LocationRow("Stadium", new TextBlock { Text = stadium.DisplayName, FontSize = 12.5, Foreground = Brush("#F1F5F9"), VerticalAlignment = VerticalAlignment.Center });
        if (continents.Any(c => c.Address == continentNow))
        {
            var combo = new ComboBox { ItemsSource = continents.Select(c => c.Name).ToArray(), MinWidth = 150 };
            combo.SelectedIndex = continents.ToList().FindIndex(c => c.Address == (nationContinentDrafts.TryGetValue(n.Address, out ulong pending) ? pending : continentNow));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                ulong chosen = continents[combo.SelectedIndex].Address;
                if (chosen == continentNow) nationContinentDrafts.Remove(n.Address); else nationContinentDrafts[n.Address] = chosen;
                Changed();
            };
            LocationRow("Continent", combo);
        }
        if (location.Children.Count > 0)
            col[0].Children.Insert(0, new Border
            {
                Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel { Children = { new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"),
                    BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = "LOCATION", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } }, location } }
            });
        var teams = backend.Session.NationTeams.Read(n.Address);
        if (teams.Count > 0)
        {
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var team in teams)
            {
                int r = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = team.Index == 0 ? n.DisplayName : $"{n.DisplayName} {team.Index + 1}", FontSize = 12.5, Foreground = Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, r); list.Children.Add(label);
                string original = team.Reputation.ToString(Inv);
                var input = new TextBox { Text = teamDraft.GetValueOrDefault(team.Address, original), Width = 84, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                input.TextChanged += (_, _) => { string t = input.Text ?? ""; if (t == original) teamDraft.Remove(team.Address); else teamDraft[team.Address] = t; Changed(); };
                Grid.SetRow(input, r); Grid.SetColumn(input, 1); list.Children.Add(input);
            }
            col[1].Children.Add(new Border
            {
                Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel { Children = { new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"),
                    BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = "TEAMS REPUTATION", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } }, list } }
            });
        }
        var rivals = backend.Session.NationRivalries.Read(n, nationRows.Select(r => r.Nation).ToArray());
        if (rivals.Count > 0)
        {
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var rival in rivals)
            {
                int r = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = rival.Opponent, FontSize = 12.5, Foreground = Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, r); list.Children.Add(label);
                var input = new TextBox { Text = rivalryDraft.TextFor(rival), Width = 84, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                input.TextChanged += (_, _) => { rivalryDraft.Set(rival, input.Text ?? ""); Changed(); };
                Grid.SetRow(input, r); Grid.SetColumn(input, 1); list.Children.Add(input);
            }
            col[0].Children.Add(new Border
            {
                Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel { Children = { new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"),
                    BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = "RIVAL NATIONS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } }, list } }
            });
        }
        var preferences = backend.Session.NationPreferences.Read(n, nationRows.Select(r => r.Nation).ToArray());
        if (preferences.Count > 0)
        {
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64,64"), RowSpacing = 3, ColumnSpacing = 4, Margin = new Thickness(12, 8) };
            foreach (var heading in new[] { "Nation", "Rating", "Scouting", "Min age", "Max age" }.Select((text, i) => (text, i)))
            {
                if (list.RowDefinitions.Count == 0) list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = heading.text, FontSize = 11, Foreground = Brush("#94A3B8") };
                Grid.SetColumn(label, heading.i); list.Children.Add(label);
            }
            foreach (var preference in preferences)
            {
                int row = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = preference.Target, FontSize = 12.5, Foreground = Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, row); list.Children.Add(label);
                var texts = preferenceDraft.TextFor(preference);
                for (int i = 0; i < 4; i++)
                {
                    int column = i;
                    var input = new TextBox { Text = texts[i], MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                    input.TextChanged += (_, _) => { preferenceDraft.Set(preference, column, input.Text ?? ""); Changed(); };
                    Grid.SetRow(input, row); Grid.SetColumn(input, i + 1); list.Children.Add(input);
                }
            }
            col[1].Children.Add(new Border { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel { Children = { new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = "TRANSFER PREFERENCES", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } }, list } } });
        }
        nationEditor.Children.Add(columns);

        save = new Button { Content = "Save changes", IsEnabled = Dirty(), Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = Dirty() };
        save.Click += (_, _) => SaveNation(n, draft, values, rivalryDraft, preferenceDraft, continentNow, continents, teamDraft);
        discard.Click += (_, _) => { nationDrafts.Remove(n.Address); nationRivalryDrafts.Remove(n.Address); nationPreferenceDrafts.Remove(n.Address); nationContinentDrafts.Remove(n.Address); nationTeamDrafts.Remove(n.Address); RenderNationEditor(); };
        nationEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
    }

    void SaveNation(NationRecord n, Dictionary<NationField, string> draft, IReadOnlyDictionary<NationField, long> snapshot, NationRivalryDraft rivalryDraft, NationPreferenceDraft preferenceDraft,
        ulong continentNow, IReadOnlyList<ContinentRecord> continents, Dictionary<ulong, string> teamDraft)
    {
        if (!NationLabels.TryParse(draft, out var v, out var error)) { status.Text = error; return; }
        if (!rivalryDraft.TryParse(out var rivals)) { status.Text = "Rivalry: enter a whole number 0–100"; return; }
        if (!preferenceDraft.TryParse(out var preferences)) { status.Text = "Transfer preferences: check values and age range"; return; }
        var teamValues = new Dictionary<ulong, int>();
        foreach (var (address, text) in teamDraft)
        {
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int rep) || rep < 0 || rep > NationTeamReader.MaxReputation)
            { status.Text = $"Team reputation: enter a whole number 0 to {NationTeamReader.MaxReputation}"; return; }
            teamValues[address] = rep;
        }
        try
        {
            var acc = backend.Session.NationFields;
            bool continentChanged = nationContinentDrafts.TryGetValue(n.Address, out ulong chosenContinent);
            backend.Edits.Execute(() =>
            {
                foreach (var (f, value) in v) acc.Set(n.Address, f, snapshot[f], value);
                foreach (var (rival, value) in rivals) backend.Session.NationRivalries.SetValue(rival, value);
                foreach (var p in preferences) backend.Session.NationPreferences.Set(p.Row, p.Rating, p.MinAge, p.MaxAge, p.Knowledge);
                if (continentChanged) backend.Session.NationLinks.SetContinent(n, continentNow, continents.First(c => c.Address == chosenContinent));
                var teamReader = backend.Session.NationTeams;
                foreach (var team in teamReader.Read(n.Address).Where(t => teamValues.ContainsKey(t.Address))) teamReader.SetReputation(team, teamValues[team.Address]);
            });
            nationTeamDrafts.Remove(n.Address);
            nationContinentDrafts.Remove(n.Address);
            nationDrafts.Remove(n.Address);
            nationRivalryDrafts.Remove(n.Address);
            nationPreferenceDrafts.Remove(n.Address);
            RenderNationEditor();
            int count = v.Count + rivals.Count + preferences.Count + (continentChanged ? 1 : 0) + teamValues.Count;
            status.Text = $"Saved {count} change{(count == 1 ? "" : "s")} to {n.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
