using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed class AttributeFilterDialog : Window
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string SelectPresetPlaceholder = "Select preset...";

    sealed class RowControls
    {
        public required PlayerAttribute Attribute { get; init; }
        public required string Label { get; init; }
        public required Border RowBorder { get; init; }
        public required CheckBox Toggle { get; init; }
        public required TextBlock NameText { get; init; }
        public required TextBlock RangeText { get; init; }
        public required NumericUpDown ValueInput { get; init; }
        public required NumericUpDown MaxInput { get; init; }
    }

    readonly Dictionary<PlayerAttribute, (int Min, int Max)> staged = [];
    readonly HashSet<PlayerPosition> stagedPositions = [];
    readonly Dictionary<PlayerPosition, int> positionRatings = [];
    readonly Dictionary<PlayerPosition, (int Min, int Max)> positionRanges = [];
    readonly Dictionary<PlayerPosition, Action> positionUpdaters = [];
    readonly Dictionary<PlayerPosition, Border> positionRowBorders = [];
    PlayerPosition selectedPosition = PlayerPosition.Striker;
    readonly Dictionary<PlayerPosition, Button> positionDots = [];
    readonly Dictionary<string, (TextBox Min, TextBox Max)> rangeInputs = [];
    readonly Dictionary<string, ComboBox> choiceInputs = [];
    sealed record FilterChoice(long? Value, string Name) { public override string ToString() => Name; }
    readonly Dictionary<string, NumericRange> initialRanges = [];
    readonly List<CheckBox> preferredMoveChecks = [];
    ulong initialPreferredMoves;
    readonly Dictionary<PlayerAttribute, RowControls> rows = [];
    readonly Dictionary<PlayerPosition, Button> positionButtons = [];
    IReadOnlyList<FilterPreset> presets;

    const string AllLeagues = "All Leagues";
    const string AllClubs = "All Clubs";
    const string AllNationalities = "All Nationalities";

    readonly IReadOnlyList<PlayerRecord> allPlayers;
    readonly ComboBox divisionCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 28, FontSize = 13 };
    readonly ComboBox clubCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 28, FontSize = 13 };
    readonly ComboBox nationCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 28, FontSize = 13 };

    readonly ComboBox presetCombo = new() { Width = 220 };
    readonly TextBox presetNameBox = new() { Watermark = "Preset name...", Width = 190 };
    readonly Button savePresetButton = new() { Content = "Save preset" };
    readonly Button deletePresetButton = new() { Content = "Delete", IsEnabled = false };

    readonly TextBox minAgeBox = new() { Watermark = "Min", Width = 54, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox maxAgeBox = new() { Watermark = "Max", Width = 54, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox minCaBox = new() { Watermark = "Min", Width = 58, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox maxCaBox = new() { Watermark = "Max", Width = 58, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox minPaBox = new() { Watermark = "Min", Width = 58, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox maxPaBox = new() { Watermark = "Max", Width = 58, HorizontalContentAlignment = HorizontalAlignment.Right };

    readonly NumericUpDown posMinRatingInput = new()
    {
        Minimum = 1,
        Maximum = 20,
        Value = 1,
        Increment = 1,
        ShowButtonSpinner = false,
        Width = 48,
        HorizontalContentAlignment = HorizontalAlignment.Right
    };

    readonly NumericUpDown defaultMinInput = new()
    {
        Minimum = 1,
        Maximum = 20,
        Value = 1,
        Increment = 1,
        ShowButtonSpinner = false,
        Width = 48,
        HorizontalContentAlignment = HorizontalAlignment.Right
    };
    readonly TextBlock summaryText = new()
    {
        FontSize = 13,
        Foreground = Brush("#CBD5E1"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0)
    };
    bool updatingControls;
    bool suppressPresetCombo;

    public bool Saved { get; private set; }
    public IReadOnlyList<AttributeThreshold> ResultThresholds =>
        staged.OrderBy(kv => kv.Key).Select(kv => new AttributeThreshold(kv.Key, kv.Value.Min, kv.Value.Max)).ToArray();
    public IReadOnlyList<PlayerPosition> ResultPositions =>
        stagedPositions.OrderBy(p => p).ToArray();
    public IReadOnlyDictionary<PlayerPosition, int> ResultPositionThresholds => new Dictionary<PlayerPosition, int>(positionRatings);
    public IReadOnlyDictionary<PlayerPosition, NumericRange> ResultPositionRanges =>
        positionRanges.ToDictionary(kv => kv.Key, kv => new NumericRange(kv.Value.Min, kv.Value.Max));
    public IReadOnlyDictionary<string, NumericRange> ResultNumericRanges
    {
        get
        {
            var values = rangeInputs.Select(entry => (entry.Key, Min: ParseOptLong(entry.Value.Min.Text), Max: ParseOptLong(entry.Value.Max.Text)))
                .Where(entry => entry.Min is not null || entry.Max is not null)
                .ToDictionary(entry => entry.Key, entry => new NumericRange(entry.Min, entry.Max));
            foreach (var (key, combo) in choiceInputs)
                if (combo.SelectedItem is FilterChoice { Value: { } value }) values[key] = new NumericRange(value, value);
            return values;
        }
    }
    public ulong ResultPreferredMoves => preferredMoveChecks.Select((box, bit) => box.IsChecked == true ? 1UL << bit : 0UL).Aggregate(0UL, (a, b) => a | b);
    public int ResultPositionMinRating => Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
    public int? ResultMinAge => ParseOptInt(minAgeBox.Text);
    public int? ResultMaxAge => ParseOptInt(maxAgeBox.Text);
    public int? ResultMinCa => ParseOptInt(minCaBox.Text);
    public int? ResultMaxCa => ParseOptInt(maxCaBox.Text);
    public int? ResultMinPa => ParseOptInt(minPaBox.Text);
    public int? ResultMaxPa => ParseOptInt(maxPaBox.Text);
    public string? ResultDivision => divisionCombo.SelectedItem is string s && s != AllLeagues ? s : null;
    public string? ResultClub => clubCombo.SelectedItem is string s && s != AllClubs ? s : null;
    public string? ResultNationality => nationCombo.SelectedItem is string s && s != AllNationalities ? s : null;
    public IReadOnlyList<FilterPreset> UpdatedPresets => presets;
    public string? SelectedPresetName { get; private set; }

    public AttributeFilterDialog(
        PlayerFilter initialFilter,
        IReadOnlyList<FilterPreset> currentPresets,
        string? activePresetName = null,
        IReadOnlyList<PlayerRecord>? players = null)
    {
        Title = "Player Filter & Presets";
        Width = 940;
        Height = 740;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0D0F17");
        Foreground = Brush("#F1F5F9");
        FontSize = 13;

        allPlayers = players ?? Array.Empty<PlayerRecord>();
        presets = currentPresets;

        var leagues = allPlayers
            .Select(p => p.DivisionName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllLeagues)
            .ToArray();

        var nations = allPlayers
            .Select(p => p.NationalityName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllNationalities)
            .ToArray();

        divisionCombo.ItemsSource = leagues;
        int divIdx = !string.IsNullOrEmpty(initialFilter.Division)
            ? Array.FindIndex(leagues, l => string.Equals(l, initialFilter.Division, StringComparison.InvariantCultureIgnoreCase))
            : 0;
        divisionCombo.SelectedIndex = divIdx >= 0 ? divIdx : 0;

        nationCombo.ItemsSource = nations;
        int natIdx = !string.IsNullOrEmpty(initialFilter.Nationality)
            ? Array.FindIndex(nations, n => string.Equals(n, initialFilter.Nationality, StringComparison.InvariantCultureIgnoreCase))
            : 0;
        nationCombo.SelectedIndex = natIdx >= 0 ? natIdx : 0;

        RefreshDialogClubs(initialFilter.Club);

        divisionCombo.SelectionChanged += (_, _) =>
        {
            if (updatingControls) return;
            RefreshDialogClubs(null);
            UpdateSummary();
        };
        clubCombo.SelectionChanged += (_, _) => { if (!updatingControls) UpdateSummary(); };
        nationCombo.SelectionChanged += (_, _) => { if (!updatingControls) UpdateSummary(); };
        foreach (var (key, range) in initialFilter.NumericRanges) initialRanges[key] = range;
        initialPreferredMoves = initialFilter.RequiredPreferredMoves;
        SelectedPresetName = activePresetName;
        foreach (var t in initialFilter.AttributeThresholds)
            staged[t.Attribute] = (Math.Clamp(t.MinValue, 1, 20), Math.Clamp(t.MaxValue ?? 20, 1, 20));
        foreach (var pos in initialFilter.Positions)
        {
            stagedPositions.Add(pos);
            if (initialFilter.PositionRanges.TryGetValue(pos, out var rng))
            {
                int rMin = (int)(rng.Min ?? 1);
                int rMax = (int)(rng.Max ?? 20);
                positionRanges[pos] = (rMin, rMax);
                positionRatings[pos] = rMin;
            }
            else
            {
                int val = initialFilter.PositionThresholds.GetValueOrDefault(pos, initialFilter.PositionMinRating);
                positionRanges[pos] = (val, 20);
                positionRatings[pos] = val;
            }
        }

        minAgeBox.Text = initialFilter.MinAge?.ToString() ?? "";
        maxAgeBox.Text = initialFilter.MaxAge?.ToString() ?? "";
        minCaBox.Text = initialFilter.MinCa?.ToString() ?? "";
        maxCaBox.Text = initialFilter.MaxCa?.ToString() ?? "";
        minPaBox.Text = initialFilter.MinPa?.ToString() ?? "";
        maxPaBox.Text = initialFilter.MaxPa?.ToString() ?? "";
        posMinRatingInput.Value = Math.Clamp(initialFilter.PositionMinRating, 1, 20);

        var root = new DockPanel();

        // Top preset bar
        var topBar = BuildPresetBar();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        // Bottom action bar (Save / Discard)
        var bottomBar = BuildBottomBar();
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        root.Children.Add(bottomBar);

        // Each page stays within the dialog width; long content scrolls vertically.
        var bodyStack = new StackPanel { Spacing = 10 };
        bodyStack.Children.Add(BuildGeneralAndPositionsCard());
        bodyStack.Children.Add(BuildAdditionalRanges());

        var boardScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 10),
            Content = bodyStack
        };
        var positionScroll = new ScrollViewer { Padding = new Thickness(14, 10), Content = BuildPositionBoard() };
        root.Children.Add(new TabControl { ItemsSource = new[] { new TabItem { Header = "General", Content = boardScroll }, new TabItem { Header = "Attributes", Content = BuildAttributeBoard() }, new TabItem { Header = "Positions", Content = positionScroll }, new TabItem { Header = "Preferred Moves", Content = BuildPreferredMoves() } } });

        Content = root;

        foreach (var tb in new[] { minAgeBox, maxAgeBox, minCaBox, maxCaBox, minPaBox, maxPaBox })
            tb.TextChanged += (_, _) => { if (!updatingControls) UpdateSummary(); };

        RefreshPresetCombo(activePresetName);
        SyncAllRowsFromStaged();
        SyncPositionButtons();
    }

    Control BuildPresetBar()
    {
        var dock = new DockPanel { LastChildFill = false };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        left.Children.Add(new TextBlock
        {
            Text = "Preset",
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#94A3B8"),
            VerticalAlignment = VerticalAlignment.Center
        });
        left.Children.Add(presetCombo);
        left.Children.Add(presetNameBox);
        left.Children.Add(savePresetButton);
        left.Children.Add(deletePresetButton);
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        right.Children.Add(new TextBlock
        {
            Text = "Default Attr >=",
            FontSize = 13.5,
            Foreground = Brush("#94A3B8"),
            VerticalAlignment = VerticalAlignment.Center
        });
        right.Children.Add(defaultMinInput);
        DockPanel.SetDock(right, Dock.Right);
        // Attribute ranges start unrestricted; there is no global minimum.

        presetCombo.SelectionChanged += (_, _) =>
        {
            if (suppressPresetCombo) return;
            if (presetCombo.SelectedItem is FilterPreset chosen)
            {
                SelectedPresetName = chosen.Name;
                presetNameBox.Text = chosen.Name;
                deletePresetButton.IsEnabled = true;

                updatingControls = true;
                if (chosen.Division is { } div && divisionCombo.ItemsSource is IEnumerable<string> divs)
                {
                    int dIdx = divs.ToList().FindIndex(l => string.Equals(l, div, StringComparison.InvariantCultureIgnoreCase));
                    divisionCombo.SelectedIndex = dIdx >= 0 ? dIdx : 0;
                }
                else divisionCombo.SelectedIndex = 0;

                RefreshDialogClubs(chosen.Club);

                if (chosen.Nationality is { } nat && nationCombo.ItemsSource is IEnumerable<string> nats)
                {
                    int nIdx = nats.ToList().FindIndex(n => string.Equals(n, nat, StringComparison.InvariantCultureIgnoreCase));
                    nationCombo.SelectedIndex = nIdx >= 0 ? nIdx : 0;
                }
                else nationCombo.SelectedIndex = 0;

                minAgeBox.Text = chosen.MinAge?.ToString() ?? "";
                maxAgeBox.Text = chosen.MaxAge?.ToString() ?? "";
                minCaBox.Text = chosen.MinCa?.ToString() ?? "";
                maxCaBox.Text = chosen.MaxCa?.ToString() ?? "";
                minPaBox.Text = chosen.MinPa?.ToString() ?? "";
                maxPaBox.Text = chosen.MaxPa?.ToString() ?? "";
                posMinRatingInput.Value = Math.Clamp(chosen.PositionMinRating, 1, 20);
                updatingControls = false;

                stagedPositions.Clear();
                positionRatings.Clear();
                positionRanges.Clear();
                if (chosen.Positions is not null)
                {
                    foreach (var pos in chosen.Positions)
                    {
                        stagedPositions.Add(pos);
                        if (chosen.PositionRanges is { } prs && prs.TryGetValue(pos, out var rng))
                        {
                            int rMin = (int)(rng.Min ?? 1);
                            int rMax = (int)(rng.Max ?? 20);
                            positionRanges[pos] = (rMin, rMax);
                            positionRatings[pos] = rMin;
                        }
                        else
                        {
                            int val = chosen.PositionThresholds?.GetValueOrDefault(pos) ?? chosen.PositionMinRating;
                            positionRanges[pos] = (val, 20);
                            positionRatings[pos] = val;
                        }
                    }
                }
                SyncPositionButtons();
                foreach (var (key, controls) in rangeInputs)
                {
                    var selectedRange = chosen.NumericRanges is { } source && source.TryGetValue(key, out var value) ? value : null;
                    controls.Min.Text = selectedRange?.Min?.ToString() ?? "";
                    controls.Max.Text = selectedRange?.Max?.ToString() ?? "";
                }
                foreach (var (key, combo) in choiceInputs)
                {
                    long? selected = chosen.NumericRanges is { } source && source.TryGetValue(key, out var value) ? value.Min : null;
                    combo.SelectedItem = ((IEnumerable<FilterChoice>)combo.ItemsSource!).FirstOrDefault(option => option.Value == selected);
                }
                for (int bit = 0; bit < preferredMoveChecks.Count; bit++) preferredMoveChecks[bit].IsChecked = (chosen.RequiredPreferredMoves & (1UL << bit)) != 0;

                staged.Clear();
                foreach (var t in chosen.AttributeThresholds)
                    staged[t.Attribute] = (Math.Clamp(t.MinValue, 1, 20), Math.Clamp(t.MaxValue ?? 20, 1, 20));
                SyncAllRowsFromStaged();
            }
            else
            {
                SelectedPresetName = null;
                deletePresetButton.IsEnabled = false;
            }
        };

        savePresetButton.Click += (_, _) =>
        {
            var name = presetNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                summaryText.Text = "Enter a preset name before saving.";
                summaryText.Foreground = Brush("#F59E0B");
                return;
            }
            var preset = new FilterPreset(
                name,
                ResultThresholds,
                ResultMinAge,
                ResultMaxAge,
                ResultMinCa,
                ResultMinPa,
                ResultMaxCa,
                ResultMaxPa,
                ResultPositions,
                ResultPositionMinRating)
            {
                PositionThresholds = ResultPositionThresholds,
                PositionRanges = ResultPositionRanges,
                NumericRanges = ResultNumericRanges,
                RequiredPreferredMoves = ResultPreferredMoves,
                Division = ResultDivision,
                Club = ResultClub,
                Nationality = ResultNationality
            };
            presets = FilterPresetStore.Upsert(presets, preset);
            try { FilterPresetStore.Save(presets); } catch { }
            SelectedPresetName = preset.Name;
            RefreshPresetCombo(preset.Name);
            summaryText.Foreground = Brush("#4ADE80");
            summaryText.Text = $"Saved preset '{preset.Name}'.";
        };

        deletePresetButton.Click += (_, _) =>
        {
            var name = (presetCombo.SelectedItem as FilterPreset)?.Name ?? presetNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            presets = FilterPresetStore.Remove(presets, name);
            try { FilterPresetStore.Save(presets); } catch { }
            SelectedPresetName = null;
            presetNameBox.Text = "";
            RefreshPresetCombo(null);
            summaryText.Foreground = Brush("#94A3B8");
            summaryText.Text = $"Deleted preset '{name}'.";
        };

        return new Border
        {
            Background = Brush("#141722"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
            Child = dock
        };
    }

    void RefreshDialogClubs(string? preferredClub)
    {
        var subset = allPlayers.AsEnumerable();
        if (divisionCombo.SelectedItem is string selectedLeague && selectedLeague != AllLeagues)
            subset = subset.Where(p => string.Equals(p.DivisionName, selectedLeague, StringComparison.InvariantCultureIgnoreCase));

        var clubs = subset
            .Select(p => p.ClubName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
            .Prepend(AllClubs)
            .ToArray();

        clubCombo.ItemsSource = clubs;
        string? target = preferredClub ?? clubCombo.SelectedItem as string;
        int idx = !string.IsNullOrEmpty(target)
            ? Array.FindIndex(clubs, c => string.Equals(c, target, StringComparison.InvariantCultureIgnoreCase))
            : 0;
        clubCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    Control BuildGeneralAndPositionsCard()
    {
        var stack = new StackPanel { Spacing = 10 };

        // Row 1: League, Club, Nationality
        var geoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var divStack = new StackPanel { Spacing = 3 };
        divStack.Children.Add(SectionLabel("LEAGUE"));
        divStack.Children.Add(divisionCombo);
        Grid.SetColumn(divStack, 0); geoGrid.Children.Add(divStack);

        var clubStack = new StackPanel { Spacing = 3 };
        clubStack.Children.Add(SectionLabel("CLUB"));
        clubStack.Children.Add(clubCombo);
        Grid.SetColumn(clubStack, 1); geoGrid.Children.Add(clubStack);

        var natStack = new StackPanel { Spacing = 3 };
        natStack.Children.Add(SectionLabel("NATIONALITY"));
        natStack.Children.Add(nationCombo);
        Grid.SetColumn(natStack, 2); geoGrid.Children.Add(natStack);

        stack.Children.Add(geoGrid);

        // Row 2: Age, CA, PA ranges
        var rangesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        rangesRow.Children.Add(SectionLabel("AGE"));
        rangesRow.Children.Add(minAgeBox);
        rangesRow.Children.Add(MutedLabel("to"));
        rangesRow.Children.Add(maxAgeBox);
        rangesRow.Children.Add(new Border { Width = 18 });

        rangesRow.Children.Add(SectionLabel("CA"));
        rangesRow.Children.Add(minCaBox);
        rangesRow.Children.Add(MutedLabel("to"));
        rangesRow.Children.Add(maxCaBox);
        rangesRow.Children.Add(new Border { Width = 18 });

        rangesRow.Children.Add(SectionLabel("PA"));
        rangesRow.Children.Add(minPaBox);
        rangesRow.Children.Add(MutedLabel("to"));
        rangesRow.Children.Add(maxPaBox);
        stack.Children.Add(rangesRow);

        // Row 2: Positions
        var posRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        posRow.Children.Add(new Border
        {
            Width = 76,
            VerticalAlignment = VerticalAlignment.Center,
            Child = SectionLabel("POSITION")
        });

        var orderedPositions = new[]
        {
            PlayerPosition.Goalkeeper,
            PlayerPosition.DefenderLeft,
            PlayerPosition.DefenderCentral,
            PlayerPosition.DefenderRight,
            PlayerPosition.WingBackLeft,
            PlayerPosition.WingBackRight,
            PlayerPosition.DefensiveMidfielder,
            PlayerPosition.MidfielderLeft,
            PlayerPosition.MidfielderCentral,
            PlayerPosition.MidfielderRight,
            PlayerPosition.AttackingMidLeft,
            PlayerPosition.AttackingMidCentral,
            PlayerPosition.AttackingMidRight,
            PlayerPosition.Striker,
        };

        foreach (var pos in orderedPositions)
        {
            var shortName = PositionLabels.Short(pos);
            var fullName = PlayerFields.All.First(f => f.Group == "Positions" && f.Id == pos.ToString()).Label;
            var btn = new Button
            {
                Content = shortName,
                MinWidth = 44,
                Padding = new Thickness(8, 0),
                Margin = new Thickness(0, 2, 5, 2),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
            };
            ToolTip.SetTip(btn, fullName);
            btn.Click += (_, _) =>
            {
                if (!stagedPositions.Remove(pos))
                {
                    stagedPositions.Add(pos);
                    int defMin = Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
                    positionRanges[pos] = (defMin, 20);
                    positionRatings[pos] = defMin;
                }
                else
                {
                    positionRanges.Remove(pos);
                    positionRatings.Remove(pos);
                }
                SyncPositionButtons();
                UpdateSummary();
            };
            positionButtons[pos] = btn;
            posRow.Children.Add(btn);
        }

        var ratingStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(10, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        ratingStack.Children.Add(MutedLabel("Rating >="));
        ratingStack.Children.Add(posMinRatingInput);
        posRow.Children.Add(ratingStack);

        // Position filters are edited individually in the Positions tab.

        return new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10),
            Child = stack
        };
    }

    static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontWeight = FontWeight.Bold,
        Foreground = Brush("#E2E8F0"),
        VerticalAlignment = VerticalAlignment.Center
    };

    static TextBlock MutedLabel(string text) => new()
    {
        Text = text,
        FontSize = 13.5,
        Foreground = Brush("#94A3B8"),
        VerticalAlignment = VerticalAlignment.Center
    };

    void SyncPositionButtons()
    {
        foreach (var (pos, btn) in positionButtons)
        {
            bool active = stagedPositions.Contains(pos);
            if (active) btn.Classes.Add("accent");
            else btn.Classes.Remove("accent");
        }
        updatingControls = true;
        foreach (var (_, updater) in positionUpdaters) updater();
        foreach (var (pos, dot) in positionDots)
        {
            bool active = positionRanges.TryGetValue(pos, out var rng);
            int rating = active ? rng.Min : 0;
            PitchSurface.SetRating(dot, rating);
            var spot = PitchLayout.Positions.First(p => p.Position == pos);
            ToolTip.SetTip(dot, active ? $"{spot.Label}: {rng.Min}–{rng.Max}" : $"{spot.Label} (Inactive)");
        }
        updatingControls = false;
    }

    Control BuildPositionBoard()
    {
        const double width = 314, height = 428, dotSize = 26;
        var leftStack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        var pitch = PitchSurface.Create(width, height);
        var selection = new ContentControl { Width = width + 2, HorizontalAlignment = HorizontalAlignment.Left };

        void UpdateSelection(PitchPosition spot)
        {
            selectedPosition = spot.Position;
            foreach (var (pos, dot) in positionDots)
            {
                bool active = pos == spot.Position;
                dot.BorderBrush = active ? Brushes.White : Brush("#1A4339");
                dot.BorderThickness = new Thickness(active ? 2 : 1);
            }
            foreach (var (pos, rb) in positionRowBorders)
            {
                rb.BorderBrush = pos == spot.Position ? UiKit.Accent : Brushes.Transparent;
            }

            bool isFiltered = positionRanges.TryGetValue(spot.Position, out var currentRange);
            int curMin = isFiltered ? currentRange.Min : Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
            int curMax = isFiltered ? currentRange.Max : 20;

            var selPanel = new DockPanel { LastChildFill = true };
            var title = new TextBlock
            {
                Text = PositionLabels.Spaced(spot.Position),
                FontSize = 13.5,
                FontWeight = FontWeight.Bold,
                Foreground = UiKit.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(title, spot.Label);

            var controlsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            var activeChk = new CheckBox
            {
                IsChecked = isFiltered,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                MinWidth = 0,
                Padding = new Thickness(0),
            };
            ToolTip.SetTip(activeChk, $"Filter by {PositionLabels.Spaced(spot.Position)}");

            var minBox = new TextBox
            {
                Text = curMin.ToString(Inv),
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
                Foreground = isFiltered ? PitchSurface.PositionFg(curMin) : PitchSurface.PositionFg(0),
                Background = isFiltered ? PitchSurface.PositionBg(curMin) : PitchSurface.PositionBg(0),
                BorderBrush = isFiltered ? PitchSurface.PositionBorder(curMin) : PitchSurface.PositionBorder(0),
            };
            var sep = new TextBlock
            {
                Text = "–",
                Width = 10,
                FontSize = 12.5,
                Foreground = Brush("#64748B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var maxBox = new TextBox
            {
                Text = curMax.ToString(Inv),
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
                Foreground = isFiltered ? PitchSurface.PositionFg(curMax) : PitchSurface.PositionFg(0),
                Background = isFiltered ? PitchSurface.PositionBg(curMax) : PitchSurface.PositionBg(0),
                BorderBrush = isFiltered ? PitchSurface.PositionBorder(curMax) : PitchSurface.PositionBorder(0),
            };

            void CommitSelection()
            {
                if (updatingControls) return;
                bool hasMin = int.TryParse(minBox.Text, out int parsedMin);
                bool hasMax = int.TryParse(maxBox.Text, out int parsedMax);
                if (activeChk.IsChecked == true)
                {
                    int nMin = hasMin ? Math.Clamp(parsedMin, 1, 20) : Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
                    int nMax = hasMax ? Math.Clamp(parsedMax, 1, 20) : 20;
                    if (nMin > nMax) nMax = nMin;
                    minBox.Text = nMin.ToString(Inv);
                    maxBox.Text = nMax.ToString(Inv);
                    positionRanges[spot.Position] = (nMin, nMax);
                    positionRatings[spot.Position] = nMin;
                    stagedPositions.Add(spot.Position);
                }
                else
                {
                    positionRanges.Remove(spot.Position);
                    positionRatings.Remove(spot.Position);
                    stagedPositions.Remove(spot.Position);
                }
                SyncPositionButtons();
                UpdateSummary();
            }

            activeChk.IsCheckedChanged += (_, _) => CommitSelection();
            minBox.LostFocus += (_, _) => { activeChk.IsChecked = true; CommitSelection(); };
            maxBox.LostFocus += (_, _) => { activeChk.IsChecked = true; CommitSelection(); };
            minBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                {
                    TopLevel.GetTopLevel(minBox)?.FocusManager?.ClearFocus();
                    e.Handled = true;
                }
            };
            maxBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                {
                    TopLevel.GetTopLevel(maxBox)?.FocusManager?.ClearFocus();
                    e.Handled = true;
                }
            };

            controlsStack.Children.Add(activeChk);
            controlsStack.Children.Add(minBox);
            controlsStack.Children.Add(sep);
            controlsStack.Children.Add(maxBox);

            DockPanel.SetDock(controlsStack, Dock.Right);
            selPanel.Children.Add(controlsStack);
            selPanel.Children.Add(title);

            selection.Content = new Border
            {
                Background = UiKit.Surface,
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Child = selPanel
            };
        }

        foreach (var spot in PitchLayout.Positions)
        {
            var pos = spot.Position;
            bool active = positionRanges.TryGetValue(pos, out var rng);
            var dot = PitchSurface.CreateDot(spot, active ? rng.Min : 0);
            Canvas.SetLeft(dot, spot.X * width - dotSize / 2);
            Canvas.SetTop(dot, spot.Y * height - dotSize / 2);
            dot.Click += (_, _) => UpdateSelection(spot);
            positionDots[pos] = dot;
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
                var toggle = new CheckBox
                {
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                };
                var colorDot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                };
                var codeTag = new TextBlock
                {
                    Text = PositionLabels.Spaced(pos),
                    Width = 36,
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

                var minBox = new TextBox
                {
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
                };
                var sepLbl = new TextBlock
                {
                    Text = "–",
                    Width = 10,
                    FontSize = 12.5,
                    Foreground = Brush("#64748B"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var maxBox = new TextBox
                {
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
                };

                var rangePanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };
                rangePanel.Children.Add(minBox);
                rangePanel.Children.Add(sepLbl);
                rangePanel.Children.Add(maxBox);

                void ApplyVisual()
                {
                    bool isAct = positionRanges.TryGetValue(pos, out var r);
                    int rMin = isAct ? r.Min : Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
                    int rMax = isAct ? r.Max : 20;

                    toggle.IsChecked = isAct;
                    minBox.Text = isAct ? rMin.ToString(Inv) : "";
                    maxBox.Text = isAct ? rMax.ToString(Inv) : "";
                    minBox.Watermark = isAct ? "" : "–";
                    maxBox.Watermark = isAct ? "" : "–";

                    minBox.Foreground = isAct ? PitchSurface.PositionFg(rMin) : PitchSurface.PositionFg(0);
                    minBox.Background = isAct ? PitchSurface.PositionBg(rMin) : PitchSurface.PositionBg(0);
                    minBox.BorderBrush = isAct ? PitchSurface.PositionBorder(rMin) : PitchSurface.PositionBorder(0);

                    maxBox.Foreground = isAct ? PitchSurface.PositionFg(rMax) : PitchSurface.PositionFg(0);
                    maxBox.Background = isAct ? PitchSurface.PositionBg(rMax) : PitchSurface.PositionBg(0);
                    maxBox.BorderBrush = isAct ? PitchSurface.PositionBorder(rMax) : PitchSurface.PositionBorder(0);

                    colorDot.Fill = isAct ? PitchSurface.PositionColor(rMin) : PitchSurface.PositionColor(0);
                    nameLbl.Foreground = isAct ? UiKit.TextPrimary : UiKit.TextSecondary;
                }
                positionUpdaters[pos] = ApplyVisual;
                ApplyVisual();

                void CommitRowValues()
                {
                    if (updatingControls) return;
                    bool hasMin = int.TryParse(minBox.Text, out int parsedMin);
                    bool hasMax = int.TryParse(maxBox.Text, out int parsedMax);
                    if (hasMin || hasMax)
                    {
                        int pMin = hasMin ? Math.Clamp(parsedMin, 1, 20) : 1;
                        int pMax = hasMax ? Math.Clamp(parsedMax, 1, 20) : 20;
                        if (pMin > pMax) pMax = pMin;
                        positionRanges[pos] = (pMin, pMax);
                        positionRatings[pos] = pMin;
                        stagedPositions.Add(pos);
                    }
                    else if (toggle.IsChecked == true)
                    {
                        int defMin = Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
                        positionRanges[pos] = (defMin, 20);
                        positionRatings[pos] = defMin;
                        stagedPositions.Add(pos);
                    }
                    else
                    {
                        positionRanges.Remove(pos);
                        positionRatings.Remove(pos);
                        stagedPositions.Remove(pos);
                    }
                    SyncPositionButtons();
                    UpdateSummary();
                    if (selectedPosition == pos) UpdateSelection(pitchPos);
                }

                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (updatingControls) return;
                    if (toggle.IsChecked == true)
                    {
                        int defMin = Math.Clamp((int)(posMinRatingInput.Value ?? 15), 1, 20);
                        positionRanges[pos] = (defMin, 20);
                        positionRatings[pos] = defMin;
                        stagedPositions.Add(pos);
                    }
                    else
                    {
                        positionRanges.Remove(pos);
                        positionRatings.Remove(pos);
                        stagedPositions.Remove(pos);
                    }
                    SyncPositionButtons();
                    UpdateSummary();
                    if (selectedPosition == pos) UpdateSelection(pitchPos);
                };

                minBox.LostFocus += (_, _) => CommitRowValues();
                maxBox.LostFocus += (_, _) => CommitRowValues();
                minBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                    {
                        TopLevel.GetTopLevel(minBox)?.FocusManager?.ClearFocus();
                        e.Handled = true;
                    }
                };
                maxBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
                    {
                        TopLevel.GetTopLevel(maxBox)?.FocusManager?.ClearFocus();
                        e.Handled = true;
                    }
                };

                var rowDock = new DockPanel { LastChildFill = true, MinHeight = 26 };
                DockPanel.SetDock(rangePanel, Dock.Right);
                rowDock.Children.Add(rangePanel);
                DockPanel.SetDock(toggle, Dock.Left);
                rowDock.Children.Add(toggle);
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
                    if (e.Source is TextBox or CheckBox) return;
                    UpdateSelection(pitchPos);
                };
                positionRowBorders[pos] = rowBorder;
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
        var defCard = BuildPosCard("Defensive", PitchLayout.DefensivePositions);
        var attCard = BuildPosCard("Midfield & Attack", PitchLayout.AttackingPositions);
        Grid.SetColumn(defCard, 0);
        Grid.SetColumn(attCard, 1);
        rightGrid.Children.Add(defCard);
        rightGrid.Children.Add(attCard);

        var initial = PitchLayout.Positions.FirstOrDefault(p => p.Position == selectedPosition)
            ?? PitchLayout.Positions.First();
        UpdateSelection(initial);

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

    Control BuildAdditionalRanges()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        var keys = new[] { "Height", "Caps", "Goals", "Home Reputation", "Current Reputation", "World Reputation", "CA/PA Difference", "Asking Price", "Wages" };
        foreach (string key in keys)
        {
            initialRanges.TryGetValue(key, out var range);
            var min = new TextBox { Width = 92, Watermark = "Min", Text = range?.Min?.ToString() ?? "", HorizontalContentAlignment = HorizontalAlignment.Right };
            var max = new TextBox { Width = 92, Watermark = "Max", Text = range?.Max?.ToString() ?? "", HorizontalContentAlignment = HorizontalAlignment.Right };
            rangeInputs[key] = (min, max);
            string label = key.StartsWith("Personality:", StringComparison.Ordinal) ? key[12..] : key;
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Width = 360, Margin = new Thickness(0, 0, 12, 6), Spacing = 6, Children = { new TextBlock { Text = label, Width = 152, VerticalAlignment = VerticalAlignment.Center }, min, max } });
        }
        void AddChoice(string key, IEnumerable<FilterChoice> options)
        {
            var choices = options.ToArray();
            long? selected = initialRanges.TryGetValue(key, out var range) ? range.Min : null;
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(option => option.Value == selected), Width = 190 };
            choiceInputs[key] = combo;
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Width = 360, Margin = new Thickness(0, 0, 12, 6), Spacing = 6, Children = { new TextBlock { Text = key, Width = 152, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center }, combo } });
        }
        AddChoice("Ethnicity", new[] { new FilterChoice(null, "Unset") }.Concat(InformationLabels.Ethnicities.Select(value => new FilterChoice(value.Code, value.Name))));
        AddChoice("Treated As Non-EU In Italy", [new(null, "Any"), new(1, "Yes"), new(0, "No")]);
        return panel;
    }

    Control BuildPreferredMoves()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10) };
        for (int bit = 0; bit < PreferredMovesAccess.Count; bit++)
        {
            var box = new CheckBox { Content = PreferredMovesLabels.Names[bit], IsChecked = (initialPreferredMoves & (1UL << bit)) != 0, Width = 420, Margin = new Thickness(0, 0, 6, 6) };
            preferredMoveChecks.Add(box);
            panel.Children.Add(box);
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    Control BuildBottomBar()
    {
        var dock = new DockPanel { LastChildFill = false };

        var clearAllBtn = new Button { Content = "Clear all" };
        clearAllBtn.Click += (_, _) =>
        {
            updatingControls = true;
            divisionCombo.SelectedIndex = 0;
            RefreshDialogClubs(null);
            nationCombo.SelectedIndex = 0;
            minAgeBox.Text = "";
            maxAgeBox.Text = "";
            minCaBox.Text = "";
            maxCaBox.Text = "";
            minPaBox.Text = "";
            maxPaBox.Text = "";
            posMinRatingInput.Value = 15;
            updatingControls = false;

            stagedPositions.Clear();
            positionRatings.Clear();
            positionRanges.Clear();
            foreach (var (_, controls) in rangeInputs) { controls.Min.Text = ""; controls.Max.Text = ""; }
            foreach (var combo in choiceInputs.Values) combo.SelectedIndex = 0;
            foreach (var box in preferredMoveChecks) box.IsChecked = false;
            SyncPositionButtons();

            staged.Clear();
            SelectedPresetName = null;
            suppressPresetCombo = true;
            presetCombo.SelectedIndex = 0;
            deletePresetButton.IsEnabled = false;
            suppressPresetCombo = false;
            SyncAllRowsFromStaged();
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { clearAllBtn, summaryText }
        };
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(left);

        var saveBtn = new Button
        {
            Content = "Save filter",
            Classes = { "accent" },
        };
        saveBtn.Click += (_, _) =>
        {
            foreach (var (_, controls) in rangeInputs)
            {
                if ((!string.IsNullOrWhiteSpace(controls.Min.Text) && ParseOptLong(controls.Min.Text) is null) ||
                    (!string.IsNullOrWhiteSpace(controls.Max.Text) && ParseOptLong(controls.Max.Text) is null) ||
                    (ParseOptLong(controls.Min.Text) is { } min && ParseOptLong(controls.Max.Text) is { } max && min > max))
                { summaryText.Text = "Invalid range"; return; }
            }
            Saved = true;
            Close();
        };

        var discardBtn = new Button
        {
            Content = "Discard",
        };
        discardBtn.Click += (_, _) =>
        {
            Saved = false;
            Close();
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { saveBtn, discardBtn }
        };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);

        return new Border
        {
            Background = Brush("#141722"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10),
            Child = dock
        };
    }

    Control BuildAttributeBoard()
    {
        Control Pair(string first, string second)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, Margin = new Thickness(8) };
            var a = BuildGroupCard(first, PlayerFields.All.Where(f => f.Group == first));
            var b = second == "Personality" ? BuildPersonalityRanges() : BuildGroupCard(second, PlayerFields.All.Where(f => f.Group == second));
            Grid.SetColumn(a, 0); grid.Children.Add(a); Grid.SetColumn(b, 1); grid.Children.Add(b);
            return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid };
        }
        return new TabControl { ItemsSource = new[]
        {
            new TabItem { Header = "Technical / GK", Content = Pair("Technical", "Goalkeeping") },
            new TabItem { Header = "Mental", Content = Pair("Mental", "Personality") },
            new TabItem { Header = "Physical / Hidden", Content = Pair("Physical", "Hidden") },
        } };
    }

    Control BuildPersonalityRanges()
    {
        var panel = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var attribute in Enum.GetValues<PersonalityAttribute>())
        {
            string key = $"Personality:{attribute}";
            initialRanges.TryGetValue(key, out var range);
            var min = new TextBox { Width = 62, Watermark = "Min", Text = range?.Min?.ToString() ?? "", HorizontalContentAlignment = HorizontalAlignment.Right };
            var max = new TextBox { Width = 62, Watermark = "Max", Text = range?.Max?.ToString() ?? "", HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0, 0, 0) };
            rangeInputs[key] = (min, max);
            var row = new DockPanel { MinHeight = 25 };
            DockPanel.SetDock(max, Dock.Right); row.Children.Add(max);
            DockPanel.SetDock(min, Dock.Right); row.Children.Add(min);
            row.Children.Add(new TextBlock { Text = attribute.ToString(), Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Border
            {
                Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(10, 2),
                Child = row,
            });
        }
        return UiKit.Card("Personality", panel);
    }

    Control BuildGroupCard(string title, IEnumerable<FieldDescriptor<PlayerRecord>> fields)
    {
        var stack = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var f in fields)
        {
            if (!Enum.TryParse<PlayerAttribute>(f.Id, out var attr)) continue;
            var row = CreateAttributeRow(attr, f.Label, idx++ % 2 == 1);
            stack.Children.Add(row);
        }
        return UiKit.Card(title, stack);
    }

    Control CreateAttributeRow(PlayerAttribute attr, string label, bool odd)
    {
        var toggle = new CheckBox
        {
            MinWidth = 0,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var nameText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var rangeText = new TextBlock
        {
            Text = "–",
            FontSize = 12.5,
            Foreground = Brush("#A78BFA"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        var input = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 20,
            Increment = 1,
            Value = 1,
            ShowButtonSpinner = false,
            Width = 38,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"),
            BorderBrush = Brush("#282E42")
        };
        var maxInput = new NumericUpDown
        {
            Minimum = 1, Maximum = 20, Increment = 1, Value = 20,
            ShowButtonSpinner = false, Width = 38, FontSize = 13, FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"), BorderBrush = Brush("#282E42")
        };

        var dock = new DockPanel { MinHeight = 28, LastChildFill = true };
        DockPanel.SetDock(maxInput, Dock.Right); dock.Children.Add(maxInput);
        DockPanel.SetDock(rangeText, Dock.Right); dock.Children.Add(rangeText);
        DockPanel.SetDock(input, Dock.Right); dock.Children.Add(input);
        DockPanel.SetDock(toggle, Dock.Left); dock.Children.Add(toggle);
        dock.Children.Add(nameText);

        var rowBorder = new Border
        {
            Padding = new Thickness(8, 1, 8, 1),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = odd ? Brush("#121520") : Brushes.Transparent,
            Child = dock
        };

        var rc = new RowControls
        {
            Attribute = attr,
            Label = label,
            RowBorder = rowBorder,
            Toggle = toggle,
            NameText = nameText,
            RangeText = rangeText,
            ValueInput = input,
            MaxInput = maxInput
        };
        rows[attr] = rc;

        void SetActive(bool active, int? newMin = null, int? newMax = null)
        {
            if (updatingControls) return;
            int defaultMin = Math.Clamp((int)(defaultMinInput.Value ?? 1), 1, 20);
            if (active)
            {
                var existing = staged.TryGetValue(attr, out var range) ? range : (Min: newMax is null ? defaultMin : 1, Max: 20);
                int min = Math.Clamp(newMin ?? existing.Min, 1, 20);
                int max = Math.Clamp(newMax ?? existing.Max, 1, 20);
                if (min > max) { if (newMax is not null) min = max; else max = min; }
                staged[attr] = (min, max);
            }
            else
            {
                staged.Remove(attr);
            }
            SyncRow(rc, odd);
            UpdateSummary();
        }

        toggle.IsCheckedChanged += (_, _) => SetActive(toggle.IsChecked == true);
        nameText.PointerPressed += (_, _) => SetActive(!staged.ContainsKey(attr));
        input.ValueChanged += (_, e) =>
        {
            if (updatingControls || e.NewValue is null) return;
            SetActive(true, newMin: (int)e.NewValue.Value);
        };
        maxInput.ValueChanged += (_, e) =>
        {
            if (updatingControls || e.NewValue is null) return;
            SetActive(true, newMax: (int)e.NewValue.Value);
        };

        return rowBorder;
    }

    void SyncAllRowsFromStaged()
    {
        int i = 0;
        foreach (var rc in rows.Values)
            SyncRow(rc, i++ % 2 == 1);
        UpdateSummary();
    }

    void SyncRow(RowControls rc, bool odd)
    {
        updatingControls = true;
        bool active = staged.TryGetValue(rc.Attribute, out var range);
        int defaultMin = Math.Clamp((int)(defaultMinInput.Value ?? 1), 1, 20);
        int shownVal = active ? range.Min : defaultMin;
        int shownMax = active ? range.Max : 20;

        rc.Toggle.IsChecked = active;
        rc.ValueInput.Value = shownVal;
        rc.MaxInput.Value = shownMax;
        rc.ValueInput.Opacity = active ? 1.0 : 0.4;
        rc.ValueInput.Foreground = active ? RatingColor(shownVal) : Brush("#64748B");
        rc.ValueInput.BorderBrush = Brush(active ? "#7C3AED" : "#282E42");
        rc.MaxInput.Opacity = active ? 1.0 : 0.4;
        rc.MaxInput.Foreground = active ? RatingColor(shownMax) : Brush("#64748B");
        rc.MaxInput.BorderBrush = Brush(active ? "#7C3AED" : "#282E42");
        rc.RangeText.IsVisible = active;
        rc.NameText.Foreground = Brush(active ? "#F8FAFC" : "#94A3B8");
        rc.NameText.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        rc.RowBorder.Background = active ? Brush("#221A3A") : (odd ? Brush("#121520") : Brushes.Transparent);
        rc.RowBorder.BorderBrush = active ? Brush("#7C3AED") : Brushes.Transparent;
        updatingControls = false;
    }

    void UpdateSummary()
    {
        var parts = new List<string>();
        if (ResultDivision is not null) parts.Add($"League: {ResultDivision}");
        if (ResultClub is not null) parts.Add($"Club: {ResultClub}");
        if (ResultNationality is not null) parts.Add($"Nation: {ResultNationality}");
        if (ResultMinAge is not null || ResultMaxAge is not null) parts.Add("Age");
        if (ResultMinCa is not null || ResultMaxCa is not null) parts.Add("CA");
        if (ResultMinPa is not null || ResultMaxPa is not null) parts.Add("PA");
        if (stagedPositions.Count > 0) parts.Add($"{stagedPositions.Count} position(s)");
        if (staged.Count > 0) parts.Add($"{staged.Count} attribute(s)");

        summaryText.Foreground = Brush("#CBD5E1");
        summaryText.Text = parts.Count == 0
            ? "No filters active"
            : $"Active: {string.Join(", ", parts)}";
    }

    void RefreshPresetCombo(string? selectName)
    {
        suppressPresetCombo = true;
        var items = new List<object> { SelectPresetPlaceholder };
        items.AddRange(presets);
        presetCombo.ItemsSource = items;

        int idx = 0;
        if (!string.IsNullOrEmpty(selectName))
        {
            int found = presets.ToList().FindIndex(p => string.Equals(p.Name, selectName, StringComparison.InvariantCultureIgnoreCase));
            if (found >= 0) idx = found + 1;
        }
        presetCombo.SelectedIndex = idx;
        deletePresetButton.IsEnabled = idx > 0;
        suppressPresetCombo = false;
    }

    static int? ParseOptInt(string? text) =>
        int.TryParse(text?.Trim(), out int v) ? v : null;
    static long? ParseOptLong(string? text) => long.TryParse(text?.Trim(), out long value) ? value : null;

    static IBrush RatingColor(int value) => Brush(value <= 5 ? "#64748B" : value <= 10 ? "#FACC15" : value <= 15 ? "#4ADE80" : "#38BDF8");
    static IBrush Brush(string color) => AppTheme.Brush(color);
}
