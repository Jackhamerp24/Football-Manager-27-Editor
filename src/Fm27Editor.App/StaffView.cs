using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>One grid row; columns bind to the plain properties.</summary>
public sealed record StaffRow(StaffRecord Staff, string Name, string Job, string Club, string Nation, int? Age, int CA, int PA)
{
    public string SearchName
    {
        get
        {
            if (searchNameSource != Name) { searchNameSource = Name; searchName = MainWindow.Fold(Name); }
            return searchName!;
        }
    }
    string? searchNameSource = Name, searchName = MainWindow.Fold(Name);
}

/// <summary>The panels of a staff member's tabs that still live in the window (contract box, career plans, relationships, languages, knowledge, formations, tendencies, move).</summary>
internal interface IStaffPanels
{
    Control Contract(StaffRecord staff);
    Control CareerPlans(StaffRecord staff);
    Control Relationships(StaffRecord staff);
    Control Languages(StaffRecord staff);
    Control Knowledge(StaffRecord staff);
    Control Formations(StaffRecord staff);
    Control Tendencies(StaffRecord staff);
    Control Move(StaffRecord staff);
}

/// <summary>Staff: the list with club and job filters, and a tab per member with attributes, contract, knowledge and the rest.</summary>
internal sealed partial class StaffScreen(IScreenHost host, IStaffPanels panels)
    : EntityScreen<StaffRecord, StaffRow, StaffDraft>(host, "staff", "Staff", "staff", "Search staff name or ID...")
{
    const string AllClubs = "All clubs", AllJobs = "All jobs";
    readonly ComboBox genderFilter = new() { ItemsSource = new[] { "Men", "Women", "All" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 260 };
    readonly ComboBox clubFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox jobFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    bool updatingFilters;
    string sectionKey = "Information";

    Fm27Editor.Core.Offsets.Gender? SelectedGender() => genderFilter.SelectedIndex switch
    {
        0 => Fm27Editor.Core.Offsets.Gender.Men,
        1 => Fm27Editor.Core.Offsets.Gender.Women,
        _ => null,
    };

    /// <summary>Runs when the game connection changes, so undo stacks of batch edits are dropped.</summary>
    public Action? ClearUndo { get; set; }

    protected override bool HasRail => true;

    protected override Control BuildSearchHeader(TextBox searchBox)
    {
        searchBox.Margin = new Thickness(0, 0, 6, 0);
        ToolTip.SetTip(genderFilter, "Gender filter");
        var searchControls = new DockPanel();
        searchControls.Children.Add(searchBox);
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*,*"), ColumnSpacing = 8 };
        Grid.SetColumn(genderFilter, 0); filters.Children.Add(genderFilter);
        Grid.SetColumn(clubFilter, 1); filters.Children.Add(clubFilter);
        Grid.SetColumn(jobFilter, 2); filters.Children.Add(jobFilter);
        genderFilter.SelectionChanged += (_, _) => { if (!updatingFilters) { PopulateFilters(); Filter(); } };
        clubFilter.SelectionChanged += (_, _) => { if (!updatingFilters) Filter(); };
        jobFilter.SelectionChanged += (_, _) => { if (!updatingFilters) Filter(); };
        ResetFilters();
        return new StackPanel { Spacing = 6, Children = { searchControls, filters } };
    }

    protected override string FilteredMessage(int shown, int total) =>
        SelectedGender() is { } g && SearchText.Length == 0 && clubFilter.SelectedIndex == 0 && jobFilter.SelectedIndex == 0
            ? $"{shown:N0} of {total:N0} staff shown ({total - shown:N0} hidden by {g} filter)"
            : base.FilteredMessage(shown, total);

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(StaffRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Job", Binding = new Binding(nameof(StaffRow.Job)), Width = new DataGridLength(156) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Club", Binding = new Binding(nameof(StaffRow.Club)), Width = new DataGridLength(150) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(StaffRow.Nation)), Width = new DataGridLength(126) });
        foreach (var (h, p) in new[] { ("Age", nameof(StaffRow.Age)), ("CA", nameof(StaffRow.CA)), ("PA", nameof(StaffRow.PA)) })
        {
            var col = new DataGridTextColumn { Header = h, Binding = new Binding(p), Width = new DataGridLength(78) };
            col.CellStyleClasses.Add("numeric");
            grid.Columns.Add(col);
        }
    }

    protected override IReadOnlyList<StaffRow> LoadRows(GameSession session)
    {
        var all = session.Staff.LoadAll();
        var date = session.ReadGameDate();
        return all
            .Select(s => new StaffRow(s, s.DisplayName, StaffLabels.Job(s.JobCode), s.Club?.DisplayName ?? "", s.Nationality?.DisplayName ?? "",
                date is { } d && s.DateOfBirth is { } b ? d.Year - b.Year - (d < b.AddYears(d.Year - b.Year) ? 1 : 0) : null,
                s.CurrentAbility, s.PotentialAbility))
            .OrderByDescending(r => r.CA)
            .ToList();
    }

    protected override void RowsLoaded() => PopulateFilters();

    protected override void OnReconnected()
    {
        ResetFilters();
        ClearUndo?.Invoke();
    }

    void ResetFilters()
    {
        updatingFilters = true;
        genderFilter.SelectedIndex = 0;
        clubFilter.ItemsSource = new[] { AllClubs }; clubFilter.SelectedIndex = 0;
        jobFilter.ItemsSource = new[] { AllJobs }; jobFilter.SelectedIndex = 0;
        updatingFilters = false;
    }

    void PopulateFilters()
    {
        string? oldClub = clubFilter.SelectedItem as string, oldJob = jobFilter.SelectedItem as string;
        var g = SelectedGender();
        var visible = Rows.Where(r => (g is null || r.Staff.Gender == g)
            && (g != Fm27Editor.Core.Offsets.Gender.Men || r.Staff.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women));
        var clubs = new[] { AllClubs }.Concat(visible.Select(r => r.Club).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)).ToArray();
        var jobs = new[] { AllJobs }.Concat(visible.Select(r => r.Job).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)).ToArray();
        updatingFilters = true;
        clubFilter.ItemsSource = clubs;
        clubFilter.SelectedIndex = Math.Max(0, Array.IndexOf(clubs, oldClub));
        jobFilter.ItemsSource = jobs;
        jobFilter.SelectedIndex = Math.Max(0, Array.IndexOf(jobs, oldJob));
        updatingFilters = false;
    }

    /// <summary>After a member changed club or job: show every club again and rebuild the filter lists.</summary>
    public void RefreshFilters(bool showAllClubs)
    {
        if (showAllClubs) { updatingFilters = true; clubFilter.SelectedIndex = 0; updatingFilters = false; }
        PopulateFilters();
    }

    protected override IReadOnlyList<StaffRow> Apply(IReadOnlyList<StaffRow> rows, string foldedQuery) =>
        StaffFilter.Apply(rows, foldedQuery,
            clubFilter.SelectedItem is string c && c != AllClubs ? c : null,
            jobFilter.SelectedItem is string j && j != AllJobs ? j : null,
            SelectedGender(),
            SelectedGender() == Fm27Editor.Core.Offsets.Gender.Men);

    protected override bool Matches(StaffRow row, string q) =>
        row.SearchName.Contains(q, StringComparison.Ordinal) || row.Staff.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);

    /// <summary>Shows one member from elsewhere (a club's staff list), loading the list first when needed.</summary>
    public async Task ShowAsync(ulong address, int id)
    {
        if (Rows.Count == 0 && Host.Backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
        var row = StaffNavigation.Find(Rows, address, id);
        if (row is null) { Host.SetStatus("Staff member no longer available."); return; }
        updatingFilters = true; clubFilter.SelectedIndex = 0; jobFilter.SelectedIndex = 0; updatingFilters = false;
        Pick(row.Staff);
        Reveal(r => ReferenceEquals(r, row));
    }

    protected override StaffRecord RecordOf(StaffRow row) => row.Staff;
    protected override ulong AddressOf(StaffRecord record) => record.Address;
    protected override string NameOf(StaffRecord record) => record.DisplayName;
    protected override StaffDraft NewDraft() => new();
    protected override bool HasChanges(StaffRecord record, StaffDraft draft) => draft.Changes.Count > 0;
    protected override StaffRecord? Reload(StaffRecord record) => Host.Backend.Session.Staff.Reload(record.Address);
    protected override StaffRow Refresh(StaffRow row, StaffRecord fresh) =>
        row with { Staff = fresh, CA = fresh.CurrentAbility, PA = fresh.PotentialAbility, Club = fresh.Club?.DisplayName ?? "", Job = StaffLabels.Job(fresh.JobCode) };

    protected override Func<ulong, int, ChangeLabel?>? LabelsOf(StaffRecord s) => StaffChangeLabels.For(Host.Backend.Session.Profile, s);

    protected override string? Validate(StaffRecord s, StaffDraft draft, out Func<int> write)
    {
        write = () => 0;
        if (!draft.TryParse(out var v, out var error)) return error;
        var w = Host.Backend.Session.StaffWriter;
        write = () =>
        {
            foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("attr:", StringComparison.Ordinal)))
                { var attribute = Enum.Parse<StaffAttribute>(key[5..]); w.SetAttribute(s, attribute, value); if (Host.Backend.Session.Staff.Reload(s.Address) is { } written) Host.Backend.AcceptFrozenStaffEdit(written, attribute); }
            foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("mgr:", StringComparison.Ordinal)))
                w.SetManagerAttribute(s, Enum.Parse<ManagerAttribute>(key[4..]), value);
            foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("pers:", StringComparison.Ordinal)))
                w.SetPersonality(s, Enum.Parse<StaffPersonality>(key[5..]), value);
            if (v.TryGetValue("caps", out int caps)) w.SetCaps(s, caps);
            if (v.TryGetValue("goals", out int goals)) w.SetGoals(s, goals);
            if (v.TryGetValue("license", out int license)) w.SetLicense(s, (StaffLicense)(sbyte)license);
            foreach (var (key, value) in v.Where(kv => kv.Key.StartsWith("role:", StringComparison.Ordinal)))
                w.SetRole(s, Enum.Parse<StaffRole>(key[5..]), value);
            // Training ratings last, one category at a time on a fresh read: categories share Determination, Authority
            // and Motivating, so each one is planned on what the previous one wrote (FMRTE's "Set to" rule).
            foreach (var (key, half) in v.Where(kv => kv.Key.StartsWith("train:", StringComparison.Ordinal)))
            {
                var fresh = Host.Backend.Session.Staff.Reload(s.Address) ?? throw new Fm27Editor.Core.Players.StaleObjectException("Staff member is gone");
                foreach (var change in StaffDetailsMassEdit.Training([fresh], Enum.Parse<TrainingCategory>(key[6..]), Fm27Editor.Core.Players.MassEditMode.Set, half / 2.0, w))
                    StaffDetailsMassEdit.Apply(change, Host.Backend.Session.Staff);
            }
            if (v.ContainsKey("CA") || v.ContainsKey("PA"))
                w.SetAbility(s, v.GetValueOrDefault("CA", s.CurrentAbility), v.GetValueOrDefault("PA", s.PotentialAbility));
            if (v.Keys.Any(k => k.StartsWith("rep:", StringComparison.Ordinal)))
                w.SetReputation(s, v.GetValueOrDefault("rep:Home", s.HomeReputation), v.GetValueOrDefault("rep:Current", s.CurrentReputation),
                    v.GetValueOrDefault("rep:World", s.WorldReputation));
            return v.Count;
        };
        return null;
    }

    protected override void BuildEditor(StaffRecord s, StaffDraft draft, Action changed)
    {
        TextBox Box(string key, string original, double width = 48)
        {
            bool isRating20 = key.StartsWith("attr:", StringComparison.Ordinal) || key.StartsWith("pers:", StringComparison.Ordinal) || key.StartsWith("role:", StringComparison.Ordinal);
            var box = new TextBox
            {
                Text = draft.Changes.GetValueOrDefault(key, original),
                Width = isRating20 ? 32 : width, MinWidth = 0,
                Height = isRating20 ? 20 : 22, MinHeight = isRating20 ? 20 : 22,
                Padding = isRating20 ? new Thickness(2, 0) : new Thickness(5, 0),
                FontSize = 13, FontWeight = isRating20 ? FontWeight.Bold : FontWeight.SemiBold,
                HorizontalContentAlignment = isRating20 ? HorizontalAlignment.Center : HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            void ApplyRatingChrome()
            {
                if (!isRating20) return;
                if (int.TryParse(box.Text, out int v))
                {
                    box.Foreground = MainWindow.RatingColor(v);
                    box.Background = MainWindow.RatingBg(v);
                    box.BorderBrush = MainWindow.RatingBorder(v);
                }
                else box.Foreground = UiKit.TextPrimary;
            }
            ApplyRatingChrome();
            box.TextChanged += (_, _) =>
            {
                if (box.Text == original) draft.Changes.Remove(key); else draft.Changes[key] = box.Text ?? "";
                ApplyRatingChrome();
                changed();
            };
            return box;
        }

        static TextBlock Text(string text, string color = "#CBD5E1", bool trim = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 13, Foreground = UiKit.Brush(color), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
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
                line.Children.Add(Text(label));
                stack.Children.Add(new Border { Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent, Padding = new Thickness(8, 2, 7, 2), Child = line });
            }
            return stack;
        }

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {s.Id.ToString(Inv)}"));
        string jobName = StaffLabels.Job(s.JobCode);
        if (!string.IsNullOrEmpty(jobName))
        {
            badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(jobName, highlight: true));
        }
        if (s.Club is { } club && !string.IsNullOrEmpty(club.DisplayName))
        {
            badges.Children.Add(UiKit.MetaDot());
            var clubLink = UiKit.MetaLinkButton(club.DisplayName);
            clubLink.Click += async (_, _) => await Host.OpenClubAsync(club.Address);
            badges.Children.Add(clubLink);
        }
        if (s.Nationality is { } nat && !string.IsNullOrEmpty(nat.DisplayName))
        {
            badges.Children.Add(UiKit.MetaDot());
            var catalogue = Host.NationCatalogue;
            int? natId = catalogue.FirstOrDefault(n => n.Address == nat.Address)?.Id
                ?? catalogue.FirstOrDefault(n => string.Equals(n.Name, nat.DisplayName, StringComparison.OrdinalIgnoreCase))?.Id;
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { Host.Graphics.NationFlag(natId, 14), new TextBlock { Text = nat.DisplayName, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center } },
            };
            var natLink = UiKit.MetaLinkButton(content);
            natLink.Click += async (_, _) => await Host.OpenNationAsync(nat.Address);
            badges.Children.Add(natLink);
        }

        var caDisplay = new TextBlock { Text = s.CurrentAbility.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#38BDF8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var paDisplay = new TextBlock { Text = s.PotentialAbility.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#C084FC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var headerCommands = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("CA", caDisplay), UiKit.StatBox("PA", paDisplay) },
        };
        // Freezer: the same Freeze / Unfreeze pair the player tools have; every attribute is frozen at its current value.
        if (Host.Backend.Freezer is { } freezer)
        {
            var freeze = new Button { Content = "Freeze" };
            var unfreeze = new Button { Content = "Unfreeze" };
            ToolTip.SetTip(freeze, "Keeps this staff member's attributes from changing while you play. Edits made here still apply.");
            ToolTip.SetTip(unfreeze, "Lets this staff member's attributes change again.");
            freeze.Click += (_, _) =>
            {
                try { Host.Backend.Edits.Execute(() => freezer.SetStaff(s, FrozenStaff.AllAttributes(s))); Host.SetStatus($"Frozen: {s.DisplayName}"); }
                catch (Exception ex) { Host.SetStatus($"Freezer: {UserMessage.For(ex)}"); }
            };
            unfreeze.Click += (_, _) =>
            {
                try { Host.Backend.Edits.Execute(() => freezer.RemoveStaff(s.Id)); Host.SetStatus($"Unfrozen: {s.DisplayName}"); }
                catch (Exception ex) { Host.SetStatus($"Freezer: {UserMessage.For(ex)}"); }
            };
            headerCommands.Children.Add(freeze);
            headerCommands.Children.Add(unfreeze);
        }        HeaderHost.Content = UiKit.EntityBanner(s.DisplayName, badges, headerCommands, Host.Graphics.Image("person", s.Id, 72));

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
        var col = new[] { new StackPanel { Spacing = 10 }, new StackPanel { Spacing = 10 }, new StackPanel { Spacing = 10 } };
        for (int i = 0; i < 3; i++) { Grid.SetColumn(col[i], i * 2); columns.Children.Add(col[i]); }

        var info = new List<(string, Control)>
        {
            ("Unique ID", Text(s.Id.ToString(Inv), "#F1F5F9")),
            ("Full Name", Text(s.FullName ?? "", "#F1F5F9", trim: true)),
            ("Born", Text(DateSetting.Format(s.DateOfBirth), "#F1F5F9")),
        };
        if (s.Caps is { } capsValue) info.Add(("Caps", Box("caps", capsValue.ToString(Inv), 58)));
        if (s.Goals is { } goalsValue) info.Add(("Goals", Box("goals", goalsValue.ToString(Inv), 58)));
        if (s.License is { } licenseValue) info.Add(("License", LicenseBox(licenseValue)));
        col[0].Children.Add(UiKit.Card("Information", Rows(info)));

        // FMRTE's licence dropdown; the draft keeps the chosen code like any other field.
        ComboBox LicenseBox(StaffLicense current)
        {
            var options = StaffDetailsMassEdit.Licenses;
            var box = new ComboBox { ItemsSource = options.Select(o => o.Name).ToList(), Width = 196, Height = 22, MinHeight = 22, FontSize = 12.5, Padding = new Thickness(6, 0) };
            string original = ((int)current).ToString(Inv);
            int at = Array.FindIndex(options, o => ((int)o.Value).ToString(Inv) == draft.Changes.GetValueOrDefault("license", original));
            box.SelectedIndex = at;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                string code = ((int)options[box.SelectedIndex].Value).ToString(Inv);
                if (code == original) draft.Changes.Remove("license"); else draft.Changes["license"] = code;
                changed();
            };
            return box;
        }
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

        if (s.Roles is { } roles)
            col[0].Children.Add(UiKit.Card("Roles", Rows(Enum.GetValues<StaffRole>().Where(r => (int)r < roles.Length)
                .Select(r => (StaffDetailsMassEdit.RoleLabel(r), (Control)Box($"role:{r}", roles[(int)r].ToString(Inv)))))));

        int[] placement = [1, 2, 1, 1, 2, 2];
        for (int g = 0; g < StaffLabels.Groups.Length; g++)
        {
            var (title, items) = StaffLabels.Groups[g];
            col[placement[g]].Children.Add(UiKit.Card(title, Rows(items.Select(it => (it.Label, (Control)Box($"attr:{it.Attribute}", s.Attribute(it.Attribute).ToString(Inv)))))));
        }

        var moveHost = new ContentControl();
        void EnsureMoveLoaded()
        {
            if (sectionKey == "Move to club" && moveHost.Content is null) moveHost.Content = panels.Move(s);
        }

        var sections = new SectionHost.Section[]
        {
            new("Information", "Information", columns),
            new("Contract", "Contract", panels.Contract(s)),
            new("Career Plans", "Career Plans", panels.CareerPlans(s)),
            new("Training Rating", "Training Rating", TrainingRatingCard(s, draft, changed)),
            new("Relationships", "Relationships", panels.Relationships(s)),
            new("Languages", "Languages", panels.Languages(s)),
            new("Knowledge", "Knowledge", panels.Knowledge(s)),
            new("Formations", "Formations", panels.Formations(s)),
            new("Tendencies", "Tendencies", panels.Tendencies(s)),
            new("Move to club", "Move to club", moveHost),
        };
        var hostContent = SectionHost.Build("staff", sections, sectionKey, key => { sectionKey = key; EnsureMoveLoaded(); }, RailHost);
        EnsureMoveLoaded();
        Editor.Children.Add(hostContent);
    }
}

