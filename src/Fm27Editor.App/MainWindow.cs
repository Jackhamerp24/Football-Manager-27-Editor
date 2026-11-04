using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
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
    readonly ComboBox modules = new() { Width = 120 };
    readonly Border gameDateBadge = new()
    {
        Background = Brush("#1B1F2E"),
        BorderBrush = Brush("#282E42"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 4),
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

    readonly TextBox search = new() { Watermark = "Search player name..." };
    readonly ComboBox sortCombo = new() { Width = 172 };
    readonly Button sortDirButton = new() { Content = "↓", Width = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button resetFiltersButton = new() { Content = "Reset" };
    readonly Button exportCsvButton = new() { Content = "CSV", IsEnabled = false };
    readonly Button exportJsonButton = new() { Content = "JSON", IsEnabled = false };
    readonly Button massEditButton = new() { Content = "Mass Edit", IsEnabled = false };

    readonly ComboBox divisionFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox clubFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox nationFilter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    readonly Button openAttrDialogButton = new() { Content = "Filters & Attributes...", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly ComboBox quickPresetCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button clearAttrButton = new() { Content = "Clear", IsVisible = false };
    readonly WrapPanel attrChipsPanel = new() { Orientation = Orientation.Horizontal, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<AttributeThreshold> activeAttrThresholds = [];
    readonly List<PlayerPosition> activePositions = [];
    int activePosMinRating = 15;
    int? activeMinAge, activeMaxAge, activeMinCa, activeMaxCa, activeMinPa, activeMaxPa;
    IReadOnlyList<FilterPreset> savedPresets = [];
    string? activePresetName;
    bool sortDescending = true;
    bool suppressFilterEvents;

    readonly Ellipse connectionDot = new() { Width = 8, Height = 8, Fill = Brush("#64748B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
    readonly TextBlock connection = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8"), FontSize = 12.5 };
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
    readonly StackPanel editor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
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
    int editorTab;
    PlayerPosition? selectedPosition;
    readonly Dictionary<ulong, PlayerDraft> drafts = [];
    Button? saveButton;
    Button? discardButton;

    public MainWindow()
    {
        Title = "Football Manager 27 Editor";
        Width = 1480;
        Height = 880;
        MinWidth = 1040;
        MinHeight = 660;
        Background = Brush("#0D0F17");
        FontSize = 13;
        Foreground = Brush("#F1F5F9");

        gameDateBadge.Child = gameDateText;
        InitializeFilterControls();

        var root = new DockPanel();

        var top = new DockPanel { Margin = new Thickness(14, 8), LastChildFill = false };
        var brand = new TextBlock
        {
            Text = "FM27 EDITOR",
            FontSize = 13.5,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F8FAFC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 16, 0),
        };
        DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
        DockPanel.SetDock(modules, Dock.Left); top.Children.Add(modules);
        DockPanel.SetDock(gameDateBadge, Dock.Left); top.Children.Add(gameDateBadge);
        DockPanel.SetDock(retry, Dock.Right); top.Children.Add(retry);
        var connStatus = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Children = { connectionDot, connection }
        };
        DockPanel.SetDock(connStatus, Dock.Right); top.Children.Add(connStatus);

        var topBar = new Border
        {
            Background = Brush("#131620"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = top
        };
        DockPanel.SetDock(topBar, Dock.Top); root.Children.Add(topBar);

        var statusBar = new Border
        {
            Background = Brush("#131620"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 27,
            Child = status
        };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(new GridLength(650)) { MinWidth = 500 },
                new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 460 },
            }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var filterArea = BuildFilterPanel();
        DockPanel.SetDock(filterArea, Dock.Top);
        left.Children.Add(filterArea);
        left.Children.Add(grid);
        Grid.SetColumn(left, 0);
        split.Children.Add(left);

        var splitter = new GridSplitter
        {
            Width = 6,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        split.Children.Add(splitter);

        var scroll = new ScrollViewer
        {
            Content = editor,
            Background = Brush("#0D0F17"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetColumn(scroll, 2);
        split.Children.Add(scroll);
        root.Children.Add(split);
        Content = root;

        modules.ItemsSource = Modules.Registered;
        modules.DisplayMemberBinding = new Binding("Title");
        modules.SelectionChanged += async (_, _) =>
        {
            module = modules.SelectedItem as IEditorModule;
            ConfigureColumns();
            if (backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
        };
        grid.SelectionChanged += (_, _) =>
        {
            if (!updating)
            {
                selected = grid.SelectedItem as PlayerRecord;
                RenderEditor();
            }
        };
        retry.Click += async (_, _) => await ConnectAsync();
        Opened += async (_, _) => { modules.SelectedIndex = 0; await ConnectAsync(); timer.Start(); };
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
        RegisterClubsView(root, split);
        RegisterStaffView(split);
        RegisterNationView(split);
        RegisterStadiumView(split);
        RegisterCityView(split);
        RegisterCompetitionView(split);
        RegisterAwardView(split);
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
        var stack = new StackPanel { Spacing = 6 };

        // Row 1: Name search + Sort selector + Direction + Reset
        var row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(search, 0); row1.Children.Add(search);
        Grid.SetColumn(sortCombo, 1); row1.Children.Add(sortCombo);
        Grid.SetColumn(sortDirButton, 2); row1.Children.Add(sortDirButton);
        Grid.SetColumn(resetFiltersButton, 3); row1.Children.Add(resetFiltersButton);
        stack.Children.Add(row1);

        // Row 2: League, Club, Nationality dropdowns
        var row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        Grid.SetColumn(divisionFilter, 0); row2.Children.Add(divisionFilter);
        Grid.SetColumn(clubFilter, 1); row2.Children.Add(clubFilter);
        Grid.SetColumn(nationFilter, 2); row2.Children.Add(nationFilter);
        stack.Children.Add(row2);

        // Row 3: Filters & Attributes Dialog button + Preset selector + Clear button (Grid with *,*,Auto -> never overflows)
        var row3 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto,Auto,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(openAttrDialogButton, 0); row3.Children.Add(openAttrDialogButton);
        Grid.SetColumn(quickPresetCombo, 1); row3.Children.Add(quickPresetCombo);
        Grid.SetColumn(clearAttrButton, 2); row3.Children.Add(clearAttrButton);
        Grid.SetColumn(exportCsvButton, 3); row3.Children.Add(exportCsvButton);
        Grid.SetColumn(exportJsonButton, 4); row3.Children.Add(exportJsonButton);
        Grid.SetColumn(massEditButton, 5); row3.Children.Add(massEditButton);
        exportCsvButton.Click += async (_, _) => await ExportPlayersAsync(false);
        exportJsonButton.Click += async (_, _) => await ExportPlayersAsync(true);
        massEditButton.Click += async (_, _) => await ShowMassEditAsync();
        stack.Children.Add(row3);
        stack.Children.Add(attrChipsPanel);

        return new Border
        {
            Background = Brush("#141722"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8),
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

        foreach (var pos in activePositions.ToArray())
        {
            var shortPos = PositionLabels.Short(pos);
            AddChip($"Pos: {shortPos} >= {activePosMinRating}", () => activePositions.Remove(pos));
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
        openAttrDialogButton.Content = hasAny ? $"Filters & Attributes ({count})" : "Filters & Attributes...";
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
            else gameDateBadge.IsVisible = false;
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
                "Age" => new DataGridLength(68),
                "CA" => new DataGridLength(64),
                "PA" => new DataGridLength(64),
                "Club" => new DataGridLength(132),
                "Nationality" => new DataGridLength(116),
                _ => new DataGridLength(68),
            };
            var col = new DataGridTextColumn
            {
                Header = c.Title,
                Binding = new Binding(c.Binding),
                Width = width,
                MinWidth = c.Title == "Name" ? 135 : 58,
            };
            if (c.Title is "Age" or "CA" or "PA") col.CellStyleClasses.Add("numeric");
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
        nextSelection ??= result.OfType<PlayerRecord>().FirstOrDefault();
        grid.SelectedItem = nextSelection;
        updating = false;

        if (backend.State.Status == ConnectionStatus.Connected)
        {
            connection.Text = result.Count == rows.Count
                ? $"{backend.State.Message} · {rows.Count:N0}"
                : $"{backend.State.Message} · {result.Count:N0} / {rows.Count:N0}";
        }

        if (!ReferenceEquals(selected, nextSelection))
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
        Background = Brush(highlight ? "#261B47" : "#1D2233"),
        BorderBrush = Brush(highlight ? "#6D28D9" : "#2C334A"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 2),
        Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = Brush(highlight ? "#E9D5FF" : "#CBD5E1")
        }
    };

    void RenderEditor()
    {
        editor.Children.Clear();
        if (selected is not { } player) return;
        if (!drafts.TryGetValue(player.Address, out var draft)) drafts[player.Address] = draft = new PlayerDraft(player);

        var headerDock = new DockPanel { LastChildFill = true };

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        saveButton = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        saveButton.Click += (_, _) => SaveDraft();
        discardButton = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        discardButton.Click += (_, _) => { drafts.Remove(player.Address); status.Text = "Changes discarded"; RenderEditor(); };
        commands.Children.Add(saveButton);
        commands.Children.Add(discardButton);
        if (backend.Freezer is not null)
        {
            var freeze = new Button { Content = "Freezer" };
            freeze.Click += async (_, _) => await ShowPlayerFreezerAsync(player);
            commands.Children.Add(freeze);
        }
        DockPanel.SetDock(commands, Dock.Right);
        headerDock.Children.Add(commands);

        var identity = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new TextBlock
        {
            Text = player.DisplayName,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F8FAFC")
        });

        var badges = new WrapPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(player.ClubName))
            badges.Children.Add(MetaBadge(player.ClubName, highlight: true));
        if (!string.IsNullOrEmpty(player.DivisionName))
            badges.Children.Add(MetaBadge(player.DivisionName));
        if (!string.IsNullOrEmpty(player.NationalityName))
            badges.Children.Add(MetaBadge(player.NationalityName));
        if (player.Age is { } age)
            badges.Children.Add(MetaBadge($"{age} yrs"));

        identity.Children.Add(badges);
        headerDock.Children.Add(identity);

        editor.Children.Add(new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(16, 12),
            Child = headerDock
        });

        status.Text = draft.Count > 0 ? $"{draft.Count} unsaved change(s)" : "";

        var strip = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var field in PlayerFields.All.Where(f => f.Group is "Ability" or "Reputation" or "Body"))
        {
            bool isAbility = field.Group == "Ability";
            var item = new Border
            {
                Width = isAbility ? 128 : field.Kind == FieldKind.Date ? 172 : 144,
                Padding = new Thickness(12, 6),
                Margin = new Thickness(0, 0, 8, 4),
                Background = Brush(isAbility ? "#191B2E" : "#151824"),
                BorderBrush = Brush(isAbility ? "#4C1D95" : "#252A3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = FieldControl(field, player)
            };
            strip.Children.Add(item);
        }
        editor.Children.Add(strip);

        var tabs = new TabControl { Padding = new Thickness(0, 8, 0, 0) };
        var attributes = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in new[] { "Technical", "Mental", "Physical", "Goalkeeping" })
            attributes.Children.Add(GroupControl(group, player));

        var personality = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in new[] { "Hidden", "Personality" })
            personality.Children.Add(GroupControl(group, player));

        var pitch = CreatePitch(player, draft);
        var tabList = new List<TabItem>
        {
            new TabItem { Header = "Information", Content = InformationPanel(player) },
            new TabItem { Header = "Attributes", Content = attributes },
            new TabItem { Header = "Comparison", Content = ComparisonPanel(player) },
            new TabItem { Header = "Positions", Content = pitch },
            new TabItem { Header = "Personality", Content = personality },
            new TabItem { Header = "Contract", Content = ContractTabs(player) },
            new TabItem { Header = "Career Plans", Content = CareerPlansPanel(player) },
            new TabItem { Header = "Clone Center", Content = ClonePanel(player) },
            new TabItem { Header = "Move to club", Content = MovePanel(player) },
            new TabItem { Header = "Swap National Call Up", Content = CallUpPanel(player) },
            new TabItem { Header = "Injury", Content = InjuryPanel(player) },
            new TabItem { Header = "Unavailability", Content = UnavailabilityPanel(player) },
            new TabItem { Header = "Transfer Proposals", Content = ProposalsPanel(player) },
            new TabItem { Header = "Preferred Moves", Content = PreferredMovesPanel(player) },
            new TabItem { Header = "Relationships", Content = RelationshipsPanel(player) },
            new TabItem { Header = "Languages", Content = LanguagesPanel(player) }
        };
        // Like FMRTE, Future Transfer appears only for players with an agreed future move.
        if (FutureTransferPanel(player) is { } future)
            tabList.Insert(tabList.FindIndex(t => (string?)t.Header == "Swap National Call Up") + 1, new TabItem { Header = "Future Transfer", Content = future });
        tabs.ItemsSource = tabList;
        tabs.SelectedIndex = Math.Min(editorTab, tabList.Count - 1);
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex >= 0) editorTab = tabs.SelectedIndex; };
        editor.Children.Add(tabs);
        HookPlayerActions(player);
    }

    Control GroupControl(string group, PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new Border
        {
            Background = Brush("#1C2032"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(10, 7),
            Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Text = group.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#F1F5F9")
            }
        });
        int rowIndex = 0;
        foreach (var field in PlayerFields.All.Where(f => f.Group == group))
        {
            var rowControl = FieldControl(field, player);
            panel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? Brush("#121520") : Brushes.Transparent,
                Padding = new Thickness(10, 1, 8, 1),
                Child = rowControl
            });
        }
        return new Border
        {
            Width = 228,
            Margin = new Thickness(0, 0, 10, 10),
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = panel
        };
    }

    Control CreatePitch(PlayerRecord player, PlayerDraft draft)
    {
        const double width = 460, height = 300, dotSize = 26;
        var result = new StackPanel { Margin = new Thickness(0, 6), Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var pitch = new Canvas { Width = width, Height = height, Background = Brush("#0F382F"), ClipToBounds = true };
        var lines = Brush("#4F7E72");
        void Rectangle(double x, double y, double w, double h)
        {
            var shape = new Rectangle { Width = w, Height = h, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); pitch.Children.Add(shape);
        }
        Rectangle(1, 1, width - 2, height - 2);
        Rectangle(0, 78, 48, 144); Rectangle(0, 112, 16, 76);
        Rectangle(width - 48, 78, 48, 144); Rectangle(width - 16, 112, 16, 76);
        var halfway = new Line { StartPoint = new Point(width / 2, 0), EndPoint = new Point(width / 2, height), Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        pitch.Children.Add(halfway);
        var circle = new Ellipse { Width = 60, Height = 60, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(circle, width / 2 - 30); Canvas.SetTop(circle, height / 2 - 30); pitch.Children.Add(circle);

        var dots = new Dictionary<PlayerPosition, Button>();
        var selection = new ContentControl { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        void Select(PitchPosition position)
        {
            selectedPosition = position.Position;
            foreach (var (key, dot) in dots)
            {
                bool active = key == position.Position;
                dot.BorderBrush = active ? Brushes.White : Brush("#1A4339");
                dot.BorderThickness = new Thickness(active ? 2 : 1);
            }
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            var panel = new DockPanel { LastChildFill = true };
            var ratingControl = FieldControl(field, player, onChanged: value =>
            {
                dots[position.Position].Background = PositionColor(value);
                dots[position.Position].Content = value.ToString();
            }, labelOverride: "Rating");
            ratingControl.Width = 130;
            DockPanel.SetDock(ratingControl, Dock.Right);
            panel.Children.Add(ratingControl);
            panel.Children.Add(new TextBlock
            {
                Text = position.Label,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            selection.Content = new Border
            {
                Background = Brush("#151926"),
                BorderBrush = Brush("#252A3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8),
                Child = panel
            };
        }
        foreach (var position in PitchLayout.Positions)
        {
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            int val = draft.Value(field);
            var dot = new Button
            {
                Width = dotSize,
                Height = dotSize,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(dotSize / 2),
                Background = PositionColor(val),
                Foreground = Brush("#0B1311"),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = val.ToString()
            };
            ToolTip.SetTip(dot, position.Label);
            Avalonia.Automation.AutomationProperties.SetName(dot, position.Label);
            Canvas.SetLeft(dot, position.X * width - dotSize / 2); Canvas.SetTop(dot, position.Y * height - dotSize / 2);
            dot.Click += (_, _) => Select(position); dots[position.Position] = dot; pitch.Children.Add(dot);
        }
        result.Children.Add(new Border
        {
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = pitch
        });
        result.Children.Add(selection);
        var initial = PitchLayout.Positions.FirstOrDefault(p => p.Position == selectedPosition) ?? PitchLayout.Positions.MaxBy(p => draft.Value(PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == p.Position.ToString())))!;
        Select(initial);
        return result;
    }

    Control FieldControl(FieldDescriptor<PlayerRecord> field, PlayerRecord player, bool compact = false, Action<int>? onChanged = null, string? labelOverride = null)
    {
        var line = new DockPanel { Height = 28, LastChildFill = true };
        var label = new TextBlock
        {
            Text = labelOverride ?? (compact ? field.Label.Replace("Defensive Midfielder", "DM").Replace("Attacking Mid", "AM").Replace("Midfielder", "M").Replace("Defender", "D").Replace("Wing Back", "WB").Replace("Goalkeeper", "GK").Replace("Striker", "ST") : field.Label),
            FontSize = 12.5,
            Foreground = Brush("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (field.Write is null)
        {
            var value = new TextBlock
            {
                Text = field.Read(player).ToString("N0"),
                Margin = new Thickness(6, 0, 2, 0),
                FontSize = 12.5,
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
                if (saveButton is not null) saveButton.IsEnabled = draft.Count > 0;
                if (discardButton is not null) discardButton.IsEnabled = draft.Count > 0;
                status.Text = draft.Count > 0 ? $"{draft.Count} unsaved change(s)" : "";
            }
        }
        if (field.Kind == FieldKind.Date)
        {
            var box = new TextBox
            {
                Text = currentVal == 0 ? "" : DateField.FromInt(currentVal).ToString("yyyy-MM-dd", Inv),
                Width = 96,
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Background = Brush("#0E1018"),
                BorderBrush = Brush("#282E42"),
                Foreground = Brush("#F8FAFC")
            };
            box.TextChanged += (_, _) =>
            {
                bool ok = DateField.TryParse(box.Text, out int value);
                box.BorderBrush = Brush(ok ? "#282E42" : "#B91C1C");
                if (!updating && ok && value != draft.Value(field)) Staged(value);
            };
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box); line.Children.Add(label);
            return line;
        }
        var input = new NumericUpDown
        {
            Minimum = field.Min,
            Maximum = field.Max,
            Increment = 1,
            Value = currentVal,
            ShowButtonSpinner = false,
            Width = field.Kind == FieldKind.Rating20 ? 46 : 58,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"),
            BorderBrush = Brush("#282E42"),
            Foreground = field.Kind == FieldKind.Rating20 ? RatingColor(currentVal) : Brush("#F8FAFC")
        };
        DockPanel.SetDock(input, Dock.Right); line.Children.Add(input); line.Children.Add(label);
        input.ValueChanged += (_, e) =>
        {
            if (updating || e.NewValue is null || e.OldValue == e.NewValue) return;
            int value = (int)e.NewValue.Value;
            Staged(value);
            if (field.Kind == FieldKind.Rating20) input.Foreground = RatingColor(value);
        };
        return line;
    }

    void SaveDraft()
    {
        if (selected is null || !drafts.TryGetValue(selected.Address, out var draft) || draft.Count == 0) return;
        var address = selected.Address;
        var count = draft.Count;
        try
        {
            selected = draft.Save(backend.Edits);
            selected.GameDate = backend.GameDate;
            drafts.Remove(address);
            ReplaceRow(selected); RenderEditor();
            status.Text = $"Saved {count} change(s). Continue in FM to see updates.";
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
    static IBrush PositionColor(int value) => Brush(value <= 5 ? "#334155" : value <= 10 ? "#F59E0B" : value <= 15 ? "#EAB308" : "#84CC16");
    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
