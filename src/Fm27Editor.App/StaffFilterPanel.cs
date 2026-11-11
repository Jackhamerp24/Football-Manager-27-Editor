using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>Coach pages of Advanced Search: General, Coaching/Mental, Tactical/Non Tactical, Others.</summary>
internal sealed class StaffFilterPanel : UserControl
{
    const string AllNationalities = "All Nationalities";
    const string AllJobs = "All Jobs";

    sealed record JobChoice(short? Code, string Name) { public override string ToString() => Name; }

    readonly IReadOnlyList<StaffRecord> source;
    readonly DateOnly today;
    readonly Func<StaffRecord, IReadOnlyList<RelationEntry>>? relations;
    readonly Dictionary<string, Control> pages = [];
    readonly List<(StaffAttribute Attribute, TextBox Min, TextBox Max)> attributeInputs = [];
    readonly List<(ComboBox Kind, TextBox Min, TextBox Max, Border Card)> relationRows = [];
    readonly StackPanel relationStack = new() { Spacing = 8 };

    readonly TextBox nameBox = new() { Watermark = "Name", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ComboBox genderCombo = new() { Width = 140, MinWidth = 140, MaxWidth = 140, ItemsSource = new[] { "Men", "Women", "All" }, SelectedIndex = 0, MaxDropDownHeight = 260, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox clubBox = new() { Watermark = "Team", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ComboBox nationCombo = new() { Width = 320, MinWidth = 320, MaxWidth = 320, Height = 28, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ComboBox jobCombo = new() { Width = 320, MinWidth = 320, MaxWidth = 320, Height = 28, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox minAge = Num("Min"), maxAge = Num("Max");
    readonly TextBox minCa = Num("Min"), maxCa = Num("Max");
    readonly TextBox minPa = Num("Min"), maxPa = Num("Max");
    readonly TextBox minHome = Num("Min"), maxHome = Num("Max");
    readonly TextBox minCurrent = Num("Min"), maxCurrent = Num("Max");
    readonly TextBox minWorld = Num("Min"), maxWorld = Num("Max");

    public event EventHandler? Changed;

    public StaffFilterPanel(IReadOnlyList<StaffRecord> source, DateOnly today, Func<StaffRecord, IReadOnlyList<RelationEntry>>? relations = null)
    {
        this.source = source;
        this.today = today;
        this.relations = relations;

        var nations = source.Select(s => s.Nationality?.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Prepend(AllNationalities).ToArray()!;
        nationCombo.ItemsSource = nations;
        nationCombo.SelectedIndex = 0;
        nationCombo.ItemTemplate = TextItem(320);
        nationCombo.SelectionChanged += (_, _) => Raise();
        genderCombo.SelectionChanged += (_, _) => Raise();

        var jobs = source.Select(s => s.JobCode).Where(c => c is not null).Distinct().OrderBy(c => c)
            .Select(c => new JobChoice(c, StaffLabels.Job(c))).Prepend(new JobChoice(null, AllJobs)).ToArray();
        jobCombo.ItemsSource = jobs;
        jobCombo.SelectedIndex = 0;
        jobCombo.ItemTemplate = new FuncDataTemplate<JobChoice>((choice, _) => new TextBlock
        {
            Text = choice.Name, MaxWidth = 284, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        });
        jobCombo.SelectionChanged += (_, _) => Raise();

        foreach (var box in new[] { nameBox, clubBox, minAge, maxAge, minCa, maxCa, minPa, maxPa, minHome, maxHome, minCurrent, maxCurrent, minWorld, maxWorld })
            box.TextChanged += (_, _) => Raise();

        pages["General"] = GeneralPage();
        pages["Coaching/Mental"] = AttributePage([StaffLabels.Groups[0], StaffLabels.Groups[3]]);
        pages["Tactical/Non Tactical"] = AttributePage([StaffLabels.Groups[1], StaffLabels.Groups[2]]);
        pages["Others"] = AttributePage([StaffLabels.Groups[4], StaffLabels.Groups[5]]);
        pages["Relationships"] = BuildRelationships();

        Content = new SectionNav(pages.Select(page => (page.Key, page.Value)).ToArray());
    }

    static TextBox Num(string watermark) => new() { Watermark = watermark, Width = 62, HorizontalContentAlignment = HorizontalAlignment.Right };

    static FuncDataTemplate<string> TextItem(double width) =>
        new((text, _) => new TextBlock { Text = text, MaxWidth = width - 36, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

    Control GeneralPage()
    {
        var personal = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(10, 8),
            Children =
            {
                Field("NAME", nameBox),
                Field("GENDER", genderCombo),
                Field("TEAM", clubBox),
                Field("NATIONALITY", nationCombo),
                Field("JOB", jobCombo),
            }
        };

        var ability = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(10, 8),
            Children =
            {
                RangeRow("AGE", minAge, maxAge),
                RangeRow("CA", minCa, maxCa),
                RangeRow("PA", minPa, maxPa),
            }
        };

        var reputation = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(10, 8),
            Children =
            {
                RangeRow("Home", minHome, maxHome),
                RangeRow("Current", minCurrent, maxCurrent),
                RangeRow("World", minWorld, maxWorld),
            }
        };

        var right = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                UiKit.Card("Ability", ability),
                UiKit.Card("Reputation", reputation),
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10, Margin = new Thickness(14, 10) };
        var leftCard = UiKit.Card("Personal Details", personal);
        leftCard.VerticalAlignment = VerticalAlignment.Top;
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(leftCard);
        grid.Children.Add(right);
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
    }

    Control AttributePage(IEnumerable<(string Title, (StaffAttribute Attribute, string Label)[] Items)> groups)
    {
        var cards = new List<Control>();
        foreach (var (title, items) in groups)
        {
            var rows = new StackPanel { Spacing = 4, Margin = new Thickness(10, 8) };
            foreach (var (attribute, label) in items)
            {
                var min = Num("Min");
                var max = Num("Max");
                attributeInputs.Add((attribute, min, max));
                foreach (var box in new[] { min, max }) box.TextChanged += (_, _) => Raise();
                var row = new DockPanel { LastChildFill = true, MinHeight = 30 };
                DockPanel.SetDock(max, Dock.Right); row.Children.Add(max);
                DockPanel.SetDock(min, Dock.Right); row.Children.Add(min);
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                rows.Children.Add(row);
            }
            cards.Add(UiKit.Card(title, rows));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10, Margin = new Thickness(14, 10) };
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards.Count == 1) { Grid.SetColumnSpan(cards[i], 2); }
            Grid.SetColumn(cards[i], i % 2);
            grid.Children.Add(cards[i]);
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
    }

    Control BuildRelationships()
    {
        var add = new Button { Content = "Add" };
        add.Click += (_, _) => AddRelationRow();
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(14, 10, 14, 4),
            Children =
            {
                new TextBlock { Text = "Add a relation", Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center },
                add,
            }
        };

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(14, 4, 14, 10) };
        stack.Children.Add(header);
        stack.Children.Add(relationStack);
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }

    void AddRelationRow()
    {
        var kind = new ComboBox
        {
            ItemsSource = RelationCatalog.All.Select(t => t.Name).ToArray(),
            SelectedIndex = -1,
            PlaceholderText = "Select type...",
            MaxDropDownHeight = 260,
            Width = 320,
            MinWidth = 320,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = TextItem(320),
        };
        var min = new TextBox { Text = "0", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
        var max = new TextBox { Text = "100", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
        var remove = new Button { Content = "Remove", Width = 80, Padding = new Thickness(0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*,80,80,80"), ColumnSpacing = 8, Margin = new Thickness(0) };
        Grid.SetColumn(kind, 0); grid.Children.Add(kind);
        Grid.SetColumn(min, 2); grid.Children.Add(min);
        Grid.SetColumn(max, 3); grid.Children.Add(max);
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);
        var card = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = grid,
        };
        var entry = (kind, min, max, card);
        relationRows.Add(entry);
        relationStack.Children.Add(card);
        remove.Click += (_, _) =>
        {
            relationRows.Remove(entry);
            relationStack.Children.Remove(card);
            Raise();
        };
        kind.SelectionChanged += (_, _) => Raise();
        min.TextChanged += (_, _) => Raise();
        max.TextChanged += (_, _) => Raise();
        Raise();
    }
    Control RangeRow(string label, TextBox min, TextBox max) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center }, min, new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextMuted }, max }
    };

    static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = UiKit.TextMuted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static Control Field(string label, Control input) => new StackPanel { Spacing = 3, Children = { Label(label), input } };

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public StaffSearchCriteria? Criteria()
    {
        var ranges = new Dictionary<StaffAttribute, (int, int)>();
        foreach (var (attribute, minBox, maxBox) in attributeInputs)
        {
            if (!TryParse(minBox.Text, out int? min) || !TryParse(maxBox.Text, out int? max)) return null;
            if (min is null && max is null) continue;
            int lo = Math.Clamp(min ?? 1, 1, 20), hi = Math.Clamp(max ?? 20, 1, 20);
            if (lo > hi) return null;
            ranges[attribute] = (lo, hi);
        }
        if (!TryRange(minAge.Text, maxAge.Text, 0, 120, out int? ageLo, out int? ageHi)) return null;
        if (!TryRange(minCa.Text, maxCa.Text, 1, 200, out int? caLo, out int? caHi)) return null;
        if (!TryRange(minPa.Text, maxPa.Text, 1, 200, out int? paLo, out int? paHi)) return null;
        if (!TryRange(minHome.Text, maxHome.Text, 0, 10000, out int? homeLo, out int? homeHi)) return null;
        if (!TryRange(minCurrent.Text, maxCurrent.Text, 0, 10000, out int? currentLo, out int? currentHi)) return null;
        if (!TryRange(minWorld.Text, maxWorld.Text, 0, 10000, out int? worldLo, out int? worldHi)) return null;
        foreach (var (kind, minBox, maxBox, _) in relationRows)
        {
            if (kind.SelectedIndex < 0) continue;
            if (!TryParse(minBox.Text, out _) || !TryParse(maxBox.Text, out _)) return null;
        }
        var selectedGender = SelectedGender();
        return new StaffSearchCriteria(
            NameContains: string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim(),
            Nationality: nationCombo.SelectedItem is string nation && nation != AllNationalities ? nation : null,
            JobCode: jobCombo.SelectedItem is JobChoice { Code: { } job } ? job : null,
            MinAge: ageLo,
            MaxAge: ageHi,
            MinCa: caLo,
            MaxCa: caHi,
            MinPa: paLo,
            MaxPa: paHi,
            AttributeRanges: ranges,
            ClubContains: string.IsNullOrWhiteSpace(clubBox.Text) ? null : clubBox.Text.Trim(),
            MinHomeReputation: homeLo,
            MaxHomeReputation: homeHi,
            MinCurrentReputation: currentLo,
            MaxCurrentReputation: currentHi,
            MinWorldReputation: worldLo,
            MaxWorldReputation: worldHi,
            Gender: selectedGender,
            ExcludeWomenClubs: selectedGender == Fm27Editor.Core.Offsets.Gender.Men);
    }

    public Fm27Editor.Core.Offsets.Gender? SelectedGender() => genderCombo.SelectedIndex switch
    {
        0 => Fm27Editor.Core.Offsets.Gender.Men,
        1 => Fm27Editor.Core.Offsets.Gender.Women,
        _ => null,
    };

    public int TotalCount => source.Count;

    IReadOnlyList<(byte Type, int Min, int Max)> RelationCriteria()
    {
        var list = new List<(byte, int, int)>();
        foreach (var (kind, minBox, maxBox, _) in relationRows)
        {
            if (kind.SelectedIndex < 0) continue;
            if (!TryParse(minBox.Text, out int? min) || !TryParse(maxBox.Text, out int? max)) continue;
            int lo = Math.Clamp(min ?? 0, 0, 100), hi = Math.Clamp(max ?? 100, 0, 100);
            list.Add((RelationCatalog.All[kind.SelectedIndex].Code, Math.Min(lo, hi), Math.Max(lo, hi)));
        }
        return list;
    }

    public IReadOnlyList<StaffRecord> Matched()
    {
        var criteria = Criteria();
        if (criteria is null) return [];
        var relationCriteria = RelationCriteria();
        return source.Where(s =>
        {
            if (!StaffSearch.Matches(s, criteria, today)) return false;
            if (relationCriteria.Count == 0) return true;
            var entries = relations?.Invoke(s) ?? [];
            return relationCriteria.All(c => entries.Any(e => e.Type == c.Type && e.Value >= c.Min && e.Value <= c.Max));
        }).ToArray();
    }

    public int FilterCount()
    {
        int count = SelectedGender() is not null ? 1 : 0;
        if (!string.IsNullOrWhiteSpace(nameBox.Text)) count++;
        if (!string.IsNullOrWhiteSpace(clubBox.Text)) count++;
        if (nationCombo.SelectedIndex > 0) count++;
        if (jobCombo.SelectedIndex > 0) count++;
        foreach (var box in new[] { minAge, maxAge, minCa, maxCa, minPa, maxPa, minHome, maxHome, minCurrent, maxCurrent, minWorld, maxWorld })
            if (!string.IsNullOrWhiteSpace(box.Text)) count++;
        foreach (var (_, min, max) in attributeInputs)
            if (!string.IsNullOrWhiteSpace(min.Text) || !string.IsNullOrWhiteSpace(max.Text)) count++;
        count += relationRows.Count(row => row.Kind.SelectedIndex >= 0);
        return count;
    }

    public string SummaryStatus()
    {
        int count = FilterCount();
        var gender = SelectedGender();
        bool hasRelations = relationRows.Any(r => r.Kind.SelectedIndex >= 0);
        if (hasRelations || Criteria() is null)
            return count == 0 ? "No filters set" : $"{count} filter(s) set{(gender is { } g ? $" (Gender: {g})" : "")}";

        int shown = Matched().Count;
        int total = source.Count;
        if (count == 0)
            return total > 0 ? $"All {total:N0} staff" : "No filters set";

        string prefix = gender is { } activeGender ? $"{count} filter(s) (Gender: {activeGender})" : $"{count} filter(s)";
        if (total == 0) return prefix;
        return shown < total
            ? $"{prefix} — {shown:N0} of {total:N0} staff shown ({total - shown:N0} hidden)"
            : $"{prefix} — {shown:N0} staff";
    }

    public void Clear()
    {
        nameBox.Text = "";
        genderCombo.SelectedIndex = 0;
        clubBox.Text = "";
        nationCombo.SelectedIndex = 0;
        jobCombo.SelectedIndex = 0;
        foreach (var box in new[] { minAge, maxAge, minCa, maxCa, minPa, maxPa, minHome, maxHome, minCurrent, maxCurrent, minWorld, maxWorld })
            box.Text = "";
        foreach (var (_, min, max) in attributeInputs) { min.Text = ""; max.Text = ""; }
        relationRows.Clear();
        relationStack.Children.Clear();
    }

    static bool TryRange(string minText, string maxText, int min, int max, out int? lo, out int? hi)
    {
        lo = hi = null;
        if (!TryParse(minText, out lo) || !TryParse(maxText, out hi)) return false;
        if (lo is { } l && (l < min || l > max)) return false;
        if (hi is { } h && (h < min || h > max)) return false;
        if (lo is { } low && hi is { } high && low > high) return false;
        return true;
    }

    static bool TryParse(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return false;
        value = parsed;
        return true;
    }
}
