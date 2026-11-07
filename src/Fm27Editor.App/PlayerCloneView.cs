using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>Players to replace, per source player, kept while the editor is open (FMRTE keeps its list per tab).</summary>
    readonly Dictionary<ulong, List<ulong>> cloneTargets = [];
    CloneParts cloneParts = CloneParts.All;

    static readonly (CloneParts Part, string Name)[] CloneGroups =
    [
        (CloneParts.PersonalDetails, "Personal Details"), (CloneParts.Ability, "Ability"), (CloneParts.Reputation, "Reputation"),
        (CloneParts.International, "International"), (CloneParts.Goalkeeper, "Goalkeeper"), (CloneParts.Technical, "Technical"),
        (CloneParts.Mental, "Mental"), (CloneParts.Physical, "Physical"), (CloneParts.Personality, "Personality"),
        (CloneParts.Hidden, "Hidden"), (CloneParts.Positions, "Positions"), (CloneParts.PreferredMoves, "Preferred Moves"),
    ];

    /// <summary>FMRTE 27's Clone Center: Players to Replace (+/−), What to copy, Clone.</summary>
    Control ClonePanel(PlayerRecord player)
    {
        var all = rows.OfType<PlayerRecord>().ToList();
        if (!cloneTargets.TryGetValue(player.Address, out var targets)) cloneTargets[player.Address] = targets = [];
        var list = new StackPanel { Spacing = 4 };
        foreach (var t in targets.Select(a => all.FirstOrDefault(p => p.Address == a)).OfType<PlayerRecord>())
        {
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { targets.Remove(t.Address); RenderEditor(); };
            list.Children.Add(new DockPanel
            {
                LastChildFill = true,
                Children = { remove, new TextBlock { Text = $"{t.DisplayName} ({t.ClubName})", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) } },
            });
            DockPanel.SetDock(remove, Dock.Right);
        }
        var search = new TextBox { Width = 160 };
        var pick = new ComboBox { Width = 260 };
        var add = new Button { Content = "Add" };
        search.TextChanged += (_, _) =>
        {
            string q = (search.Text ?? "").Trim();
            if (q.Length < 2) return;
            pick.ItemsSource = all.Where(p => p.Address != player.Address && !targets.Contains(p.Address) && Matches(p.DisplayName, q)).Take(40)
                .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", p.Address)).ToArray();
        };
        add.Click += (_, _) => { if (pick.SelectedItem is RelationTarget t) { targets.Add(t.Address); RenderEditor(); } };
        list.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, pick, add } });

        var what = new StackPanel { Spacing = 2 };
        foreach (var (part, name) in CloneGroups)
        {
            var box = new CheckBox { Content = name, IsChecked = cloneParts.HasFlag(part) };
            box.IsCheckedChanged += (_, _) => cloneParts = box.IsChecked == true ? cloneParts | part : cloneParts & ~part;
            what.Children.Add(box);
        }

        var clone = new Button { Content = "Clone", Classes = { "accent" }, IsEnabled = targets.Count > 0 };
        clone.Click += (_, _) =>
        {
            int fields = 0, done = 0;
            var failed = new List<string>();
            foreach (var t in targets.Select(a => all.FirstOrDefault(p => p.Address == a)).OfType<PlayerRecord>().ToList())
            {
                try
                {
                    backend.Edits.Execute(() => fields += backend.Session.ClonePlayer(player, t, cloneParts));
                    done++;
                    if (backend.Players.Reload(t.Address) is { } fresh) { fresh.GameDate = backend.GameDate; ReplaceRow(fresh); }
                }
                catch (Exception ex) { failed.Add($"{t.DisplayName}: {ex.Message}"); }
            }
            status.Text = failed.Count == 0
                ? $"Cloned {player.DisplayName} onto {done} player(s), {fields} field(s). Continue in FM to see updates."
                : $"Cloned onto {done}, {failed.Count} failed ({string.Join(", ", failed.Take(2))}).";
        };

        list.Margin = new Thickness(12, 10);
        what.Margin = new Thickness(12, 10);
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10,
                    Children = { UiKit.Card("Players to Replace", list), WhatToCopy(what) },
                },
                clone,
            },
        };
    }

    static Control WhatToCopy(Control what)
    {
        var card = UiKit.Card("What to Copy", what);
        card.Width = 220;
        Grid.SetColumn(card, 1);
        return card;
    }
}
