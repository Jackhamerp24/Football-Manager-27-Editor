using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>FMRTE 27's Swap National Call Up: Swap / From, With / From, then Swap.</summary>
    Control CallUpPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        if (session.Profile.CallUps is null) return panel;
        var all = session.CallUps.All();
        string From(PlayerRecord p) => all.TryGetValue(p.Address, out var c) ? session.CareerPlans.Item(c.NationFootball)?.Name ?? "National team" : "None";

        var search = new TextBox { Width = 160 };
        var pick = new ComboBox { Width = 300 };
        var otherFrom = new TextBlock { Text = "None" };
        var swap = new Button { Content = "Swap", IsEnabled = false, Classes = { "accent" } };
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
                backend.Edits.Execute(() => session.CallUpWriter.Swap(player, all.GetValueOrDefault(player.Address), o, all.GetValueOrDefault(o.Address)));
                status.Text = $"Swapped national call-ups of {player.DisplayName} and {o.DisplayName}. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(Card(Pairs(("Swap", new TextBlock { Text = player.DisplayName }), ("From", new TextBlock { Text = From(player) }),
            ("With", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, pick } }), ("From", otherFrom))));
        panel.Children.Add(swap);
        return panel;
    }
}
