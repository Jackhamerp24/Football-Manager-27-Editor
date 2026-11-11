using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE-style mass edit: one window, Player/Coach/Club scopes plus Nation (which FMRTE does not have), a field list
/// per section and a single Apply. Every write goes through the guarded writers; the whole Apply is one history group.
/// </summary>
internal sealed partial class MassEditWindow : Window
{
    const int ModeNone = 0, ModeSet = 1, ModeChange = 2;

    enum Kind
    {
        PlayerAttribute, PlayerQuality, PlayerPersonality, PlayerPosition, PlayerWage, PlayerField, PlayerScalar,
        StaffField, StaffAttribute, StaffPersonality, StaffWage,
        ClubField, NationField,
    }

    sealed record InjuryChoice(InjuryType? Type) { public override string ToString() => Type?.Name ?? "Don't change"; }

    /// <summary>A field row's values copied on the window thread, so a plan can be built in the background without touching controls.</summary>
    sealed record FieldInput(Kind Kind, int Index, string Label, long Min, long Max, int ModeIndex, long Number);

    sealed class FieldRow(Kind kind, int index, string group, string label, long min, long max, ComboBox mode, NumericUpDown value, IReadOnlyList<int>? codes = null)
    {
        public Kind Kind { get; } = kind;
        public int Index { get; } = index;
        public string Group { get; } = group;
        public string Label { get; } = label;
        public long Min { get; } = min;
        public long Max { get; } = max;
        public ComboBox Mode { get; } = mode;
        public NumericUpDown Value { get; } = value;
        /// <summary>For a field with named values (Hair Colour, Yes / No): the dropdown lists "Don't change" then the names, and picking one sets that code.</summary>
        public int ModeIndex => codes is null ? Mode.SelectedIndex : Mode.SelectedIndex <= 0 ? ModeNone : ModeSet;
        public long Number => codes is null ? (long)(Value.Value ?? 0) : Mode.SelectedIndex > 0 ? codes[Mode.SelectedIndex - 1] : 0;
        public void Reset() { Mode.SelectedIndex = ModeNone; Value.Value = Min; }
    }

