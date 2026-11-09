using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>FMRTE 27's Comparison tab: the two players' values side by side, grouped like FMRTE (Attributes, then Positions).</summary>
public static class PlayerComparison
{
    public sealed record Row(string Group, string Label, int Left, int Right, int Max);

    static readonly string[] AttributeGroups = ["Personality", "Reputation", "Ability", "Technical", "Mental", "Physical", "Goalkeeping", "Hidden"];

    public static IReadOnlyList<Row> Attributes(PlayerRecord left, PlayerRecord right) => Rows(left, right, AttributeGroups);

    public static IReadOnlyList<Row> Positions(PlayerRecord left, PlayerRecord right) => Rows(left, right, ["Positions"]);

    static IReadOnlyList<Row> Rows(PlayerRecord left, PlayerRecord right, IEnumerable<string> groups) =>
        groups.SelectMany(g => PlayerFields.All.Where(f => f.Group == g).Select(f => new Row(g, f.Label, f.Read(left), f.Read(right), f.Max))).ToList();
}

public sealed partial class MainWindow
{
    ulong compareWith;

    Control ComparisonPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var others = rows.OfType<PlayerRecord>().ToList();
        var search = new TextBox { Width = 220, Watermark = "Compare with" };
        var pick = new ComboBox { Width = 300 };
        void Find()
        {
            string q = (search.Text ?? "").Trim();
            if (q.Length < 2) return;
            pick.ItemsSource = others.Where(p => p.Address != player.Address && Matches(p.DisplayName, q)).Take(40)
                .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", p.Address)).ToArray();
        }
        search.TextChanged += (_, _) => Find();
        pick.SelectionChanged += (_, _) => { if (pick.SelectedItem is RelationTarget t) { compareWith = t.Address; RenderEditor(); } };
        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 8), Children = { search, pick } };
        panel.Children.Add(UiKit.Card("Compare Player", pickerRow));

        if (others.FirstOrDefault(p => p.Address == compareWith) is not { } other || other.Address == player.Address) return panel;
        var groups = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var g in PlayerComparison.Attributes(player, other).GroupBy(r => r.Group))
            groups.Children.Add(ComparisonGroup(g.Key, g.ToList()));
        var positions = new WrapPanel { Orientation = Orientation.Horizontal };
        positions.Children.Add(ComparisonGroup("Positions", PlayerComparison.Positions(player, other)));
        panel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8,
            Children = { ComparisonCard(player, 0), ComparisonCard(other, 1) },
        });
        panel.Children.Add(new TabControl
        {
            ItemsSource = new[] { new TabItem { Header = "Attributes", Content = groups }, new TabItem { Header = "Positions", Content = positions } },
        });
        return panel;
    }

    Control ComparisonCard(PlayerRecord p, int column)
    {
        var card = new Border
        {
            Background = Brush("#161822"), BorderBrush = Brush(column == 0 ? "#7C3AED" : "#242636"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = p.DisplayName, FontSize = 16, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = $"{p.ClubName} · {p.NationalityName} · {p.Age} yrs", Foreground = Brush("#94A3B8") },
                    new TextBlock { Text = p.Contract is { } c ? $"£{c.Wage:N0} p/w · {DateSetting.Format(c.Expires)}" : "", Foreground = Brush("#94A3B8") },
                },
            },
        };
        Grid.SetColumn(card, column);
        return card;
    }

    /// <summary>One group: label, left value, a bar for the difference (left of centre when the selected player is higher), right value.</summary>
    Control ComparisonGroup(string title, IReadOnlyList<PlayerComparison.Row> rowsOf)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,36,60,60,36"), RowSpacing = 2, Margin = new Thickness(10, 6) };
        foreach (var r in rowsOf)
        {
            int i = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            double share = r.Max == 0 ? 0 : Math.Min(1, Math.Abs(r.Left - r.Right) / (double)r.Max);
            var bar = new Border { Height = 8, Width = Math.Max(share > 0 ? 2 : 0, share * 60), Background = Brush(r.Left > r.Right ? "#7C3AED" : "#475569"), VerticalAlignment = VerticalAlignment.Center };
            bar.HorizontalAlignment = r.Left > r.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Control[] cells =
            [
                new TextBlock { Text = r.Label, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = r.Left.ToString(Inv), HorizontalAlignment = HorizontalAlignment.Right, FontWeight = r.Left > r.Right ? FontWeight.Bold : FontWeight.Normal },
                r.Left > r.Right ? bar : new Border(), r.Right > r.Left ? bar : new Border(),
                new TextBlock { Text = r.Right.ToString(Inv), HorizontalAlignment = HorizontalAlignment.Right, FontWeight = r.Right > r.Left ? FontWeight.Bold : FontWeight.Normal },
            ];
            for (int c = 0; c < cells.Length; c++) { Grid.SetRow(cells[c], i); Grid.SetColumn(cells[c], c); grid.Children.Add(cells[c]); }
        }
        var card = UiKit.Card(title, grid);
        card.Margin = new Thickness(0, 0, 8, 8);
        return card;
    }
}
