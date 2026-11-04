using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record CityNavItem
    {
        public string Title => "Cities";
        public override string ToString() => Title;
    }

    public sealed record CityRow(CityRecord City, string Name, string Nation, int Id);

    readonly CityNavItem cityNavItem = new();
    readonly Grid cityViewRoot = new() { IsVisible = false };
    readonly TextBox citySearch = new() { Watermark = "Search city..." };
    readonly DataGrid cityGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 29, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly StackPanel cityEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, Dictionary<CityField, string>> cityDrafts = [];
    IReadOnlyList<CityRow> cityRows = [];
    CityRecord? selectedCity;
    bool cityActive;

    void RegisterCityView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(cityViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, cityNavItem];

        cityGrid.Columns.Add(new DataGridTextColumn { Header = "City", Binding = new Binding(nameof(CityRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        cityGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(CityRow.Nation)), Width = new DataGridLength(150) });
        cityGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CityRow.Id)), Width = new DataGridLength(100) });
        citySearch.TextChanged += (_, _) => FilterCities();
        cityGrid.SelectionChanged += (_, _) => { selectedCity = (cityGrid.SelectedItem as CityRow)?.City; RenderCityEditor(); };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = citySearch };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(cityGrid);
        cityViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(520)) { MinWidth = 360 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 380 },
        };
        Grid.SetColumn(left, 0);
        cityViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        cityViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = cityEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        cityViewRoot.Children.Add(scroll);

        // Registered after Clubs, Staff, Nations and Stadiums: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toCities = ReferenceEquals(modules.SelectedItem, cityNavItem);
            if (toCities == cityActive) return;
            cityActive = toCities;
            cityViewRoot.IsVisible = toCities;
            if (toCities) playersSplit.IsVisible = false;
            else if (!clubsActive && !staffActive && !nationActive && !stadiumActive) playersSplit.IsVisible = true;
            if (toCities && cityRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadCitiesAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            cityRows = [];
            cityDrafts.Clear();
            selectedCity = null;
            cityGrid.ItemsSource = null;
            cityEditor.Children.Clear();
            if (cityActive && backend.State.Status == ConnectionStatus.Connected) await LoadCitiesAsync();
        });
    }

    async Task LoadCitiesAsync()
    {
        status.Text = "Loading cities...";
        try
        {
            var session = backend.Session;
            cityRows = await Task.Run(() =>
            {
                var nations = session.Nations.LoadAll().ToDictionary(n => n.Address, n => n.DisplayName);
                return session.Cities.LoadAll()
                    .Select(c => new CityRow(c, c.Name, nations.GetValueOrDefault(c.Nation, ""), c.Id))
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            });
            FilterCities();
            status.Text = $"{cityRows.Count:N0} cities";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterCities()
    {
        string q = Fold(citySearch.Text ?? "");
        cityGrid.ItemsSource = q.Length == 0 ? cityRows : cityRows.Where(r => Fold(r.Name).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderCityEditor()
    {
        cityEditor.Children.Clear();
        if (selectedCity is not { } c) return;
        if (!cityDrafts.TryGetValue(c.Address, out var draft)) cityDrafts[c.Address] = draft = [];
        Button? save = null, discard = null;
        void Changed() { if (save is not null) save.IsEnabled = discard!.IsEnabled = draft.Count > 0; }

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Box(CityField f)
        {
            string original = CityLabels.Format(c, f);
            var box = new TextBox { Text = draft.GetValueOrDefault(f, original), Width = 100, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { string t = box.Text ?? ""; if (t == original) draft.Remove(f); else draft[f] = t; Changed(); };
            return box;
        }
        Control Range()
        {
            var combo = new ComboBox { ItemsSource = CityLabels.InhabitantRanges, MinWidth = 170 };
            combo.SelectedIndex = draft.TryGetValue(CityField.Inhabitants, out var d) && int.TryParse(d, out int cur) ? cur : c.Inhabitants;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                if (combo.SelectedIndex == c.Inhabitants) draft.Remove(CityField.Inhabitants); else draft[CityField.Inhabitants] = combo.SelectedIndex.ToString(Inv);
                Changed();
            };
            return combo;
        }
        Border Panel(string title, IEnumerable<(string Label, Control Value)> items)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var (label, value) in items)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = Label(label); Grid.SetRow(l, r); g.Children.Add(l);
                Grid.SetRow(value, r); Grid.SetColumn(value, 1); g.Children.Add(value);
            }
            return new()
            {
                Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Child = new StackPanel
                {
                    Children =
                    {
                        new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                            Child = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } },
                        g,
                    }
                }
            };
        }

        string nation = cityRows.FirstOrDefault(r => r.City.Address == c.Address)?.Nation ?? "";
        cityEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = c.Name, FontSize = 20, FontWeight = FontWeight.Bold }, Label($"{c.Id.ToString(Inv)}  ·  {nation}", "#94A3B8") }
        });
        cityEditor.Children.Add(Panel("City", [("Inhabitants Range", Range()), ("Attraction", Box(CityField.Attraction))]));
        cityEditor.Children.Add(Panel("Location", [("Latitude", Box(CityField.Latitude)), ("Longitude", Box(CityField.Longitude)), ("Altitude", Box(CityField.Altitude))]));

        save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        save.Click += (_, _) => SaveCity(c, draft);
        discard.Click += (_, _) => { cityDrafts.Remove(c.Address); RenderCityEditor(); };
        cityEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
    }

    void SaveCity(CityRecord c, Dictionary<CityField, string> draft)
    {
        var values = new Dictionary<CityField, double>();
        foreach (var (f, text) in draft)
        {
            if (!CityLabels.TryParse(f, text, out double v, out var error)) { status.Text = error; return; }
            values[f] = v;
        }
        try
        {
            var w = backend.Session.CityWriter;
            int count = draft.Count;
            backend.Edits.Execute(() => { foreach (var (f, v) in values) w.Set(c, f, v); });
            cityDrafts.Remove(c.Address);
            if (backend.Session.Cities.Read(c.Address) is { } fresh)
            {
                selectedCity = fresh;
                cityRows = cityRows.Select(r => r.City.Address == c.Address ? r with { City = fresh } : r).ToList();
            }
            RenderCityEditor();
            status.Text = $"Saved {count} change{(count == 1 ? "" : "s")} to {c.Name}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
