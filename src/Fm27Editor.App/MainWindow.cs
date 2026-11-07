using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
        FontSize = 12,
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
    readonly Button massEditButton = new() { Content = "Mass Edit", IsEnabled = false };

    readonly ComboBox divisionFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox clubFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox nationFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    readonly Button openAttrDialogButton = new() { Content = "Scout", MinWidth = 72, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly ComboBox quickPresetCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button clearAttrButton = new() { Content = "Clear", IsVisible = false };
    readonly WrapPanel attrChipsPanel = new() { Orientation = Orientation.Horizontal, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<AttributeThreshold> activeAttrThresholds = [];
    readonly List<PlayerPosition> activePositions = [];
    readonly Dictionary<PlayerPosition, int> activePositionThresholds = [];
    readonly Dictionary<string, NumericRange> activeNumericRanges = [];
    ulong activePreferredMoves;
    int activePosMinRating = 15;
    int? activeMinAge, activeMaxAge, activeMinCa, activeMaxCa, activeMinPa, activeMaxPa;
    IReadOnlyList<FilterPreset> savedPresets = [];
    string? activePresetName;
    bool sortDescending = true;
    bool suppressFilterEvents;

    readonly Ellipse connectionDot = new() { Width = 8, Height = 8, Fill = Brush("#64748B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    readonly TextBlock connection = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8"), FontSize = 11.5 };
    readonly Button retry = new() { Content = "Retry" };
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
    readonly TextBlock status = new() { Margin = new Thickness(14, 5), Foreground = Brush("#94A3B8"), FontSize = 12 };
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
    Button? saveButton;
    Button? discardButton;
    Func<bool>? informationDirty;
    Func<bool>? saveInformation;

    public MainWindow()
    {
        Title = "Football Manager 27 Editor";
        Width = 1100;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        Background = UiKit.WindowBg;
        FontSize = 11.5;
        Foreground = UiKit.TextPrimary;

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
            Children = { gameDateBadge, connectionDot, connection },
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

        root.Children.Add(bodyHost);
        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(BuildSearchOverlay());
        Content = shell;
        AddModuleTab("Players", playersHost, null);

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
        grid.DoubleTapped += (_, _) =>
        {
            if (grid.SelectedItem is PlayerRecord picked) ShowDetailTab(picked);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && grid.SelectedItem is PlayerRecord picked)
            {
                ShowDetailTab(picked);
                e.Handled = true;
            }
        };
        retry.Click += async (_, _) => await ConnectAsync();
        Opened += (_, _) => { modules.SelectedIndex = 0; timer.Start(); };
        Closed += (_, _) => { timer.Stop(); backend.Dispose(); };
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
        RegisterClubsView();
        RegisterStaffView();
        RegisterNationView();
        RegisterStadiumView();
        RegisterCityView();
        RegisterCompetitionView();
        RegisterAwardView();
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

        openAttrDialogButton.Click += async (_, _) => await OpenAttributeFilterDialogAsync();

        quickPresetCombo.SelectionChanged += (_, _) =>
        {
            if (suppressFilterEvents) return;
            if (quickPresetCombo.SelectedItem is FilterPreset preset)
            {
                activePresetName = preset.Name;
                activeMinAge = preset.MinAge;
                activeMaxAge = preset.MaxAge;
                activeMinCa = preset.MinCa;
                activeMaxCa = preset.MaxCa;
                activeMinPa = preset.MinPa;
                activeMaxPa = preset.MaxPa;
                activePositions.Clear();
                if (preset.Positions is not null) activePositions.AddRange(preset.Positions);
                activePositionThresholds.Clear();
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
            divisionFilter.SelectedIndex = 0;
            RefreshClubDropdown();
            clubFilter.SelectedIndex = 0;
            nationFilter.SelectedIndex = 0;
            ClearDialogFilters();
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
        activeMinAge = activeMaxAge = activeMinCa = activeMaxCa = activeMinPa = activeMaxPa = null;
        activePositions.Clear();
        activePositionThresholds.Clear();
        activeNumericRanges.Clear();
        activePreferredMoves = 0;
        activePosMinRating = 15;
        activeAttrThresholds.Clear();
    }

    async Task OpenAttributeFilterDialogAsync()
    {
        var current = BuildCurrentFilter();
        var dialog = new AttributeFilterDialog(current, savedPresets, activePresetName);
        await dialog.ShowDialog(this);
        savedPresets = dialog.UpdatedPresets;
        if (dialog.Saved)
        {
            activeMinAge = dialog.ResultMinAge;
            activeMaxAge = dialog.ResultMaxAge;
            activeMinCa = dialog.ResultMinCa;
            activeMaxCa = dialog.ResultMaxCa;
            activeMinPa = dialog.ResultMinPa;
            activeMaxPa = dialog.ResultMaxPa;
            activePositions.Clear();
            activePositions.AddRange(dialog.ResultPositions);
            activePositionThresholds.Clear();
            foreach (var (position, rating) in dialog.ResultPositionThresholds) activePositionThresholds[position] = rating;
            activeNumericRanges.Clear();
            foreach (var (key, range) in dialog.ResultNumericRanges) activeNumericRanges[key] = range;
            activePreferredMoves = dialog.ResultPreferredMoves;
            activePosMinRating = dialog.ResultPositionMinRating;
            activeAttrThresholds.Clear();
            activeAttrThresholds.AddRange(dialog.ResultThresholds);
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
        var stack = new StackPanel { Spacing = 5 };

        // Row 1: Search + Scout + Preset + Sort + Direction + Actions
        var row1 = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,160,148,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 5,
        };
        Grid.SetColumn(search, 0); row1.Children.Add(search);
        Grid.SetColumn(openAttrDialogButton, 1); row1.Children.Add(openAttrDialogButton);
        Grid.SetColumn(quickPresetCombo, 2); row1.Children.Add(quickPresetCombo);
        Grid.SetColumn(sortCombo, 3); row1.Children.Add(sortCombo);
        Grid.SetColumn(sortDirButton, 4); row1.Children.Add(sortDirButton);
        Grid.SetColumn(clearAttrButton, 5); row1.Children.Add(clearAttrButton);
        Grid.SetColumn(resetFiltersButton, 6); row1.Children.Add(resetFiltersButton);
        Grid.SetColumn(massEditButton, 7); row1.Children.Add(massEditButton);
        Grid.SetColumn(exportCsvButton, 8); row1.Children.Add(exportCsvButton);
        Grid.SetColumn(exportJsonButton, 9); row1.Children.Add(exportJsonButton);
        stack.Children.Add(row1);

        // Row 2: League, Club, Nationality dropdowns
        var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        Grid.SetColumn(divisionFilter, 0); row2.Children.Add(divisionFilter);
        Grid.SetColumn(clubFilter, 1); row2.Children.Add(clubFilter);
        Grid.SetColumn(nationFilter, 2); row2.Children.Add(nationFilter);
        stack.Children.Add(row2);

        exportCsvButton.Click += async (_, _) => await ExportPlayersAsync(false);
        exportJsonButton.Click += async (_, _) => await ExportPlayersAsync(true);
        massEditButton.Click += async (_, _) => await ShowMassEditAsync();
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
        if (activePreferredMoves != 0) AddChip("Preferred Moves", () => activePreferredMoves = 0);

        foreach (var pos in activePositions.ToArray())
        {
            var shortPos = PositionLabels.Short(pos);
            AddChip($"Pos: {shortPos} >= {activePositionThresholds.GetValueOrDefault(pos, activePosMinRating)}", () => { activePositions.Remove(pos); activePositionThresholds.Remove(pos); });
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
        openAttrDialogButton.Content = hasAny ? $"Scout ({count})" : "Scout";
        if (hasAny) openAttrDialogButton.Classes.Add("accent");
        else openAttrDialogButton.Classes.Remove("accent");
    }

    void PopulateFilterDropdowns()
    {
        var players = rows.OfType<PlayerRecord>().ToArray();
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
        var players = rows.OfType<PlayerRecord>();
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

    async Task ConnectAsync()
    {
        if (busy) return;
        busy = true; retry.IsEnabled = false; connection.Text = "Connecting…"; connectionDot.Fill = Brush("#94A3B8");
        try
        {
            await backend.ConnectAsync(CancellationToken.None);
            massUndo.Clear();
            squadPresetUndo.Clear();
            releaseUndo.Clear();
            captainUndo.Clear();
            lastFreezeError = null;
            UpdateConnectionBadge();
            connection.Text = backend.State.Message;
            if (backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
            else { gameDateBadge.IsVisible = false; status.Text = backend.State.Message; }
            if (backend.FreezeError is { } error) status.Text = $"Freezer unavailable: {error}";
        }
        finally { busy = false; retry.IsEnabled = true; }
    }

    async Task LoadAsync()
    {
        if (module is null) return;
        int current = ++generation;
        try
        {
            var loaded = await module.LoadAsync(backend, CancellationToken.None);
            if (current != generation) return;
            rows = loaded;
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
            UpdateConnectionBadge();
            status.Text = $"{rows.Count:N0} players";
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
                "Age" => new DataGridLength(72),
                "CA" => new DataGridLength(72),
                "PA" => new DataGridLength(72),
                "Position" => new DataGridLength(150),
                "Club" => new DataGridLength(180),
                "Nationality" => new DataGridLength(155),
                "Asking Price" => new DataGridLength(145),
                _ => new DataGridLength(68),
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
    }

    PlayerFilter BuildCurrentFilter()
    {
        var clubSel = clubFilter.SelectedItem as string;
        var natSel = nationFilter.SelectedItem as string;
        var divSel = divisionFilter.SelectedItem as string;
        return new PlayerFilter
        {
            NameQuery = search.Text,
            Club = clubSel is null or AllClubsLabel ? null : clubSel,
            Nationality = natSel is null or AllNationalitiesLabel ? null : natSel,
            Division = divSel is null or AllLeaguesLabel ? null : divSel,
            MinAge = activeMinAge,
            MaxAge = activeMaxAge,
            MinCa = activeMinCa,
            MaxCa = activeMaxCa,
            MinPa = activeMinPa,
            MaxPa = activeMaxPa,
            Positions = activePositions.ToArray(),
            PositionThresholds = new Dictionary<PlayerPosition, int>(activePositionThresholds),
            NumericRanges = new Dictionary<string, NumericRange>(activeNumericRanges),
            RequiredPreferredMoves = activePreferredMoves,
            PositionMinRating = activePosMinRating,
            AttributeThresholds = activeAttrThresholds.ToArray(),
        };
    }

    async void Filter()
    {
        int version = ++searchVersion;
        exportCsvButton.IsEnabled = exportJsonButton.IsEnabled = massEditButton.IsEnabled = false;
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
        }
        else
        {
            string query = search.Text?.Trim() ?? "";
            result = query.Length == 0 ? rows : rows.Where(r => module.Matches(r, query)).ToArray();
        }

        var prevSelectedAddress = selected?.Address;
        updating = true;
        grid.ItemsSource = result;
        exportCsvButton.IsEnabled = exportJsonButton.IsEnabled = massEditButton.IsEnabled = module is PlayersModule;
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
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = Brush(highlight ? "#E9D5FF" : "#CBD5E1")
        }
    };

    static Button MetaLink(string text, bool highlight = false) => new()
    {
        Content = text, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 6, 0),
        FontSize = 11.5, MinHeight = 0, Background = Brush(highlight ? "#261B47" : "#1D2233"),
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

        saveButton = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" }, Margin = new Thickness(4, 0, 0, 0) };
        saveButton.Click += (_, _) => SavePlayerChanges(player);
        discardButton = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        discardButton.Click += (_, _) =>
        {
            drafts.Remove(player.Address);
            playerScalarDrafts.Remove(player.Address);
            RenderEditor();
            status.Text = "Changes discarded";
        };

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
            nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            int? natId = nationCatalogue.FirstOrDefault(n => n.Address == nation.Address)?.Id
                ?? nationCatalogue.FirstOrDefault(n => string.Equals(n.Name, player.NationalityName, StringComparison.OrdinalIgnoreCase))?.Id;
            var flag = graphics.Image("nationflag", natId, 15);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { flag, new TextBlock { Text = nation.Name ?? player.NationalityName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
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

        var portrait = graphics.Image("person", player.Id, 72);
        var identityHeader = UiKit.EntityBanner(player.DisplayName, badges, commands.Children.Count > 0 ? commands : null, portrait);
        var playerTab = playerTabs.FirstOrDefault(t => ReferenceEquals(t.Panel, editor));
        if (playerTab is not null)
        {
            playerTab.Player = player;
            playerTab.Header.Content = identityHeader;
        }
        else editor.Children.Add(identityHeader);

        status.Text = draft.Count > 0 ? $"{draft.Count} unsaved change(s)" : "";

        var pitch = CreatePitch(player, draft);
        var sections = new List<SectionHost.Section>
        {
            new("Information", "Information", InformationSection(player)),
            new("Positions", "Positions", pitch),
            new("Career Plans", "Career Plans", CareerPlansPanel(player)),
            new("Move to club", "Move to club", MovePanel(player)),
            new("Work Permit", "Work Permit", EmptySection()),
            new("Promises", "Promises", EmptySection()),
            new("Unavailability", "Unavailability", UnavailabilityPanel(player)),
        };
        // Like FMRTE, Future Transfer appears only for players with an agreed future move.
        if (FutureTransferPanel(player) is { } future)
            sections.Add(new("Future Transfer", "Future Transfer", future));
        sections.AddRange(
        [
            new("Transfer Proposals", "Transfer Proposals", ProposalsPanel(player)),
            new("Injury", "Injury", InjuryPanel(player)),
            new("Contract", "Contract", ContractTabs(player)),
            new("Preferred Moves", "Preferred Moves", PreferredMovesPanel(player)),
            new("Relationships", "Relationships", RelationshipsPanel(player)),
            new("Languages", "Languages", LanguagesPanel(player)),
            new("Scout Information", "Scout Information", EmptySection()),
            new("Comparison", "Comparison", ComparisonPanel(player)),
            new("Clone Center", "Clone Center", ClonePanel(player)),
            new("Swap National Call Up", "Swap National Call Up", CallUpPanel(player)),
        ]);
        var actions = CreatePlayerActionsPanel(
            player: player,
            isSquadMode: false,
            getSquadPlayers: null,
            onStatus: msg => status.Text = msg);
        var sectionHost = SectionHost.Build("player", sections, pendingSection,
            key => actions.IsVisible = key == "Information", playerTab?.Rail);
        pendingSection = null;
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

    static Control EmptySection() => new Panel();

    Control GroupControl(string group, PlayerRecord player, Control? customHeader = null)
    {
        var rowsPanel = new StackPanel { Spacing = 0 };
        int rowIndex = 0;
        foreach (var field in PlayerFields.All.Where(f => f.Group == group))
        {
            var rowControl = FieldControl(field, player);
            rowsPanel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(4, 1, 4, 1),
                Child = rowControl
            });
        }
        return UiKit.Card(group, rowsPanel, customTitle: customHeader);
    }

    static readonly PlayerPosition[] DefensivePositions =
    [
        PlayerPosition.Goalkeeper,
        PlayerPosition.DefenderLeft,
        PlayerPosition.DefenderCentral,
        PlayerPosition.DefenderRight,
        PlayerPosition.WingBackLeft,
        PlayerPosition.WingBackRight,
        PlayerPosition.DefensiveMidfielder,
    ];

    static readonly PlayerPosition[] AttackingPositions =
    [
        PlayerPosition.MidfielderLeft,
        PlayerPosition.MidfielderCentral,
        PlayerPosition.MidfielderRight,
        PlayerPosition.AttackingMidLeft,
        PlayerPosition.AttackingMidCentral,
        PlayerPosition.AttackingMidRight,
        PlayerPosition.Striker,
    ];

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
                FontSize = 11.5,
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
                    Fill = PositionColor(initialVal),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                };
                var codeTag = new TextBlock
                {
                    Text = PositionLabels.Short(pos),
                    Width = 30,
                    FontSize = 10.5,
                    FontWeight = FontWeight.Bold,
                    Foreground = UiKit.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var nameLbl = new TextBlock
                {
                    Text = pitchPos.Label,
                    FontSize = 11.5,
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
                    FontSize = 11.5,
                    FontWeight = FontWeight.Bold,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = RatingBg(initialVal),
                    BorderBrush = RatingBorder(initialVal),
                    Foreground = RatingColor(initialVal),
                };
                void ApplyVisual(int v)
                {
                    box.Text = v.ToString(Inv);
                    box.Foreground = RatingColor(v);
                    box.Background = RatingBg(v);
                    box.BorderBrush = RatingBorder(v);
                    colorDot.Fill = PositionColor(v);
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
            FontSize = 11.5,
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
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#F1F5F9"),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            DockPanel.SetDock(value, Dock.Right); line.Children.Add(value); line.Children.Add(label); return line;
        }
        var draft = drafts[player.Address];
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
            static string FormatBorn(int v) => v == 0 ? "" : DateField.FromInt(v).ToString("dd-MM-yyyy", Inv);
            int stagedDateVal = currentVal;
            var dateBox = new TextBox
            {
                Text = FormatBorn(stagedDateVal),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 0),
            };
            var cal = new Avalonia.Controls.Calendar
            {
                DisplayDateStart = new DateTime(1900, 1, 1),
                DisplayDateEnd = new DateTime(2100, 12, 31),
                SelectedDate = stagedDateVal == 0 ? null : DateField.FromInt(stagedDateVal).ToDateTime(TimeOnly.MinValue),
                DisplayDate = stagedDateVal == 0 ? new DateTime(2000, 1, 1) : DateField.FromInt(stagedDateVal).ToDateTime(TimeOnly.MinValue),
            };
            var flyout = new Flyout { Content = cal };
            var calBtn = new Button
            {
                Width = 24,
                MinWidth = 24,
                Height = 24,
                Padding = new Thickness(0),
                Flyout = flyout,
                Content = new PathIcon
                {
                    Width = 12,
                    Height = 12,
                    Foreground = Brush("#94A3B8"),
                    Data = Geometry.Parse("M3,4H5V2H7V4H17V2H19V4H21A1,1 0 0,1 22,5V20A1,1 0 0,1 21,21H3A1,1 0 0,1 2,20V5A1,1 0 0,1 3,4M4,9V19H20V9H4Z"),
                },
            };
            bool syncingCal = false;
            cal.SelectedDatesChanged += (_, _) =>
            {
                if (syncingCal || updating || cal.SelectedDate is not { } dt) return;
                int next = DateField.ToInt(DateOnly.FromDateTime(dt));
                stagedDateVal = next;
                dateBox.Text = FormatBorn(next);
                Staged(next);
                flyout.Hide();
            };
            dateBox.LostFocus += (_, _) =>
            {
                string raw = (dateBox.Text ?? "").Trim();
                if (DateOnly.TryParseExact(raw, ["dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "yyyy-MM-dd"], Inv, System.Globalization.DateTimeStyles.None, out var parsed)
                    && parsed.Year is >= 1900 and <= 2100)
                {
                    int next = DateField.ToInt(parsed);
                    stagedDateVal = next;
                    dateBox.Text = FormatBorn(next);
                    syncingCal = true;
                    cal.SelectedDate = parsed.ToDateTime(TimeOnly.MinValue);
                    cal.DisplayDate = cal.SelectedDate.Value;
                    syncingCal = false;
                    if (next != draft.Value(field)) Staged(next);
                }
                else
                {
                    dateBox.Text = FormatBorn(stagedDateVal);
                }
            };
            var dateHost = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24"), ColumnSpacing = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(dateBox, 0);
            Grid.SetColumn(calBtn, 1);
            dateHost.Children.Add(dateBox);
            dateHost.Children.Add(calBtn);
            if (resolvedLabel.Length == 0) return dateHost;
            dateHost.Width = 148;
            DockPanel.SetDock(dateHost, Dock.Right); line.Children.Add(dateHost); line.Children.Add(label);
            return line;
        }
        if (field.Kind == FieldKind.Rating20)
        {
            int beforeEdit = currentVal;
            var rating = new TextBox
            {
                Text = currentVal.ToString(Inv), Width = 28, MinWidth = 0, MaxWidth = 28, Height = 20, MinHeight = 20,
                HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2, 0),
                FontSize = 11.5, FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = RatingBg(currentVal), BorderBrush = RatingBorder(currentVal),
                Foreground = RatingColor(currentVal),
            };
            rating.GotFocus += (_, _) => beforeEdit = draft.Value(field);
            rating.LostFocus += (_, _) =>
            {
                if (!int.TryParse(rating.Text, out int value) || value < field.Min || value > field.Max)
                    value = beforeEdit;
                rating.Text = value.ToString(Inv);
                rating.Foreground = RatingColor(value);
                rating.Background = RatingBg(value);
                rating.BorderBrush = RatingBorder(value);
                if (value != draft.Value(field)) Staged(value);
            };
            rating.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                {
                    TopLevel.GetTopLevel(rating)?.FocusManager?.ClearFocus();
                    e.Handled = true;
                }
            };
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
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Foreground = field.Kind == FieldKind.Rating20 ? RatingColor(currentVal) : UiKit.TextPrimary
        };
        input.ValueChanged += (_, e) =>
        {
            if (updating || e.NewValue is null || e.OldValue == e.NewValue) return;
            int value = (int)e.NewValue.Value;
            Staged(value);
            if (field.Kind == FieldKind.Rating20) input.Foreground = RatingColor(value);
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
                    backend.Edits.Execute(() => backend.Session.PlayerFields.Set(player, field, expected, value));
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
        if (index >= 0) { arr[index] = p; rows = arr; }
        Filter(); updating = true; grid.SelectedItem = p; updating = false;
    }

    static IBrush RatingColor(int value) => new SolidColorBrush(Color.Parse(value <= 5 ? "#64748B" : value <= 10 ? "#FACC15" : value <= 15 ? "#4ADE80" : "#38BDF8"));
    static IBrush RatingBg(int value) => Brush(value <= 5 ? "#0E1119" : value <= 10 ? "#181611" : value <= 15 ? "#0E1B18" : "#0D1A26");
    static IBrush RatingBorder(int value) => Brush(value <= 5 ? "#23293B" : value <= 10 ? "#3B341B" : value <= 15 ? "#1D3B2F" : "#1C3B57");
    static IBrush PositionColor(int value) => Brush(value <= 5 ? "#334155" : value <= 10 ? "#F59E0B" : value <= 15 ? "#EAB308" : "#84CC16");
    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
