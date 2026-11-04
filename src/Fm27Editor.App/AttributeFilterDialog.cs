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
        Value = 15,
        Increment = 1,
        ShowButtonSpinner = false,
        Width = 48,
        HorizontalContentAlignment = HorizontalAlignment.Right
    };

    readonly NumericUpDown defaultMinInput = new()
    {
        Minimum = 1,
        Maximum = 20,
        Value = 15,
        Increment = 1,
        ShowButtonSpinner = false,
        Width = 48,
        HorizontalContentAlignment = HorizontalAlignment.Right
    };
    readonly TextBlock summaryText = new()
    {
        FontSize = 12.5,
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
        Width = 1040;
        Height = 760;
        MinWidth = 880;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0D0F17");
        Foreground = Brush("#F1F5F9");
        FontSize = 13;

        presets = currentPresets;
        SelectedPresetName = activePresetName;
        foreach (var t in initialFilter.AttributeThresholds)
            staged[t.Attribute] = (Math.Clamp(t.MinValue, 1, 20), Math.Clamp(t.MaxValue ?? 20, 1, 20));
        foreach (var pos in initialFilter.Positions)
            stagedPositions.Add(pos);

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

        // Center content: General + Positions box on top, 4-column attribute board below
        var bodyStack = new StackPanel { Spacing = 10 };
        bodyStack.Children.Add(BuildGeneralAndPositionsCard());
        bodyStack.Children.Add(BuildAttributeBoard());

        var boardScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 10),
            Content = bodyStack
        };
        root.Children.Add(boardScroll);

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
        dock.Children.Add(right);

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
                if (chosen.Positions is not null)
                    foreach (var pos in chosen.Positions) stagedPositions.Add(pos);
                SyncPositionButtons();

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
                ResultPositionMinRating);
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

        stack.Children.Add(posRow);

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
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };

        var col0 = BuildGroupCard("Technical", PlayerFields.All.Where(f => f.Group == "Technical"));
        var col1 = BuildGroupCard("Mental", PlayerFields.All.Where(f => f.Group == "Mental"));
        var col2 = BuildGroupCard("Physical", PlayerFields.All.Where(f => f.Group == "Physical"));

        var col3Stack = new StackPanel { Spacing = 10 };
        col3Stack.Children.Add(BuildGroupCard("Goalkeeping", PlayerFields.All.Where(f => f.Group == "Goalkeeping")));
        col3Stack.Children.Add(BuildGroupCard("Hidden", PlayerFields.All.Where(f => f.Group == "Hidden")));

        Grid.SetColumn(col0, 0); grid.Children.Add(col0);
        Grid.SetColumn(col1, 1); grid.Children.Add(col1);
        Grid.SetColumn(col2, 2); grid.Children.Add(col2);
        Grid.SetColumn(col3Stack, 3); grid.Children.Add(col3Stack);

        return grid;
    }

    Control BuildGroupCard(string title, IEnumerable<FieldDescriptor<PlayerRecord>> fields)
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(new Border
        {
            Background = Brush("#1C2032"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#F1F5F9")
            }
        });

        int idx = 0;
        foreach (var f in fields)
        {
            if (!Enum.TryParse<PlayerAttribute>(f.Id, out var attr)) continue;
            var row = CreateAttributeRow(attr, f.Label, idx++ % 2 == 1);
            stack.Children.Add(row);
        }

        return new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = stack
        };
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
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
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
            Value = 15,
            ShowButtonSpinner = false,
            Width = 38,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"),
            BorderBrush = Brush("#282E42")
        };
        var maxInput = new NumericUpDown
        {
            Minimum = 1, Maximum = 20, Increment = 1, Value = 20,
            ShowButtonSpinner = false, Width = 38, FontSize = 12.5, FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"), BorderBrush = Brush("#282E42")
        };

        var dock = new DockPanel { Height = 28, LastChildFill = true };
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
            int defaultMin = Math.Clamp((int)(defaultMinInput.Value ?? 15), 1, 20);
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
        int defaultMin = Math.Clamp((int)(defaultMinInput.Value ?? 15), 1, 20);
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

    static IBrush RatingColor(int value) => new SolidColorBrush(Color.Parse(value <= 5 ? "#64748B" : value <= 10 ? "#FACC15" : value <= 15 ? "#4ADE80" : "#38BDF8"));
    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
