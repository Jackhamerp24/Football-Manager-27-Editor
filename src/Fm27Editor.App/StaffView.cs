using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
    readonly TextBox staffSearch = new() { Watermark = "Search staff name or ID...", Margin = new Thickness(0, 0, 6, 0) };
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
        RowHeight = 28,
        Background = Brush("#11131C"),
        BorderThickness = new Thickness(0),
    };
    readonly ContentControl staffHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly ContentControl staffRailHost = new() { Width = 168, Margin = new Thickness(12, 0, 0, 10) };
    readonly StackPanel staffEditor = new() { Spacing = 10, Margin = new Thickness(0, 0, 12, 12) };
    readonly Dictionary<ulong, StaffDraft> staffDrafts = [];
    IReadOnlyList<StaffRow> staffRows = [];
    StaffRecord? selectedStaff;
    Button? staffSaveButton;
    bool staffActive;
    Control staffDetailRoot = new Panel();
    (ulong Address, int Id)? pendingStaffNavigation;
    string staffSectionKey = "Information";

    void OpenSelectedStaff()
    {
        selectedStaff = (staffGrid.SelectedItem as StaffRow)?.Staff;
        if (selectedStaff is null) return;
        RenderStaffEditor();
        OpenKindTab("staff", selectedStaff.DisplayName, staffDetailRoot);
    }

    void RegisterStaffView()
    {
        AddModuleTab("Staff", staffViewRoot, staffNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, staffNavItem];

        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(StaffRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Job", Binding = new Binding(nameof(StaffRow.Job)), Width = new DataGridLength(136) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Club", Binding = new Binding(nameof(StaffRow.Club)), Width = new DataGridLength(130) });
        staffGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(StaffRow.Nation)), Width = new DataGridLength(110) });
        foreach (var (h, p) in new[] { ("Age", nameof(StaffRow.Age)), ("CA", nameof(StaffRow.CA)), ("PA", nameof(StaffRow.PA)) })
        {
            var col = new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = new DataGridLength(68) };
            col.CellStyleClasses.Add("numeric");
            staffGrid.Columns.Add(col);
        }

        staffSearch.TextChanged += (_, _) => FilterStaff();
        staffClubFilter.SelectionChanged += (_, _) => { if (!updatingStaffFilters) FilterStaff(); };
        staffJobFilter.SelectionChanged += (_, _) => { if (!updatingStaffFilters) FilterStaff(); };
        staffGrid.SelectionChanged += (_, _) => selectedStaff = (staffGrid.SelectedItem as StaffRow)?.Staff;
        staffGrid.DoubleTapped += (_, _) => OpenSelectedStaff();
        staffGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedStaff(); e.Handled = true; }
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
        var searchHeader = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = searchStack,
        };
        DockPanel.SetDock(searchHeader, Dock.Top);
        left.Children.Add(searchHeader);
        left.Children.Add(staffGrid);
        staffViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = staffEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(staffHeaderHost, Dock.Top);
        DockPanel.SetDock(staffRailHost, Dock.Left);
        detailDock.Children.Add(staffHeaderHost);
        detailDock.Children.Add(staffRailHost);
        detailDock.Children.Add(scroll);
        staffDetailRoot = detailDock;

        // Runs after the Clubs handler, which shows the players screen whenever Clubs is not selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toStaff = ReferenceEquals(modules.SelectedItem, staffNavItem);
            if (toStaff == staffActive) return;
            staffActive = toStaff;
            if (!toStaff) pendingStaffNavigation = null;
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
            staffSaveButton = null;
            staffGrid.ItemsSource = null;
            staffHeaderHost.Content = null;
            staffRailHost.Content = null;
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
        selectedStaff = row.Staff;
        RenderStaffEditor();
        OpenKindTab("staff", row.Staff.DisplayName, staffDetailRoot);
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
        string q = Fold((staffSearch.Text ?? "").Trim());
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
        staffHeaderHost.Content = null;
        staffRailHost.Content = null;
        staffEditor.Children.Clear();
        if (selectedStaff is not { } s) { staffSaveButton = null; return; }
        if (!staffDrafts.TryGetValue(s.Address, out var draft)) staffDrafts[s.Address] = draft = new StaffDraft();
        var save = new Button { Content = "Save changes", IsEnabled = draft.Changes.Count > 0, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = draft.Changes.Count > 0 };
        staffSaveButton = save;
        save.Click += (_, _) => SaveStaff(s, draft);
        discard.Click += (_, _) => { staffDrafts.Remove(s.Address); RenderStaffEditor(); };

        TextBox Box(string key, string original, double width = 48)
        {
            bool isRating20 = key.StartsWith("attr:", StringComparison.Ordinal) || key.StartsWith("pers:", StringComparison.Ordinal);
            var box = new TextBox
            {
                Text = draft.Changes.GetValueOrDefault(key, original),
                Width = isRating20 ? 32 : width,
                MinWidth = 0,
                Height = isRating20 ? 20 : 22,
                MinHeight = isRating20 ? 20 : 22,
                Padding = isRating20 ? new Thickness(2, 0) : new Thickness(5, 0),
                FontSize = 11.5,
                FontWeight = isRating20 ? FontWeight.Bold : FontWeight.SemiBold,
                HorizontalContentAlignment = isRating20 ? HorizontalAlignment.Center : HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            void ApplyRatingChrome()
            {
                if (!isRating20) return;
                if (int.TryParse(box.Text, out int v))
                {
                    box.Foreground = RatingColor(v);
                    box.Background = RatingBg(v);
                    box.BorderBrush = RatingBorder(v);
                }
                else
                {
                    box.Foreground = UiKit.TextPrimary;
                }
            }
            ApplyRatingChrome();
            box.TextChanged += (_, _) =>
            {
                if (box.Text == original) draft.Changes.Remove(key); else draft.Changes[key] = box.Text ?? "";
                ApplyRatingChrome();
                save.IsEnabled = discard.IsEnabled = draft.Changes.Count > 0;
            };
            return box;
        }

        TextBlock Label(string text, string color = "#CBD5E1", bool trim = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = Brush(color),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
            };
            if (trim)
            {
                tb.TextTrimming = TextTrimming.CharacterEllipsis;
                tb.HorizontalAlignment = HorizontalAlignment.Right;
                tb.MaxWidth = 156;
                if (!string.IsNullOrEmpty(text)) ToolTip.SetTip(tb, text);
            }
            return tb;
        }

        StackPanel Rows(IEnumerable<(string Label, Control Value)> items)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var (label, value) in items)
            {
                var line = new DockPanel { MinHeight = 23, LastChildFill = true };
                DockPanel.SetDock(value, Dock.Right);
                line.Children.Add(value);
                line.Children.Add(Label(label));
                stack.Children.Add(new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(8, 2, 7, 2),
                    Child = line,
                });
            }
            return stack;
        }

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {s.Id.ToString(Inv)}"));
        string jobName = StaffLabels.Job(s.JobCode);
        if (!string.IsNullOrEmpty(jobName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(jobName, highlight: true));
        }
        if (s.Club is { } club && !string.IsNullOrEmpty(club.DisplayName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var clubLink = UiKit.MetaLinkButton(club.DisplayName);
            clubLink.Click += async (_, _) => await OpenPlayerClubAsync(club.Address);
            badges.Children.Add(clubLink);
        }
        if (s.Nationality is { } nat && !string.IsNullOrEmpty(nat.DisplayName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            int? natId = nationCatalogue.FirstOrDefault(n => n.Address == nat.Address)?.Id
                ?? nationCatalogue.FirstOrDefault(n => string.Equals(n.Name, nat.DisplayName, StringComparison.OrdinalIgnoreCase))?.Id;
            var flag = graphics.Image("nationflag", natId, 15);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { flag, new TextBlock { Text = nat.DisplayName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
            };
            var natLink = UiKit.MetaLinkButton(content);
            natLink.Click += async (_, _) => await OpenPlayerNationAsync(nat.Address);
            badges.Children.Add(natLink);
        }

        var caDisplay = new TextBlock { Text = s.CurrentAbility.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#38BDF8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var paDisplay = new TextBlock { Text = s.PotentialAbility.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brush("#C084FC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

        var headerCommands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("CA", caDisplay), UiKit.StatBox("PA", paDisplay) },
        };

        var portraitImg = graphics.Image("person", s.Id, 72);
        staffHeaderHost.Content = UiKit.EntityBanner(s.DisplayName, badges, headerCommands, portraitImg);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
        var col = new[] { new StackPanel { Spacing = 10 }, new StackPanel { Spacing = 10 }, new StackPanel { Spacing = 10 } };
        for (int i = 0; i < 3; i++) { Grid.SetColumn(col[i], i * 2); columns.Children.Add(col[i]); }

        col[0].Children.Add(UiKit.Card("Information", Rows([
            ("Unique ID", Label(s.Id.ToString(Inv), "#F1F5F9")),
            ("Full Name", Label(s.FullName ?? "", "#F1F5F9", trim: true)),
            ("Born", Label(s.DateOfBirth?.ToString("yyyy-MM-dd", Inv) ?? "", "#F1F5F9")),
        ])));
        col[0].Children.Add(UiKit.Card("Ability", Rows([
            ("CA", Box("CA", s.CurrentAbility.ToString(Inv), 58)),
            ("PA", Box("PA", s.PotentialAbility.ToString(Inv), 58)),
        ])));
        col[0].Children.Add(UiKit.Card("Reputation", Rows([
            ("Home", Box("rep:Home", s.HomeReputation.ToString(Inv), 68)),
            ("Current", Box("rep:Current", s.CurrentReputation.ToString(Inv), 68)),
            ("World", Box("rep:World", s.WorldReputation.ToString(Inv), 68)),
        ])));
        if (s.ManagerAttributes is { } mgr)
            col[0].Children.Add(UiKit.Card("Manager", Rows(StaffLabels.Manager
                .Select(m => (m.Label, (Control)Box($"mgr:{m.Attribute}", mgr[(int)m.Attribute].ToString(Inv), 42))))));
        col[0].Children.Add(UiKit.Card("Personality", Rows(Enum.GetValues<StaffPersonality>()
            .Select(t => (t.ToString(), (Control)Box($"pers:{t}", s.Personality[(int)t].ToString(Inv)))))));

        int[] placement = [1, 2, 1, 1, 2, 2];
        for (int g = 0; g < StaffLabels.Groups.Length; g++)
        {
            var (title, items) = StaffLabels.Groups[g];
            col[placement[g]].Children.Add(UiKit.Card(title, Rows(items.Select(it => (it.Label, (Control)Box($"attr:{it.Attribute}", s.Attribute(it.Attribute).ToString(Inv)))))));
        }

        var moveHost = new ContentControl();
        void EnsureMoveLoaded()
        {
            if (staffSectionKey == "Move to club" && moveHost.Content is null)
                moveHost.Content = StaffMovePanel(s);
        }

        var staffSections = new SectionHost.Section[]
        {
            new("Information", "Information", columns),
            new("Contract", "Contract", StaffContractPanel(s)),
            new("Career Plans", "Career Plans", CareerPlansPanel(backend.Session.Owner(s), RenderStaffEditor)),
            new("Relationships", "Relationships", RelationshipsPanel(backend.Session.Owner(s), RenderStaffEditor)),
            new("Languages", "Languages", LanguagesPanel(backend.Session.Owner(s), RenderStaffEditor)),
            new("Knowledge", "Knowledge", StaffKnowledgePanel(s)),
            new("Formations", "Formations", StaffFormationsPanel(s)),
            new("Tendencies", "Tendencies", StaffTendenciesPanel(s)),
            new("Move to club", "Move to club", moveHost),
        };

        var hostContent = SectionHost.Build(
            "staff",
            staffSections,
            staffSectionKey,
            key =>
            {
                staffSectionKey = key;
                EnsureMoveLoaded();
            },
            staffRailHost);
        EnsureMoveLoaded();
        staffEditor.Children.Add(hostContent);
    }

    /// <summary>FMRTE 27's staff Contract tab: Type, Wages, Started, Expires, On Rolling Contract.</summary>
    Control StaffContractPanel(StaffRecord s)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left };
        var access = backend.Session.StaffContracts;
        if (access.Read(s) is not { } contract) return panel;
        var save = new Button { Content = "Save contract", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        OfferContractEditor? editor = null;
        editor = OfferContractBox(contract, () => save.IsEnabled = discard.IsEnabled = editor?.Changed() == true, out var view);
        view.Margin = new Thickness(12, 10);
        panel.Children.Add(UiKit.Card("Contract Details", view));
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
        var renew = new Button { Content = "Renew for 4 Years" };
        renew.Click += (_, _) =>
        {
            try
            {
                var today = backend.Session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
                backend.Edits.Execute(() => access.Renew(s, contract, 4, today));
                status.Text = $"Renewed {s.DisplayName} for 4 years. Continue in FM to see updates.";
                RenderStaffEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        var expire = new Button { Content = "Make it expire" };
        expire.Click += (_, _) =>
        {
            try
            {
                var today = backend.Session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
                backend.Edits.Execute(() => access.Expire(s, contract, today));
                status.Text = $"{s.DisplayName}'s contract now expires. Continue in FM to see updates.";
                RenderStaffEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard, renew, expire } });
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
