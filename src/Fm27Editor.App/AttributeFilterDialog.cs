using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed class AttributeFilterDialog : Window
{
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
    readonly Dictionary<PlayerPosition, NumericUpDown> positionInputs = [];
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
        FontSize = 11.5,
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
    public IReadOnlyList<FilterPreset> UpdatedPresets => presets;
    public string? SelectedPresetName { get; private set; }

    public AttributeFilterDialog(
        PlayerFilter initialFilter,
        IReadOnlyList<FilterPreset> currentPresets,
        string? activePresetName = null)
    {
        Title = "Player Filter & Presets";
        Width = 940;
        Height = 740;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0D0F17");
        Foreground = Brush("#F1F5F9");
        FontSize = 11.5;

        presets = currentPresets;
        foreach (var (key, range) in initialFilter.NumericRanges) initialRanges[key] = range;
        initialPreferredMoves = initialFilter.RequiredPreferredMoves;
        SelectedPresetName = activePresetName;
        foreach (var t in initialFilter.AttributeThresholds)
            staged[t.Attribute] = (Math.Clamp(t.MinValue, 1, 20), Math.Clamp(t.MaxValue ?? 20, 1, 20));
        foreach (var pos in initialFilter.Positions)
        {
            stagedPositions.Add(pos);
            positionRatings[pos] = initialFilter.PositionThresholds.GetValueOrDefault(pos, initialFilter.PositionMinRating);
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
            FontSize = 12,
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
            FontSize = 12,
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
                if (chosen.Positions is not null)
                    foreach (var pos in chosen.Positions) { stagedPositions.Add(pos); positionRatings[pos] = chosen.PositionThresholds?.GetValueOrDefault(pos) ?? chosen.PositionMinRating; }
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
                ResultPositionMinRating) { PositionThresholds = ResultPositionThresholds, NumericRanges = ResultNumericRanges, RequiredPreferredMoves = ResultPreferredMoves };
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

    Control BuildGeneralAndPositionsCard()
    {
        var stack = new StackPanel { Spacing = 8 };

        // Row 1: Age, CA, PA ranges
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
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
            };
            ToolTip.SetTip(btn, fullName);
            btn.Click += (_, _) =>
            {
                if (!stagedPositions.Remove(pos))
                    stagedPositions.Add(pos);
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
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Foreground = Brush("#E2E8F0"),
        VerticalAlignment = VerticalAlignment.Center
    };

    static TextBlock MutedLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
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
        foreach (var (pos, input) in positionInputs) input.Value = positionRatings.GetValueOrDefault(pos);
        foreach (var (pos, dot) in positionDots) PitchSurface.SetRating(dot, positionRatings.GetValueOrDefault(pos));
        updatingControls = false;
    }

    Control BuildPositionBoard()
    {
        const double width = 340, height = 460, dotSize = 26;
        var pitch = PitchSurface.Create(width, height);
        var details = new ContentControl { Width = width };
        var container = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel? currentDetails = null;
        void Select(PitchPosition spot)
        {
            foreach (var (position, dot) in positionDots)
            {
                bool active = position == spot.Position;
                dot.BorderBrush = active ? Brushes.White : Brush("#1A4339");
                dot.BorderThickness = new Thickness(active ? 2 : 1);
            }
            currentDetails?.Children.Clear();
            var panel = new DockPanel { LastChildFill = true };
            currentDetails = panel;
            var input = positionInputs[spot.Position];
            DockPanel.SetDock(input, Dock.Right); panel.Children.Add(input);
            panel.Children.Add(new TextBlock { Text = spot.Label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
            details.Content = new Border { Background = Brush("#151926"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8), Child = panel };
        }
        foreach (var spot in PitchLayout.Positions)
        {
            var position = spot.Position;
            var input = new NumericUpDown { Minimum = 0, Maximum = 20, Value = positionRatings.GetValueOrDefault(position), Width = 68, Increment = 1 };
            input.ValueChanged += (_, _) =>
            {
                if (updatingControls) return;
                int rating = (int)(input.Value ?? 0);
                if (rating == 0) { positionRatings.Remove(position); stagedPositions.Remove(position); }
                else { positionRatings[position] = rating; stagedPositions.Add(position); }
                PitchSurface.SetRating(positionDots[position], rating);
                UpdateSummary();
            };
            positionInputs[position] = input;
            input.Height = 28; input.ShowButtonSpinner = false;
            Avalonia.Automation.AutomationProperties.SetName(input, spot.Label);
            var dot = PitchSurface.CreateDot(spot, positionRatings.GetValueOrDefault(position));
            Canvas.SetLeft(dot, spot.X * width - dotSize / 2); Canvas.SetTop(dot, spot.Y * height - dotSize / 2);
            dot.Click += (_, _) => Select(spot); positionDots[position] = dot; pitch.Children.Add(dot);
        }
        container.Children.Add(pitch); container.Children.Add(details);
        Select(PitchLayout.Positions.First());
        return container;
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
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var rangeText = new TextBlock
        {
            Text = "–",
            FontSize = 11,
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
            FontSize = 11.5,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"),
            BorderBrush = Brush("#282E42")
        };
        var maxInput = new NumericUpDown
        {
            Minimum = 1, Maximum = 20, Increment = 1, Value = 20,
            ShowButtonSpinner = false, Width = 38, FontSize = 11.5, FontWeight = FontWeight.Bold,
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

    static IBrush RatingColor(int value) => new SolidColorBrush(Color.Parse(value <= 5 ? "#64748B" : value <= 10 ? "#FACC15" : value <= 15 ? "#4ADE80" : "#38BDF8"));
    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
