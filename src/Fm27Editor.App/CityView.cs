using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
    readonly TextBox citySearch = new() { Watermark = "Search city, nation or ID..." };
    readonly DataGrid cityGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 28, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly ContentControl cityHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly StackPanel cityEditor = new() { Spacing = 12, Margin = new Thickness(12, 0, 12, 12) };
    readonly Dictionary<ulong, Dictionary<CityField, string>> cityDrafts = [];
    IReadOnlyList<CityRow> cityRows = [];
    CityRecord? selectedCity;
    Button? citySaveButton;
    bool cityActive;
    Control cityDetailRoot = new Panel();

    void OpenSelectedCity()
    {
        selectedCity = (cityGrid.SelectedItem as CityRow)?.City;
        if (selectedCity is null) return;
        RenderCityEditor();
        OpenKindTab("city", selectedCity.Name, cityDetailRoot);
    }

    void RegisterCityView()
    {
        AddModuleTab("Cities", cityViewRoot, cityNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, cityNavItem];

        cityGrid.Columns.Add(new DataGridTextColumn { Header = "City", Binding = new Binding(nameof(CityRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        cityGrid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(CityRow.Nation)), Width = new DataGridLength(160) });
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CityRow.Id)), Width = new DataGridLength(100) };
        idCol.CellStyleClasses.Add("numeric");
        cityGrid.Columns.Add(idCol);

        citySearch.TextChanged += (_, _) => FilterCities();
        cityGrid.SelectionChanged += (_, _) => selectedCity = (cityGrid.SelectedItem as CityRow)?.City;
        cityGrid.DoubleTapped += (_, _) => OpenSelectedCity();
        cityGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedCity(); e.Handled = true; }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = citySearch,
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(cityGrid);
        cityViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = cityEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(cityHeaderHost, Dock.Top);
        detailDock.Children.Add(cityHeaderHost);
        detailDock.Children.Add(scroll);
        cityDetailRoot = detailDock;

        // Registered after Clubs, Staff, Nations and Stadiums: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toCities = ReferenceEquals(modules.SelectedItem, cityNavItem);
            if (toCities == cityActive) return;
            cityActive = toCities;
            if (toCities && cityRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadCitiesAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            cityRows = [];
            cityDrafts.Clear();
            selectedCity = null;
            citySaveButton = null;
            cityGrid.ItemsSource = null;
            cityHeaderHost.Content = null;
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
        string q = Fold((citySearch.Text ?? "").Trim());
        cityGrid.ItemsSource = q.Length == 0
            ? cityRows
            : cityRows.Where(r =>
                Fold(r.Name).Contains(q, StringComparison.Ordinal) ||
                Fold(r.Nation).Contains(q, StringComparison.Ordinal) ||
                r.Id.ToString(Inv).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderCityEditor()
    {
        cityHeaderHost.Content = null;
        cityEditor.Children.Clear();
        if (selectedCity is not { } c) { citySaveButton = null; return; }
        if (!cityDrafts.TryGetValue(c.Address, out var draft)) cityDrafts[c.Address] = draft = [];
        var save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        citySaveButton = save;
        void Changed() => save.IsEnabled = discard.IsEnabled = draft.Count > 0;
        save.Click += (_, _) => SaveCity(c, draft);
        discard.Click += (_, _) => { cityDrafts.Remove(c.Address); RenderCityEditor(); };

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 11.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Box(CityField f)
        {
            string original = CityLabels.Format(c, f);
            var box = new TextBox { Text = draft.GetValueOrDefault(f, original), Width = 110, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { string t = box.Text ?? ""; if (t == original) draft.Remove(f); else draft[f] = t; Changed(); };
            return box;
        }
        Control Range()
        {
            var combo = new ComboBox { ItemsSource = CityLabels.InhabitantRanges, MinWidth = 176 };
            combo.SelectedIndex = draft.TryGetValue(CityField.Inhabitants, out var d) && int.TryParse(d, out int cur) ? cur : c.Inhabitants;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                if (combo.SelectedIndex == c.Inhabitants) draft.Remove(CityField.Inhabitants); else draft[CityField.Inhabitants] = combo.SelectedIndex.ToString(Inv);
                Changed();
            };
            return combo;
        }

        Border CardRows(string title, IEnumerable<(string Label, Control Value)> items)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var (label, value) in items)
            {
                var line = new DockPanel { MinHeight = 26, LastChildFill = true };
                DockPanel.SetDock(value, Dock.Right);
                line.Children.Add(value);
                line.Children.Add(Label(label));
                stack.Children.Add(new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(12, 3),
                    Child = line,
                });
            }
            return UiKit.Card(title, stack);
        }

        string nation = cityRows.FirstOrDefault(r => r.City.Address == c.Address)?.Nation ?? "";
        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {c.Id.ToString(Inv)}"));
        if (!string.IsNullOrEmpty(nation) && c.Nation != 0)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            var natRecord = nationCatalogue.FirstOrDefault(n => n.Address == c.Nation);
            var flag = graphics.Image("nationflag", natRecord?.Id, 15);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { flag, new TextBlock { Text = nation, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
            };
            var natLink = UiKit.MetaLinkButton(content, highlight: true);
            natLink.Click += async (_, _) => await OpenPlayerNationAsync(c.Nation);
            badges.Children.Add(natLink);
        }
        else if (!string.IsNullOrEmpty(nation))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(nation));
        }

        cityHeaderHost.Content = UiKit.EntityBanner(c.Name, badges, null);

        var leftCard = CardRows("City", [
            ("Unique ID", Label(c.Id.ToString(Inv), "#F1F5F9")),
            ("Inhabitants Range", Range()),
            ("Attraction", Box(CityField.Attraction)),
        ]);
        var rightCard = CardRows("Location", [
            ("Latitude", Box(CityField.Latitude)),
            ("Longitude", Box(CityField.Longitude)),
            ("Altitude", Box(CityField.Altitude)),
        ]);

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
        };
        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        columns.Children.Add(leftCard);
        columns.Children.Add(rightCard);
        cityEditor.Children.Add(columns);
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
