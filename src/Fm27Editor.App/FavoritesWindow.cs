using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>
/// The pop-up the star opens: the players the user starred, in the same columns as the Players table, with the same click-to-sort
/// headers. Double-click opens the player; Remove un-stars the selected row.
/// </summary>
sealed class FavoritesWindow : Window
{
    readonly IReadOnlyList<PlayerRecord> all;
    readonly DataGrid grid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = UiKit.Brush("#1D2130"), RowHeight = 26, Background = UiKit.Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly Button remove = new() { Content = "Remove", IsEnabled = false, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };

    public FavoritesWindow(IReadOnlyList<PlayerRecord> players, Action<PlayerRecord> open)
    {
        all = players;
        Title = "Favourites";
        Width = 760; Height = 400; MinWidth = 520; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiKit.WindowBg;

        void Column(string title, string binding, DataGridLength width, bool numeric = false, string? format = null)
        {
            var col = new DataGridTextColumn { Header = title, Binding = format is null ? new Binding(binding) : new Binding(binding) { StringFormat = format }, Width = width };
            if (numeric) col.CellStyleClasses.Add("numeric");
            grid.Columns.Add(col);
        }
        Column("Name", nameof(PlayerRecord.DisplayName), new DataGridLength(1, DataGridLengthUnitType.Star));
        Column("Age", nameof(PlayerRecord.Age), new DataGridLength(52), numeric: true);
        Column("CA", nameof(PlayerRecord.CurrentAbility), new DataGridLength(52), numeric: true);
        Column("PA", nameof(PlayerRecord.PotentialAbility), new DataGridLength(52), numeric: true);
        Column("Position", nameof(PlayerRecord.PositionSummary), new DataGridLength(110));
        Column("Club", nameof(PlayerRecord.ClubName), new DataGridLength(130));
        Column("Nationality", nameof(PlayerRecord.NationalityName), new DataGridLength(110));
        Column("Asking Price", nameof(PlayerRecord.DisplayAskingPrice), new DataGridLength(100), numeric: true, format: "N0");

        Refill();
        grid.SelectionChanged += (_, _) => remove.IsEnabled = grid.SelectedItem is PlayerRecord;
        grid.DoubleTapped += (_, e) =>
        {
            if (!UiKit.OnRow(e) || grid.SelectedItem is not PlayerRecord p) return;
            Close();
            open(p);
        };
        remove.Click += (_, _) =>
        {
            if (grid.SelectedItem is not PlayerRecord p || p.Id is not { } id) return;
            FavoriteStore.Set(id, false);
            Refill();
        };

        var footer = new DockPanel { Margin = new Thickness(10, 8) };
        DockPanel.SetDock(remove, Dock.Right);
        footer.Children.Add(remove);
        var count = new TextBlock { Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        footer.Children.Add(count);
        this.count = count;
        UpdateCount();

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(grid);
        Content = root;
    }

    readonly TextBlock? count;

    public void Refill()
    {
        grid.ItemsSource = all.Where(p => FavoriteStore.Contains(p.Id)).OrderByDescending(p => p.CurrentAbility).ToList();
        UpdateCount();
    }

    void UpdateCount()
    {
        if (count is null) return;
        int n = (grid.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
        count.Text = n == 1 ? "1 player" : $"{n} players";
    }
}