internal sealed partial class StaffScreen
{
    /// <summary>
    /// FMRTE's Training Rating tab: the eleven categories in stars (0-5, half steps), worked out from the coach's attributes
    /// the way FMRTE does (<see cref="TrainingRatings"/>). Each one is a slider; saving sets every attribute in that
    /// category's formula to stars × 4, as FMRTE does.
    /// </summary>
    static Control TrainingRatingCard(StaffRecord s, StaffDraft draft, Action changed)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var rows = Enum.GetValues<TrainingCategory>().Select(c =>
        {
            string key = $"train:{c}";
            int original = (int)Math.Round(TrainingRatings.Stars(s, c) * 2);
            int shown = draft.Changes.TryGetValue(key, out var text) && int.TryParse(text, inv, out int staged) ? staged : original;
            var slider = new StarSlider(shown / 2.0) { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 10, 0) };
            var number = new TextBlock
            {
                Width = 30, FontSize = 13, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            void Show(int half)
            {
                number.Text = (half / 2.0).ToString("0.0", inv);
                number.Foreground = half == original ? MainWindow.RatingColor(half * 2) : UiKit.Brush("#F1F5F9");
            }
            Show(shown);
            slider.ValueChanged += v =>
            {
                int half = (int)Math.Round(v * 2);
                if (half == original) draft.Changes.Remove(key); else draft.Changes[key] = half.ToString(inv);
                Show(half);
                changed();
            };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*,Auto"), MinHeight = 28 };
            var label = new TextBlock { Text = TrainingRatings.Label(c), FontSize = 13, Foreground = UiKit.Brush("#CBD5E1"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1); Grid.SetColumn(number, 2);
            line.Children.Add(label); line.Children.Add(slider); line.Children.Add(number);
            return (Control)line;
        });
        var card = MainWindow.AttributeCard("Training Rating", rows, rightHeader: UiKit.InfoBadge("How good this coach is at training each area, from 0 to 5 stars, based on his attributes. " +
            "Changing a rating changes the attributes behind it, so other ratings can move too."));
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        return card;
    }
}

