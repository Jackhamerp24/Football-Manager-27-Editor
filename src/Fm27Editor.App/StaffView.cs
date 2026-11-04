using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record StaffNavItem
    {
        public string Title => "Staff";
        public override string ToString() => Title;
    }

    /// <summary>One grid row; columns bind to the plain properties.</summary>
    public sealed record StaffRow(StaffRecord Staff, string Name, string Job, string Club, string Nation, int? Age, int CA, int PA)
    {
        public string SearchName
        {
            get
            {
                if (searchNameSource != Name) { searchNameSource = Name; searchName = Fold(Name); }
                return searchName!;
            }
        }
        string? searchNameSource = Name, searchName = Fold(Name);
    }

    readonly StaffNavItem staffNavItem = new();
    readonly Grid staffViewRoot = new() { IsVisible = false };
    readonly TextBox staffSearch = new() { Watermark = "Search staff name..." };
    readonly ComboBox staffClubFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox staffJobFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    const string AllStaffClubs = "All clubs", AllStaffJobs = "All jobs";
    bool updatingStaffFilters;
    readonly DataGrid staffGrid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"),
        RowHeight = 29,
        Background = Brush("#11131C"),
        BorderThickness = new Thickness(0),
    };
    readonly StackPanel staffEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, StaffDraft> staffDrafts = [];
    IReadOnlyList<StaffRow> staffRows = [];
    StaffRecord? selectedStaff;
    bool staffActive;
    (ulong Address, int Id)? pendingStaffNavigation;

    void RegisterStaffView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(staffViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, staffNavItem];

        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(StaffRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Job", Binding = new Binding(nameof(StaffRow.Job)), Width = new DataGridLength(130) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Club", Binding = new Binding(nameof(StaffRow.Club)), Width = new DataGridLength(120) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(StaffRow.Nation)), Width = new DataGridLength(110) });
        foreach (var (h, p) in new[] { ("Age", nameof(StaffRow.Age)), ("CA", nameof(StaffRow.CA)), ("PA", nameof(StaffRow.PA)) })
            staffGrid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = new DataGridLength(64) });

        staffSearch.TextChanged += (_, _) => FilterStaff();
        staffClubFilter.SelectionChanged += (_, _) => { if (!updatingStaffFilters) FilterStaff(); };
        staffJobFilter.SelectionChanged += (_, _) => { if (!updatingStaffFilters) FilterStaff(); };
        staffGrid.SelectionChanged += (_, _) =>
        {
            selectedStaff = (staffGrid.SelectedItem as StaffRow)?.Staff;
            RenderStaffEditor();
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var massEdit = new Button { Content = "Mass Edit" };
        massEdit.Click += async (_, _) => await ShowStaffMassEditAsync();
        var searchControls = new DockPanel();
        DockPanel.SetDock(massEdit, Dock.Right); searchControls.Children.Add(massEdit);
        searchControls.Children.Add(staffSearch);
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        Grid.SetColumn(staffClubFilter, 0); filters.Children.Add(staffClubFilter);
        Grid.SetColumn(staffJobFilter, 1); filters.Children.Add(staffJobFilter);
        var searchStack = new StackPanel { Spacing = 6, Children = { searchControls, filters } };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = searchStack };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(staffGrid);
        staffViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(760)) { MinWidth = 520 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 460 },
        };
        Grid.SetColumn(left, 0);
        staffViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        staffViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = staffEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        staffViewRoot.Children.Add(scroll);

        // Runs after the Clubs handler, which shows the players screen whenever Clubs is not selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toStaff = ReferenceEquals(modules.SelectedItem, staffNavItem);
            if (toStaff == staffActive) return;
            staffActive = toStaff;
            if (!toStaff) pendingStaffNavigation = null;
            staffViewRoot.IsVisible = toStaff;
            if (toStaff) playersSplit.IsVisible = false;
            else if (!clubsActive) playersSplit.IsVisible = true;
            if (toStaff && staffRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadStaffAsync();
            if (toStaff && staffActive) CompleteStaffNavigation();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            staffRows = [];
            updatingStaffFilters = true;
            staffClubFilter.ItemsSource = new[] { AllStaffClubs }; staffClubFilter.SelectedIndex = 0;
            staffJobFilter.ItemsSource = new[] { AllStaffJobs }; staffJobFilter.SelectedIndex = 0;
            updatingStaffFilters = false;
            pendingStaffNavigation = null;
            staffDrafts.Clear();
            staffMassUndo.Clear();
            staffMoveUndo.Clear();
            selectedStaff = null;
            staffGrid.ItemsSource = null;
            staffEditor.Children.Clear();
            if (staffActive && backend.State.Status == ConnectionStatus.Connected) await LoadStaffAsync();
        });
    }

    void OpenClubStaff(StaffRecord staff)
    {
        pendingStaffNavigation = (staff.Address, staff.Id);
        modules.SelectedItem = staffNavItem;
        if (staffActive && staffRows.Count > 0) CompleteStaffNavigation();
    }

    void CompleteStaffNavigation()
    {
        if (pendingStaffNavigation is not { } target) return;
        pendingStaffNavigation = null;
        var row = StaffNavigation.Find(staffRows, target.Address, target.Id);
        if (row is null) { status.Text = "Staff member no longer available."; return; }
        updatingStaffFilters = true;
        staffClubFilter.SelectedIndex = 0;
        staffJobFilter.SelectedIndex = 0;
        updatingStaffFilters = false;
        staffSearch.Text = "";
        FilterStaff();
        staffGrid.SelectedItem = row;
        staffGrid.ScrollIntoView(row, null);
    }

    async Task LoadStaffAsync()
    {
        status.Text = "Loading staff...";
        try
        {
            var session = backend.Session;
            var (all, date) = await Task.Run(() => (session.Staff.LoadAll(), session.ReadGameDate()));
            staffRows = all
                .Select(s => new StaffRow(s, s.DisplayName, StaffLabels.Job(s.JobCode), s.Club?.DisplayName ?? "", s.Nationality?.DisplayName ?? "",
                    date is { } d && s.DateOfBirth is { } b ? d.Year - b.Year - (d < b.AddYears(d.Year - b.Year) ? 1 : 0) : null,
                    s.CurrentAbility, s.PotentialAbility))
                .OrderByDescending(r => r.CA)
                .ToList();
            PopulateStaffFilters();
            FilterStaff();
            status.Text = $"{staffRows.Count:N0} staff";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterStaff()
    {
        string q = Fold(staffSearch.Text ?? "");
        string? club = staffClubFilter.SelectedItem is string c && c != AllStaffClubs ? c : null;
        string? job = staffJobFilter.SelectedItem is string j && j != AllStaffJobs ? j : null;
        staffGrid.ItemsSource = StaffFilter.Apply(staffRows, q, club, job);
    }

    void PopulateStaffFilters()
    {
        string? oldClub = staffClubFilter.SelectedItem as string, oldJob = staffJobFilter.SelectedItem as string;
        var clubs = new[] { AllStaffClubs }.Concat(staffRows.Select(r => r.Club).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)).ToArray();
        var jobs = new[] { AllStaffJobs }.Concat(staffRows.Select(r => r.Job).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)).ToArray();
        updatingStaffFilters = true;
        staffClubFilter.ItemsSource = clubs;
        staffClubFilter.SelectedIndex = Math.Max(0, Array.IndexOf(clubs, oldClub));
        staffJobFilter.ItemsSource = jobs;
        staffJobFilter.SelectedIndex = Math.Max(0, Array.IndexOf(jobs, oldJob));
        updatingStaffFilters = false;
    }

    static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }

    void RenderStaffEditor()
    {
        staffEditor.Children.Clear();
        if (selectedStaff is not { } s) return;
        if (!staffDrafts.TryGetValue(s.Address, out var draft)) staffDrafts[s.Address] = draft = new StaffDraft();
        Button? save = null, discard = null;

        TextBox Box(string key, string original, double width = 44)
        {
            var box = new TextBox
            {
                Text = draft.Changes.GetValueOrDefault(key, original), Width = width, MinHeight = 24, Padding = new Thickness(4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (key.StartsWith("attr:", StringComparison.Ordinal) || key.StartsWith("pers:", StringComparison.Ordinal))
                box.Foreground = int.TryParse(box.Text, out int v) ? RatingColor(v) : Brush("#F1F5F9");
            box.TextChanged += (_, _) =>
            {
                if (box.Text == original) draft.Changes.Remove(key); else draft.Changes[key] = box.Text ?? "";
                if (save is not null) save.IsEnabled = discard!.IsEnabled = draft.Changes.Count > 0;
            };
            return box;
        }
        TextBlock Label(string text, string color = "#CBD5E1") => new() { Text = text, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Border Panel(string title, Control body) => new()
        {
            Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                        Child = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } },
                    body,
                }
            }
        };
        Grid Rows(IEnumerable<(string Label, Control Value)> items)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var (label, value) in items)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = Label(label); Grid.SetRow(l, r); g.Children.Add(l);
                Grid.SetRow(value, r); Grid.SetColumn(value, 1); g.Children.Add(value);
            }
            return g;
        }

        staffEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = s.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold },
                Label(string.Join("  ·  ", new[] { StaffLabels.Job(s.JobCode), s.Club?.DisplayName, s.Nationality?.DisplayName }.Where(x => !string.IsNullOrEmpty(x))), "#94A3B8"),
            }
        });

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*") };
        var col = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        for (int i = 0; i < 3; i++) { Grid.SetColumn(col[i], i * 2); columns.Children.Add(col[i]); }

        col[0].Children.Add(Panel("Information", Rows([
            ("Unique ID", Label(s.Id.ToString(Inv))), ("Full Name", Label(s.FullName ?? "")),
            ("Born", Label(s.DateOfBirth?.ToString("d/M/yyyy", Inv) ?? "")),
        ])));
        col[0].Children.Add(Panel("Reputation", Rows([
            ("Home", Box("rep:Home", s.HomeReputation.ToString(Inv), 64)), ("Current", Box("rep:Current", s.CurrentReputation.ToString(Inv), 64)),
            ("World", Box("rep:World", s.WorldReputation.ToString(Inv), 64)),
        ])));
        col[0].Children.Add(Panel("Ability", Rows([("CA", Box("CA", s.CurrentAbility.ToString(Inv), 56)), ("PA", Box("PA", s.PotentialAbility.ToString(Inv), 56))])));
        if (s.ManagerAttributes is { } mgr)
            col[0].Children.Add(Panel("Manager", Rows(StaffLabels.Manager
                .Select(m => (m.Label, (Control)Box($"mgr:{m.Attribute}", mgr[(int)m.Attribute].ToString(Inv)))))));
        col[0].Children.Add(Panel("Personality", Rows(Enum.GetValues<StaffPersonality>()
            .Select(t => (t.ToString(), (Control)Box($"pers:{t}", s.Personality[(int)t].ToString(Inv)))))));
        int[] placement = [1, 2, 1, 2, 2, 2];
        for (int g = 0; g < StaffLabels.Groups.Length; g++)
        {
            var (title, items) = StaffLabels.Groups[g];
            col[placement[g]].Children.Add(Panel(title, Rows(items.Select(it => (it.Label, (Control)Box($"attr:{it.Attribute}", s.Attribute(it.Attribute).ToString(Inv)))))));
        }
        save = new Button { Content = "Save changes", IsEnabled = draft.Changes.Count > 0, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Changes.Count > 0 };
        save.Click += (_, _) => SaveStaff(s, draft);
        discard.Click += (_, _) => { staffDrafts.Remove(s.Address); RenderStaffEditor(); };
        var information = new StackPanel { Spacing = 8, Children = { columns, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } } } };

        var staffMoveTab = new TabItem { Header = "Move to club" };
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Information", Content = information },
                new TabItem { Header = "Contract", Content = StaffContractPanel(s) },
                new TabItem { Header = "Career Plans", Content = CareerPlansPanel(backend.Session.Owner(s), RenderStaffEditor) },
                new TabItem { Header = "Relationships", Content = RelationshipsPanel(backend.Session.Owner(s), RenderStaffEditor) },
                new TabItem { Header = "Languages", Content = LanguagesPanel(backend.Session.Owner(s), RenderStaffEditor) },
                new TabItem { Header = "Knowledge", Content = StaffKnowledgePanel(s) },
                new TabItem { Header = "Formations", Content = StaffFormationsPanel(s) },
                new TabItem { Header = "Tendencies", Content = StaffTendenciesPanel(s) },
                staffMoveTab,
            },
            SelectedIndex = staffTab,
        };
        void LoadMoveTab() { if (tabs.SelectedItem == staffMoveTab && staffMoveTab.Content is null) staffMoveTab.Content = StaffMovePanel(s); }
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex >= 0) staffTab = tabs.SelectedIndex; LoadMoveTab(); };
        LoadMoveTab();
        staffEditor.Children.Add(tabs);
    }

    int staffTab;

    /// <summary>FMRTE 27's staff Contract tab: Type, Wages, Started, Expires, On Rolling Contract.</summary>
    Control StaffContractPanel(StaffRecord s)
    {
        var panel = new StackPanel { Spacing = 8 };
        var access = backend.Session.StaffContracts;
        if (access.Read(s) is not { } contract) return panel;
        var save = new Button { Content = "Save contract", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        OfferContractEditor? editor = null;
        editor = OfferContractBox(contract, () => save.IsEnabled = discard.IsEnabled = editor?.Changed() == true, out var view);
        panel.Children.Add(Card(view));
        save.Click += (_, _) =>
        {
            if (editor.Parse() is not { } fields) { status.Text = "Contract: enter wages of 0 or more and dates like 2027-06-30, expiry after start"; return; }
            try
            {
                backend.Edits.Execute(() => access.Set(s, contract, fields));
                status.Text = $"Saved {s.DisplayName}'s contract. Continue in FM to see updates.";
                RenderStaffEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderStaffEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }

    void SaveStaff(StaffRecord s, StaffDraft draft)
    {
        if (!draft.TryParse(out var v, out var error)) { status.Text = error; return; }
        try
        {
            var w = backend.Session.StaffWriter;
            backend.Edits.Execute(() =>
            {
                foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("attr:", StringComparison.Ordinal)))
                    w.SetAttribute(s, Enum.Parse<StaffAttribute>(key[5..]), value);
                foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("mgr:", StringComparison.Ordinal)))
                    w.SetManagerAttribute(s, Enum.Parse<ManagerAttribute>(key[4..]), value);
                foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("pers:", StringComparison.Ordinal)))
                    w.SetPersonality(s, Enum.Parse<StaffPersonality>(key[5..]), value);
                if (v.ContainsKey("CA") || v.ContainsKey("PA"))
                    w.SetAbility(s, v.GetValueOrDefault("CA", s.CurrentAbility), v.GetValueOrDefault("PA", s.PotentialAbility));
                if (v.Keys.Any(k => k.StartsWith("rep:", StringComparison.Ordinal)))
                    w.SetReputation(s, v.GetValueOrDefault("rep:Home", s.HomeReputation), v.GetValueOrDefault("rep:Current", s.CurrentReputation),
                        v.GetValueOrDefault("rep:World", s.WorldReputation));
            });
            staffDrafts.Remove(s.Address);
            var fresh = backend.Session.Staff.Reload(s.Address);
            if (fresh is not null)
            {
                staffRows = staffRows.Select(r => r.Staff.Address == s.Address ? r with { Staff = fresh, CA = fresh.CurrentAbility, PA = fresh.PotentialAbility } : r).ToList();
                selectedStaff = fresh;
                FilterStaff();
            }
            RenderStaffEditor();
            status.Text = $"Saved {v.Count} change{(v.Count == 1 ? "" : "s")} to {s.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
