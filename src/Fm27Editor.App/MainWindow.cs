using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow : Window
{
    const string AllClubsLabel = "All Clubs";
    const string AllNationalitiesLabel = "All Nationalities";
    const string AllLeaguesLabel = "All Leagues";
    const string NoPresetLabel = "Preset: None";

    sealed record SortOption(PlayerSortKey Key, string Label, PlayerAttribute? Attribute = null, bool DefaultDescending = true)
    {
        public override string ToString() => Label;
    }

    sealed record AttributeOption(PlayerAttribute Attribute, string Label)
    {
        public override string ToString() => Label;
    }

    readonly EditorBackend backend = new();
    readonly GraphicsCatalog graphics = new();
    readonly ComboBox modules = new() { Width = 120 };
    readonly Border gameDateBadge = new()
    {
        Margin = new Thickness(10, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };
    readonly TextBlock gameDateText = new()
    {
        FontSize = 13.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("#E2E8F0"),
        VerticalAlignment = VerticalAlignment.Center
    };

    readonly TextBox search = new() { Watermark = "Search player name or ID..." };
    readonly ComboBox sortCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button sortDirButton = new() { Content = "↓", Width = 28, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button resetFiltersButton = new() { Content = "Reset" };
    readonly Button exportCsvButton = new() { Content = "CSV", IsEnabled = false };
    readonly Button exportJsonButton = new() { Content = "JSON", IsEnabled = false };

    readonly ComboBox divisionFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox clubFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox nationFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };


    readonly ComboBox genderFilter = new() { ItemsSource = new[] { "Men", "Women", "All" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 260 };
    readonly ComboBox quickPresetCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    Fm27Editor.Core.Offsets.Gender? SelectedPlayerGender() => genderFilter.SelectedIndex switch
    {
        0 => Fm27Editor.Core.Offsets.Gender.Men,
        1 => Fm27Editor.Core.Offsets.Gender.Women,
        _ => null,
    };
    readonly Button favouritesFilter = new()
    {
        Width = 34, MinWidth = 34, Height = 28, MinHeight = 28, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = UiKit.Brush("#181C29"), BorderBrush = UiKit.Brush("#272D42"), BorderThickness = new Thickness(1),
    };
    readonly Button clearAttrButton = new() { Content = "Clear", IsVisible = false };
    readonly WrapPanel attrChipsPanel = new() { Orientation = Orientation.Horizontal, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<AttributeThreshold> activeAttrThresholds = [];
    readonly List<RelationshipCriterion> activeRelationships = [];
    readonly List<PlayerPosition> activePositions = [];
    readonly Dictionary<PlayerPosition, int> activePositionThresholds = [];
    readonly Dictionary<PlayerPosition, NumericRange> activePositionRanges = [];
    readonly Dictionary<string, NumericRange> activeNumericRanges = [];
    ulong activePreferredMoves;
    int activePosMinRating = 15;
    int? activeMinAge, activeMaxAge, activeMinCa, activeMaxCa, activeMinPa, activeMaxPa;
    string? activeDivision, activeClub, activeNationality;
    string? activeRoleCode;
    double? activeMinRoleScore;
    TalentedRegensTab? talentedRegensTab;
    ulong? activeTeamClubAddress;
    string? activeTeamClubName;
    ContractStatusFilter? activeContractStatus;
    ContractType? activeContractType;
    PlayingTime? activePlayingTime;
    IReadOnlyList<ContractClauseCriterion> activeClauses = [];
    IReadOnlyList<FilterPreset> savedPresets = [];
    string? activePresetName;
    bool sortDescending = true;
    bool suppressFilterEvents;

    readonly Ellipse connectionDot = new() { Width = 8, Height = 8, Fill = Brush("#64748B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    readonly TextBlock connection = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8"), FontSize = 13 };
    readonly DataGrid grid = new()
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
    StackPanel editor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly TextBlock status = new() { Margin = new Thickness(14, 5), Foreground = Brush("#94A3B8"), FontSize = 13.5 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    IReadOnlyList<object> rows = [];
    readonly PlayerSearchIndex playerSearchIndex = new();
    readonly LatestSearch latestPlayerSearch = new();
    IEditorModule? module;
    PlayerRecord? selected;
    bool updating, busy;
    string? lastFreezeError;
    int generation, searchVersion;
    string? pendingSection;
    PlayerPosition? selectedPosition;
    readonly Dictionary<ulong, PlayerDraft> drafts = [];
    readonly Dictionary<ulong, HashSet<string>> playerDirtySections = [];
    Button? saveButton;
    Func<bool>? informationDirty;
    Func<bool>? saveInformation;

    public MainWindow()
    {
        Title = "Football Manager 27 Editor";
        Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Fm27Editor/Assets/icon.png")));
        Width = 1100;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        Background = UiKit.WindowBg;
        FontSize = 13;
        Foreground = UiKit.TextPrimary;

        unifiedReleaseTimer = CreateUnifiedReleaseTimer();
        StartSaveWatcher();
        // A child tab's own pending edits star that tab (SectionHost) and the entity tab above it.
        SectionHost.SectionEdited += (_, dirty) => { if (dirty) SetActiveTabDirty(true); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.M && (e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0)
            {
                OpenMassEdit();
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.W && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control)
            {
                CloseActiveTab();
                e.Handled = true;
            }
        };
        status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty && e.NewValue is string t && t.Length > 0) FlightRecorder.Current?.Log("status", t); };
        Opened += (_, _) => _ = NotifyIfUpdateAsync();
        _ = graphics.LoadAsync();
        Opened += async (_, _) => { if (graphics.Folder is null) await ChooseGraphicsAsync(); };

        gameDateBadge.Child = gameDateText;
        InitializeFilterControls();

        var root = new DockPanel();

        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top); root.Children.Add(topBar);

        var statusRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Children = { gameDateBadge, connectionDot, connection, BuildBugButton() },
        };
        var statusDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(statusRight, Dock.Right);
        statusDock.Children.Add(statusRight);
        statusDock.Children.Add(status);

        var statusBar = new Border
        {
            Background = UiKit.SurfaceAlt,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 26,
            Child = statusDock
        };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);

        var moduleRow = new Border
        {
            Background = Brush("#11141E"),
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 3),
            Child = moduleBar,
        };
        DockPanel.SetDock(moduleRow, Dock.Top); root.Children.Add(moduleRow);

        var entityRow = BuildEntityRow();
        DockPanel.SetDock(entityRow, Dock.Top); root.Children.Add(entityRow);

        var filterArea = BuildFilterPanel();
        var playersHost = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(filterArea, Dock.Top);
        playersHost.Children.Add(filterArea);
        playersHost.Children.Add(grid);

        var bodyArea = new Grid();
        bodyArea.Children.Add(bodyHost);
        bodyArea.Children.Add(BuildStaleOverlay());
        bodyArea.Children.Add(BuildLoadingOverlay());
        root.Children.Add(bodyArea);
        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(BuildSearchOverlay());
        Content = shell;
        talentedRegensTab = new TalentedRegensTab(ShowDetailTab);
        AddModuleTab("Players", playersHost, null);
        AddModuleTab("Regens", talentedRegensTab.Root, null);

        modules.ItemsSource = Modules.Registered;
        modules.DisplayMemberBinding = new Binding("Title");
        modules.SelectionChanged += async (_, _) =>
        {
            module = modules.SelectedItem as IEditorModule;
            ConfigureColumns();
            status.Text = "";
            if (backend.State.Status == ConnectionStatus.Connected && module is not null && rows.Count == 0) await LoadAsync();
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (!updating) selected = grid.SelectedItem as PlayerRecord;
        };
        grid.DoubleTapped += (_, e) =>
        {
            if (UiKit.OnRow(e) && grid.SelectedItem is PlayerRecord picked) ShowDetailTab(picked);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && grid.SelectedItem is PlayerRecord picked)
            {
                ShowDetailTab(picked);
                e.Handled = true;
            }
        };
        Opened += (_, _) => { modules.SelectedIndex = 0; timer.Start(); };
        Closed += (_, _) => { timer.Stop(); backend.Dispose(); FlightRecorder.Current?.Close(); };
        timer.Tick += async (_, _) => await CheckSelectedAsync();
        timer.Tick += (_, _) =>
        {
            if (busy || backend.State.Status != ConnectionStatus.Connected) return;
            if (backend.Freezer is not null) backend.TickFreezer();
            if (backend.FreezeError is { } error && error != lastFreezeError)
            {
                lastFreezeError = error;
                status.Text = $"Freezer stopped: {error}";
            }
        };
        RegisterClubs();
        staffScreen = AddScreen(host => new StaffScreen(host, this) { ClearUndo = () => staffMoveUndo.Clear() });
        nationScreen = AddScreen(host => new NationScreen(host));
        stadiumScreen = AddScreen(host => new StadiumScreen(host));
        cityScreen = AddScreen(host => new CityScreen(host));
        competitionScreen = AddScreen(host => new CompetitionScreen(host));
        awardScreen = AddScreen(host => new AwardScreen(host));
        refereeScreen = AddScreen(host => new RefereeScreen(host));
        AddScreen(host => new InjuryTypeScreen(host));
        ShowModule("Players");
    }

    void InitializeFilterControls()
    {
        var attrOptions = Enum.GetValues<PlayerAttribute>()
            .Select(a => new AttributeOption(a, PlayerFields.All.First(f => f.Id == a.ToString()).Label))
            .OrderBy(a => a.Label, StringComparer.InvariantCultureIgnoreCase)
            .ToArray();

        var sortOptions = new List<SortOption>
        {
            new(PlayerSortKey.CurrentAbility, "Sort: CA", null, true),
            new(PlayerSortKey.PotentialAbility, "Sort: PA", null, true),
            new(PlayerSortKey.Rca, "Sort: RCA", null, true),
            new(PlayerSortKey.BestRoleScore, "Sort: Best Role %", null, true),
            new(PlayerSortKey.Personality, "Sort: Personality", null, false),
            new(PlayerSortKey.Age, "Sort: Age", null, false),
            new(PlayerSortKey.Name, "Sort: Name", null, false),
            new(PlayerSortKey.Club, "Sort: Club", null, false),
            new(PlayerSortKey.Nationality, "Sort: Nationality", null, false),
            new(PlayerSortKey.Division, "Sort: League", null, false),
        };
        foreach (var a in attrOptions)
            sortOptions.Add(new(PlayerSortKey.Attribute, $"Attr: {a.Label}", a.Attribute, true));

        sortCombo.ItemsSource = sortOptions;
        sortCombo.SelectedIndex = 0;

        savedPresets = FilterPresetStore.LoadOrDefaults();
        RefreshQuickPresetCombo();

        divisionFilter.ItemsSource = new[] { AllLeaguesLabel };
        divisionFilter.SelectedIndex = 0;
        clubFilter.ItemsSource = new[] { AllClubsLabel };
        clubFilter.SelectedIndex = 0;
        nationFilter.ItemsSource = new[] { AllNationalitiesLabel };
        nationFilter.SelectedIndex = 0;

        search.TextChanged += (_, _) => { if (!suppressFilterEvents) Filter(); };
        sortCombo.SelectionChanged += (_, _) =>
        {
            if (suppressFilterEvents) return;
            if (sortCombo.SelectedItem is SortOption opt)
            {
                sortDescending = opt.DefaultDescending;
                sortDirButton.Content = sortDescending ? "↓" : "↑";
            }
            Filter();
        };
        sortDirButton.Click += (_, _) =>
        {
            sortDescending = !sortDescending;
            sortDirButton.Content = sortDescending ? "↓" : "↑";
            Filter();
        };
        divisionFilter.SelectionChanged += (_, _) =>
        {
            if (suppressFilterEvents) return;
            RefreshClubDropdown();
            Filter();
        };
        clubFilter.SelectionChanged += (_, _) => { if (!suppressFilterEvents) Filter(); };
        nationFilter.SelectionChanged += (_, _) => { if (!suppressFilterEvents) Filter(); };
        genderFilter.SelectionChanged += (_, _) => { if (!suppressFilterEvents) { PopulateFilterDropdowns(); Filter(); } };

        favouritesFilter.Content = new Avalonia.Controls.PathIcon { Width = 14, Height = 14, Data = Avalonia.Media.Geometry.Parse("M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.63L12,2L9.19,8.63L2,9.24L7.46,13.97L5.82,21L12,17.27Z") };
        Avalonia.Controls.ToolTip.SetTip(favouritesFilter, "Favourites");
        favouritesFilter.Click += (_, _) => ShowFavourites();

        quickPresetCombo.SelectionChanged += (_, _) =>
        {
            if (suppressFilterEvents) return;
            if (quickPresetCombo.SelectedItem is FilterPreset preset)
            {
                activePresetName = preset.Name;
                activeDivision = preset.Division;
                activeClub = preset.Club;
                activeNationality = preset.Nationality;
                activeMinAge = preset.MinAge;
                activeMaxAge = preset.MaxAge;
                activeMinCa = preset.MinCa;
                activeMaxCa = preset.MaxCa;
                activeMinPa = preset.MinPa;
                activeMaxPa = preset.MaxPa;
                activePositions.Clear();
                if (preset.Positions is not null) activePositions.AddRange(preset.Positions);
                activePositionThresholds.Clear();
                activePositionRanges.Clear();
                if (preset.PositionRanges is { } pRanges)
                    foreach (var (position, range) in pRanges) activePositionRanges[position] = range;
                if (preset.PositionThresholds is { } thresholds)
                    foreach (var (position, rating) in thresholds) activePositionThresholds[position] = rating;
                activeNumericRanges.Clear();
                if (preset.NumericRanges is { } ranges)
                    foreach (var (key, range) in ranges) activeNumericRanges[key] = range;
                activePreferredMoves = preset.RequiredPreferredMoves;
                activePosMinRating = Math.Clamp(preset.PositionMinRating, 1, 20);
                activeAttrThresholds.Clear();
                activeAttrThresholds.AddRange(preset.AttributeThresholds);
                SelectSortByFirstAttributeIfNeeded();
            }
            else
            {
                ClearDialogFilters();
            }
            RenderAttrChips();
            Filter();
        };

        clearAttrButton.Click += (_, _) =>
        {
            ClearDialogFilters();
            RefreshQuickPresetCombo();
            RenderAttrChips();
            Filter();
        };

        resetFiltersButton.Click += (_, _) =>
        {
            suppressFilterEvents = true;
            search.Text = "";
            genderFilter.SelectedIndex = 0;
            ClearDialogFilters();
            PopulateFilterDropdowns();
            RefreshQuickPresetCombo();
            RenderAttrChips();
            sortCombo.SelectedIndex = 0;
            sortDescending = true;
            sortDirButton.Content = "↓";
            suppressFilterEvents = false;
            Filter();
        };
    }

    void ClearDialogFilters()
    {
        activePresetName = null;
        activeDivision = null;
        activeClub = null;
        activeNationality = null;
        activeMinAge = activeMaxAge = activeMinCa = activeMaxCa = activeMinPa = activeMaxPa = null;
        activeRoleCode = null;
        activeMinRoleScore = null;
        activePositions.Clear();
        activePositionThresholds.Clear();
        activePositionRanges.Clear();
        activeNumericRanges.Clear();
        activePreferredMoves = 0;
        activePosMinRating = 15;
        activeAttrThresholds.Clear();
        activeRelationships.Clear();
        activeTeamClubAddress = null;
        activeTeamClubName = null;
        activeContractStatus = null;
        activeContractType = null;
        activePlayingTime = null;
        activeClauses = [];
    }

    /// <summary>Opens at most one at a time: a second click while it is loading or open does nothing.</summary>
    async Task OpenAttributeFilterDialogAsync()
    {
        if (!openers.Add("OpenAttributeFilterDialogAsync")) return;
        try { await OpenAttributeFilterDialogAsyncOnce(); }
        finally { openers.Remove("OpenAttributeFilterDialogAsync"); }
    }

    readonly object catalogueLock = new();

    /// <summary>Fills the nation and club catalogues for this save if they are empty; safe from any thread.</summary>
    void WarmCatalogues(IReadOnlyList<PlayerRecord> players)
    {
        lock (catalogueLock)
        {
            nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            clubCache ??= backend.Session.LoadClubs(players);
        }
    }

    async Task OpenAttributeFilterDialogAsyncOnce()
    {
        var current = BuildCurrentFilter();
        var players = rows.OfType<PlayerRecord>().ToArray();
        var connected = backend.State.Status == ConnectionStatus.Connected;
        var statusReader = connected ? new PlayerStatusReader(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase) : null;
        IReadOnlyList<Fm27Editor.Core.Nations.NationRecord>? nations = null;
        IReadOnlyList<Fm27Editor.Core.Clubs.ClubEntry>? clubs = null;
        if (connected)
        {
            // Read once per save and kept (also warmed in the background after Load Game), so the window opens at once.
            if (nationCatalogue is null || clubCache is null)
                await Task.Run(() => WarmCatalogues(players));
            nations = nationCatalogue;
            clubs = clubCache;
        }
        var dialog = new AttributeFilterDialog(current, savedPresets, activePresetName, players,
            nations,
            clubs,
            staffSource: connected ? () => backend.Session.Staff.LoadAll() : null,
            clubSource: connected ? () => backend.Session.LoadClubs(players) : null,
            staffRelations: statusReader is null ? null : staff => statusReader.Read(backend.Session.Owner(staff)).Entries,
            clubAccess: connected ? new Fm27Editor.Core.Clubs.ClubFieldAccess(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase) : null,
            today: connected ? backend.Session.ReadGameDate() : null);
        await dialog.ShowDialog(this);
        savedPresets = dialog.UpdatedPresets;
        if (dialog.Saved && dialog.SelectedScope != "Player")
        {
            await new SearchResultsWindow(dialog.SelectedScope, dialog.MatchedStaff, dialog.MatchedClubs, backend.Session.ReadGameDate(), dialog.ScopeTotalCount, dialog.ScopeFilterNote).ShowDialog(this);
            RefreshQuickPresetCombo();
            return;
        }
        if (dialog.Saved)
        {
            search.Text = dialog.ResultNameQuery ?? "";
            activeDivision = dialog.ResultDivision;
            activeClub = dialog.ResultClub;
            activeNationality = dialog.ResultNationality;
            suppressFilterEvents = true;
            genderFilter.SelectedIndex = dialog.ResultGender switch
            {
                Fm27Editor.Core.Offsets.Gender.Men => 0,
                Fm27Editor.Core.Offsets.Gender.Women => 1,
                _ => 2,
            };
            suppressFilterEvents = false;
            PopulateFilterDropdowns();
            activeMinAge = dialog.ResultMinAge;
            activeMaxAge = dialog.ResultMaxAge;
            activeMinCa = dialog.ResultMinCa;
            activeMaxCa = dialog.ResultMaxCa;
            activeMinPa = dialog.ResultMinPa;
            activeMaxPa = dialog.ResultMaxPa;
            activeRoleCode = dialog.ResultRoleCode;
            activeMinRoleScore = dialog.ResultMinRoleScore;
            activePositions.Clear();
            activePositions.AddRange(dialog.ResultPositions);
            activePositionThresholds.Clear();
            foreach (var (position, rating) in dialog.ResultPositionThresholds) activePositionThresholds[position] = rating;
            activePositionRanges.Clear();
            foreach (var (position, range) in dialog.ResultPositionRanges) activePositionRanges[position] = range;
            activeNumericRanges.Clear();
            foreach (var (key, range) in dialog.ResultNumericRanges) activeNumericRanges[key] = range;
            activePreferredMoves = dialog.ResultPreferredMoves;
            activePosMinRating = dialog.ResultPositionMinRating;
            activeAttrThresholds.Clear();
            activeAttrThresholds.AddRange(dialog.ResultThresholds);
            activeRelationships.Clear();
            activeRelationships.AddRange(dialog.ResultRelationships);
            activeTeamClubAddress = dialog.ResultTeamClubAddress;
            activeTeamClubName = dialog.ResultTeamClubName;
            activeContractStatus = dialog.ResultContractStatus;
            activeContractType = dialog.ResultContractType;
            activePlayingTime = dialog.ResultPlayingTime;
            activeClauses = dialog.ResultClauses;
            activePresetName = dialog.SelectedPresetName;
            SelectSortByFirstAttributeIfNeeded();
            RefreshQuickPresetCombo();
            RenderAttrChips();
            Filter();
        }
        else
        {
            RefreshQuickPresetCombo();
        }
    }

    void SelectSortByFirstAttributeIfNeeded()
    {
        if (activeAttrThresholds.Count == 1 && sortCombo.ItemsSource is IEnumerable<SortOption> opts)
        {
            var firstAttr = activeAttrThresholds[0].Attribute;
            var match = opts.FirstOrDefault(o => o.Key == PlayerSortKey.Attribute && o.Attribute == firstAttr);
            if (match is not null)
            {
                suppressFilterEvents = true;
                sortCombo.SelectedItem = match;
                sortDescending = true;
                sortDirButton.Content = "↓";
                suppressFilterEvents = false;
            }
        }
    }

    void RefreshQuickPresetCombo()
    {
        bool wasSuppressed = suppressFilterEvents;
        suppressFilterEvents = true;
        var items = new List<object> { NoPresetLabel };
        items.AddRange(savedPresets);
        quickPresetCombo.ItemsSource = items;

        int idx = 0;
        if (!string.IsNullOrEmpty(activePresetName))
        {
            int found = savedPresets.ToList().FindIndex(p => string.Equals(p.Name, activePresetName, StringComparison.InvariantCultureIgnoreCase));
            if (found >= 0) idx = found + 1;
        }
        quickPresetCombo.SelectedIndex = idx;
        suppressFilterEvents = wasSuppressed;
    }

    Control BuildFilterPanel()
    {
        var stack = new StackPanel { Spacing = 8 };

        // Row 1: Search + Preset (Advanced Search now lives in the top bar)
        ToolTip.SetTip(genderFilter, "Gender filter");
        var row1 = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,104,180"),
            ColumnSpacing = 8,
        };
        Grid.SetColumn(search, 0); row1.Children.Add(search);
        Grid.SetColumn(genderFilter, 1); row1.Children.Add(genderFilter);
        Grid.SetColumn(quickPresetCombo, 2); row1.Children.Add(quickPresetCombo);
        stack.Children.Add(row1);

        stack.Children.Add(attrChipsPanel);

        return new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = stack
        };
    }

    static string ContractStatusChip(ContractStatusFilter status) => status switch
    {
        ContractStatusFilter.FreePlayer => "Free Player",
        ContractStatusFilter.Expired => "Expired",
        ContractStatusFilter.Expiring6Months => "Expiring in 6 Months",
        _ => "Expiring in 1 Year",
    };

    static string ClauseChip(ContractClauseCriterion clause)
    {
        if (!clause.HasClause)
            return $"{ContractLabels.Term(clause.Type)} No";
        string range = (clause.MinimumValue, clause.MaximumValue) switch
        {
            ({ } min, { } max) => $"{min:N0}–{max:N0}",
            ({ } min, null) => $">= {min:N0}",
            (null, { } max) => $"<= {max:N0}",
            _ => "",
        };
        return $"{ContractLabels.Term(clause.Type)} Yes{(range.Length > 0 ? " " + range : "")}";
    }

    void RenderAttrChips()
    {
        attrChipsPanel.Children.Clear();

        void AddChip(string text, Action onRemove)
        {
            var chip = new Button
            {
                Content = $"{text}  ×",
                Classes = { "chip" },
                Margin = new Thickness(0, 0, 6, 4)
            };
            chip.Click += (_, _) =>
            {
                onRemove();
                activePresetName = null;
                RefreshQuickPresetCombo();
                RenderAttrChips();
                Filter();
            };
            attrChipsPanel.Children.Add(chip);
        }

        if (!string.IsNullOrEmpty(activeDivision))
            AddChip($"League: {activeDivision}", () => { activeDivision = null; });
        if (!string.IsNullOrEmpty(activeClub))
            AddChip($"Club: {activeClub}", () => { activeClub = null; });
        if (activeTeamClubAddress is not null && !string.IsNullOrEmpty(activeTeamClubName))
            AddChip($"Team: {activeTeamClubName}", () => { activeTeamClubAddress = null; activeTeamClubName = null; });
        if (activeContractStatus is { } status)
            AddChip(ContractStatusChip(status), () => { activeContractStatus = null; });
        if (activeContractType is { } contractType)
            AddChip(ContractLabels.Of(contractType), () => { activeContractType = null; });
        if (activePlayingTime is { } playingTime)
            AddChip(ContractLabels.Of(playingTime), () => { activePlayingTime = null; });
        foreach (var clause in activeClauses.ToArray())
            AddChip(ClauseChip(clause), () => { activeClauses = activeClauses.Where(c => c.Type != clause.Type).ToArray(); });
        if (!string.IsNullOrEmpty(activeNationality))
            AddChip($"Nation: {activeNationality}", () => { activeNationality = null; });

        if (activeMinAge is not null || activeMaxAge is not null)
        {
            var label = (activeMinAge, activeMaxAge) switch
            {
                ({ } min, { } max) => $"Age {min}–{max}",
                ({ } min, null) => $"Age >= {min}",
                (null, { } max) => $"Age <= {max}",
                _ => "Age"
            };
            AddChip(label, () => { activeMinAge = null; activeMaxAge = null; });
        }

        if (activeMinCa is not null || activeMaxCa is not null)
        {
            var label = (activeMinCa, activeMaxCa) switch
            {
                ({ } min, { } max) => $"CA {min}–{max}",
                ({ } min, null) => $"CA >= {min}",
                (null, { } max) => $"CA <= {max}",
                _ => "CA"
            };
            AddChip(label, () => { activeMinCa = null; activeMaxCa = null; });
        }

        if (activeMinPa is not null || activeMaxPa is not null)
        {
            var label = (activeMinPa, activeMaxPa) switch
            {
                ({ } min, { } max) => $"PA {min}–{max}",
                ({ } min, null) => $"PA >= {min}",
                (null, { } max) => $"PA <= {max}",
                _ => "PA"
            };
            AddChip(label, () => { activeMinPa = null; activeMaxPa = null; });
        }

        foreach (var (key, range) in activeNumericRanges.ToArray())
            AddChip($"{key} {range.Min?.ToString() ?? "…"}–{range.Max?.ToString() ?? "…"}", () => activeNumericRanges.Remove(key));
        if (activeRoleCode is not null || activeMinRoleScore is not null)
        {
            string roleLabel = (activeRoleCode, activeMinRoleScore) switch
            {
                ({ } code, { } min) => $"Role: {code} >= {min:0.#}%",
                ({ } code, null) => $"Role: {code}",
                (null, { } min) => $"Best Role >= {min:0.#}%",
                _ => "Role"
            };
            AddChip(roleLabel, () => { activeRoleCode = null; activeMinRoleScore = null; });
        }
        if (activePreferredMoves != 0) AddChip("Preferred Moves", () => activePreferredMoves = 0);

        foreach (var pos in activePositions.ToArray())
        {
            var shortPos = PositionLabels.Short(pos);
            if (activePositionRanges.TryGetValue(pos, out var pr) && (pr.Min is not null || pr.Max is not null))
            {
                string text = pr.Max is not null && pr.Max < 20
                    ? $"Pos: {shortPos} {pr.Min ?? 1}–{pr.Max}"
                    : $"Pos: {shortPos} >= {pr.Min ?? activePosMinRating}";
                AddChip(text, () => { activePositions.Remove(pos); activePositionThresholds.Remove(pos); activePositionRanges.Remove(pos); });
            }
            else
            {
                AddChip($"Pos: {shortPos} >= {activePositionThresholds.GetValueOrDefault(pos, activePosMinRating)}", () => { activePositions.Remove(pos); activePositionThresholds.Remove(pos); activePositionRanges.Remove(pos); });
            }
        }

        foreach (var threshold in activeAttrThresholds.ToArray())
        {
            var label = PlayerFields.All.First(f => f.Id == threshold.Attribute.ToString()).Label;
            AddChip($"{label} >= {threshold.MinValue}", () => activeAttrThresholds.RemoveAll(t => t.Attribute == threshold.Attribute));
        }

        int count = attrChipsPanel.Children.Count;
        bool hasAny = count > 0;
        attrChipsPanel.IsVisible = hasAny;
        clearAttrButton.IsVisible = hasAny;
    }

    void PopulateFilterDropdowns()
    {
        var g = SelectedPlayerGender();
        var players = rows.OfType<PlayerRecord>()
            .Where(p => (g is null || p.Gender == g) && (g != Fm27Editor.Core.Offsets.Gender.Men || p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women))
            .ToArray();
        var leagues = players
            .Select(p => p.DivisionName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllLeaguesLabel)
            .ToArray();
        var nations = players
            .Select(p => p.NationalityName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllNationalitiesLabel)
            .ToArray();

        suppressFilterEvents = true;
        divisionFilter.ItemsSource = leagues;
        divisionFilter.SelectedIndex = 0;
        nationFilter.ItemsSource = nations;
        nationFilter.SelectedIndex = 0;
        RefreshClubDropdown();
        suppressFilterEvents = false;
    }

    void RefreshClubDropdown()
    {
        var g = SelectedPlayerGender();
        var players = rows.OfType<PlayerRecord>()
            .Where(p => (g is null || p.Gender == g) && (g != Fm27Editor.Core.Offsets.Gender.Men || p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women));
        var selectedLeague = divisionFilter.SelectedItem as string;
        if (!string.IsNullOrEmpty(selectedLeague) && selectedLeague != AllLeaguesLabel)
            players = players.Where(p => string.Equals(p.DivisionName, selectedLeague, StringComparison.InvariantCultureIgnoreCase));

        var clubs = players
            .Select(p => p.ClubName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllClubsLabel)
            .ToArray();

        bool wasSuppressed = suppressFilterEvents;
        suppressFilterEvents = true;
        var prevClub = clubFilter.SelectedItem as string;
        clubFilter.ItemsSource = clubs;
        int idx = prevClub is not null ? Array.FindIndex(clubs, c => string.Equals(c, prevClub, StringComparison.InvariantCultureIgnoreCase)) : 0;
        clubFilter.SelectedIndex = idx >= 0 ? idx : 0;
        suppressFilterEvents = wasSuppressed;
    }

    void UpdateConnectionBadge()
    {
        connectionDot.Fill = backend.State.Status switch
        {
            ConnectionStatus.Connected => Brush("#22C55E"),
            ConnectionStatus.UnsupportedBuild => Brush("#F59E0B"),
            _ => Brush("#64748B")
        };
    }

    /// <summary>What the user is editing right now; a write nobody grouped by name goes to this in the history.</summary>
    (string Kind, string Entity)? EditContext() => true switch
    {
        _ when ScreenEditContext() is { } screenContext => screenContext,
        _ when selected is { } player => ("Player", player.DisplayName),
        _ => null,
    };

    async Task ConnectAsync()
    {
        if (busy) return;
        busy = true;
        ShowLoading("Connecting to the game…");
        connection.Text = "Connecting…"; connectionDot.Fill = Brush("#94A3B8");
        try
        {
            await backend.ConnectAsync(CancellationToken.None);
            Fm27Editor.Core.Settings.GameCurrency.TryApply();
            FlightRecorder.Current?.Log("game", $"connect result {backend.State.Status}: {backend.State.Message}");
            squadPresetUndo.Clear();
            releaseUndo.Clear();
            captainUndo.Clear();
            lastFreezeError = null;
            UpdateConnectionBadge();
            connection.Text = backend.State.Message;
            if (backend.State.Status == ConnectionStatus.Connected)
            {
                backend.Edits.ContextProvider = EditContext;
                ShowLoading("Reading players…");
                await LoadAsync();
                bool saveLoaded = SaveIsLoaded();
                if (!saveLoaded) ShowNoSaveNotice(); else noSaveAtConnect = false;
                if (saveLoaded)
                {
                    // Everything a screen or search would otherwise read on its first click is read now, one list at a
                    // time, under the loading screen. The work-permit table stays lazy: that heap scan is the slow one and
                    // only the tabs that need it should pay for it.
                    var loadedPlayers = rows.OfType<PlayerRecord>().ToArray();
                    ShowLoading("Reading clubs and nations…");
                    await Task.Run(() => WarmCatalogues(loadedPlayers));
                    ShowLoading("Reading search lists…");
                    await EnsureUnifiedCacheAsync(SetLoadingDetail);
                    ShowLoading("Reading career plans…");
                    try { await Task.Run(() => WarmCareerCatalogue(loadedPlayers)); }
                    catch (Exception ex) { FlightRecorder.Current?.Log("warm", $"career plans: {ex.Message}"); }
                    ShowLoading("Reading…");
                    await Task.Run(() =>
                    {
                        // Each step on its own, so one that fails on this build does not skip the others.
                        void Warm(string what, Action step)
                        {
                            try { step(); }
                            catch (Exception ex) { FlightRecorder.Current?.Log("warm", $"{what}: {ex.Message}"); }
                        }
                        Warm("wonderkid academy site", () => backend.Session.GoldenGenerationHook.FindSite());
                        Warm("development speed site", () => backend.Session.AcceleratedDevelopment.FindSite());
                        Warm("transfer offer site", () => backend.Session.ForceTransferOffersHook.FindSite());
                    });
                    // Profiles still on the clause heap scan (Game Pass until measured live) warm it in the
                    // background; Steam reads the club vector and skips this.
                    if (backend.Session.Profile.Club?.TransferClauses is { Scan: true })
                        _ = Task.Run(() => backend.Session.EnsureTransferClausesLoaded());
                }
            }
            else { gameDateBadge.IsVisible = false; status.Text = backend.State.Message; }
            if (backend.FreezeError is { } error) status.Text = $"Freezer unavailable: {error}";
        }
        finally { HideLoading(); busy = false; }
    }

    async Task LoadAsync()
    {
        if (module is null) return;
        int current = ++generation;
        var elapsed = Stopwatch.StartNew();
        try
        {
            var loaded = await module.LoadAsync(backend, CancellationToken.None);
            if (current != generation) return;
            rows = loaded;
            talentedRegensTab?.SetPlayers(loaded.OfType<PlayerRecord>());
            selected = null;
            drafts.Clear();
            contractDrafts.Clear();
            clubCache = null; competitionCache = null; playerSource = null; languageCatalogue = null; nationCatalogue = null; cityCatalogue = null; careerCatalogue = null;
            editor.Children.Clear();

            if (backend.GameDate is { } gd)
            {
                gameDateText.Text = gd.ToString("dd MMM yyyy", Inv);
                gameDateBadge.IsVisible = true;
            }
            else
            {
                gameDateBadge.IsVisible = false;
            }

            PopulateFilterDropdowns();
            Filter();
            RebindPlayerTabs();
            UpdateConnectionBadge();
            if (rows.Count == 0) status.Text = "0 players";
            FlightRecorder.Current?.Log("game", $"loaded {rows.Count} {module.Title} rows in {elapsed.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    void ConfigureColumns()
    {
        grid.Columns.Clear();
        if (module is null) return;
        foreach (var c in module.Columns)
        {
            DataGridLength width = c.Title switch
            {
                "Name" => new DataGridLength(1, DataGridLengthUnitType.Star),
                "Age" => new DataGridLength(83),
                "CA" => new DataGridLength(83),
                "PA" => new DataGridLength(83),
                "Position" => new DataGridLength(173),
                "Club" => new DataGridLength(207),
                "Nationality" => new DataGridLength(178),
                "Asking Price" => new DataGridLength(167),
                _ => new DataGridLength(78),
            };
            var col = new DataGridTextColumn
            {
                Header = c.Title,
                Binding = c.Title == "Asking Price" ? new Binding(c.Binding) { StringFormat = "N0" } : new Binding(c.Binding),
                Width = width,
                MinWidth = c.Title == "Name" ? 135 : 54,
            };
            if (c.Title is "Age" or "CA" or "PA" or "Asking Price") col.CellStyleClasses.Add("numeric");
            grid.Columns.Add(col);
        }
        if (module is PlayersModule)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "RCA",
                Width = new DataGridLength(68),
                MinWidth = 54,
                CellTemplate = new FuncDataTemplate<PlayerRecord>((p, _) => new TextBlock
                {
                    Text = p is null ? "" : RcaCalculator.Calculate(p).ToString(Inv),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0),
                    Foreground = Brush("#38BDF8"),
                })
            });
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Best Role",
                Width = new DataGridLength(130),
                MinWidth = 100,
                CellTemplate = new FuncDataTemplate<PlayerRecord>((p, _) =>
                {
                    var text = "";
                    if (p is not null)
                    {
                        var best = RoleScores.BestFitRole(p);
                        text = $"{best.Role.Code} {best.Score:0.0}%";
                    }
                    return new TextBlock
                    {
                        Text = text,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0),
                    };
                })
            });
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Personality",
                Width = new DataGridLength(145),
                MinWidth = 110,
                CellTemplate = new FuncDataTemplate<PlayerRecord>((p, _) =>
                {
                    var classified = p is null ? null : PersonalityClassifier.Classify(p);
                    return new TextBlock
                    {
                        Text = classified?.Name ?? "",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0),
                        Foreground = classified?.IsNegative == true ? Brush("#F87171") : UiKit.TextPrimary,
                    };
                })
            });
        }
    }

    PlayerFilter BuildCurrentFilter() => new()
    {
        NameQuery = search.Text,
        Club = activeClub,
        Nationality = activeNationality,
        Division = activeDivision,
        TeamClubAddress = activeTeamClubAddress,
        TeamClubName = activeTeamClubName,
        ContractStatus = activeContractStatus,
        ContractType = activeContractType,
        AgreedPlayingTime = activePlayingTime,
        Clauses = activeClauses,
        MinAge = activeMinAge,
            MaxAge = activeMaxAge,
            MinCa = activeMinCa,
            MaxCa = activeMaxCa,
            MinPa = activeMinPa,
            MaxPa = activeMaxPa,
            Positions = activePositions.ToArray(),
            PositionThresholds = new Dictionary<PlayerPosition, int>(activePositionThresholds),
            PositionRanges = new Dictionary<PlayerPosition, NumericRange>(activePositionRanges),
            NumericRanges = new Dictionary<string, NumericRange>(activeNumericRanges),
            RequiredPreferredMoves = activePreferredMoves,
            PositionMinRating = activePosMinRating,
            AttributeThresholds = activeAttrThresholds.ToArray(),
            RoleCode = activeRoleCode,
            MinRoleScore = activeMinRoleScore,
            Gender = SelectedPlayerGender(),
            ExcludeWomenClubs = SelectedPlayerGender() == Fm27Editor.Core.Offsets.Gender.Men,
    };

    /// <summary>Contract Type, Agreed Playing Time and clause filters need live contract reads; the rest is snapshot.</summary>
    static bool NeedsContractDetailSearch(PlayerFilter filter) =>
        filter.ContractType is not null || filter.AgreedPlayingTime is not null || filter.Clauses.Count > 0;

    IReadOnlyList<PlayerRecord> FilterByRelationships(IReadOnlyList<PlayerRecord> rows) =>
        RelationFilter.Find(rows, activeRelationships, new PlayerStatusReader(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase));

    void Filter() => _ = FilterInternal(reportStatus: true);

    bool HasExtraPlayerFilters() =>
        !string.IsNullOrWhiteSpace(search.Text) || activeDivision is not null || activeClub is not null || activeNationality is not null ||
        activeTeamClubAddress is not null || activeContractStatus is not null || activeContractType is not null || activePlayingTime is not null ||
        activeClauses.Count > 0 ||
        activeMinAge is not null || activeMaxAge is not null || activeMinCa is not null || activeMaxCa is not null ||
        activeMinPa is not null || activeMaxPa is not null || activePositions.Count > 0 || activePositionRanges.Count > 0 ||
        activeRoleCode is not null || activeMinRoleScore is not null ||
        activeNumericRanges.Count > 0 || activePreferredMoves != 0 || activeAttrThresholds.Count > 0 || activeRelationships.Count > 0;

    async Task FilterInternal(bool reportStatus)
    {
        int version = ++searchVersion;
        exportCsvButton.IsEnabled = exportJsonButton.IsEnabled = false;
        if (module is null) return;
        var currentRows = rows;
        var currentModule = module;
        IReadOnlyList<object>? result;
        if (module is PlayersModule pm)
        {
            var filter = BuildCurrentFilter();
            var sortOpt = sortCombo.SelectedItem as SortOption;
            var sort = new PlayerSortSpec(
                sortOpt?.Key ?? PlayerSortKey.CurrentAbility,
                sortDescending,
                sortOpt?.Attribute);
            try
            {
                result = await latestPlayerSearch.RunAsync(
                    ct => playerSearchIndex.Filter(currentRows, pm, filter, sort, ct), TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
            if (result is null || version != searchVersion || !ReferenceEquals(rows, currentRows) || !ReferenceEquals(module, currentModule)) return;
            if (result is not null && NeedsContractDetailSearch(filter))
            {
                try
                {
                    var criteria = new ContractSearchCriteria(filter.ContractType, filter.AgreedPlayingTime) { Clauses = filter.Clauses };
                    var snapshot = result.OfType<PlayerRecord>().ToArray();
                    var found = await Task.Run(() => new ContractSearch(backend.Session.Contracts).Find(snapshot, criteria));
                    if (version != searchVersion || !ReferenceEquals(rows, currentRows) || !ReferenceEquals(module, currentModule)) return;
                    result = found.Matches.Cast<object>().ToArray();
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
            }
        }
        else
        {
            var g = SelectedPlayerGender();
            string query = search.Text?.Trim() ?? "";
            result = rows.Where(r =>
                (r is not PlayerRecord p || ((g is null || p.Gender == g) && (g != Fm27Editor.Core.Offsets.Gender.Men || p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women))) &&
                (query.Length == 0 || module.Matches(r, query))).ToArray();
        }

        if (module is PlayersModule && activeRelationships.Count > 0 && result is not null)
        {
            var candidates = result.OfType<PlayerRecord>().ToArray();
            result = (await Task.Run(() => FilterByRelationships(candidates))).Cast<object>().ToArray();
        }

        if (result is null) return;
        var prevSelectedAddress = selected?.Address;
        updating = true;
        grid.ItemsSource = result;
        exportCsvButton.IsEnabled = exportJsonButton.IsEnabled = module is PlayersModule;
        PlayerRecord? nextSelection = null;
        if (prevSelectedAddress is { } addr)
            nextSelection = result.OfType<PlayerRecord>().FirstOrDefault(p => p.Address == addr);
        grid.SelectedItem = nextSelection;
        updating = false;

        if (backend.State.Status == ConnectionStatus.Connected)
        {
            connection.Text = result.Count == rows.Count
                ? $"{backend.State.Message} · {rows.Count:N0}"
                : $"{backend.State.Message} · {result.Count:N0} / {rows.Count:N0}";
        }
        if (reportStatus && rows.Count > 0)
        {
            string noun = currentModule.Title.ToLowerInvariant();
            if (result.Count == rows.Count)
                status.Text = $"{rows.Count:N0} {noun}";
            else
            {
                string reason = SelectedPlayerGender() is { } g && !HasExtraPlayerFilters() ? $"{g} filter" : "filter";
                status.Text = $"{result.Count:N0} of {rows.Count:N0} {noun} shown ({rows.Count - result.Count:N0} hidden by {reason})";
            }
        }

        bool viewingPlayerTab = playerTabs.Any(t => ReferenceEquals(t.Root, bodyHost.Content));
        if (!viewingPlayerTab && !ReferenceEquals(selected, nextSelection))
        {
            selected = nextSelection;
            RenderEditor();
        }
    }

    async Task CheckSelectedAsync()
    {
        if (selected is null || busy || backend.State.Status != ConnectionStatus.Connected) return;
        try
        {
            var address = selected.Address;
            var player = await Task.Run(() => backend.Players.Reload(address));
            if (player is null && selected?.Address == address) { status.Text = "Player no longer available; reloading"; await LoadAsync(); }
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    static Border MetaBadge(string text, bool highlight = false) => new()
    {
        Height = 24,
        VerticalAlignment = VerticalAlignment.Center,
        Background = Brush(highlight ? "#261B47" : "#1D2233"),
        BorderBrush = Brush(highlight ? "#6D28D9" : "#2C334A"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 0),
        Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = Brush(highlight ? "#E9D5FF" : "#CBD5E1")
        }
    };

    static Button MetaLink(string text, bool highlight = false) => new()
    {
        Content = text, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 6, 0),
        FontSize = 13, MinHeight = 0, Background = Brush(highlight ? "#261B47" : "#1D2233"),
        BorderBrush = Brush(highlight ? "#6D28D9" : "#2C334A"), BorderThickness = new Thickness(1),
        Foreground = Brush(highlight ? "#E9D5FF" : "#CBD5E1"),
    };

    void RenderEditor()
    {
        editor.Children.Clear();
        informationDirty = null;
        saveInformation = null;
        if (selected is not { } player) return;
        if (!drafts.TryGetValue(player.Address, out var draft)) drafts[player.Address] = draft = new PlayerDraft(player);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var field in PlayerFields.All.Where(f => f.Group == "Ability"))
        {
            var ability = new NumericUpDown
            {
                Minimum = field.Min, Maximum = field.Max, Value = draft.Value(field),
                Width = 46, MinHeight = 24, Height = 24, ShowButtonSpinner = false,
                FontSize = 16, FontWeight = FontWeight.Black,
                Classes = { "borderless" },
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brush(field.Id == "CurrentAbility" ? "#38BDF8" : "#C084FC"),
            };
            ability.ValueChanged += (_, e) =>
            {
                if (updating || e.NewValue is null || e.NewValue == e.OldValue) return;
                draft.Stage(field, (int)e.NewValue.Value);
                UpdateScalarDraftState(player);
            };
            commands.Children.Add(UiKit.StatBox(field.Label, ability));
        }
        var rcaValue = new TextBlock
        {
            Text = RcaCalculator.Calculate(player).ToString(Inv),
            FontSize = 16,
            FontWeight = FontWeight.Black,
            Foreground = Brush("#4ADE80"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 34,
        };
        commands.Children.Add(UiKit.StatBox("RCA", rcaValue));

        var bestRole = RoleScores.BestFitRole(player);
        var roleValue = new TextBlock
        {
            Text = $"{bestRole.Role.Code} {bestRole.Score:0.0}%",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E2E8F0"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        commands.Children.Add(UiKit.StatBox("Best Role", roleValue));

        saveButton = new Button { IsVisible = false, Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" }, Margin = new Thickness(4, 0, 0, 0) };
        saveButton.Click += (_, _) => SavePlayerChanges(player);

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (player.Id is { } pid)
            badges.Children.Add(UiKit.MetaText($"ID {pid}"));

        if (player.Club is { } club && !string.IsNullOrEmpty(player.ClubName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var link = UiKit.MetaLinkButton(club.Name ?? player.ClubName, highlight: true);
            link.Click += async (_, _) => await OpenPlayerClubAsync(club.Address);
            badges.Children.Add(link);
        }

        if (player.Division is { } division && !string.IsNullOrEmpty(player.DivisionName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var link = UiKit.MetaLinkButton(division.Name ?? player.DivisionName);
            link.Click += (_, _) => OpenPlayerCompetition(division.Address);
            badges.Children.Add(link);
        }

        if (player.Nationality is { } nation && !string.IsNullOrEmpty(player.NationalityName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            int? natId = nation.Id;
            if (natId is null or <= 0)
            {
                nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                natId = nationCatalogue.FirstOrDefault(n => n.Address == nation.Address)?.Id
                    ?? nationCatalogue.FirstOrDefault(n => string.Equals(n.Name, player.NationalityName, StringComparison.OrdinalIgnoreCase))?.Id;
            }
            var flag = graphics.NationFlag(natId, 14);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { flag, new TextBlock { Text = nation.Name ?? player.NationalityName, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center } },
            };
            var link = UiKit.MetaLinkButton(content);
            link.Click += async (_, _) => await OpenPlayerNationAsync(nation.Address);
            badges.Children.Add(link);
        }

        if (player.Age is { } age)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"{age} yrs"));
        }

        if (player.Gender == Fm27Editor.Core.Offsets.Gender.Women)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText("Women", highlight: true));
        }

        commands.Children.Insert(0, new FavoriteStar(player.Id));
        var portrait = graphics.Image("person", player.Id, 72);
        var identityHeader = UiKit.EntityBanner(player.DisplayName, badges, commands.Children.Count > 0 ? commands : null, portrait);
        var playerTab = playerTabs.FirstOrDefault(t => ReferenceEquals(t.Panel, editor));
        if (playerTab is not null)
        {
            playerTab.Player = player;
            playerTab.Header.Content = identityHeader;
        }
        else editor.Children.Add(identityHeader);

        if (draft.Count > 0) status.Text = $"{draft.Count} unsaved change(s)";
        int changeCount = draft.Count + (playerScalarDrafts.TryGetValue(player.Address, out var scalarsForCount) ? scalarsForCount.Count : 0);
        bool contractPending = contractDrafts.Any(c => c.Value.Player.Address == player.Address && c.Value.Changes.Count > 0);
        SetActiveTabDirty(changeCount > 0 || contractPending);
        // Stars of the page's own edits: kept while their drafts exist, dropped once saved or discarded.
        if (playerPageSections.TryGetValue(player.Address, out var pageSections))
        {
            if (changeCount == 0) pageSections.RemoveWhere(k => k != "Contract");
            if (!contractPending) pageSections.Remove("Contract");
        }
        PublishSections(player);

        var sections = new List<SectionHost.Section>
        {
            new("Information", "Information", () => InformationSection(player)),
            new("Role Scores", "Role Scores", () => RoleScoresPanel(player)),
            new("Positions", "Positions", () => CreatePitch(player, draft)),
            new("Career Plans", "Career Plans", () => CareerPlansPanel(player)),
            new("Move to club", "Move to club", () => MovePanel(player)),
            new("Swap Player", "Swap Player", () => SwapPlayerPanel(player)),
            new("Work Permit", "Work Permit", () => WorkPermitPanel(player)),
            new("Promises", "Promises", () => PromisesPanel(player)),
            new("Unavailability", "Unavailability", () => UnavailabilityPanel(player)),
        };
        // Like FMRTE, Future Transfer appears only for players with an agreed future move.
        if (HasFutureTransfer(player))
            sections.Add(new("Future Transfer", "Future Transfer", () => FutureTransferPanel(player)!));
        if (HasTransferProposals(player))
            sections.Add(new("Transfer Proposals", "Transfer Proposals", () => ProposalsPanel(player)));
        sections.AddRange(
        [
            new("Injury", "Injury", () => InjuryPanel(player)),
            new("Contract", "Contract", () => ContractTabs(player)),
            new("Preferred Moves", "Preferred Moves", () => PreferredMovesPanel(player)),
            new("Relationships", "Relationships", () => RelationshipsPanel(player)),
            new("Languages", "Languages", () => LanguagesPanel(player)),
            new("Comparison", "Comparison", () => ComparisonPanel(player)),
            new("Clone Center", "Clone Center", () => ClonePanel(player)),
            new("Swap National Call Up", "Swap National Call Up", () => CallUpPanel(player)),
        ]);
        var actions = CreatePlayerActionsPanel(
            player: player,
            isSquadMode: false,
            getSquadPlayers: null,
            onStatus: msg => status.Text = msg);
        var sectionHost = SectionHost.Build("player", sections, pendingSection,
            key => actions.IsVisible = key == "Information", playerTab?.Rail);
        pendingSection = null;
        PublishSections(player);
        var bodyGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(sectionHost, 0);
        bodyGrid.Children.Add(sectionHost);
        actions.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(actions, 1);
        bodyGrid.Children.Add(actions);
        editor.Children.Add(bodyGrid);
        HookPlayerActions(player);
    }


    Control GroupControl(string group, PlayerRecord player, Control? customHeader = null) =>
        AttributeCard(group, PlayerFields.All.Where(f => f.Group == group).Select(f => FieldControl(f, player)), customHeader);

    /// <summary>The player attribute card: zebra rows of label + value, as on the player's Information page.</summary>
    internal static Control AttributeCard(string title, IEnumerable<Control> rows, Control? customHeader = null, Control? rightHeader = null)
    {
        var rowsPanel = new StackPanel { Spacing = 0 };
        int rowIndex = 0;
        foreach (var row in rows)
            rowsPanel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(4, 1, 4, 1),
                Child = row
            });
        return UiKit.Card(title, rowsPanel, customTitle: customHeader, rightHeader: rightHeader);
    }

    /// <summary>One attribute row: the label on the left, the value control docked right.</summary>
    internal static Control AttributeRow(string label, Control value)
    {
        var line = new DockPanel { MinHeight = 24, LastChildFill = true };
        var text = new TextBlock
        {
            Text = label, FontSize = 13, Foreground = Brush("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        };
        DockPanel.SetDock(value, Dock.Right);
        line.Children.Add(value);
        line.Children.Add(text);
        return line;
    }

    /// <summary>
    /// The 1-20 rating box of the player attribute page: bold number coloured by band, Enter or leaving the box commits,
    /// text outside <paramref name="min"/>..<paramref name="max"/> goes back to the last value.
    /// </summary>
    internal static TextBox RatingBox(int value, int min, int max, Action<int> commit, Func<int>? current = null)
    {
        int last = value;
        var rating = new TextBox
        {
            Text = value.ToString(Inv), Width = 28, MinWidth = 0, MaxWidth = 28, Height = 20, MinHeight = 20,
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2, 0),
            FontSize = 13, FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Background = RatingBg(value), BorderBrush = RatingBorder(value), Foreground = RatingColor(value),
        };
        if (current is not null) rating.GotFocus += (_, _) => last = current();
        rating.LostFocus += (_, _) =>
        {
            if (!int.TryParse(rating.Text, out int v) || v < min || v > max) v = last;
            rating.Text = v.ToString(Inv);
            rating.Foreground = RatingColor(v);
            rating.Background = RatingBg(v);
            rating.BorderBrush = RatingBorder(v);
            if (v != last) { last = v; commit(v); }
        };
        rating.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                TopLevel.GetTopLevel(rating)?.FocusManager?.ClearFocus();
                e.Handled = true;
            }
        };
        return rating;
    }

    static readonly PlayerPosition[] DefensivePositions = PitchLayout.DefensivePositions;
    static readonly PlayerPosition[] AttackingPositions = PitchLayout.AttackingPositions;

    Control CreatePitch(PlayerRecord player, PlayerDraft draft)
    {
        const double width = 314, height = 428, dotSize = 26;
        var leftStack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        var pitch = PitchSurface.Create(width, height);

        var dots = new Dictionary<PlayerPosition, Button>();
        var rowUpdaters = new Dictionary<PlayerPosition, Action<int>>();
        var rowBorders = new Dictionary<PlayerPosition, Border>();
        var selection = new ContentControl { Width = width + 2, HorizontalAlignment = HorizontalAlignment.Left };
        bool syncingPos = false;

        void Select(PitchPosition position)
        {
            selectedPosition = position.Position;
            foreach (var (key, dot) in dots)
            {
                bool active = key == position.Position;
                dot.BorderBrush = active ? Brushes.White : Brush("#1A4339");
                dot.BorderThickness = new Thickness(active ? 2 : 1);
            }
            foreach (var (key, rb) in rowBorders)
            {
                rb.BorderBrush = key == position.Position ? UiKit.Accent : Brushes.Transparent;
            }
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var ratingControl = FieldControl(field, player, onChanged: value =>
            {
                if (syncingPos) return;
                syncingPos = true;
                PitchSurface.SetRating(dots[position.Position], value);
                if (rowUpdaters.TryGetValue(position.Position, out var upd)) upd(value);
                syncingPos = false;
            }, labelOverride: "Rating");
            ratingControl.Width = 72;
            var title = new TextBlock
            {
                Text = $"{position.Label} ({PositionLabels.Short(position.Position)})",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = UiKit.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            Grid.SetColumn(ratingControl, 1);
            panel.Children.Add(title);
            panel.Children.Add(ratingControl);
            selection.Content = new Border
            {
                Background = UiKit.Surface,
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Child = panel
            };
        }

        foreach (var position in PitchLayout.Positions)
        {
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            int val = draft.Value(field);
            var dot = PitchSurface.CreateDot(position, val);
            Canvas.SetLeft(dot, position.X * width - dotSize / 2);
            Canvas.SetTop(dot, position.Y * height - dotSize / 2);
            dot.Click += (_, _) => Select(position);
            dots[position.Position] = dot;
            pitch.Children.Add(dot);
        }

        leftStack.Children.Add(new Border
        {
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = pitch
        });
        leftStack.Children.Add(selection);

        Border BuildPosCard(string title, IReadOnlyList<PlayerPosition> list)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var pos in list)
            {
                var pitchPos = PitchLayout.Positions.Single(p => p.Position == pos);
                var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == pos.ToString());
                int initialVal = draft.Value(field);

                var colorDot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = PitchSurface.PositionColor(initialVal),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                };
                var codeTag = new TextBlock
                {
                    Text = PositionLabels.Short(pos),
                    Width = 30,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = UiKit.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var nameLbl = new TextBlock
                {
                    Text = pitchPos.Label,
                    FontSize = 13,
                    Foreground = UiKit.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                };

                int beforeEdit = initialVal;
                var box = new TextBox
                {
                    Text = initialVal.ToString(Inv),
                    Width = 28,
                    MinWidth = 0,
                    MaxWidth = 28,
                    Height = 20,
                    MinHeight = 20,
                    Padding = new Thickness(2, 0),
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = PitchSurface.PositionBg(initialVal),
                    BorderBrush = PitchSurface.PositionBorder(initialVal),
                    Foreground = PitchSurface.PositionFg(initialVal),
                };
                void ApplyVisual(int v)
                {
                    box.Text = v.ToString(Inv);
                    box.Foreground = PitchSurface.PositionFg(v);
                    box.Background = PitchSurface.PositionBg(v);
                    box.BorderBrush = PitchSurface.PositionBorder(v);
                    colorDot.Fill = PitchSurface.PositionColor(v);
                }
                rowUpdaters[pos] = ApplyVisual;

                box.GotFocus += (_, _) => beforeEdit = draft.Value(field);
                box.LostFocus += (_, _) =>
                {
                    if (!int.TryParse(box.Text, out int value) || value < field.Min || value > field.Max)
                        value = beforeEdit;
                    ApplyVisual(value);
                    if (value != draft.Value(field))
                    {
                        draft.Stage(field, value);
                        PitchSurface.SetRating(dots[pos], value);
                        if (selectedPosition == pos && !syncingPos)
                        {
                            syncingPos = true;
                            Select(pitchPos);
                            syncingPos = false;
                        }
                        if (selected?.Address == player.Address) UpdateScalarDraftState(player);
                    }
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                    {
                        TopLevel.GetTopLevel(box)?.FocusManager?.ClearFocus();
                        e.Handled = true;
                    }
                };

                var rowDock = new DockPanel { LastChildFill = true, MinHeight = 26 };
                DockPanel.SetDock(box, Dock.Right);
                rowDock.Children.Add(box);
                DockPanel.SetDock(colorDot, Dock.Left);
                rowDock.Children.Add(colorDot);
                DockPanel.SetDock(codeTag, Dock.Left);
                rowDock.Children.Add(codeTag);
                rowDock.Children.Add(nameLbl);

                var rowBorder = new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    BorderBrush = Brushes.Transparent,
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = rowDock,
                };
                rowBorder.PointerPressed += (_, e) =>
                {
                    if (e.Source is TextBox) return;
                    Select(pitchPos);
                };
                rowBorders[pos] = rowBorder;
                stack.Children.Add(rowBorder);
            }
            return UiKit.Card(title, stack);
        }

        var rightGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var defCard = BuildPosCard("Defensive", DefensivePositions);
        var attCard = BuildPosCard("Midfield & Attack", AttackingPositions);
        Grid.SetColumn(defCard, 0);
        Grid.SetColumn(attCard, 1);
        rightGrid.Children.Add(defCard);
        rightGrid.Children.Add(attCard);

        var initial = PitchLayout.Positions.FirstOrDefault(p => p.Position == selectedPosition)
            ?? PitchLayout.Positions.MaxBy(p => draft.Value(PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == p.Position.ToString())))!;
        Select(initial);

        var outer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(leftStack, 0);
        Grid.SetColumn(rightGrid, 1);
        outer.Children.Add(leftStack);
        outer.Children.Add(rightGrid);
        return outer;
    }

    Control FieldControl(FieldDescriptor<PlayerRecord> field, PlayerRecord player, bool compact = false, Action<int>? onChanged = null, string? labelOverride = null)
    {
        var line = new DockPanel { MinHeight = 24, LastChildFill = true };
        string resolvedLabel = labelOverride ?? (compact ? field.Label.Replace("Defensive Midfielder", "DM").Replace("Attacking Mid", "AM").Replace("Midfielder", "M").Replace("Defender", "D").Replace("Wing Back", "WB").Replace("Goalkeeper", "GK").Replace("Striker", "ST") : field.Label);
        var label = new TextBlock
        {
            Text = resolvedLabel,
            FontSize = 13,
            Foreground = Brush("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            IsVisible = resolvedLabel.Length > 0,
        };
        if (field.Write is null)
        {
            var value = new TextBlock
            {
                Text = field.Read(player).ToString("N0"),
                Margin = new Thickness(6, 0, 2, 0),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#F1F5F9"),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            DockPanel.SetDock(value, Dock.Right); line.Children.Add(value); line.Children.Add(label); return line;
        }
        // A section body is built lazily, possibly after the draft was dropped by a save or reload.
        if (!drafts.TryGetValue(player.Address, out var draft)) drafts[player.Address] = draft = new PlayerDraft(player);
        int currentVal = draft.Value(field);
        void Staged(int value)
        {
            draft.Stage(field, value);
            onChanged?.Invoke(value);
            if (selected?.Address == player.Address)
            {
                UpdateScalarDraftState(player);
            }
        }
        if (field.Kind == FieldKind.Date)
        {
            static string FormatBorn(int v) => v == 0 ? "" : DateSetting.Format(DateField.FromInt(v));
            int stagedDateVal = currentVal;
            var dateBox = new DateBox
            {
                // the format follows the date setting
                Text = FormatBorn(stagedDateVal),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 0),
            };
            dateBox.Committed += picked =>
            {
                if (updating || picked.Year is < 1900 or > 2100) return;
                int next = DateField.ToInt(picked);
                stagedDateVal = next;
                if (next != draft.Value(field)) Staged(next);
            };
            dateBox.LostFocus += (_, _) =>
            {
                // Text that is not a date goes back to the last good value.
                if (!DateBox.TryParse(dateBox.Text, out var day) || day.Year is < 1900 or > 2100) dateBox.Text = FormatBorn(stagedDateVal);
            };
            Control dateHost = dateBox;
            if (resolvedLabel.Length == 0) return dateHost;
            dateHost.Width = 148;
            DockPanel.SetDock(dateHost, Dock.Right); line.Children.Add(dateHost); line.Children.Add(label);
            return line;
        }
        if (field.Kind == FieldKind.Rating20)
        {
            var rating = RatingBox(currentVal, field.Min, field.Max, value => { if (value != draft.Value(field)) Staged(value); }, () => draft.Value(field));
            DockPanel.SetDock(rating, Dock.Right); line.Children.Add(rating); line.Children.Add(label);
            return line;
        }
        var input = new NumericUpDown
        {
            Minimum = field.Min,
            Maximum = field.Max,
            Increment = 1,
            Value = currentVal,
            ShowButtonSpinner = false,
            Width = resolvedLabel.Length == 0 ? double.NaN : 64,
            HorizontalAlignment = resolvedLabel.Length == 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Right,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Foreground = field.Kind == FieldKind.Rating20
                ? (field.Group == "Positions" ? PitchSurface.PositionFg(currentVal) : RatingColor(currentVal))
                : UiKit.TextPrimary,
            Background = field.Group == "Positions" ? PitchSurface.PositionBg(currentVal) : UiKit.SurfaceInput,
            BorderBrush = field.Group == "Positions" ? PitchSurface.PositionBorder(currentVal) : UiKit.BorderControl,
        };
        input.ValueChanged += (_, e) =>
        {
            if (updating || e.NewValue is null || e.OldValue == e.NewValue) return;
            int value = (int)e.NewValue.Value;
            Staged(value);
            if (field.Kind == FieldKind.Rating20)
            {
                input.Foreground = field.Group == "Positions" ? PitchSurface.PositionFg(value) : RatingColor(value);
                if (field.Group == "Positions")
                {
                    input.Background = PitchSurface.PositionBg(value);
                    input.BorderBrush = PitchSurface.PositionBorder(value);
                }
            }
        };
        if (resolvedLabel.Length == 0) return input;
        DockPanel.SetDock(input, Dock.Right); line.Children.Add(input); line.Children.Add(label);
        return line;
    }

    void SavePlayerChanges(PlayerRecord player)
    {
        bool hadInformationChanges = informationDirty?.Invoke() == true;
        if (hadInformationChanges && saveInformation?.Invoke() != true) return;
        if (playerScalarDrafts.TryGetValue(player.Address, out var scalars) && scalars.Count > 0)
        {
            foreach (var (field, (expected, value)) in scalars.ToArray())
            {
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.PlayerFields.Set(player, field, expected, value));
                    scalars.Remove(field);
                }
                catch (Exception ex)
                {
                    if (hadInformationChanges && backend.Players.Reload(player.Address) is { } updated)
                    {
                        updated.GameDate = backend.GameDate;
                        selected = updated;
                        ReplaceRow(updated);
                        RenderEditor();
                    }
                    else UpdateScalarDraftState(player);
                    status.Text = $"Some changes may have been saved. Failed to save {field}: {ex.Message}";
                    return;
                }
            }
            playerScalarDrafts.Remove(player.Address);
        }
        if (!SaveDraft()) return;
        if (backend.Players.Reload(player.Address) is { } fresh)
        {
            fresh.GameDate = backend.GameDate;
            selected = fresh;
            ReplaceRow(fresh);
        }
        RenderEditor();
        status.Text = "Changes saved. Continue in FM to see updates.";
    }

    bool SaveDraft()
    {
        if (selected is null || !drafts.TryGetValue(selected.Address, out var draft) || draft.Count == 0) return true;
        var address = selected.Address;
        var count = draft.Count;
        try
        {
            selected = draft.Save(backend.Edits);
            selected.GameDate = backend.GameDate;
            drafts.Remove(address);
            ReplaceRow(selected);
            status.Text = $"Saved {count} change(s). Continue in FM to see updates.";
            return true;
        }
        catch (Exception ex)
        {
            // Some earlier fields may already have succeeded; never present a partial save as atomic.
            int saved = count - draft.Count;
            try
            {
                var fresh = backend.Players.Reload(address);
                if (fresh is null) { drafts.Remove(address); _ = LoadAsync(); }
                else { fresh.GameDate = backend.GameDate; selected = fresh; drafts[address] = draft.Rebase(fresh); ReplaceRow(fresh); RenderEditor(); }
                status.Text = $"{saved} saved. {UserMessage.For(ex)} Review remaining changes.";
            }
            catch (Exception reloadError) { status.Text = $"{saved} saved. {UserMessage.For(reloadError)}"; }
            return false;
        }
    }

    void ReplaceRow(PlayerRecord p)
    {
        p.GameDate ??= backend.GameDate;
        var arr = rows.ToArray(); int index = Array.FindIndex(arr, x => x is PlayerRecord old && old.Address == p.Address);
        if (index >= 0) { arr[index] = p; rows = arr; talentedRegensTab?.SetPlayers(arr.OfType<PlayerRecord>()); }
        _ = FilterInternal(reportStatus: false); updating = true; grid.SelectedItem = p; updating = false;
    }

    internal static IBrush RatingColor(int value) => Brush(value <= 5 ? "#64748B" : value <= 10 ? "#FACC15" : value <= 15 ? "#4ADE80" : "#38BDF8");
    internal static IBrush RatingBg(int value) => Brush(value <= 5 ? "#0E1119" : value <= 10 ? "#181611" : value <= 15 ? "#0E1B18" : "#0D1A26");
    internal static IBrush RatingBorder(int value) => Brush(value <= 5 ? "#23293B" : value <= 10 ? "#3B341B" : value <= 15 ? "#1D3B2F" : "#1C3B57");
    static IBrush PositionColor(int value) => Brush(value <= 5 ? "#334155" : value <= 10 ? "#F59E0B" : value <= 15 ? "#EAB308" : "#84CC16");
    static IBrush Brush(string color) => AppTheme.Brush(color);
}
