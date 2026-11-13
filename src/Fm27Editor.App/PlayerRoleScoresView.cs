using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record RoleScoreRow(
        string Code,
        string Name,
        string Group,
        double Score,
        string ScoreText,
        string PrimaryText,
        string SecondaryText);

    static readonly CultureInfo InvCulture = CultureInfo.InvariantCulture;

    internal static Control MiniScoutingCard(PlayerRecord player)
    {
        var rca = RcaCalculator.Analyze(player);
        var bestRole = RoleScores.BestFitRole(player);
        var personality = PersonalityClassifier.Classify(player);
        double progress = RoleScores.ProgressRate(player);
        double coreMental = RoleScores.CoreMentalPercent(player);

        Control Row(string label, string value, string hex = "#E2E8F0", bool bold = false)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            var l = new TextBlock { Text = label, FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = UiKit.Brush(hex),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(l);
            g.Children.Add(v);
            return g;
        }

        string persColor = personality.IsNegative ? "#F87171" : "#E2E8F0";
        return AttributeCard("Scouting Summary",
        [
            Row("RCA", $"{rca.BestRca} ({PositionLabels.Short(rca.BestPosition)})", "#38BDF8", bold: true),
            Row("Best Role", $"{bestRole.Role.Code} ({bestRole.Score.ToString("0.0", InvCulture)}%)", "#C084FC", bold: true),
            Row("Personality", personality.Name, persColor, bold: personality.IsNegative),
            Row("Progress Rate", $"{progress.ToString("0.0", InvCulture)} / 10.0"),
            Row("Core Mental", $"{coreMental.ToString("0", InvCulture)}%"),
        ]);
    }

    Control RoleScoresPanel(PlayerRecord player)
    {
        var rca = RcaCalculator.Analyze(player);
        var allScores = RoleScores.ForPlayer(player);
        var bestFit = RoleScores.BestFitRole(player);
        var personality = PersonalityClassifier.Classify(player);
        double progress = RoleScores.ProgressRate(player);
        double coreMental = RoleScores.CoreMentalPercent(player);

        var root = new StackPanel { Spacing = 10 };

        // Top summary card: RCA + Best Fit Role + Personality + Progress Rate + Core Mental + position RCA breakdown
        var metrics = new WrapPanel { Orientation = Orientation.Horizontal };
        void AddStat(string label, string text, string hex) =>
            metrics.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 8, 4),
                Child = UiKit.StatBox(label, new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = UiKit.Brush(hex),
                    VerticalAlignment = VerticalAlignment.Center,
                }),
            });

        AddStat("RCA", $"{rca.BestRca} ({PositionLabels.Short(rca.BestPosition)})", "#38BDF8");
        AddStat("Best Fit", $"{bestFit.Role.Code} {bestFit.Score.ToString("0.0", InvCulture)}%", "#C084FC");
        AddStat("Personality", personality.Name, personality.IsNegative ? "#F87171" : "#E2E8F0");
        AddStat("Progress", $"{progress.ToString("0.0", InvCulture)} / 10.0", "#E2E8F0");
        AddStat("Core Mental", $"{coreMental.ToString("0", InvCulture)}%", "#E2E8F0");

        var summaryStack = new StackPanel { Spacing = 6, Margin = new Thickness(12, 10) };
        summaryStack.Children.Add(metrics);
        if (rca.Positions.Count > 0)
        {
            string posLine = string.Join("   |   ", rca.Positions.Select(p =>
                $"{PositionLabels.Short(p.Position)} ({p.Rating}): {(int)Math.Floor(p.UnweightedBase + 0.5)}"));
            summaryStack.Children.Add(new TextBlock
            {
                Text = "Position RCA (rating >= 12): " + posLine,
                FontSize = 12,
                Foreground = UiKit.Brush("#94A3B8"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        root.Children.Add(UiKit.Card("Scouting Summary", summaryStack));

        // 85 Role Scores filter + ToolGrid
        RoleGroup? activeGroup = null;
        var filterBox = new TextBox { Watermark = "Filter roles (e.g. BPD, Pressing, Keeper)...", Width = 260 };
        var countLabel = new TextBlock { FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center };

        var table = ToolGrid.Create();
        table.Height = 460;
        table.Columns.Add(ToolGrid.Text<RoleScoreRow>("Code", r => r.Code, 84, "Role short code", "#E2E8F0", bold: true));
        table.Columns.Add(ToolGrid.Text<RoleScoreRow>("Role", r => r.Name, 230, "Full role and duty name", "#CBD5E1"));
        table.Columns.Add(ToolGrid.Text<RoleScoreRow>("Group", r => r.Group, 95, "Tactical position group", "#94A3B8"));
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = ToolGrid.Header("Score", "Role suitability score (0 - 100%)", HorizontalAlignment.Right),
            Width = new DataGridLength(82),
            CellTemplate = new FuncDataTemplate<RoleScoreRow>((r, _) => new TextBlock
            {
                Text = r?.ScoreText ?? "",
                FontWeight = FontWeight.SemiBold,
                Foreground = UiKit.Brush(r is null ? "#CBD5E1" : r.Score >= 75 ? "#4ADE80" : r.Score >= 60 ? "#38BDF8" : "#CBD5E1"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            }),
        });
        table.Columns.Add(ToolGrid.Text<RoleScoreRow>("Key Attributes (x3)", r => r.PrimaryText, double.NaN, "Primary role attributes (weight x3)", "#CBD5E1", minWidth: 200));
        table.Columns.Add(ToolGrid.Text<RoleScoreRow>("Secondary (x2)", r => r.SecondaryText, double.NaN, "Secondary role attributes (weight x2)", "#94A3B8", minWidth: 180));

        ToolGrid.EnableSort<RoleScoreRow>(table, new Dictionary<string, Func<RoleScoreRow, IComparable?>>
        {
            ["Code"] = r => r.Code,
            ["Role"] = r => r.Name,
            ["Group"] = r => r.Group,
            ["Score"] = r => r.Score,
        });

        var allRows = allScores.Select(s => new RoleScoreRow(
            s.Role.Code,
            s.Role.Name,
            s.Role.Group.ToString(),
            s.Score,
            s.Score.ToString("0.0", InvCulture) + "%",
            string.Join(", ", s.Role.Primary),
            string.Join(", ", s.Role.Secondary))).ToList();

        void ApplyFilter()
        {
            string? needle = string.IsNullOrWhiteSpace(filterBox.Text) ? null : filterBox.Text.Trim();
            var filtered = allRows.Where(r =>
                (activeGroup is null || r.Group == activeGroup.Value.ToString())
                && (needle is null
                    || r.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || r.PrimaryText.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || r.SecondaryText.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            ToolGrid.SetItems(table, filtered);
            countLabel.Text = $"{filtered.Count} / {allRows.Count} roles";
        }

        var groupOptions = new (string Label, RoleGroup? Group)[]
        {
            ($"All ({allRows.Count})", null),
            ($"GK ({allRows.Count(r => r.Group == nameof(RoleGroup.Goalkeeper))})", RoleGroup.Goalkeeper),
            ($"Def ({allRows.Count(r => r.Group == nameof(RoleGroup.Defence))})", RoleGroup.Defence),
            ($"Mid ({allRows.Count(r => r.Group == nameof(RoleGroup.Midfield))})", RoleGroup.Midfield),
            ($"Wing ({allRows.Count(r => r.Group == nameof(RoleGroup.Wing))})", RoleGroup.Wing),
            ($"Att ({allRows.Count(r => r.Group == nameof(RoleGroup.Attack))})", RoleGroup.Attack),
        };
        var tabs = UiKit.OptionTabs(
            groupOptions.Select(o => (o.Label, (Control)new Panel())).ToList(),
            0,
            idx => { activeGroup = groupOptions[idx].Group; ApplyFilter(); });

        filterBox.TextChanged += (_, _) => ApplyFilter();
        ApplyFilter();

        var bar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(tabs, Dock.Left);
        bar.Children.Add(tabs);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { filterBox, countLabel } };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);

        root.Children.Add(bar);
        root.Children.Add(table);
        return root;
    }
}
