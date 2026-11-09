using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>FMRTE 27's Swap National Call Up: Swap / From, With / From, then Swap.</summary>
    Control CallUpPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var session = backend.Session;
        if (session.Profile.CallUps is null) return panel;
        var all = session.CallUps.All();
        string From(PlayerRecord p) => all.TryGetValue(p.Address, out var c) ? session.CareerPlans.Item(c.NationFootball)?.Name ?? "National team" : "None";

        var search = new TextBox { Width = 150, Watermark = "Filter player..." };
        var pick = new ComboBox { Width = 280 };
        var otherFrom = new TextBlock { Text = "None", Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var swap = new Button { Content = "Swap Call-Up", IsEnabled = false, Classes = { "accent" } };
        var others = rows.OfType<PlayerRecord>().ToList();
        PlayerRecord? Other() => pick.SelectedItem is RelationTarget t ? others.FirstOrDefault(p => p.Address == t.Address) : null;
        search.TextChanged += (_, _) =>
        {
            string q = (search.Text ?? "").Trim();
            if (q.Length < 2) return;
            pick.ItemsSource = others.Where(p => p.Address != player.Address && Matches(p.DisplayName, q)).Take(40)
                .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", p.Address)).ToArray();
        };
        pick.SelectionChanged += (_, _) =>
        {
            var o = Other();
            otherFrom.Text = o is null ? "None" : From(o);
            swap.IsEnabled = o is not null && (all.ContainsKey(player.Address) || all.ContainsKey(o.Address));
        };
        swap.Click += (_, _) =>
        {
            if (Other() is not { } o) return;
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.CallUpWriter.Swap(player, all.GetValueOrDefault(player.Address), o, all.GetValueOrDefault(o.Address)));
                status.Text = $"Swapped national call-ups of {player.DisplayName} and {o.DisplayName}. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };

        var body = Pairs(
            ("Swap", new TextBlock { Text = player.DisplayName, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary }),
            ("From", new TextBlock { Text = From(player), Foreground = UiKit.TextSecondary }),
            ("With", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { search, pick } }),
            ("From", otherFrom));
        body.Margin = new Thickness(12, 10);

        panel.Children.Add(UiKit.Card("Swap National Call Up", body));
        panel.Children.Add(swap);
        return panel;
    }
}