public sealed partial class MainWindow : IStaffPanels
{
    StaffScreen staffScreen = null!;
    IReadOnlyList<StaffRow> staffRows => staffScreen.Rows;
    Task LoadStaffAsync() => staffScreen.LoadAsync();
    void RenderStaffEditor() => staffScreen.Reopen();
    void OpenClubStaff(StaffRecord staff) => _ = staffScreen.ShowAsync(staff.Address, staff.Id);
    void PickStaff(StaffRecord staff) => staffScreen.Pick(staff);

    Control IStaffPanels.Contract(StaffRecord s) => StaffContractPanel(s);
    Control IStaffPanels.CareerPlans(StaffRecord s) => CareerPlansPanel(backend.Session.Owner(s), RenderStaffEditor);
    Control IStaffPanels.Relationships(StaffRecord s) => RelationshipsPanel(backend.Session.Owner(s), RenderStaffEditor);
    Control IStaffPanels.Languages(StaffRecord s) => LanguagesPanel(backend.Session.Owner(s), RenderStaffEditor);
    Control IStaffPanels.Knowledge(StaffRecord s) => StaffKnowledgePanel(s);
    Control IStaffPanels.Formations(StaffRecord s) => StaffFormationsPanel(s);
    Control IStaffPanels.Tendencies(StaffRecord s) => StaffTendenciesPanel(s);
    Control IStaffPanels.Move(StaffRecord s) => StaffMovePanel(s);

