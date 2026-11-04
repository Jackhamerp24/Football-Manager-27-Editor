using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    Control PreferredMovesPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        var access = new PreferredMovesAccess(session.Memory, session.Profile, session.Game.ModuleBase);
        if (access.Read(player) is not { } current) return panel;
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(12, 8) };
        var boxes = new CheckBox[PreferredMovesAccess.Count];
        ulong Moves() => boxes.Select((b, i) => b.IsChecked == true ? 1UL << i : 0).Aggregate(0UL, (a, b) => a | b);
        void Changed() => save.IsEnabled = discard.IsEnabled = Moves() != (current & PreferredMovesAccess.Editable);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            boxes[i] = new CheckBox { Content = PreferredMovesLabels.Names[i], IsChecked = (current >> i & 1) != 0 };
            Grid.SetRow(boxes[i], i / 2); Grid.SetColumn(boxes[i], i % 2);
            grid.Children.Add(boxes[i]);
            boxes[i].IsCheckedChanged += (_, _) => Changed();
        }
        panel.Children.Add(new Border { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), Child = grid });
        var all = new Button { Content = "Select All" };
        var clear = new Button { Content = "Clear" };
        all.Click += (_, _) => { foreach (var b in boxes) b.IsChecked = true; };
        clear.Click += (_, _) => { foreach (var b in boxes) b.IsChecked = false; };
        save.Click += (_, _) =>
        {
            try
            {
                ulong moves = Moves();
                backend.Edits.Execute(() => access.Set(player, current, moves));
                status.Text = "Saved preferred moves. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, clear, save, discard } });
        return panel;
    }
}
