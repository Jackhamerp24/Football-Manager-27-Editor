using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    async Task ShowPlayerFreezerAsync(PlayerRecord player)
    {
        if (backend.Freezer is not { } freezer || player.Id is not > 0) return;
        var existing = freezer.Players.FirstOrDefault(p => p.Id == player.Id)?.Fields.ToDictionary(f => f.Attribute)
            ?? new Dictionary<PlayerAttribute, FrozenAttribute>();
        var window = new Window { Title = $"Freezer — {player.DisplayName}", Width = 550, Height = 650, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var rows = new StackPanel { Spacing = 3, Margin = new Thickness(12) };
        var choices = new List<(PlayerAttribute Attribute, CheckBox Freeze, CheckBox Increase, CheckBox Decrease)>();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,65,105,105") };
        var holdHeading = new TextBlock { Text = "Freeze" };
        var growHeading = new TextBlock { Text = "Allow increase" };
        var lowerHeading = new TextBlock { Text = "Allow decrease" };
        Grid.SetColumn(holdHeading, 1); Grid.SetColumn(growHeading, 2);
        Grid.SetColumn(lowerHeading, 3);
        header.Children.Add(holdHeading); header.Children.Add(growHeading); header.Children.Add(lowerHeading); rows.Children.Add(header);
        foreach (var attr in Enum.GetValues<PlayerAttribute>())
        {
            if (player.Raw(attr) is < 1 or > 100) continue;
            existing.TryGetValue(attr, out var frozen);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,65,105,105") };
            var label = PlayerFields.All.First(f => f.Id == attr.ToString()).Label;
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var hold = new CheckBox { IsChecked = frozen is not null };
            var increase = new CheckBox { IsChecked = frozen?.Mode == FreezeMode.AllowIncrease };
            var decrease = new CheckBox { IsChecked = frozen?.Mode == FreezeMode.AllowDecrease };
            increase.IsCheckedChanged += (_, _) => { if (increase.IsChecked == true) decrease.IsChecked = false; };
            decrease.IsCheckedChanged += (_, _) => { if (decrease.IsChecked == true) increase.IsChecked = false; };
            Grid.SetColumn(hold, 1); Grid.SetColumn(increase, 2);
            Grid.SetColumn(decrease, 3);
            line.Children.Add(hold); line.Children.Add(increase); line.Children.Add(decrease); rows.Children.Add(line);
            choices.Add((attr, hold, increase, decrease));
        }
        var selectAll = new Button { Content = "Select all" };
        selectAll.Click += (_, _) => { foreach (var choice in choices) choice.Freeze.IsChecked = true; };
        var clear = new Button { Content = "Unfreeze" };
        var apply = new Button { Content = "Apply", Classes = { "accent" } };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12), Children = { selectAll, clear, apply } };
        var panel = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer { Content = rows });
        window.Content = panel;
        clear.Click += (_, _) =>
        {
            try { backend.Edits.Execute(() => freezer.RemovePlayer(player.Id.Value)); status.Text = "Player unfrozen"; window.Close(); }
            catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
        };
        apply.Click += (_, _) =>
        {
            var fields = choices.Where(c => c.Freeze.IsChecked == true)
                .Select(c => (c.Attribute, c.Increase.IsChecked == true ? FreezeMode.AllowIncrease : c.Decrease.IsChecked == true ? FreezeMode.AllowDecrease : FreezeMode.Fixed)).ToArray();
            if (fields.Length == 0) { status.Text = "Select an attribute"; return; }
            try { backend.Edits.Execute(() => freezer.SetPlayer(player, fields)); status.Text = $"Frozen: {player.DisplayName}"; window.Close(); }
            catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
        };
        await window.ShowDialog(this);
    }

    async Task ShowClubFreezerAsync(ClubEntry club)
    {
        if (backend.Freezer is not { } freezer || club.Id is not > 0) return;
        var window = new Window { Title = $"Freezer — {club.DisplayName}", Width = 360, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var fixedButton = new Button { Content = "Hold balance" };
        var growButton = new Button { Content = "Allow increase" };
        var clear = new Button { Content = "Unfreeze" };
        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Balance" });
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { fixedButton, growButton, clear } });
        window.Content = panel;
        void Set(FreezeMode? mode)
        {
            try
            {
                backend.Edits.Execute(() => { if (mode is { } m) freezer.SetClub(club, m); else freezer.RemoveClub(club.Id.Value); });
                status.Text = mode is null ? "Club unfrozen" : $"Frozen: {club.DisplayName} balance";
                window.Close();
            }
            catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
        }
        fixedButton.Click += (_, _) => Set(FreezeMode.Fixed);
        growButton.Click += (_, _) => Set(FreezeMode.AllowIncrease);
        clear.Click += (_, _) => Set(null);
        await window.ShowDialog(this);
    }
}
