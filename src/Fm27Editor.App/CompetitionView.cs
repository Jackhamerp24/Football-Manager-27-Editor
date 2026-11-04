using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record CompetitionNavItem
    {
        public string Title => "Competitions";
        public override string ToString() => Title;
    }

    public sealed record CompetitionRow(CompetitionRecord Competition, string Name, int Reputation, int Id);

    readonly CompetitionNavItem competitionNavItem = new();
    readonly Grid competitionViewRoot = new() { IsVisible = false };
    readonly TextBox competitionSearch = new() { Watermark = "Search competition..." };
    readonly DataGrid competitionGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 29, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly StackPanel competitionEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, string> competitionDrafts = [];
    IReadOnlyList<CompetitionRow> competitionRows = [];
    CompetitionRecord? selectedCompetition;
    bool competitionActive;

    void RegisterCompetitionView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(competitionViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, competitionNavItem];

        competitionGrid.Columns.Add(new DataGridTextColumn { Header = "Competition", Binding = new Binding(nameof(CompetitionRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        competitionGrid.Columns.Add(new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(CompetitionRow.Reputation)), Width = new DataGridLength(90) });
        competitionGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CompetitionRow.Id)), Width = new DataGridLength(90) });
        competitionSearch.TextChanged += (_, _) => FilterCompetitions();
        competitionGrid.SelectionChanged += (_, _) => { selectedCompetition = (competitionGrid.SelectedItem as CompetitionRow)?.Competition; RenderCompetitionEditor(); };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = competitionSearch };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(competitionGrid);
        competitionViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(560)) { MinWidth = 360 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 380 },
        };
        Grid.SetColumn(left, 0);
        competitionViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        competitionViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = competitionEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        competitionViewRoot.Children.Add(scroll);

        // Registered after Clubs, Staff, Nations, Stadiums and Cities: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toCompetitions = ReferenceEquals(modules.SelectedItem, competitionNavItem);
            if (toCompetitions == competitionActive) return;
            competitionActive = toCompetitions;
            competitionViewRoot.IsVisible = toCompetitions;
            if (toCompetitions) playersSplit.IsVisible = false;
            else if (!clubsActive && !staffActive && !nationActive && !stadiumActive && !cityActive) playersSplit.IsVisible = true;
            if (toCompetitions && competitionRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadCompetitionsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            competitionRows = [];
            competitionDrafts.Clear();
            selectedCompetition = null;
            competitionGrid.ItemsSource = null;
            competitionEditor.Children.Clear();
            if (competitionActive && backend.State.Status == ConnectionStatus.Connected) await LoadCompetitionsAsync();
        });
    }

    async Task LoadCompetitionsAsync()
    {
        status.Text = "Loading competitions...";
        try
        {
            var session = backend.Session;
            competitionRows = await Task.Run(() => session.Competitions.LoadAll()
                .Select(c => new CompetitionRow(c, c.DisplayName, c.Reputation, c.Id))
                .OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
            FilterCompetitions();
            status.Text = $"{competitionRows.Count:N0} competitions";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterCompetitions()
    {
        string q = Fold(competitionSearch.Text ?? "");
        competitionGrid.ItemsSource = q.Length == 0 ? competitionRows : competitionRows.Where(r => Fold(r.Name).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderCompetitionEditor()
    {
        competitionEditor.Children.Clear();
        if (selectedCompetition is not { } c) return;
        competitionDrafts.TryGetValue(c.Address, out string? pending);
        Button? save = null, discard = null;
        void Changed() { if (save is not null) save.IsEnabled = discard!.IsEnabled = competitionDrafts.ContainsKey(c.Address); }

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Swatch(uint argb) => new Border { Width = 70, Height = 16, Background = new SolidColorBrush(Color.FromUInt32(argb)), BorderBrush = Brush("#3A3F55"), BorderThickness = new Thickness(1) };
        string Date(DateOnly? d) => d is { } v ? v.ToString("yyyy-MM-dd", Inv) : "";
        var reputation = new TextBox { Text = pending ?? c.Reputation.ToString(Inv), Width = 70, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        reputation.TextChanged += (_, _) =>
        {
            string t = reputation.Text ?? "";
            if (t == c.Reputation.ToString(Inv)) competitionDrafts.Remove(c.Address); else competitionDrafts[c.Address] = t;
            Changed();
        };
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

        competitionEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = c.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold }, Label(c.Id.ToString(Inv), "#94A3B8") }
        });
        competitionEditor.Children.Add(Panel("Competition", [("Reputation", reputation)]));
        competitionEditor.Children.Add(Panel("Colours",
        [
            ("Foreground Colour", Swatch(c.Foreground)), ("Background Colour", Swatch(c.Background)), ("Trim Colour", Swatch(c.Trim)),
            ("Winter Ball Colour", Swatch(c.WinterBallColour)), ("Winter Ball Start Date", Label(Date(c.WinterBallStart))), ("Winter Ball End Date", Label(Date(c.WinterBallEnd))),
        ]));

        save = new Button { Content = "Save changes", IsEnabled = pending is not null, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = pending is not null };
        save.Click += (_, _) => SaveCompetition(c);
        discard.Click += (_, _) => { competitionDrafts.Remove(c.Address); RenderCompetitionEditor(); };
        competitionEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
    }

    void SaveCompetition(CompetitionRecord c)
    {
        if (!competitionDrafts.TryGetValue(c.Address, out string? text)) return;
        if (!short.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out short value) || value is < 0 or > CompetitionWriter.MaxReputation)
        { status.Text = $"Reputation: enter a whole number 0 to {CompetitionWriter.MaxReputation}"; return; }
        try
        {
            backend.Edits.Execute(() => backend.Session.CompetitionWriter.SetReputation(c, value));
            competitionDrafts.Remove(c.Address);
            if (backend.Session.Competitions.Reload(c.Address) is { } fresh)
            {
                selectedCompetition = fresh;
                competitionRows = competitionRows.Select(r => r.Competition.Address == c.Address ? r with { Competition = fresh, Reputation = fresh.Reputation } : r).ToList();
                FilterCompetitions();
                competitionGrid.SelectedItem = (competitionGrid.ItemsSource as IEnumerable<CompetitionRow>)?.FirstOrDefault(r => r.Competition.Address == c.Address);
            }
            RenderCompetitionEditor();
            status.Text = $"Saved reputation of {c.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