    readonly MainWindow host;
    readonly EditorBackend backend;
    readonly TextBlock status = new() { Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
    readonly Button apply = new() { Content = "Review changes", Classes = { "accent" }, IsEnabled = false, MinWidth = 110 };
    // Scope bar styled like the main window module bar: text buttons, the active one underlined in the accent colour.
    readonly StackPanel scopeBar = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(12, 4, 12, 4) };
    readonly ContentControl scopeHost = new() { MinHeight = 320 };
    readonly Dictionary<string, (Button Button, Control Panel)> scopePages = new(StringComparer.Ordinal);
    string scope = "Player";
    readonly List<FieldRow> fields = [];
    readonly List<CheckBox> presetAll = [];
    readonly List<CheckBox> presetTu = [];
    readonly List<CheckBox> moveBoxes = [];
    int[] moveStates = [];
    readonly List<(string Scope, ScrollViewer View)> sectionViews = [];
    readonly TextBlock target = new() { FontSize = 12.5, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button clear = new() { Content = "Clear all" };
    readonly Button choose = new() { Content = "Choose who to edit...", IsEnabled = false };
    IReadOnlyList<PlayerRecord>? chosenPlayers;
    IReadOnlyList<StaffRecord>? chosenStaff;
    IReadOnlyList<ClubEntry>? chosenClubs;
    ComboBox? injuryType;
    CheckBox? injuryClear;

    public MassEditWindow(MainWindow host)
    {
        this.host = host;
        backend = host.Backend;
        Title = "Mass Edit";
        Width = 1022;
        Height = 682;
        MinWidth = 860;
        MinHeight = 520;
        Background = UiKit.WindowBg;
        FontSize = 13;
        Foreground = UiKit.TextPrimary;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BuildScopes();
        Content = BuildLayout();
        ShowScope("Player");
        Clear();
        Closed += (_, _) => host.MassEditClosed(this);
        UpdateTarget();
    }

    Control BuildLayout()
    {
        var top = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 10, 12, 6) };
        var label = new TextBlock { Text = "Apply to", Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(choose, "Pick targets with Advanced Search; the result becomes the batch for the current scope.");
        choose.Click += (_, _) => _ = ChooseTargetsAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { label, target, choose } };
        top.Children.Add(row);

        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 6, 12, 10) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => _ = ReviewAsync();
        right.Children.Add(cancel); right.Children.Add(apply);
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right);
        clear.Click += (_, _) => Clear();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { clear, status } };
        bottom.Children.Add(left);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var body = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(scopeBar, Dock.Top);
        body.Children.Add(scopeBar);
        body.Children.Add(scopeHost);
        root.Children.Add(body);
        return root;
    }

    void BuildScopes()
    {
        AddScope("Player", ScopePanel(BuildPlayerSections()));
        AddScope("Coach", ScopePanel(BuildCoachSections()));
        AddScope("Club", ScopePanel(BuildClubSections()));
        AddScope("Nation", ScopePanel(BuildNationSections()));
    }

    void AddScope(string title, Control panel)
    {
        var button = new Button
        {
            Content = title, FontSize = 13, Padding = new Thickness(11, 3), MinHeight = 25, Height = 25,
            CornerRadius = new CornerRadius(3), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Foreground = UiKit.Brush("#94A3B8"),
        };
        button.Click += (_, _) => ShowScope(title);
        scopePages[title] = (button, panel);
        scopeBar.Children.Add(button);
    }

    void ShowScope(string title)
    {
        if (!scopePages.TryGetValue(title, out var page)) return;
        scope = title;
        scopeHost.Content = page.Panel;
        foreach (var (name, (button, _)) in scopePages)
        {
            bool active = name == title;
            button.Foreground = UiKit.Brush(active ? "#F8FAFC" : "#94A3B8");
            button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            button.Background = active ? UiKit.Brush("#201B38") : Brushes.Transparent;
            button.BorderBrush = active ? UiKit.Accent : Brushes.Transparent;
        }
        UpdateTarget();
    }

    static Control ScopePanel(IReadOnlyList<(string Section, Control Panel)> sections)
    {
        var list = new ListBox { Width = 150, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 8) };
        list.ItemsSource = sections.Select(s => s.Section).ToArray();
        var scroll = new ScrollViewer { Content = sections[0].Panel, Padding = new Thickness(14, 8, 14, 8), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        Grid.SetColumn(scroll, 1);
        panel.Children.Add(list); panel.Children.Add(scroll);
        list.Tag = sections;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < sections.Count) scroll.Content = sections[list.SelectedIndex].Panel;
        };
        list.SelectedIndex = 0;
        return panel;
    }

    IReadOnlyList<(string, Control)> BuildPlayerSections()
    {
        var sections = new List<(string, Control)>();
        var general = Panel();
        AddHeader(general, "Ability");
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.CurrentAbility, "Ability", "Current Ability", 1, 200);
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.PotentialAbility, "Ability", "Potential Ability", 1, 200);
        AddCheck(general, "Match CA to PA", out _);
        AddHeader(general, "Reputation");
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.HomeReputation, "Reputation", "Home", 0, 10000);
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.CurrentReputation, "Reputation", "Current", 0, 10000);
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.WorldReputation, "Reputation", "World", 0, 10000);
        AddHeader(general, "Personal Details");
        Add(general, Kind.PlayerQuality, (int)PlayerQuality.Height, "Personal Details", "Height (cm)", 100, 250);
        AddChoice(general, Kind.PlayerField, (int)PlayerField.HairColour, "Personal Details", "Hair Colour", InformationLabels.HairColours);
        AddChoice(general, Kind.PlayerField, (int)PlayerField.HairLength, "Personal Details", "Hair Length", InformationLabels.HairLengths);
        AddChoice(general, Kind.PlayerField, (int)PlayerField.SkinColour, "Personal Details", "Skin Colour", InformationLabels.SkinColours);
        AddChoice(general, Kind.PlayerField, (int)PlayerField.Ethnicity, "Personal Details", "Ethnicity", InformationLabels.Ethnicities);
        Add(general, Kind.PlayerField, (int)PlayerField.Caps, "Personal Details", "Caps", 0, 10000);
        Add(general, Kind.PlayerField, (int)PlayerField.InternationalGoals, "Personal Details", "Goals", 0, 10000);
        AddChoice(general, Kind.PlayerField, (int)PlayerField.TreatedAsNonEuInItaly, "Personal Details", "Treated As Non-EU In Italy", [(1, "Yes"), (0, "No")]);
        sections.Add(("General", general));

        var contract = Panel();
        AddHeader(contract, "Contract Details");
        contractStatus = new ComboBox { ItemsSource = new[] { "Unset", "Expired", "Expiring in 6 Months", "Expiring in 1 Year" }, SelectedIndex = 0, Width = 200, FontSize = 12.5 };
        contractStatus.SelectionChanged += (_, _) => MarkDirty();
        contract.Children.Add(Labeled("Contract", contractStatus));
        contractType = new ComboBox { ItemsSource = new[] { "Unset", "Part-Time", "Full-Time", "Amateur", "Youth", "Non-Contract" }, SelectedIndex = 0, Width = 200, FontSize = 12.5 };
        contractType.SelectionChanged += (_, _) => MarkDirty();
        contract.Children.Add(Labeled("Type", contractType));
        Add(contract, Kind.PlayerField, (int)PlayerField.AskingPrice, "Contract", "Asking Price", 0, 2_000_000_000);
        Add(contract, Kind.PlayerWage, 0, "Contract", "Wages", 0, 10_000_000);

        AddHeader(contract, "Release clause");
        clauseType = new ComboBox { ItemsSource = new[] { "Don't change" }.Concat(ReleaseClauses.Select(c => ContractLabels.Term(c))).ToArray(), SelectedIndex = 0, Width = 260, FontSize = 12.5 };
        clauseType.SelectionChanged += (_, _) => MarkDirty();
        clauseFee = new NumericUpDown { Minimum = 0, Maximum = 2_000_000_000, Increment = 1, Value = 0, Width = 150, FontSize = 12.5, FormatString = "0" };
        clauseFee.ValueChanged += (_, _) => MarkDirty();
        var clauseRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 2) };
        clauseRow.Children.Add(new TextBlock { Text = "Release clause", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary });
        clauseRow.Children.Add(clauseType);
        contract.Children.Add(clauseRow);
        var feeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 2) };
        feeRow.Children.Add(new TextBlock { Text = "Release clause fee", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary });
        feeRow.Children.Add(clauseFee);
        contract.Children.Add(feeRow);
        removeClause = new CheckBox { Content = "Remove release clause", FontSize = 12.5, Margin = new Thickness(4, 4, 0, 2) };
        removeClause.IsCheckedChanged += (_, _) => MarkDirty();
        contract.Children.Add(removeClause);
        sections.Add(("Contract", contract));
        var fitness = Panel();
        AddHeader(fitness, "Fitness");
        Add(fitness, Kind.PlayerScalar, (int)PlayerField.Condition, "Fitness", "Condition", 5000, 10000);
        Add(fitness, Kind.PlayerScalar, (int)PlayerField.MatchSharpness, "Fitness", "Match Sharpness", 5000, 10000);
        Add(fitness, Kind.PlayerScalar, (int)PlayerField.Jadedness, "Fitness", "Jadedness", -500, 1000);
        Add(fitness, Kind.PlayerScalar, (int)PlayerField.Morale, "Fitness", "Morale", 1, 20);
        sections.Add(("Fitness", fitness));

        var injury = Panel();
        injuryClear = new CheckBox { Content = "Clear current injuries", FontSize = 12.5, Margin = new Thickness(4, 4, 0, 2) };
        injuryClear.IsCheckedChanged += (_, _) => MarkDirty();
        injury.Children.Add(injuryClear);
        injuryType = new ComboBox { ItemsSource = new[] { new InjuryChoice(null) }, SelectedIndex = 0, Width = 320, FontSize = 12.5 };
        injuryType.SelectionChanged += (_, _) => MarkDirty();
        injuryType.GotFocus += (_, _) =>
        {
            if (injuryType.ItemsSource is IEnumerable<InjuryChoice> existing && existing.Count() > 1) return;
            var types = backend.State.Status == ConnectionStatus.Connected ? backend.Session.InjuryTypes() : [];
            injuryType.ItemsSource = new[] { new InjuryChoice(null) }.Concat(types.Select(t => new InjuryChoice(t))).ToArray();
        };
        injury.Children.Add(Labeled("Add injury", injuryType));
        sections.Add(("Injury", injury));

        var technical = Panel();
        AddGroup(technical, Kind.PlayerAttribute, "Technical", [PlayerAttribute.Corners, PlayerAttribute.Crossing, PlayerAttribute.Dribbling, PlayerAttribute.Finishing, PlayerAttribute.FirstTouch, PlayerAttribute.FreeKicks, PlayerAttribute.Heading, PlayerAttribute.LongShots, PlayerAttribute.LongThrows, PlayerAttribute.Marking, PlayerAttribute.Passing, PlayerAttribute.PenaltyTaking, PlayerAttribute.Tackling, PlayerAttribute.Technique]);
        AddGroup(technical, Kind.PlayerAttribute, "Goal Keeper", [PlayerAttribute.AerialReach, PlayerAttribute.CommandOfArea, PlayerAttribute.Communication, PlayerAttribute.Eccentricity, PlayerAttribute.Handling, PlayerAttribute.Kicking, PlayerAttribute.OneOnOnes, PlayerAttribute.Reflexes, PlayerAttribute.RushingOut, PlayerAttribute.TendencyToPunch, PlayerAttribute.Throwing]);
        sections.Add(("Technical/Gk", technical));

        var mental = Panel();
        AddGroup(mental, Kind.PlayerAttribute, "Mental", [PlayerAttribute.Aggression, PlayerAttribute.Anticipation, PlayerAttribute.Bravery, PlayerAttribute.Composure, PlayerAttribute.Concentration, PlayerAttribute.Decisions, PlayerAttribute.Determination, PlayerAttribute.Flair, PlayerAttribute.Leadership, PlayerAttribute.OffTheBall, PlayerAttribute.Positioning, PlayerAttribute.Teamwork, PlayerAttribute.Vision, PlayerAttribute.WorkRate]);
        AddGroup(mental, Kind.PlayerPersonality, "Personality", Enum.GetValues<PersonalityAttribute>());
        sections.Add(("Mental Attributes", mental));

        var physical = Panel();
        AddGroup(physical, Kind.PlayerAttribute, "Physical", [PlayerAttribute.Acceleration, PlayerAttribute.Agility, PlayerAttribute.Balance, PlayerAttribute.JumpingReach, PlayerAttribute.LeftFoot, PlayerAttribute.NaturalFitness, PlayerAttribute.Pace, PlayerAttribute.RightFoot, PlayerAttribute.Stamina, PlayerAttribute.Strength]);
        AddGroup(physical, Kind.PlayerAttribute, "Hidden", [PlayerAttribute.Consistency, PlayerAttribute.Dirtiness, PlayerAttribute.ImportantMatches, PlayerAttribute.InjuryProneness, PlayerAttribute.Versatility]);
        sections.Add(("Physical/Hidden", physical));

        var positions = Panel();
        AddGroup(positions, Kind.PlayerPosition, "Player Positions", Enum.GetValues<PlayerPosition>());
        sections.Add(("Positions", positions));

        var moves = new StackPanel { Spacing = 6 };
        var moveHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var selectAll = new Button { Content = "All on" };
        var clearMoves = new Button { Content = "All off" };
        var resetMoves = new Button { Content = "Reset" };
        selectAll.Click += (_, _) => SetAllMoves(2);
        clearMoves.Click += (_, _) => SetAllMoves(1);
        resetMoves.Click += (_, _) => SetAllMoves(0);
        moveHeader.Children.Add(selectAll); moveHeader.Children.Add(clearMoves); moveHeader.Children.Add(resetMoves);
        moveHeader.Children.Add(new TextBlock { Text = "Filled = don't change, empty = off, ticked = on", Foreground = UiKit.TextMuted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        moves.Children.Add(moveHeader);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowSpacing = 2, ColumnSpacing = 16 };
        var names = PreferredMovesLabels.Names;
        int half = (names.Length + 1) / 2;
        moveStates = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var box = new CheckBox { Content = names[i], FontSize = 12.5, IsThreeState = true, IsChecked = null };
            box.Click += (_, e) =>
            {
                // FMRTE order: filled (don't change) -> empty (off) -> ticked (on) -> filled.
                moveStates[index] = (moveStates[index] + 1) % 3;
                box.IsChecked = moveStates[index] switch { 1 => false, 2 => true, _ => null };
                e.Handled = true;
                MarkDirty();
            };
            moveBoxes.Add(box);
            Grid.SetRow(box, i % half); Grid.SetColumn(box, i / half);
            grid.Children.Add(box);
        }
        for (int r = 0; r < half; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        moves.Children.Add(grid);
        sections.Add(("Preferred Moves", moves));

        var presets = Panel();
        AddPreset(presets, "All-in-one (heal, inspire, bans, withdrawal, tactical familiarity)", presetAll);
        AddPreset(presets, "Tactical understanding (familiarity and team cohesion to maximum)", presetTu);
        sections.Add(("Presets", presets));
        return sections;
    }

    IReadOnlyList<(string, Control)> BuildCoachSections()
    {
        var sections = new List<(string, Control)>();
        var general = Panel();
        AddHeader(general, "Reputation");
        Add(general, Kind.StaffField, (int)StaffMassField.HomeReputation, "Reputation", "Home", 0, 10000);
        Add(general, Kind.StaffField, (int)StaffMassField.CurrentReputation, "Reputation", "Current", 0, 10000);
        Add(general, Kind.StaffField, (int)StaffMassField.WorldReputation, "Reputation", "World", 0, 10000);
        AddHeader(general, "Ability");
        Add(general, Kind.StaffField, (int)StaffMassField.CurrentAbility, "Ability", "CA", 1, 200);
        Add(general, Kind.StaffField, (int)StaffMassField.PotentialAbility, "Ability", "PA", 1, 200);
        AddCheck(general, "Match CA to PA", out _);
        AddHeader(general, "Contract");
        Add(general, Kind.StaffWage, 0, "Contract", "Wages", 0, 10_000_000);
        sections.Add(("General", general));

        var coaching = Panel();
        AddGroup(coaching, Kind.StaffAttribute, "Coaching Attributes", [StaffAttribute.CoachingAttacking, StaffAttribute.CoachingDefending, StaffAttribute.CoachingFitness, StaffAttribute.CoachingGoalkeeping, StaffAttribute.CoachingPossession, StaffAttribute.CoachingPlayer, StaffAttribute.CoachingTactical, StaffAttribute.CoachingTechnical, StaffAttribute.PeopleManagement, StaffAttribute.WorkingWithYoungsters, StaffAttribute.DirtinessAllowance, StaffAttribute.Versatility, StaffAttribute.SetPieces]);
        AddGroup(coaching, Kind.StaffAttribute, "Mental Attributes", [StaffAttribute.Determination, StaffAttribute.JudgingPlayerAbility, StaffAttribute.JudgingPlayerPotential, StaffAttribute.JudgingStaffAbility, StaffAttribute.Authority, StaffAttribute.Motivating, StaffAttribute.Physiotherapy, StaffAttribute.TacticalKnowledge, StaffAttribute.Negotiating, StaffAttribute.SportsScience]);
        AddGroup(coaching, Kind.StaffAttribute, "Scouting", [StaffAttribute.JudgingPlayerData, StaffAttribute.JudgingTeamData, StaffAttribute.PresentingData]);
        sections.Add(("Coaching/Mental", coaching));

        var tactical = Panel();
        AddGroup(tactical, Kind.StaffAttribute, "Tactical Attributes", [StaffAttribute.TacticalAttacking, StaffAttribute.Depth, StaffAttribute.Directness, StaffAttribute.Flamboyancy, StaffAttribute.Flexibility, StaffAttribute.FreeRoles, StaffAttribute.Marking, StaffAttribute.Offside, StaffAttribute.Pressing, StaffAttribute.SittingBack, StaffAttribute.Tempo, StaffAttribute.UseOfPlaymaker, StaffAttribute.UseOfSubstitutions, StaffAttribute.Width]);
        AddGroup(tactical, Kind.StaffAttribute, "Non Tactical Attributes", [StaffAttribute.BuyingPlayers, StaffAttribute.HardnessOfTraining, StaffAttribute.MindGames, StaffAttribute.SquadRotation]);
        sections.Add(("Tactical/Non Tactical", tactical));

        var others = Panel();
        AddGroup(others, Kind.StaffPersonality, "Personality", Enum.GetValues<StaffPersonality>());
        AddGroup(others, Kind.StaffAttribute, "Chairman Attributes", [StaffAttribute.Business, StaffAttribute.Interference, StaffAttribute.Patience, StaffAttribute.Resources]);
        sections.Add(("Others", others));

        var presets = Panel();
        AddPreset(presets, "All-in-one (heal, inspire, bans, withdrawal, tactical familiarity)", presetAll);
        sections.Add(("Presets", presets));
        return sections;
    }

    /// <summary>FMRTE's Club General groups in FMRTE's order (Overview, Finances, Stadium); fields FMRTE does not have but the editor does stay at the end of their group or in their own group.</summary>
    static readonly (string Group, ClubField[] Fields)[] ClubLayout =
    [
        ("Overview", [ClubField.YearFounded, ClubField.Status, ClubField.ChairmanStatus, ClubField.Training, ClubField.YouthCoaching, ClubField.YouthFacilities, ClubField.YouthRecruitment,
            ClubField.YouthImportance, ClubField.CorporateFacilities, ClubField.Morale, ClubField.SeasonTicketHolders, ClubField.SocialMediaFollowers, ClubField.OwnershipType, ClubField.ChairpersonTitle]),
        ("Finances", [ClubField.Balance, ClubField.TransferBudgetSeason, ClubField.TransferBudgetRemaining, ClubField.TransferRevenuePercent, ClubField.WageBudget, ClubField.WageUsed,
            ClubField.MaxWage, ClubField.RichBenefactor, ClubField.AverageTicketPrice, ClubField.StadiumRentalPerYear]),
        ("Stadium", [ClubField.AverageAttendance, ClubField.MinimumAttendance, ClubField.MaximumAttendance]),
        ("Transfer Embargo", [ClubField.EmbargoAgeRestriction]),
        ("Distribution", [ClubField.SupportersHardcore, ClubField.SupportersCore, ClubField.SupportersFamily, ClubField.SupportersFairWeather, ClubField.SupportersCorporate, ClubField.SupportersCasual]),
        ("Profile", [ClubField.SupporterLoyalty, ClubField.SupporterPassion, ClubField.SupporterPatience, ClubField.SupporterAffluence, ClubField.SupporterTemperament]),
    ];

    IReadOnlyList<(string, Control)> BuildClubSections()
    {
        var panel = Panel();
        if (backend.State.Status != ConnectionStatus.Connected)
        {
            panel.Children.Add(new TextBlock { Text = "Load a save first.", Foreground = UiKit.TextMuted, Margin = new Thickness(4, 8) });
            return [("General", panel)];
        }
        var profile = backend.Session.Profile;
        var metas = ClubFieldInfo.All.Where(m => ClubMassEdit.IsSupported(profile, m.Field)).GroupBy(m => m.Field).ToDictionary(g => g.Key, g => g.First());
        foreach (var (group, layout) in ClubLayout)
        {
            var present = layout.Where(metas.ContainsKey).Select(f => metas[f]).Where(m => m.Options is not null || m.DisplayKind is ClubDisplayKind.PlainInt or ClubDisplayKind.FormattedInt or ClubDisplayKind.Money
                or ClubDisplayKind.Rating20 or ClubDisplayKind.Percent or ClubDisplayKind.Rating100).ToArray();
            if (present.Length == 0) continue;
            AddHeader(panel, group);
            foreach (var meta in present)
            {
                if (meta.Options is { Count: > 0 } options)
                    AddChoice(panel, Kind.ClubField, (int)meta.Field, group, meta.Label, options.OrderBy(o => o.Key).Select(o => ((int)o.Key, o.Value)).ToArray());
                else Add(panel, Kind.ClubField, (int)meta.Field, group, meta.Label, meta.Min, meta.Max);
            }
        }
        return [("General", panel)];
    }

    IReadOnlyList<(string, Control)> BuildNationSections()
    {
        var panel = Panel();
        foreach (var (title, items) in NationLabels.Groups)
        {
            AddHeader(panel, title);
            foreach (var (field, label) in items)
            {
                var (min, max) = NationFieldInfo.Range(field);
                Add(panel, Kind.NationField, (int)field, title, label, min, max);
            }
        }
        return [("Status", panel)];
    }

    static StackPanel Panel() => new() { Spacing = 2 };

    static Control Labeled(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(4, 2) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary });
        row.Children.Add(control);
        return row;
    }

    static Control Scroll(Control content) => new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    static void AddHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#A78BFA"), Margin = new Thickness(0, 12, 0, 4) });
    }

    static string Spaced(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 6);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(text[i - 1]) && text[i - 1] != ' ') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Replace("Freekicks", "Free Kicks").Replace("Longthrows", "Long Throws").Replace("Fifa", "FIFA").Replace("Fa ", "FA ");
    }

    FieldRow Add(StackPanel panel, Kind kind, int index, string group, string label, long min, long max)
    {
        var mode = new ComboBox { ItemsSource = new[] { "Don't change", "Set to", "Change by" }, SelectedIndex = ModeNone, Width = 128, FontSize = 12.5 };
        var value = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, Value = min, Width = 118, FontSize = 12.5, FormatString = "0", IsEnabled = false };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,128,118"), ColumnSpacing = 10, Margin = new Thickness(4, 2) };
        var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(mode, 1); Grid.SetColumn(value, 2);
        grid.Children.Add(text); grid.Children.Add(mode); grid.Children.Add(value);
        panel.Children.Add(grid);
        var row = new FieldRow(kind, index, group, label, min, max, mode, value);
        fields.Add(row);
        mode.SelectionChanged += (_, _) => { value.IsEnabled = mode.SelectedIndex != ModeNone; MarkDirty(); };
        value.ValueChanged += (_, _) => MarkDirty();
        return row;
    }

    /// <summary>A field whose values have names (FMRTE shows a dropdown, not a number): "Don't change" first, then each name.</summary>
    FieldRow AddChoice(StackPanel panel, Kind kind, int index, string group, string label, IReadOnlyList<(int Code, string Name)> options)
    {
        var mode = new ComboBox { ItemsSource = new[] { "Don't change" }.Concat(options.Select(o => o.Name)).ToArray(), SelectedIndex = ModeNone, Width = 250, FontSize = 12.5 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,250"), ColumnSpacing = 10, Margin = new Thickness(4, 2) };
        var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(mode, 1);
        grid.Children.Add(text); grid.Children.Add(mode);
        panel.Children.Add(grid);
        var codes = options.Select(o => o.Code).ToArray();
        var row = new FieldRow(kind, index, group, label, codes.Min(), codes.Max(), mode, new NumericUpDown(), codes);
        fields.Add(row);
        mode.SelectionChanged += (_, _) => MarkDirty();
        return row;
    }

    void AddGroup<T>(StackPanel panel, Kind kind, string group, IReadOnlyList<T> values) where T : struct, Enum
    {
        AddHeader(panel, group);
        foreach (var value in values) Add(panel, kind, Convert.ToInt32(value), group, Spaced(value.ToString()) is "Professionalism" ? "Professional" : Spaced(value.ToString()), 1, 20);
    }

    void AddCheck(StackPanel panel, string label, out CheckBox box)
    {
        box = new CheckBox { Content = label, FontSize = 12.5, Margin = new Thickness(4, 6, 0, 2) };
        box.IsCheckedChanged += (_, _) => MarkDirty();
        if (label.StartsWith("Match CA", StringComparison.Ordinal)) matchCaToPa.Add(box);
        panel.Children.Add(box);
    }

    void AddPreset(StackPanel panel, string label, List<CheckBox> store)
    {
        var box = new CheckBox { Content = label, FontSize = 12.5, Margin = new Thickness(4, 6, 0, 2) };
        box.IsCheckedChanged += (_, _) => MarkDirty();
        store.Add(box);
        panel.Children.Add(box);
    }

    readonly List<CheckBox> matchCaToPa = [];
    ComboBox? contractType;
    ComboBox? contractStatus;
    ComboBox? clauseType;
    NumericUpDown? clauseFee;
    CheckBox? removeClause;
    static readonly short[] ReleaseClauses = [0, 1, 2, 6, 7, 30];
    IReadOnlyList<PlayerRecord>? cachedPlayers;
    IReadOnlyList<StaffRecord>? cachedStaff;
    IReadOnlyList<ClubEntry>? cachedClubs;
    IReadOnlyList<NationRecord>? cachedNations;

    // What Review copied from the controls on the window thread; the background plan reads only these.
    List<FieldInput> planFields = [];
    bool planInjuryClear, planPresetAll, planPresetTu, planMatchCaToPa, planRemoveClause;
    InjuryType? planInjuryType;
    int planContractStatus, planContractType, planClauseType, planClauseFee;
    int[] planMoveStates = [];
    IReadOnlyList<PlayerRecord>? snapShownPlayers;
    IReadOnlyList<StaffRecord>? snapShownStaff;
    IReadOnlyList<ClubEntry>? snapShownClubs;
    IReadOnlyList<NationRecord>? snapShownNations;

    void SnapshotPlanInputs()
    {
        planFields = fields.Select(f => new FieldInput(f.Kind, f.Index, f.Label, f.Min, f.Max, f.ModeIndex, f.Number)).ToList();
        planInjuryClear = injuryClear?.IsChecked == true;
        planInjuryType = (injuryType?.SelectedItem as InjuryChoice)?.Type;
        planPresetAll = presetAll.Any(c => c.IsChecked == true);
        planPresetTu = presetTu.Any(c => c.IsChecked == true);
        planMatchCaToPa = matchCaToPa.Any(c => c.IsChecked == true);
        planMoveStates = moveStates.ToArray();
        planContractStatus = contractStatus?.SelectedIndex ?? 0;
        planContractType = contractType?.SelectedIndex ?? 0;
        planClauseType = clauseType?.SelectedIndex ?? 0;
        planClauseFee = (int)(clauseFee?.Value ?? 0);
        planRemoveClause = removeClause?.IsChecked == true;
        snapShownPlayers = host.MassEditPlayers;
        snapShownStaff = host.MassEditStaff;
        snapShownClubs = host.MassEditClubs;
        snapShownNations = host.MassEditNations;
    }

    void ClearPlanSnapshot()
    {
        snapShownPlayers = null; snapShownStaff = null; snapShownClubs = null; snapShownNations = null;
    }

    string Scope => scope;

    IReadOnlyList<PlayerRecord> PlayerTargets()
    {
        if (chosenPlayers is not null) return chosenPlayers;
        var shown = snapShownPlayers ?? host.MassEditPlayers;
        if (shown.Count > 0) return shown;
        if (cachedPlayers is not null) return cachedPlayers;
        return backend.State.Status == ConnectionStatus.Connected
            ? cachedPlayers = backend.Session.Players.LoadAll()
                .Where(p => p.Gender != Fm27Editor.Core.Offsets.Gender.Women && p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .ToArray()
            : [];
    }

    async Task ChooseTargetsAsync()
    {
        if (backend.State.Status != ConnectionStatus.Connected) return;
        snapShownPlayers = host.MassEditPlayers;
        (IReadOnlyList<PlayerRecord> Source, IReadOnlyList<NationRecord> Nations, IReadOnlyList<ClubEntry> Clubs) targets;
        try
        {
            targets = await Task.Run(() =>
            {
                var source = PlayerTargets();
                return (source, backend.Session.Nations.LoadAll(), backend.Session.LoadClubs(source));
            });
        }
        finally { ClearPlanSnapshot(); }
        var dialog = new AttributeFilterDialog(
            new PlayerFilter { Gender = Fm27Editor.Core.Offsets.Gender.Men, ExcludeWomenClubs = true },
            FilterPresetStore.LoadOrDefaults(), null, targets.Source,
            targets.Nations, targets.Clubs,
            initialScope: Scope,
            staffSource: () => StaffTargets(),
            clubSource: () => ClubTargets(),
            clubAccess: new ClubFieldAccess(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase),
            today: backend.Session.ReadGameDate());
        await dialog.ShowDialog(this);
        if (!dialog.Saved) return;

        switch (dialog.SelectedScope)
        {
            case "Coach":
                chosenStaff = dialog.MatchedStaff;
                SelectScope("Coach");
                break;
            case "Club":
                chosenClubs = dialog.MatchedClubs;
                SelectScope("Club");
                break;
            default:
            {
                var filter = new PlayerFilter
                {
                    Club = dialog.ResultClub,
                    Nationality = dialog.ResultNationality,
                    Division = dialog.ResultDivision,
                    MinAge = dialog.ResultMinAge,
                    MaxAge = dialog.ResultMaxAge,
                    MinCa = dialog.ResultMinCa,
                    MaxCa = dialog.ResultMaxCa,
                    MinPa = dialog.ResultMinPa,
                    MaxPa = dialog.ResultMaxPa,
                    Positions = dialog.ResultPositions.ToArray(),
                    PositionThresholds = new Dictionary<PlayerPosition, int>(dialog.ResultPositionThresholds),
                    PositionRanges = new Dictionary<PlayerPosition, NumericRange>(dialog.ResultPositionRanges),
                    NumericRanges = new Dictionary<string, NumericRange>(dialog.ResultNumericRanges),
                    RequiredPreferredMoves = dialog.ResultPreferredMoves,
                    PositionMinRating = dialog.ResultPositionMinRating,
                    AttributeThresholds = dialog.ResultThresholds,
                    Gender = dialog.ResultGender,
                    ExcludeWomenClubs = dialog.ResultExcludeWomenClubs,
                };
                var criteria = dialog.ResultRelationships;
                var matched = await Task.Run(() =>
                {
                    var module = new PlayersModule();
                    IEnumerable<PlayerRecord> found = targets.Source.Where(p => module.MatchesFilter(p, filter));
                    if (criteria.Count > 0)
                        found = RelationFilter.Find(found, criteria, new PlayerStatusReader(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase));
                    return found.ToArray();
                });
                chosenPlayers = matched;
                SelectScope("Player");
                break;
            }
        }
        UpdateTarget();
    }

    void SelectScope(string scope)
    {
        ShowScope(scope);
    }
    IReadOnlyList<StaffRecord> StaffTargets()
    {
        if (chosenStaff is not null) return chosenStaff;
        var shown = snapShownStaff ?? host.MassEditStaff;
        if (shown.Count > 0) return shown;
        if (cachedStaff is not null) return cachedStaff;
        return backend.State.Status == ConnectionStatus.Connected
            ? cachedStaff = backend.Session.Staff.LoadAll()
                .Where(s => s.Gender != Fm27Editor.Core.Offsets.Gender.Women && s.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .ToArray()
            : [];
    }

    IReadOnlyList<ClubEntry> ClubTargets()
    {
        if (chosenClubs is not null) return chosenClubs;
        var shown = snapShownClubs ?? host.MassEditClubs;
        if (shown.Count > 0) return shown;
        if (cachedClubs is not null) return cachedClubs;
        return backend.State.Status == ConnectionStatus.Connected
            ? cachedClubs = backend.Session.LoadClubs(PlayerTargets()).Where(c => c.Gender != Fm27Editor.Core.Offsets.Gender.Women).ToArray()
            : [];
    }

    IReadOnlyList<NationRecord> NationTargets()
    {
        var shown = snapShownNations ?? host.MassEditNations;
        if (shown.Count > 0) return shown;
        if (cachedNations is not null) return cachedNations;
        return backend.State.Status == ConnectionStatus.Connected ? cachedNations = backend.Session.Nations.LoadAll().ToArray() : [];
    }

    void UpdateTarget()
    {
        if (backend.State.Status != ConnectionStatus.Connected)
        {
            target.Text = "Not connected - press Load Game first";
            choose.IsEnabled = false;
            MarkDirty();
            return;
        }
        choose.IsEnabled = Scope is "Player" or "Coach" or "Club";
        if (Scope == "Player" && chosenPlayers is not null)
        {
            target.Text = $"{chosenPlayers.Count:N0} players (Advanced Search)";
            MarkDirty();
            return;
        }
        if (Scope == "Coach" && chosenStaff is not null)
        {
            target.Text = $"{chosenStaff.Count:N0} staff (Advanced Search)";
            MarkDirty();
            return;
        }
        if (Scope == "Club" && chosenClubs is not null)
        {
            target.Text = $"{chosenClubs.Count:N0} clubs (Advanced Search)";
            MarkDirty();
            return;
        }
        bool available = Scope switch
        {
            "Player" => host.MassEditPlayers.Count > 0 || cachedPlayers is not null,
            "Coach" => host.MassEditStaff.Count > 0 || cachedStaff is not null,
            "Club" => host.MassEditClubs.Count > 0 || cachedClubs is not null,
            _ => host.MassEditNations.Count > 0 || cachedNations is not null,
        };
        if (available) { ShowTargetCount(TargetCount()); MarkDirty(); return; }
        // Nothing is loaded yet: counting the targets reads the whole list from the game, so it runs in the
        // background and only the newest scope's count is shown.
        int version = ++targetVersion;
        string loadingScope = Scope;
        snapShownPlayers = host.MassEditPlayers; snapShownStaff = host.MassEditStaff;
        snapShownClubs = host.MassEditClubs; snapShownNations = host.MassEditNations;
        _ = Task.Run(TargetCount).ContinueWith(task =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ClearPlanSnapshot();
                if (version != targetVersion || Scope != loadingScope || task.IsFaulted) return;
                ShowTargetCount(task.Result);
                MarkDirty();
            }));
    }

    int targetVersion;

    int TargetCount() => Scope switch
    {
        "Player" => PlayerTargets().Count,
        "Coach" => StaffTargets().Count,
        "Club" => ClubTargets().Count,
        _ => NationTargets().Count,
    };

    void ShowTargetCount(int count)
    {
        string text = Scope switch { "Player" => "N players", "Coach" => "N coaches", "Club" => "N clubs", _ => "N nations" };
        target.Text = text.Replace("N", count.ToString("N0"));
    }
    void SetAllMoves(int state)
    {
        for (int i = 0; i < moveBoxes.Count; i++)
        {
            moveStates[i] = state;
            moveBoxes[i].IsChecked = state switch { 1 => false, 2 => true, _ => null };
        }
        MarkDirty();
    }

    void MarkDirty()
    {
        int changed = fields.Count(f => f.ModeIndex != ModeNone) + presetAll.Count(c => c.IsChecked == true) + presetTu.Count(c => c.IsChecked == true) + moveStates.Count(s => s != 0);
        if (contractType?.SelectedIndex > 0 || contractStatus?.SelectedIndex > 0) changed++;
        if (clauseType?.SelectedIndex > 0 || removeClause?.IsChecked == true) changed++;
        if (injuryClear?.IsChecked == true) changed++;
        if (injuryType?.SelectedIndex > 0) changed++;
        status.Text = changed == 0 ? "No changes set" : $"{changed:N0} change(s) set";
        apply.IsEnabled = changed > 0 && backend.State.Status == ConnectionStatus.Connected;
    }

    void Clear()
    {
        foreach (var field in fields) field.Reset();
        foreach (var box in presetAll) box.IsChecked = false;
        foreach (var box in presetTu) box.IsChecked = false;
        for (int i = 0; i < moveBoxes.Count; i++) { moveStates[i] = 0; moveBoxes[i].IsChecked = null; }
        foreach (var box in matchCaToPa) box.IsChecked = false;
        if (contractType is not null) contractType.SelectedIndex = 0;
        if (contractStatus is not null) contractStatus.SelectedIndex = 0;
        if (clauseType is not null) clauseType.SelectedIndex = 0;
        if (clauseFee is not null) clauseFee.Value = 0;
        if (removeClause is not null) removeClause.IsChecked = false;
        if (injuryClear is not null) injuryClear.IsChecked = false;
        if (injuryType is not null) injuryType.SelectedIndex = 0;
        MarkDirty();
    }

    static long Target(FieldInput row, MassEditMode mode, long before)
    {
        long after = mode == MassEditMode.Set ? row.Number : checked(before + row.Number);
        if (after < row.Min || after > row.Max) throw new ArgumentOutOfRangeException(nameof(row.Number), after, $"{row.Label} must be between {row.Min} and {row.Max}");
        return after;
    }
}