    /// <summary>
    /// FMRTE 27's staff Contract tab (Type, Wages, Started, Expires, On Rolling Contract). A person with both a club and a national
    /// contract gets one tab for each; Renew and the year buttons live in the Staff lists of the club and the nation, not here.
    /// </summary>
    Control StaffContractPanel(StaffRecord s)
    {
        var session = backend.Session;
        var clubContract = session.StaffContracts.Read(s);
        var nationalContract = session.NationStaffContracts.Read(s);
        var pages = new List<(string Title, Control Page)>();
        if (clubContract is not null)
            pages.Add(("Club", ContractEditor(s, "Club Contract", clubContract, fields => session.StaffContracts.Set(s, clubContract, fields))));
        if (nationalContract is not null)
            pages.Add(("National", ContractEditor(s, "National Contract", nationalContract, fields => session.NationStaffContracts.Set(s, nationalContract, fields))));
        if (pages.Count == 0) return new StackPanel();
        if (pages.Count == 1) return pages[0].Page;

        var host = new ContentControl { Content = pages[0].Page };
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var buttons = new List<Button>();
        for (int i = 0; i < pages.Count; i++)
        {
            var (title, page) = pages[i];
            var button = new Button { Content = title };
            button.Click += (_, _) =>
            {
                host.Content = page;
                foreach (var b in buttons) b.Classes.Set("accent", ReferenceEquals(b, button));
            };
            buttons.Add(button);
            strip.Children.Add(button);
        }
        buttons[0].Classes.Add("accent");
        return new StackPanel { Children = { strip, host } };
    }

    /// <summary>One contract's fields; its own Save is hidden and run by the page's Save button.</summary>
    Control ContractEditor(StaffRecord s, string title, Fm27Editor.Core.Players.OfferContract contract, Action<Fm27Editor.Core.Players.OfferContractFields> write)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { IsVisible = false, Content = "Save contract", IsEnabled = false, Classes = { "accent" } };
        OfferContractEditor? editor = null;
        editor = OfferContractBox(contract, () => save.IsEnabled = editor?.Changed() == true, out var view);
        view.Margin = new Thickness(12, 10);
        panel.Children.Add(UiKit.Card(title, view));
        save.Click += (_, _) =>
        {
            if (editor.Parse() is not { } fields) { status.Text = "Contract: enter wages of 0 or more and dates like 2027-06-30, expiry after start"; return; }
            try
            {
                backend.Edits.Execute("Staff", s.DisplayName, $"{title}", () => write(fields));
                status.Text = $"Saved {s.DisplayName}'s {title.ToLowerInvariant()}. Continue in FM to see updates.";
                RenderStaffEditor();
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return panel;
    }
}