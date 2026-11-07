using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record NationNavItem
    {
        public string Title => "Nations";
        public override string ToString() => Title;
    }

    public sealed record NationRow(NationRecord Nation, string Name, int? Id, long? Fifa, long? Youth, long? Economy)
    {
        public override string ToString() => Name;
    }

    readonly NationNavItem nationNavItem = new();
    readonly Grid nationViewRoot = new() { IsVisible = false };
    readonly TextBox nationSearch = new() { Watermark = "Search nation or ID..." };
    readonly DataGrid nationGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 28, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly ContentControl nationHeader = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly ContentControl nationRailHost = new() { Width = 168, Margin = new Thickness(12, 0, 0, 10) };
    readonly StackPanel nationEditor = new() { Spacing = 10, Margin = new Thickness(0, 0, 12, 12) };
    int nationSelectedTab;
    readonly Dictionary<ulong, Dictionary<NationField, string>> nationDrafts = [];
    readonly Dictionary<(ulong Nation, ulong Address), (NationAgreementRow Row, string From, string Until)> agreementDates = [];
    sealed record AgreementTargetOption(ulong Address, string Name) { public override string ToString() => Name; }
    sealed record AgreementTypeOption(AgreementKind Kind, string Name) { public override string ToString() => Name; }
    readonly Dictionary<(ulong Nation, ulong Address), (NationAgreementRow Row, AgreementKind Kind, ulong Target)> agreementTargets = [];
    readonly Dictionary<(ulong Nation, ulong Address), NationAgreementRow> agreementRemovals = [];
    sealed class AgreementAddition(NationAgreementRow source, ulong sourceNation, byte[]? emptyHeader)
    {
        public NationAgreementRow Source { get; } = source;
        public ulong SourceNation { get; } = sourceNation;
        public byte[]? EmptyHeader { get; } = emptyHeader;
        public AgreementKind Kind { get; set; } = source.Kind;
        public ulong Target { get; set; } = source.Target;
        public string From { get; set; } = source.From?.ToString("yyyy-MM-dd", Inv) ?? "";
        public string Until { get; set; } = source.Until?.ToString("yyyy-MM-dd", Inv) ?? "";
    }
    readonly Dictionary<(ulong Nation, int Vector), List<AgreementAddition>> agreementAdds = [];
    IReadOnlyList<AgreementTargetOption>? agreementOrganisations;
    readonly Dictionary<ulong, NationRivalryDraft> nationRivalryDrafts = [];
    readonly Dictionary<ulong, NationPreferenceDraft> nationPreferenceDrafts = [];
    /// <summary>Pending Continent per nation: the address of the chosen continent object.</summary>
    readonly Dictionary<ulong, ulong> nationContinentDrafts = [];
    readonly Dictionary<ulong, bool> nationKitDrafts = [];
    readonly Dictionary<ulong, (string? Background, string? Foreground)> nationColourDrafts = [];
    readonly Dictionary<(ulong Nation, int Vector), Dictionary<ulong, ulong>> nationPreferenceTargets = [];
    readonly Dictionary<(ulong Nation, int Vector), HashSet<ulong>> nationPreferenceRemovals = [];
    readonly Dictionary<(ulong Nation, int Vector), int> nationPreferenceAdds = [];
    readonly Dictionary<(ulong Nation, ulong Slot), (string? Foreground, string? Background, string? Style)> nationKitItemDrafts = [];
    readonly Dictionary<ulong, int> nationSquadTeam = [];
    /// <summary>Pending national-team Reputation per nation: team address to the typed text.</summary>
    readonly Dictionary<ulong, Dictionary<ulong, string>> nationTeamDrafts = [];
    IReadOnlyList<NationRow> nationRows = [];
    NationRecord? selectedNation;
    Button? nationSaveButton;
    bool nationActive;
    Control nationDetailRoot = new Panel();

    void OpenSelectedNation()
    {
        selectedNation = (nationGrid.SelectedItem as NationRow)?.Nation;
        if (selectedNation is null) return;
        RenderNationEditor();
        OpenKindTab("nation", selectedNation.DisplayName, nationDetailRoot);
    }

    void RegisterNationView()
    {
        AddModuleTab("Nations", nationViewRoot, nationNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, nationNavItem];

        nationGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(NationRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        foreach (var (h, p) in new[] { ("ID", nameof(NationRow.Id)), ("FIFA", nameof(NationRow.Fifa)), ("Youth", nameof(NationRow.Youth)), ("Economy", nameof(NationRow.Economy)) })
        {
            var col = new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = new DataGridLength(h == "Economy" ? 94 : 84) };
            col.CellStyleClasses.Add("numeric");
            nationGrid.Columns.Add(col);
        }
        nationSearch.TextChanged += (_, _) => FilterNations();
        nationGrid.SelectionChanged += (_, _) => selectedNation = (nationGrid.SelectedItem as NationRow)?.Nation;
        nationGrid.DoubleTapped += (_, _) => OpenSelectedNation();
        nationGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedNation(); e.Handled = true; }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = nationSearch,
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(nationGrid);
        nationViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = nationEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var editorHost = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(nationHeader, Dock.Top);
        DockPanel.SetDock(nationRailHost, Dock.Left);
        editorHost.Children.Add(nationHeader);
        editorHost.Children.Add(nationRailHost);
        editorHost.Children.Add(scroll);
        nationDetailRoot = editorHost;

        // Registered after Clubs and Staff: those handlers show the players screen when neither is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toNations = ReferenceEquals(modules.SelectedItem, nationNavItem);
            if (toNations == nationActive) return;
            nationActive = toNations;
            if (toNations && nationRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadNationsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            nationRows = [];
            agreementDates.Clear();
            agreementTargets.Clear();
            agreementRemovals.Clear();
            agreementAdds.Clear();
            agreementOrganisations = null;
            nationDrafts.Clear();
            nationRivalryDrafts.Clear();
            nationPreferenceDrafts.Clear();
            nationContinentDrafts.Clear();
            nationKitDrafts.Clear();
            nationColourDrafts.Clear();
            nationPreferenceTargets.Clear();
            nationPreferenceRemovals.Clear();
            nationPreferenceAdds.Clear();
            nationKitItemDrafts.Clear();
            nationTeamDrafts.Clear();
            selectedNation = null;
            nationSaveButton = null;
            nationGrid.ItemsSource = null;
            nationHeader.Content = null;
            nationRailHost.Content = null;
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
        string q = Fold((nationSearch.Text ?? "").Trim());
        nationGrid.ItemsSource = q.Length == 0
            ? nationRows
            : nationRows.Where(r =>
                Fold(r.Name).Contains(q, StringComparison.Ordinal) ||
                (r.Id?.ToString(Inv).Contains(q, StringComparison.Ordinal) ?? false)).ToList();
    }

    void RenderNationEditor()
    {
        nationHeader.Content = null;
        nationRailHost.Content = null;
        nationEditor.Children.Clear();
        if (selectedNation is not { } n) { nationSaveButton = null; return; }
        if (!nationDrafts.TryGetValue(n.Address, out var draft)) nationDrafts[n.Address] = draft = [];
        if (!nationRivalryDrafts.TryGetValue(n.Address, out var rivalryDraft)) nationRivalryDrafts[n.Address] = rivalryDraft = new();
        if (!nationPreferenceDrafts.TryGetValue(n.Address, out var preferenceDraft)) nationPreferenceDrafts[n.Address] = preferenceDraft = new();
        if (!nationTeamDrafts.TryGetValue(n.Address, out var teamDraft)) nationTeamDrafts[n.Address] = teamDraft = [];
        var values = backend.Session.NationFields.ReadAll(n.Address);
        Button? save = null, discard = null;
        bool Dirty() => draft.Count > 0 || rivalryDraft.Count > 0 || preferenceDraft.Count > 0 || nationContinentDrafts.ContainsKey(n.Address) || teamDraft.Count > 0 || nationKitDrafts.ContainsKey(n.Address) || nationColourDrafts.ContainsKey(n.Address) || nationPreferenceTargets.Any(kv => kv.Key.Nation == n.Address && kv.Value.Count > 0) || nationPreferenceRemovals.Any(kv => kv.Key.Nation == n.Address && kv.Value.Count > 0) || nationPreferenceAdds.Any(kv => kv.Key.Nation == n.Address && kv.Value > 0) || nationKitItemDrafts.Keys.Any(k => k.Nation == n.Address) || agreementDates.Keys.Any(k => k.Nation == n.Address) || agreementTargets.Keys.Any(k => k.Nation == n.Address) || agreementRemovals.Keys.Any(k => k.Nation == n.Address) || agreementAdds.Keys.Any(k => k.Nation == n.Address);
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
                var combo = new ComboBox { ItemsSource = codes.Select(name).ToArray(), MinWidth = 148 };
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
                Text = draft.GetValueOrDefault(f, text), Width = 84, MinHeight = 24, Padding = new Thickness(5, 1),
                HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) => { if (box.Text == text) draft.Remove(f); else draft[f] = box.Text ?? ""; Changed(); };
            return box;
        }

        Border BuildRowsCard(string title, IEnumerable<(string Label, Control Control)> entries)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var (lbl, ctrl) in entries)
            {
                var line = new DockPanel { MinHeight = 25, LastChildFill = true };
                DockPanel.SetDock(ctrl, Dock.Right);
                line.Children.Add(ctrl);
                line.Children.Add(new TextBlock { Text = lbl, FontSize = 11.5, Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                stack.Children.Add(new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 2),
                    Child = line,
                });
            }
            return UiKit.Card(title, stack);
        }

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var col = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(col[0], 0); Grid.SetColumn(col[1], 2);
        columns.Children.Add(col[0]); columns.Children.Add(col[1]);

        var teamColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var teamCol = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(teamCol[0], 0); Grid.SetColumn(teamCol[1], 2);
        teamColumns.Children.Add(teamCol[0]); teamColumns.Children.Add(teamCol[1]);

        var pages = Enumerable.Range(0, 6).Select(_ => new StackPanel { Spacing = 12 }).ToArray();
        pages[0].Children.Add(columns);
        pages[1].Children.Add(teamColumns);

        for (int g = 0; g < NationLabels.Groups.Length; g++)
        {
            var (title, items) = NationLabels.Groups[g];
            var cardItems = items.Where(i => values.ContainsKey(i.Field))
                .Select(i => (i.Label, Value(i.Field, values[i.Field]))).ToList();
            if (cardItems.Count == 0) continue;
            (g == 0 ? col[1] : teamCol[(g - 1) % 2]).Children.Add(BuildRowsCard(title, cardItems));
        }

        var links = backend.Session.NationLinks;
        ulong continentNow = links.ContinentPointer(n.Address);
        var continents = links.Continents(nationRows.Select(r => r.Nation));
        var locationItems = new List<(string Label, Control Control)>();
        if (links.Capital(n.Address) is { } capital)
            locationItems.Add(("Capital", new TextBlock { Text = capital.Name, FontSize = 11.5, FontWeight = FontWeight.Medium, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center }));
        if (links.Stadium(n.Address) is { } stadium)
            locationItems.Add(("Stadium", new TextBlock { Text = stadium.DisplayName, FontSize = 11.5, FontWeight = FontWeight.Medium, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center }));
        if (continents.Any(c => c.Address == continentNow))
        {
            var combo = new ComboBox { ItemsSource = continents.Select(c => c.Name).ToArray(), MinWidth = 152 };
            combo.SelectedIndex = continents.ToList().FindIndex(c => c.Address == (nationContinentDrafts.TryGetValue(n.Address, out ulong pending) ? pending : continentNow));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                ulong chosen = continents[combo.SelectedIndex].Address;
                if (chosen == continentNow) nationContinentDrafts.Remove(n.Address); else nationContinentDrafts[n.Address] = chosen;
                Changed();
            };
            locationItems.Add(("Continent", combo));
        }
        if (locationItems.Count > 0)
            col[0].Children.Insert(0, BuildRowsCard("Location", locationItems));

        // Kits and Colours (pages[3]) with live National Kit vector preview
        var colours = backend.Session.NationColours.Read(n.Address);
        var kitItems = backend.Session.NationKits.Read(n.Address);
        var kitPreview = UiKit.CreateKitPreview("Home Kit");

        static uint ToOpaqueArgb(string? hex, uint fallbackRgb)
        {
            if (hex is not null && Fm27Editor.Core.Nations.NationColours.TryParse(hex, out uint parsed))
                return parsed | 0xFF000000u;
            return fallbackRgb | 0xFF000000u;
        }

        void RefreshNationKitPreview()
        {
            uint defBg = colours?.Background ?? 0xF8FAFCu;
            uint defFg = colours?.Foreground ?? 0x1E293Bu;
            if (nationColourDrafts.TryGetValue(n.Address, out var cd))
            {
                if (cd.Background is not null) defBg = ToOpaqueArgb(cd.Background, defBg) & 0x00FFFFFFu;
                if (cd.Foreground is not null) defFg = ToOpaqueArgb(cd.Foreground, defFg) & 0x00FFFFFFu;
            }
            var homeShirt = kitItems.FirstOrDefault(x => x.Kit == 0 && x.Type == 1) ?? kitItems.FirstOrDefault(x => x.Kit == 0);
            var homeShorts = kitItems.FirstOrDefault(x => x.Kit == 0 && x.Type == 2);
            var homeSocks = kitItems.FirstOrDefault(x => x.Kit == 0 && x.Type == 3);

            uint shirtBg = defBg | 0xFF000000u;
            uint shirtFg = defFg | 0xFF000000u;
            if (homeShirt is not null)
            {
                var kd = nationKitItemDrafts.TryGetValue((n.Address, homeShirt.Slot), out var p) ? p : (null, null, null);
                shirtFg = ToOpaqueArgb(kd.Item1, homeShirt.Colour(0));
                shirtBg = ToOpaqueArgb(kd.Item2, homeShirt.Colour(1));
            }
            uint? shortsBg = null;
            if (homeShorts is not null)
            {
                var kd = nationKitItemDrafts.TryGetValue((n.Address, homeShorts.Slot), out var p) ? p : (null, null, null);
                shortsBg = ToOpaqueArgb(kd.Item2, homeShorts.Colour(1));
            }
            uint? socksBg = null;
            if (homeSocks is not null)
            {
                var kd = nationKitItemDrafts.TryGetValue((n.Address, homeSocks.Slot), out var p) ? p : (null, null, null);
                socksBg = ToOpaqueArgb(kd.Item2, homeSocks.Colour(1));
            }
            kitPreview.Update(shirtBg, shirtFg, 0xFF283046u, shirtFg, shirtBg, shortsBg, socksBg);
        }

        if (colours is { } nationCols)
        {
            string bgOriginal = Fm27Editor.Core.Nations.NationColours.Format(nationCols.Background);
            string fgOriginal = Fm27Editor.Core.Nations.NationColours.Format(nationCols.Foreground);
            var colourDraftNow = nationColourDrafts.TryGetValue(n.Address, out var pending) ? pending : (null, null);
            var colRows = new List<(string Label, Control Control)>();
            foreach (var (title, original, isBackground) in new[] { ("Background Colour", bgOriginal, true), ("Foreground Colour", fgOriginal, false) })
            {
                string current = (isBackground ? colourDraftNow.Item1 : colourDraftNow.Item2) ?? original;
                var box = new TextBox { Text = current, Width = 106, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Left };
                box.TextChanged += (_, _) =>
                {
                    string text = box.Text ?? "";
                    var entry = nationColourDrafts.TryGetValue(n.Address, out var old) ? old : (null, null);
                    entry = isBackground ? (text == bgOriginal ? null : text, entry.Item2) : (entry.Item1, text == fgOriginal ? null : text);
                    if (entry.Item1 is null && entry.Item2 is null) nationColourDrafts.Remove(n.Address); else nationColourDrafts[n.Address] = entry;
                    RefreshNationKitPreview();
                    Changed();
                };
                colRows.Add((title, ColourInput(box)));
            }
            var coloursCard = BuildRowsCard("Colours", colRows);
            coloursCard.VerticalAlignment = VerticalAlignment.Top;
            var topKitSplit = new Grid { ColumnDefinitions = new ColumnDefinitions("*,186"), ColumnSpacing = 12 };
            Grid.SetColumn(coloursCard, 0);
            Grid.SetColumn(kitPreview.Control, 1);
            topKitSplit.Children.Add(coloursCard);
            topKitSplit.Children.Add(kitPreview.Control);
            pages[3].Children.Add(topKitSplit);
        }

        if (kitItems.Count > 0)
        {
            var table = new Grid { ColumnDefinitions = new ColumnDefinitions("50,54,56,*,*"), RowSpacing = 4, ColumnSpacing = 6, Margin = new Thickness(12, 8) };
            foreach (var heading in new[] { "Kit", "Type", "Style", "Foreground", "Background" }.Select((text, i) => (text, i)))
            {
                if (table.RowDefinitions.Count == 0) table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = heading.text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8") };
                Grid.SetColumn(label, heading.i); table.Children.Add(label);
            }
            foreach (var item in kitItems)
            {
                int row = table.RowDefinitions.Count; table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var kitDraftNow = nationKitItemDrafts.TryGetValue((n.Address, item.Slot), out var pendingKit)
                    ? pendingKit : (null, null, null);
                var first = new TextBlock { Text = item.Kit == 0 ? "Home" : item.Kit == 1 ? "Away" : item.Kit == 2 ? "Third" : $"GK {item.Kit - 2}", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                var second = new TextBlock { Text = $"0x{item.Type:X2}", FontSize = 11.5, Foreground = Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(first, row); Grid.SetColumn(first, 0); table.Children.Add(first);
                Grid.SetRow(second, row); Grid.SetColumn(second, 1); table.Children.Add(second);
                string styleOriginal = item.Style.ToString(Inv);
                var styleBox = new TextBox { Text = kitDraftNow.Item3 ?? styleOriginal, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                styleBox.TextChanged += (_, _) =>
                {
                    var entry = nationKitItemDrafts.TryGetValue((n.Address, item.Slot), out var old) ? old : (null, null, null);
                    string text = styleBox.Text ?? "";
                    entry = (entry.Item1, entry.Item2, text == styleOriginal ? null : text);
                    if (entry.Item1 is null && entry.Item2 is null && entry.Item3 is null) nationKitItemDrafts.Remove((n.Address, item.Slot));
                    else nationKitItemDrafts[(n.Address, item.Slot)] = entry;
                    Changed();
                };
                Grid.SetRow(styleBox, row); Grid.SetColumn(styleBox, 2); table.Children.Add(styleBox);
                for (int i = 0; i < 2; i++)
                {
                    bool foreground = i == 0;
                    string original = Fm27Editor.Core.Nations.NationColours.Format(item.Colour(foreground ? 0 : 1));
                    string shown = (foreground ? kitDraftNow.Item1 : kitDraftNow.Item2) ?? original;
                    var box = new TextBox { Text = shown, Width = 106, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Left };
                    box.TextChanged += (_, _) =>
                    {
                        var entry = nationKitItemDrafts.TryGetValue((n.Address, item.Slot), out var old) ? old : (null, null, null);
                        string text = box.Text ?? "";
                        string? staged = text == original ? null : text;
                        entry = foreground ? (staged, entry.Item2, entry.Item3) : (entry.Item1, staged, entry.Item3);
                        if (entry.Item1 is null && entry.Item2 is null && entry.Item3 is null) nationKitItemDrafts.Remove((n.Address, item.Slot));
                        else nationKitItemDrafts[(n.Address, item.Slot)] = entry;
                        RefreshNationKitPreview();
                        Changed();
                    };
                    var input = ColourInput(box);
                    Grid.SetRow(input, row); Grid.SetColumn(input, 3 + i); table.Children.Add(input);
                }
            }
            var kitsCard = UiKit.Card("Kits", table);
            if (colours is null)
            {
                var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,186"), ColumnSpacing = 12 };
                Grid.SetColumn(kitsCard, 0);
                Grid.SetColumn(kitPreview.Control, 1);
                split.Children.Add(kitsCard);
                split.Children.Add(kitPreview.Control);
                pages[3].Children.Add(split);
            }
            else
            {
                pages[3].Children.Add(kitsCard);
            }
        }
        RefreshNationKitPreview();

        // Squads (pages[2])
        var squads = backend.Session.NationSquads.ReadNation(n.Address);
        if (squads.Count > 0)
        {
            int chosen = Math.Clamp(nationSquadTeam.GetValueOrDefault(n.Address), 0, squads.Count - 1);
            var picker = new ComboBox
            {
                Width = 186, Height = 22, MinHeight = 22, FontSize = 11,
                ItemsSource = Enumerable.Range(0, squads.Count).ToArray(),
                SelectedIndex = chosen,
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((index, _) =>
                    new TextBlock { Text = index == 0 ? n.DisplayName : $"{n.DisplayName} {index + 1}" }),
            };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex >= 0) { nationSquadTeam[n.Address] = picker.SelectedIndex; RenderNationEditor(); }
            };
            var squad = squads[chosen].Squad;
            var squadStack = new StackPanel { Spacing = 0 };

            var headGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,210,54,54,48"), ColumnSpacing = 6 };
            foreach (var heading in new[] { "Name", "Positions", "CA", "PA", "Age" }.Select((text, i) => (text, i)))
            {
                var label = new TextBlock
                {
                    Text = heading.text,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#94A3B8"),
                    HorizontalAlignment = heading.i >= 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                Grid.SetColumn(label, heading.i);
                headGrid.Children.Add(label);
            }
            squadStack.Children.Add(new Border
            {
                Background = Brush("#161A26"),
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 5),
                Child = headGrid,
            });

            var today = backend.Session.ReadGameDate();
            int sIdx = 0;
            foreach (var player in squad)
            {
                var squadPlayer = player;
                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,210,54,54,48"), ColumnSpacing = 6, MinHeight = 24 };
                var nameCell = new TextBlock
                {
                    Text = squadPlayer.DisplayName,
                    FontSize = 11.5,
                    FontWeight = FontWeight.Medium,
                    Foreground = Brush("#F1F5F9"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                nameCell.PointerPressed += (_, e) => { if (e.ClickCount >= 2) OpenPlayerFromSquad(squadPlayer); };
                var cells = new[]
                {
                    nameCell,
                    new TextBlock { Text = SquadFormatters.FormatPositions(squadPlayer), FontSize = 11.5, Foreground = Brush("#C4B5FD"), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = squadPlayer.CurrentAbility.ToString(Inv), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = squadPlayer.PotentialAbility.ToString(Inv), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = (today is { } d ? squadPlayer.AgeOn(d) : (int?)null)?.ToString(Inv) ?? "", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
                };
                for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); rowGrid.Children.Add(cells[i]); }
                squadStack.Children.Add(new Border
                {
                    Background = sIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(12, 2),
                    Child = rowGrid,
                });
            }
            pages[2].Children.Add(UiKit.Card("Squads", squadStack, rightHeader: picker));
        }

        // Teams Reputation (teamCol[1])
        var teams = backend.Session.NationTeams.Read(n.Address);
        if (teams.Count > 0)
        {
            var teamStack = new StackPanel { Spacing = 0 };
            var senior = teams[0];
            int tIdx = 0;
            if (backend.Session.NationTeams.AllowLicensedKits(senior.Address) is { } kitNow)
            {
                bool shown = nationKitDrafts.TryGetValue(n.Address, out bool pendingKit) ? pendingKit : kitNow;
                var kits = new CheckBox { Content = "Allow Licensed Kits", IsChecked = shown, FontSize = 11.5, Foreground = UiKit.TextSecondary };
                kits.IsCheckedChanged += (_, _) =>
                {
                    bool value = kits.IsChecked == true;
                    if (value == kitNow) nationKitDrafts.Remove(n.Address); else nationKitDrafts[n.Address] = value;
                    Changed();
                };
                teamStack.Children.Add(new Border
                {
                    Background = tIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 4),
                    Child = kits,
                });
            }
            foreach (var team in teams)
            {
                var line = new DockPanel { MinHeight = 25, LastChildFill = true };
                string original = team.Reputation.ToString(Inv);
                var input = new TextBox { Text = teamDraft.GetValueOrDefault(team.Address, original), Width = 84, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                input.TextChanged += (_, _) => { string t = input.Text ?? ""; if (t == original) teamDraft.Remove(team.Address); else teamDraft[team.Address] = t; Changed(); };
                DockPanel.SetDock(input, Dock.Right);
                line.Children.Add(input);
                line.Children.Add(new TextBlock { Text = team.Index == 0 ? n.DisplayName : $"{n.DisplayName} {team.Index + 1}", FontSize = 11.5, Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                teamStack.Children.Add(new Border
                {
                    Background = tIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 2),
                    Child = line,
                });
            }
            teamCol[1].Children.Add(UiKit.Card("Teams Reputation", teamStack));
        }

        // Rival Nations (pages[4])
        var rivals = backend.Session.NationRivalries.Read(n, nationRows.Select(r => r.Nation).ToArray());
        if (rivals.Count > 0)
        {
            var rivalRows = new List<(string Label, Control Control)>();
            foreach (var rival in rivals)
            {
                var input = new TextBox { Text = rivalryDraft.TextFor(rival), Width = 84, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                input.TextChanged += (_, _) => { rivalryDraft.Set(rival, input.Text ?? ""); Changed(); };
                rivalRows.Add((rival.Opponent, input));
            }
            var rivalCard = BuildRowsCard("Rival Nations", rivalRows);
            rivalCard.MaxWidth = 540;
            rivalCard.HorizontalAlignment = HorizontalAlignment.Left;
            pages[4].Children.Add(rivalCard);
        }

        // Transfer Preferences & Continents (pages[5])
        int preferenceVector = backend.Session.Profile.Nation?.Preferences?.Vector ?? 0xF0;
        var nationsKey = (n.Address, preferenceVector);
        var nationTargets = nationPreferenceTargets.TryGetValue(nationsKey, out var nt) ? nt : nationPreferenceTargets[nationsKey] = [];
        var nationRemovals = nationPreferenceRemovals.TryGetValue(nationsKey, out var nr) ? nr : nationPreferenceRemovals[nationsKey] = [];
        var preferences = backend.Session.NationPreferences.Read(n, nationRows.Select(r => r.Nation).ToArray())
            .Where(p => !nationRemovals.Contains(p.Address)).ToArray();
        {
            var options = nationRows.ToArray();
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64,64,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 8) };
            foreach (var heading in new[] { "Nation", "Rating", "Scouting", "Min age", "Max age" }.Select((text, i) => (text, i)))
            {
                if (list.RowDefinitions.Count == 0) list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = heading.text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8"), HorizontalAlignment = heading.i >= 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                Grid.SetColumn(label, heading.i); list.Children.Add(label);
            }
            foreach (var preference in preferences)
            {
                int row = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var pick = new ComboBox
                {
                    ItemsSource = options, Width = 160, MinHeight = 24, FontSize = 11.5,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<NationRow>((entry, _) => new TextBlock { Text = entry is null ? "" : entry.Name }),
                    SelectedItem = options.FirstOrDefault(o => backend.Session.NationPreferences.TargetFor(o.Nation) == (nationTargets.TryGetValue(preference.Address, out ulong staged) ? staged : preference.TargetTeam)),
                };
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedItem is not NationRow chosen) return;
                    ulong chosenTeam = backend.Session.NationPreferences.TargetFor(chosen.Nation);
                    if (chosenTeam == preference.TargetTeam) nationTargets.Remove(preference.Address);
                    else nationTargets[preference.Address] = chosenTeam;
                    Changed();
                };
                Grid.SetRow(pick, row); list.Children.Add(pick);
                var texts = preferenceDraft.TextFor(preference);
                for (int i = 0; i < 4; i++)
                {
                    int column = i;
                    var input = new TextBox { Text = texts[i], MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                    input.TextChanged += (_, _) => { preferenceDraft.Set(preference, column, input.Text ?? ""); Changed(); };
                    Grid.SetRow(input, row); Grid.SetColumn(input, i + 1); list.Children.Add(input);
                }
                var remove = new Button { Content = "\u2212", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) => { nationRemovals.Add(preference.Address); RenderNationEditor(); };
                Grid.SetRow(remove, row); Grid.SetColumn(remove, 5); list.Children.Add(remove);
            }
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) => { nationPreferenceAdds[nationsKey] = nationPreferenceAdds.GetValueOrDefault(nationsKey) + 1; RenderNationEditor(); };
            pages[5].Children.Add(UiKit.Card("Transfer Preferences", list, rightHeader: add));
        }

        int continentVector = Fm27Editor.Core.Nations.NationPreferences.ContinentsVector;
        var continentsKey = (n.Address, continentVector);
        var continentTargets = nationPreferenceTargets.TryGetValue(continentsKey, out var ct) ? ct : nationPreferenceTargets[continentsKey] = [];
        var continentRemovals = nationPreferenceRemovals.TryGetValue(continentsKey, out var cr) ? cr : nationPreferenceRemovals[continentsKey] = [];
        var continentPreferences = backend.Session.NationPreferences.ReadContinents(n, continents)
            .Where(p => !continentRemovals.Contains(p.Address)).ToArray();
        {
            var clist = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 8) };
            foreach (var heading in new[] { "Continent", "Rating", "Min age", "Max age" }.Select((text, i) => (text, i)))
            {
                if (clist.RowDefinitions.Count == 0) clist.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = heading.text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8"), HorizontalAlignment = heading.i >= 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                Grid.SetColumn(label, heading.i); clist.Children.Add(label);
            }
            foreach (var preference in continentPreferences)
            {
                int row = clist.RowDefinitions.Count; clist.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var pick = new ComboBox
                {
                    ItemsSource = continents, Width = 160, MinHeight = 24, FontSize = 11.5,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ContinentRecord>((entry, _) => new TextBlock { Text = entry is null ? "" : entry.Name }),
                    SelectedItem = continents.FirstOrDefault(c => c.Address == (continentTargets.TryGetValue(preference.Address, out ulong stagedContinent) ? stagedContinent : preference.TargetTeam)),
                };
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedItem is not ContinentRecord chosen) return;
                    if (chosen.Address == preference.TargetTeam) continentTargets.Remove(preference.Address);
                    else continentTargets[preference.Address] = chosen.Address;
                    Changed();
                };
                Grid.SetRow(pick, row); clist.Children.Add(pick);
                var texts = preferenceDraft.TextFor(preference);
                foreach (var (column, text) in new[] { (0, texts[0]), (2, texts[2]), (3, texts[3]) })
                {
                    int index = column;
                    var input = new TextBox { Text = text, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                    input.TextChanged += (_, _) => { preferenceDraft.Set(preference, index, input.Text ?? ""); Changed(); };
                    Grid.SetRow(input, row); Grid.SetColumn(input, column + 1); clist.Children.Add(input);
                }
                var remove = new Button { Content = "\u2212", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) => { continentRemovals.Add(preference.Address); RenderNationEditor(); };
                Grid.SetRow(remove, row); Grid.SetColumn(remove, 4); clist.Children.Add(remove);
            }
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) => { nationPreferenceAdds[continentsKey] = nationPreferenceAdds.GetValueOrDefault(continentsKey) + 1; RenderNationEditor(); };
            pages[5].Children.Add(UiKit.Card("Continents", clist, rightHeader: add));
        }

        save = new Button { Content = "Save changes", IsEnabled = Dirty(), Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = Dirty() };
        nationSaveButton = save;
        save.Click += (_, _) => SaveNation(n, draft, values, rivalryDraft, preferenceDraft, continentNow, continents, teamDraft);
        discard.Click += (_, _) =>
        {
            nationDrafts.Remove(n.Address); nationRivalryDrafts.Remove(n.Address); nationPreferenceDrafts.Remove(n.Address);
            nationContinentDrafts.Remove(n.Address); nationTeamDrafts.Remove(n.Address); nationKitDrafts.Remove(n.Address);
            nationColourDrafts.Remove(n.Address);
            foreach (var stale in nationKitItemDrafts.Keys.Where(k => k.Nation == n.Address).ToArray()) nationKitItemDrafts.Remove(stale);
            foreach (var stale in agreementDates.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementDates.Remove(stale);
            foreach (var stale in agreementTargets.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementTargets.Remove(stale);
            foreach (var stale in agreementRemovals.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementRemovals.Remove(stale);
            foreach (var stale in agreementAdds.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementAdds.Remove(stale);
            RenderNationEditor();
        };

        var agreementsPanel = new StackPanel { Spacing = 0 };
        var allNations = nationRows.Select(r => r.Nation).ToArray();
        var nationOptions = allNations.Select(x => new AgreementTargetOption(x.Address, x.DisplayName)).OrderBy(x => x.Name).ToArray();
        var continentOptions = continents.Select(x => new AgreementTargetOption(x.Address, x.Name)).ToArray();
        var regionOptions = backend.Session.NationAgreements.Regions().Select(x => new AgreementTargetOption(x.Address, x.Name)).ToArray();
        if (agreementOrganisations is null)
        {
            var found = new Dictionary<ulong, AgreementTargetOption>();
            foreach (var nation in allNations)
                foreach (int source in new[] { NationAgreements.EuVector, NationAgreements.NonForeignVector })
                {
                    try
                    {
                        foreach (var item in backend.Session.NationAgreements.Read(nation.Address, source, continents, allNations))
                            if (item.Kind == AgreementKind.Agreement && !string.IsNullOrWhiteSpace(item.TargetName))
                                found.TryAdd(item.Target, new(item.Target, item.TargetName));
                    }
                    catch (Fm27Editor.Core.Memory.MemoryAccessException) { }
                }
            agreementOrganisations = found.Values.OrderBy(x => x.Name).ToArray();
        }
        int agGroupIdx = 0;
        foreach (var (title, vector) in new[] { ("NATIONS TREATED AS EU", Fm27Editor.Core.Nations.NationAgreements.EuVector),
                     ("NATIONS TREATED AS NON-FOREIGN", Fm27Editor.Core.Nations.NationAgreements.NonForeignVector) })
        {
            var rows = backend.Session.NationAgreements.Read(n.Address, vector, continents, nationRows.Select(r => r.Nation).ToArray());
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("122,*,96,96,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 6, 12, 8) };
            void Reload() => RenderNationEditor();
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) =>
            {
                var addKey = (n.Address, vector);
                var source = rows.FirstOrDefault();
                ulong sourceNation = n.Address;
                byte[]? emptyHeader = null;
                if (source is null)
                {
                    if (vector != NationAgreements.NonForeignVector) { status.Text = "Agreements: this list has no row to copy"; return; }
                    if (agreementAdds.TryGetValue(addKey, out var existing) && existing.Count > 0)
                    {
                        source = existing[0].Source;
                        sourceNation = existing[0].SourceNation;
                        emptyHeader = existing[0].EmptyHeader;
                    }
                    else
                    {
                        foreach (var other in allNations.Where(candidate => candidate.Address != n.Address))
                        {
                            var template = backend.Session.NationAgreements.Read(other.Address, vector, continents, allNations)
                                .FirstOrDefault(candidate => candidate.Kind == AgreementKind.Nation);
                            if (template is null) continue;
                            source = template;
                            sourceNation = other.Address;
                            break;
                        }
                        if (source is null) { status.Text = "Agreements: no Non-Foreign template is available"; return; }
                        emptyHeader = backend.Session.Memory.Read(n.Address + (ulong)vector, 24);
                    }
                }
                if (!agreementAdds.TryGetValue(addKey, out var pending)) agreementAdds[addKey] = pending = [];
                pending.Add(new AgreementAddition(source, sourceNation, emptyHeader));
                Reload();
            };
            var subHeader = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(add, Dock.Right);
            subHeader.Children.Add(add);
            subHeader.Children.Add(new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center });
            agreementsPanel.Children.Add(new Border
            {
                Background = Brush("#161A26"),
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, agGroupIdx++ > 0 ? 1 : 0, 0, 1),
                Padding = new Thickness(12, 4),
                Child = subHeader,
            });
            foreach (var heading in new[] { "Type", "Target", "From", "Until" }.Select((text, i) => (text, i)))
            {
                if (list.RowDefinitions.Count == 0) list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = heading.text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8") };
                Grid.SetColumn(label, heading.i); list.Children.Add(label);
            }
            foreach (var row in rows)
            {
                if (agreementRemovals.ContainsKey((n.Address, row.Address))) continue;
                int index = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var types = new List<AgreementTypeOption> { new(AgreementKind.Nation, "Nation"), new(AgreementKind.ExcludedNation, "Excluded Nation"), new(AgreementKind.Continent, "Continent"), new(AgreementKind.Agreement, "Agreement") };
                if (regionOptions.Length > 0 || row.Kind == AgreementKind.Region) types.Add(new(AgreementKind.Region, "Region"));
                if (row.Kind == AgreementKind.Unknown) types.Add(new(AgreementKind.Unknown, "?"));
                var key = (n.Address, row.Address);
                var pendingTarget = agreementTargets.GetValueOrDefault(key);
                var chosenKind = pendingTarget.Row is null ? row.Kind : pendingTarget.Kind;
                ulong chosenTarget = pendingTarget.Row is null ? row.Target : pendingTarget.Target;
                var typeBox = new ComboBox { ItemsSource = types, SelectedItem = types.First(x => x.Kind == chosenKind), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                var targetBox = new ComboBox { MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                IReadOnlyList<AgreementTargetOption> Choices(AgreementKind kind) => kind switch
                {
                    AgreementKind.Nation or AgreementKind.ExcludedNation => nationOptions,
                    AgreementKind.Continent => continentOptions,
                    AgreementKind.Agreement => agreementOrganisations,
                    AgreementKind.Region => regionOptions.Length > 0 ? regionOptions : row.Kind == AgreementKind.Region && row.Target != 0 ? [new(row.Target, row.TargetName ?? "")] : [],
                    _ => row.Target == 0 ? [] : [new(row.Target, row.TargetName ?? $"0x{row.Target:X}")]
                };
                void SetChoices(AgreementKind kind, ulong target)
                {
                    var choices = Choices(kind);
                    targetBox.ItemsSource = choices;
                    targetBox.SelectedItem = choices.FirstOrDefault(x => x.Address == target) ?? choices.FirstOrDefault();
                }
                SetChoices(chosenKind, chosenTarget);
                void TargetChanged()
                {
                    if (typeBox.SelectedItem is not AgreementTypeOption type || targetBox.SelectedItem is not AgreementTargetOption target) return;
                    if (type.Kind == row.Kind && target.Address == row.Target) agreementTargets.Remove(key);
                    else agreementTargets[key] = (row, type.Kind, target.Address);
                    Changed();
                }
                typeBox.SelectionChanged += (_, _) => { if (typeBox.SelectedItem is AgreementTypeOption type) { SetChoices(type.Kind, 0); TargetChanged(); } };
                targetBox.SelectionChanged += (_, _) => TargetChanged();
                string fromOriginal = row.From?.ToString("yyyy-MM-dd", Inv) ?? "";
                string untilOriginal = row.Until?.ToString("yyyy-MM-dd", Inv) ?? "";
                var pendingDates = agreementDates.GetValueOrDefault(key);
                var fromBox = new TextBox { Text = pendingDates.Row is null ? fromOriginal : pendingDates.From, Watermark = "No limit", MinHeight = 24 };
                var untilBox = new TextBox { Text = pendingDates.Row is null ? untilOriginal : pendingDates.Until, Watermark = "No limit", MinHeight = 24 };
                void DatesChanged()
                {
                    string from = fromBox.Text ?? "", until = untilBox.Text ?? "";
                    if (from == fromOriginal && until == untilOriginal) agreementDates.Remove(key);
                    else agreementDates[key] = (row, from, until);
                    Changed();
                }
                fromBox.TextChanged += (_, _) => DatesChanged();
                untilBox.TextChanged += (_, _) => DatesChanged();
                Control[] cells =
                {
                    typeBox, targetBox,
                    fromBox, untilBox,
                };
                for (int i = 0; i < cells.Length; i++) { Grid.SetRow(cells[i], index); Grid.SetColumn(cells[i], i); list.Children.Add(cells[i]); }
                var remove = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) =>
                {
                    agreementDates.Remove(key);
                    agreementTargets.Remove(key);
                    agreementRemovals[key] = row;
                    Reload();
                };
                Grid.SetRow(remove, index); Grid.SetColumn(remove, 4); list.Children.Add(remove);
            }
            var pendingKey = (n.Address, vector);
            if (agreementAdds.TryGetValue(pendingKey, out var additions))
            {
                foreach (var addition in additions.ToArray())
                {
                    int index = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var kinds = new List<AgreementTypeOption> { new(AgreementKind.Nation, "Nation"), new(AgreementKind.ExcludedNation, "Excluded Nation"), new(AgreementKind.Continent, "Continent"), new(AgreementKind.Agreement, "Agreement") };
                    if (regionOptions.Length > 0 || addition.Kind == AgreementKind.Region) kinds.Add(new(AgreementKind.Region, "Region"));
                    if (addition.Kind == AgreementKind.Unknown) kinds.Add(new(AgreementKind.Unknown, "?"));
                    var type = new ComboBox { ItemsSource = kinds, SelectedItem = kinds.First(x => x.Kind == addition.Kind), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var target = new ComboBox { MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                    void SelectTargets(AgreementKind kind, ulong selected)
                    {
                        IReadOnlyList<AgreementTargetOption> options = kind switch
                        {
                            AgreementKind.Nation or AgreementKind.ExcludedNation => nationOptions,
                            AgreementKind.Continent => continentOptions,
                            AgreementKind.Agreement => agreementOrganisations,
                            AgreementKind.Region => regionOptions.Length > 0 ? regionOptions : addition.Source.Kind == AgreementKind.Region && addition.Source.Target != 0 ? [new(addition.Source.Target, addition.Source.TargetName ?? "")] : [],
                            _ => addition.Source.Target == 0 ? [] : [new(addition.Source.Target, addition.Source.TargetName ?? "")]
                        };
                        target.ItemsSource = options;
                        target.SelectedItem = options.FirstOrDefault(x => x.Address == selected) ?? options.FirstOrDefault();
                    }
                    SelectTargets(addition.Kind, addition.Target);
                    type.SelectionChanged += (_, _) => { if (type.SelectedItem is AgreementTypeOption chosen) { addition.Kind = chosen.Kind; SelectTargets(chosen.Kind, 0); if (target.SelectedItem is AgreementTargetOption item) addition.Target = item.Address; } };
                    target.SelectionChanged += (_, _) => { if (target.SelectedItem is AgreementTargetOption chosen) addition.Target = chosen.Address; };
                    var from = new TextBox { Text = addition.From, Watermark = "No limit", MinHeight = 24 };
                    var until = new TextBox { Text = addition.Until, Watermark = "No limit", MinHeight = 24 };
                    from.TextChanged += (_, _) => addition.From = from.Text ?? "";
                    until.TextChanged += (_, _) => addition.Until = until.Text ?? "";
                    Control[] cells = [type, target, from, until];
                    for (int column = 0; column < cells.Length; column++) { Grid.SetRow(cells[column], index); Grid.SetColumn(cells[column], column); list.Children.Add(cells[column]); }
                    var cancel = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                    cancel.Click += (_, _) => { var pending = agreementAdds[pendingKey]; pending.Remove(addition); if (pending.Count == 0) agreementAdds.Remove(pendingKey); Reload(); };
                    Grid.SetRow(cancel, index); Grid.SetColumn(cancel, 4); list.Children.Add(cancel);
                }
            }
            agreementsPanel.Children.Add(list);
        }
        pages[0].Children.Add(UiKit.Card("Agreements", agreementsPanel));

        var coefficients = backend.Session.NationEuroCoefficients.Read(n.Address);
        if (coefficients.Count > 0)
        {
            var season = (backend.Session.ReadGameDate()?.Year ?? 0) is var year && year > 1900 ? year : 0;
            var labels = Fm27Editor.Core.Nations.NationEuroCoefficients.Labels(season, coefficients.Count);
            var coeffRows = Enumerable.Range(0, coefficients.Count)
                .Select(i => (labels[i], (Control)new TextBlock { Text = coefficients[i].ToString("0.###", Inv), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }));
            teamCol[0].Children.Add(BuildRowsCard("Euro Coefficients", coeffRows));
        }

        string[] tabNames = ["Information", "National Teams", "Squads", "Kits and Colours", "Relationships", "Transfer Preferences"];
        var nationSections = tabNames.Select((name, index) => new SectionHost.Section(name, name, pages[index])).ToArray();
        nationEditor.Children.Add(SectionHost.Build(
            "nation",
            nationSections,
            tabNames[Math.Clamp(nationSelectedTab, 0, tabNames.Length - 1)],
            key => nationSelectedTab = Array.IndexOf(tabNames, key),
            nationRailHost));

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (n.Id is { } nid)
            badges.Children.Add(UiKit.MetaText($"ID {nid.ToString(Inv)}"));
        if (values.TryGetValue(NationField.FifaPosition, out var fifa) && fifa > 0)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"FIFA #{fifa.ToString(Inv)}", highlight: true));
        }
        if (values.TryGetValue(NationField.YouthRating, out var youth))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"Youth {youth.ToString(Inv)}"));
        }

        var logoImg = graphics.Image("nation", n.Id, 72);
        nationHeader.Content = UiKit.EntityBanner(n.DisplayName, badges, null, logoImg);
    }

    void SaveNation(NationRecord n, Dictionary<NationField, string> draft, IReadOnlyDictionary<NationField, long> snapshot, NationRivalryDraft rivalryDraft, NationPreferenceDraft preferenceDraft,
        ulong continentNow, IReadOnlyList<ContinentRecord> continents, Dictionary<ulong, string> teamDraft)
    {
        if (!NationLabels.TryParse(draft, out var v, out var error)) { status.Text = error; return; }
        if (!rivalryDraft.TryParse(out var rivals)) { status.Text = "Rivalry: enter a whole number 0–100"; return; }
        if (!preferenceDraft.TryParse(out var preferences)) { status.Text = "Transfer preferences: check values and age range"; return; }
        var dates = new List<(NationAgreementRow Row, DateOnly? From, DateOnly? Until)>();
        foreach (var (_, entry) in agreementDates.Where(kv => kv.Key.Nation == n.Address))
        {
            bool Parse(string text, out DateOnly? result)
            {
                result = null;
                if (string.IsNullOrWhiteSpace(text)) return true;
                if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var value) || value.Year <= 1900) return false;
                result = value; return true;
            }
            if (!Parse(entry.From, out var from) || !Parse(entry.Until, out var until)) { status.Text = "Agreement dates: use yyyy-MM-dd or leave blank"; return; }
            dates.Add((entry.Row, from, until));
        }
        var teamValues = new Dictionary<ulong, int>();
        foreach (var (address, text) in teamDraft)
        {
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int rep) || rep < 0 || rep > NationTeamReader.MaxReputation)
            { status.Text = $"Team reputation: enter a whole number 0 to {NationTeamReader.MaxReputation}"; return; }
            teamValues[address] = rep;
        }
        int kitChanges = 0, colourChanges = 0, preferenceStructure = 0, kitColourChanges = 0;
        var targetChanges = agreementTargets.Where(kv => kv.Key.Nation == n.Address).ToDictionary(kv => kv.Key.Address, kv => kv.Value);
        var adds = agreementAdds.Where(kv => kv.Key.Nation == n.Address).SelectMany(kv => kv.Value).ToArray();
        if (adds.GroupBy(addition => addition.Source.Vector).Any(group => (group.First().EmptyHeader is null ? (group.First().Source.End - group.First().Source.Begin) / 8 : 0) + (ulong)group.Count() > 64))
        { status.Text = "Too many agreement rows."; return; }
        bool ParseAdditionDate(string text, out DateOnly? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var value) || value.Year <= 1900) return false;
            result = value; return true;
        }
        var addDates = new Dictionary<AgreementAddition, (DateOnly? From, DateOnly? Until)>();
        foreach (var addition in adds)
        {
            if (!ParseAdditionDate(addition.From, out var from) || !ParseAdditionDate(addition.Until, out var until))
            { status.Text = "Agreement dates: use yyyy-MM-dd or leave blank"; return; }
            addDates[addition] = (from, until);
        }
        try
        {
            var acc = backend.Session.NationFields;
            bool continentChanged = nationContinentDrafts.TryGetValue(n.Address, out ulong chosenContinent);
            backend.Edits.Execute(() =>
            {
                var agreements = backend.Session.NationAgreements;
                foreach (var addition in adds)
                {
                    var source = addition.Source;
                    var current = agreements.Read(addition.SourceNation, source.Vector, continents).FirstOrDefault(x => x.Address == source.Address);
                    if (current is null || current.Begin != source.Begin || current.End != source.End || current.Capacity != source.Capacity || current.Slot != source.Slot || !current.Snapshot.AsSpan().SequenceEqual(source.Snapshot))
                        throw new Fm27Editor.Core.Players.StaleObjectException("Agreement source changed since it was selected for duplication");
                    if (addition.EmptyHeader is { } empty && !backend.Session.Memory.Read(n.Address + (ulong)source.Vector, 24).AsSpan().SequenceEqual(empty))
                        throw new Fm27Editor.Core.Players.StaleObjectException("The empty agreement list changed since it was read");
                }
                foreach (var removal in agreementRemovals.Where(kv => kv.Key.Nation == n.Address).Select(kv => kv.Value))
                {
                    var current = agreements.Read(n.Address, removal.Vector, continents).FirstOrDefault(x => x.Address == removal.Address);
                    if (current is null || current.Begin != removal.Begin || current.End != removal.End || current.Capacity != removal.Capacity || current.Slot != removal.Slot || !current.Snapshot.AsSpan().SequenceEqual(removal.Snapshot))
                        throw new Fm27Editor.Core.Players.StaleObjectException("Agreement row changed since it was selected for removal");
                }
                foreach (var change in targetChanges.Values) agreements.SetTarget(n.Address, change.Row, change.Kind, change.Target);
                foreach (var date in dates)
                {
                    var row = date.Row;
                    if (targetChanges.TryGetValue(row.Address, out var change))
                    {
                        var current = agreements.Read(n.Address, row.Vector, continents, nationRows.Select(x => x.Nation).ToArray()).Single(x => x.Address == row.Address);
                        byte[] expected = (byte[])row.Snapshot.Clone();
                        Array.Clear(expected, 0, 0x28);
                        int offset = change.Kind switch { AgreementKind.Nation => 0, AgreementKind.Continent => 8, AgreementKind.ExcludedNation => 16, AgreementKind.Region => 24, AgreementKind.Agreement => 32, _ => throw new InvalidOperationException("Unsupported agreement type") };
                        BitConverter.GetBytes(change.Target).CopyTo(expected, offset);
                        if (current.Begin != row.Begin || current.End != row.End || current.Capacity != row.Capacity || current.Slot != row.Slot || !current.Snapshot.AsSpan().SequenceEqual(expected))
                            throw new Fm27Editor.Core.Players.StaleObjectException("Agreement changed while saving");
                        row = current;
                    }
                    agreements.SetDates(n.Address, row, date.From, date.Until);
                }
                foreach (var addition in adds)
                {
                    byte[] expected = (byte[])addition.Source.Snapshot.Clone();
                    if (targetChanges.TryGetValue(addition.Source.Address, out var changed))
                    {
                        Array.Clear(expected, 0, 0x28);
                        int offset = changed.Kind switch { AgreementKind.Nation => 0, AgreementKind.Continent => 8, AgreementKind.ExcludedNation => 16, AgreementKind.Region => 24, AgreementKind.Agreement => 32, _ => throw new InvalidOperationException("Unsupported agreement type") };
                        BitConverter.GetBytes(changed.Target).CopyTo(expected, offset);
                    }
                    var changedDate = dates.FirstOrDefault(date => date.Row.Address == addition.Source.Address);
                    if (changedDate.Row is not null)
                    {
                        void EncodeDate(int offset, DateOnly? value)
                        {
                            var date = value ?? new DateOnly(1900, 1, 1);
                            ushort day = (ushort)((BitConverter.ToUInt16(expected, offset) & 0xFE00) | date.DayOfYear);
                            BitConverter.GetBytes(day).CopyTo(expected, offset);
                            BitConverter.GetBytes((short)date.Year).CopyTo(expected, offset + 2);
                        }
                        EncodeDate(NationAgreements.FromOffset, changedDate.From);
                        EncodeDate(NationAgreements.UntilOffset, changedDate.Until);
                    }
                    var source = agreements.Read(addition.SourceNation, addition.Source.Vector, continents).FirstOrDefault(x => x.Address == addition.Source.Address);
                    if (source is null || !source.Snapshot.AsSpan().SequenceEqual(expected))
                        throw new Fm27Editor.Core.Players.StaleObjectException("Agreement source changed since it was selected for duplication");
                    var allocate = (backend.Session.Memory as Fm27Editor.Core.Memory.WindowsProcessMemory) is { } wpm ? wpm.Allocate : (Func<int, ulong>?)null;
                    NationAgreementRow? added;
                    if (addition.EmptyHeader is { } empty)
                    {
                        var currentRows = agreements.Read(n.Address, addition.Source.Vector, continents);
                        added = currentRows.Count == 0
                            ? agreements.AddToEmpty(n.Address, empty, addition.SourceNation, source, allocate, continents)
                            : agreements.Duplicate(n.Address, currentRows[0], allocate, continents);
                    }
                    else added = agreements.Duplicate(n.Address, source, allocate, continents);
                    if (added is null) throw new Fm27Editor.Core.Memory.MemoryAccessException("Could not read the new agreement");
                    if (added.Kind != addition.Kind || added.Target != addition.Target)
                    {
                        agreements.SetTarget(n.Address, added, addition.Kind, addition.Target);
                        added = agreements.Read(n.Address, addition.Source.Vector, continents).FirstOrDefault(x => x.Address == added.Address)
                            ?? throw new Fm27Editor.Core.Players.StaleObjectException("The new agreement changed while saving");
                    }
                    var addDate = addDates[addition];
                    if (added.From != addDate.From || added.Until != addDate.Until) agreements.SetDates(n.Address, added, addDate.From, addDate.Until);
                }
                foreach (var removal in agreementRemovals.Where(kv => kv.Key.Nation == n.Address).Select(kv => kv.Value).ToArray())
                {
                    var current = agreements.Read(n.Address, removal.Vector, continents).FirstOrDefault(x => x.Address == removal.Address)
                        ?? throw new Fm27Editor.Core.Players.StaleObjectException("Agreement row changed since it was selected for removal");
                    if (!current.Snapshot.AsSpan().SequenceEqual(removal.Snapshot))
                        throw new Fm27Editor.Core.Players.StaleObjectException("Agreement row changed since it was selected for removal");
                    agreements.Remove(n.Address, current);
                }
                foreach (var (f, value) in v) acc.Set(n.Address, f, snapshot[f], value);
                foreach (var (rival, value) in rivals) backend.Session.NationRivalries.SetValue(rival, value);
                foreach (var p in preferences)
                {
                    bool isContinent = continents.Any(c => c.Address == p.Row.TargetTeam);
                    if (isContinent) backend.Session.NationPreferences.SetContinent(p.Row, p.Rating, p.MinAge, p.MaxAge, p.Knowledge);
                    else backend.Session.NationPreferences.Set(p.Row, p.Rating, p.MinAge, p.MaxAge, p.Knowledge);
                }
                int preferenceVectorSave = backend.Session.Profile.Nation?.Preferences?.Vector ?? 0xF0;
                foreach (int vectorSave in new[] { preferenceVectorSave, Fm27Editor.Core.Nations.NationPreferences.ContinentsVector })
                {
                    var key = (n.Address, vectorSave);
                    IReadOnlyList<Fm27Editor.Core.Nations.NationPreference> Rows() => vectorSave == Fm27Editor.Core.Nations.NationPreferences.ContinentsVector
                        ? backend.Session.NationPreferences.ReadContinents(n, continents)
                        : backend.Session.NationPreferences.Read(n, backend.Session.Nations.LoadAll());
                    if (nationPreferenceTargets.TryGetValue(key, out var targets))
                        foreach (var (address, target) in targets.ToArray())
                        {
                            var row = Rows().FirstOrDefault(x => x.Address == address) ?? throw new Fm27Editor.Core.Players.StaleObjectException("Preference row changed");
                            backend.Session.NationPreferences.SetTarget(row, vectorSave, target);
                            preferenceStructure++;
                        }
                    if (nationPreferenceRemovals.TryGetValue(key, out var removals))
                        foreach (var address in removals.ToArray())
                        {
                            var row = Rows().FirstOrDefault(x => x.Address == address) ?? throw new Fm27Editor.Core.Players.StaleObjectException("Preference row changed");
                            backend.Session.NationPreferences.Remove(row, vectorSave);
                            preferenceStructure++;
                        }
                    if (nationPreferenceAdds.TryGetValue(key, out int prefAdds) && prefAdds > 0)
                    {
                        var rows = Rows();
                        ulong team = rows.Count > 0 ? rows[0].Team : backend.Session.NationPreferences.TargetFor(n);
                        ulong target = rows.Count > 0 ? rows[0].TargetTeam
                            : vectorSave == Fm27Editor.Core.Nations.NationPreferences.ContinentsVector
                                ? (continents.Count > 0 ? continents[0].Address : 0)
                                : backend.Session.NationPreferences.TargetFor(backend.Session.Nations.LoadAll().First(x => x.Address != n.Address));
                        if (target != 0)
                            for (int i = 0; i < prefAdds; i++)
                            {
                                sbyte rating = rows.Count > 0 ? rows[0].Rating : (sbyte)-1;
                                byte minAge = rows.Count > 0 ? rows[0].MinAge : (byte)14;
                                byte maxAge = rows.Count > 0 ? rows[0].MaxAge : (byte)45;
                                byte knowledge = rows.Count > 0 ? rows[0].Knowledge : (byte)0;
                                backend.Session.NationPreferences.Add(team, vectorSave, target, rating, minAge, maxAge, knowledge,
                                    (backend.Session.Memory as Fm27Editor.Core.Memory.WindowsProcessMemory) is { } wpm ? wpm.Allocate : null);
                                preferenceStructure++;
                            }
                    }
                }
                if (continentChanged) backend.Session.NationLinks.SetContinent(n, continentNow, continents.First(c => c.Address == chosenContinent));
                var teamReader = backend.Session.NationTeams;
                foreach (var team in teamReader.Read(n.Address).Where(t => teamValues.ContainsKey(t.Address))) teamReader.SetReputation(team, teamValues[team.Address]);
                if (nationColourDrafts.TryGetValue(n.Address, out var colourDraft) && backend.Session.NationColours.Read(n.Address) is { } colourNow)
                {
                    if (colourDraft.Background is { } bgText)
                    {
                        if (!Fm27Editor.Core.Nations.NationColours.TryParse(bgText, out uint bg)) throw new InvalidOperationException("Background colour must be #RRGGBB");
                        backend.Session.NationColours.Set(n.Address, colourNow, true, bg);
                        colourChanges++;
                    }
                    if (colourDraft.Foreground is { } fgText)
                    {
                        if (!Fm27Editor.Core.Nations.NationColours.TryParse(fgText, out uint fg)) throw new InvalidOperationException("Foreground colour must be #RRGGBB");
                        backend.Session.NationColours.Set(n.Address, colourNow, false, fg);
                        colourChanges++;
                    }
                }
                if (nationKitItemDrafts.Count > 0)
                {
                    var kitNow = backend.Session.NationKits.Read(n.Address);
                    foreach (var ((kitNation, slot), entry) in nationKitItemDrafts.Where(kv => kv.Key.Nation == n.Address).ToArray())
                    {
                        var item = kitNow.FirstOrDefault(x => x.Slot == slot) ?? throw new Fm27Editor.Core.Players.StaleObjectException("Kit item changed");
                        if (entry.Foreground is { } fgText)
                        {
                            if (!Fm27Editor.Core.Nations.NationColours.TryParse(fgText, out uint fg)) throw new InvalidOperationException("Kit foreground must be #RRGGBB");
                            backend.Session.NationKits.SetColour(item, 0, fg);
                            kitColourChanges++;
                        }
                        if (entry.Background is { } bgText)
                        {
                            if (!Fm27Editor.Core.Nations.NationColours.TryParse(bgText, out uint bg)) throw new InvalidOperationException("Kit background must be #RRGGBB");
                            backend.Session.NationKits.SetColour(item, 1, bg);
                            kitColourChanges++;
                        }
                        if (entry.Style is { } styleText)
                        {
                            if (!int.TryParse(styleText, out int style)) throw new InvalidOperationException("Kit style must be a whole number");
                            backend.Session.NationKits.SetStyle(item, style);
                            kitColourChanges++;
                        }
                    }
                }
                if (nationKitDrafts.TryGetValue(n.Address, out bool kitsWanted) && teamReader.Read(n.Address).FirstOrDefault() is { } seniorTeam)
                {
                    teamReader.SetAllowLicensedKits(seniorTeam, kitsWanted);
                    kitChanges = 1;
                }
            });
            nationTeamDrafts.Remove(n.Address);
            nationKitDrafts.Remove(n.Address);
            nationColourDrafts.Remove(n.Address);
            foreach (var stale in nationKitItemDrafts.Keys.Where(k => k.Nation == n.Address).ToArray()) nationKitItemDrafts.Remove(stale);
            foreach (var stale in nationPreferenceTargets.Keys.Where(k => k.Nation == n.Address).ToArray()) nationPreferenceTargets.Remove(stale);
            foreach (var stale in nationPreferenceRemovals.Keys.Where(k => k.Nation == n.Address).ToArray()) nationPreferenceRemovals.Remove(stale);
            foreach (var stale in nationPreferenceAdds.Keys.Where(k => k.Nation == n.Address).ToArray()) nationPreferenceAdds.Remove(stale);
            nationContinentDrafts.Remove(n.Address);
            nationDrafts.Remove(n.Address);
            nationRivalryDrafts.Remove(n.Address);
            nationPreferenceDrafts.Remove(n.Address);
            foreach (var stale in agreementDates.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementDates.Remove(stale);
            foreach (var stale in agreementTargets.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementTargets.Remove(stale);
            foreach (var stale in agreementRemovals.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementRemovals.Remove(stale);
            foreach (var stale in agreementAdds.Keys.Where(k => k.Nation == n.Address).ToArray()) agreementAdds.Remove(stale);
            RenderNationEditor();
            int count = v.Count + rivals.Count + preferences.Count + (continentChanged ? 1 : 0) + teamValues.Count + kitChanges + colourChanges + preferenceStructure + kitColourChanges;
            status.Text = $"Saved {count} change{(count == 1 ? "" : "s")} to {n.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
