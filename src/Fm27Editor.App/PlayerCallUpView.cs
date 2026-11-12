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

        // Walking every national squad takes a few seconds on the first call; keep it off the UI thread and cache the
        // result in the session reader, so opening this tab never freezes the window.
        IReadOnlyDictionary<ulong, NationalCallUp>? all = null;
        string From(PlayerRecord p) => all is null
            ? "Reading call-ups…"
            : all.TryGetValue(p.Address, out var c) ? session.CareerPlans.Item(c.NationFootball)?.Name ?? "National team" : "None";

        var fromPlayer = new TextBlock { Text = From(player), Foreground = UiKit.TextSecondary };
        var pick = new Button { Width = 300, HorizontalContentAlignment = HorizontalAlignment.Left, Content = "Choose player…" };
        PlayerRecord? other = null;
        var otherFrom = new TextBlock { Text = "None", Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var swap = new Button { Content = "Swap Call-Up", IsEnabled = false, Classes = { "accent" } };
        var others = rows.OfType<PlayerRecord>().ToList();
        PlayerRecord? Other() => other;
        void RefreshState()
        {
            fromPlayer.Text = From(player);
            otherFrom.Text = Other() is { } o ? From(o) : "None";
            swap.IsEnabled = all is not null && Other() is { } chosen &&
                (all.ContainsKey(player.Address) || all.ContainsKey(chosen.Address));
        }

        pick.Click += async (_, _) =>
        {
            var choices = others.Where(p => p.Address != player.Address
                && p.Gender != Fm27Editor.Core.Offsets.Gender.Women && p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", p.Address)).ToList();
            var chosen = await PickerDialog.Show<RelationTarget>(this, "Choose player to swap with", q => choices.Where(t => PickerSearch.Hit(q, t.Label)).Select(t => (t.Label, t)).ToList());
            if (chosen is null) return;
            other = others.FirstOrDefault(p => p.Address == chosen.Address);
            pick.Content = chosen.Label;
            RefreshState();
        };
        swap.Click += (_, _) =>
        {
            if (all is null || Other() is not { } o) return;
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.CallUpWriter.Swap(player, all.GetValueOrDefault(player.Address), o, all.GetValueOrDefault(o.Address)));
                session.CallUps.Invalidate();
                status.Text = $"Swapped national call-ups of {player.DisplayName} and {o.DisplayName}. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };

        _ = Task.Run(() => session.CallUps.All()).ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (task.IsFaulted) return;
            all = task.Result;
            RefreshState();
        }));

        var body = Pairs(
            ("Swap", new TextBlock { Text = player.DisplayName, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary }),
            ("From", fromPlayer),
            ("With", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pick } }),
            ("From", otherFrom));
        body.Margin = new Thickness(12, 10);

        panel.Children.Add(UiKit.Card("Swap National Call Up", body));
        panel.Children.Add(swap);
        return panel;
    }
}
