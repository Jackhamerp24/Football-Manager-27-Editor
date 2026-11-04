using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Awards;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record AwardNavItem
    {
        public string Title => "Awards";
        public override string ToString() => Title;
    }

    public sealed record AwardRow(AwardRecord Award, string Name, int Reputation, int Id);

    readonly AwardNavItem awardNavItem = new();
    readonly Grid awardViewRoot = new() { IsVisible = false };
    readonly TextBox awardSearch = new() { Watermark = "Search award..." };
    readonly DataGrid awardGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 29, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly StackPanel awardEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, Dictionary<AwardField, string>> awardDrafts = [];
    IReadOnlyList<AwardRow> awardRows = [];
    AwardRecord? selectedAward;
    bool awardActive;

    void RegisterAwardView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(awardViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, awardNavItem];

        awardGrid.Columns.Add(new DataGridTextColumn { Header = "Award", Binding = new Binding(nameof(AwardRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        awardGrid.Columns.Add(new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(AwardRow.Reputation)), Width = new DataGridLength(90) });
        awardGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(AwardRow.Id)), Width = new DataGridLength(100) });
        awardSearch.TextChanged += (_, _) => FilterAwards();
        awardGrid.SelectionChanged += (_, _) => { selectedAward = (awardGrid.SelectedItem as AwardRow)?.Award; RenderAwardEditor(); };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = awardSearch };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(awardGrid);
        awardViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(560)) { MinWidth = 360 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 380 },
        };
        Grid.SetColumn(left, 0);
        awardViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        awardViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = awardEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        awardViewRoot.Children.Add(scroll);

        // Registered after Clubs, Staff, Nations, Stadiums, Cities and Competitions: those show the players screen when none is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toAwards = ReferenceEquals(modules.SelectedItem, awardNavItem);
            if (toAwards == awardActive) return;
            awardActive = toAwards;
            awardViewRoot.IsVisible = toAwards;
            if (toAwards) playersSplit.IsVisible = false;
            else if (!clubsActive && !staffActive && !nationActive && !stadiumActive && !cityActive && !competitionActive) playersSplit.IsVisible = true;
            if (toAwards && awardRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadAwardsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            awardRows = [];
            awardDrafts.Clear();
            selectedAward = null;
            awardGrid.ItemsSource = null;
            awardEditor.Children.Clear();
            if (awardActive && backend.State.Status == ConnectionStatus.Connected) await LoadAwardsAsync();
        });
    }

    async Task LoadAwardsAsync()
    {
        status.Text = "Loading awards...";
        try
        {
            var session = backend.Session;
            awardRows = await Task.Run(() => session.Awards.LoadAll()
                .Select(a => new AwardRow(a, a.DisplayName, a.Reputation, a.Id))
                .OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
            FilterAwards();
            status.Text = $"{awardRows.Count:N0} awards";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterAwards()
    {
        string q = Fold(awardSearch.Text ?? "");
        awardGrid.ItemsSource = q.Length == 0 ? awardRows : awardRows.Where(r => Fold(r.Name).Contains(q, StringComparison.Ordinal)).ToList();
    }

    static string AwardLabel(AwardField f) => f switch
    {
        AwardField.Reputation => "Reputation", AwardField.WinnerHomeReputation => "Winner home reputation", _ => "Winner world reputation",
    };

    static string AwardValue(AwardRecord a, AwardField f) => f switch
    {
        AwardField.Reputation => a.Reputation.ToString(Inv), AwardField.WinnerHomeReputation => a.WinnerHomeReputation.ToString(Inv), _ => a.WinnerWorldReputation.ToString(Inv),
    };

    void RenderAwardEditor()
    {
        awardEditor.Children.Clear();
        if (selectedAward is not { } a) return;
        if (!awardDrafts.TryGetValue(a.Address, out var draft)) awardDrafts[a.Address] = draft = [];
        Button? save = null, discard = null;
        void Changed() { if (save is not null) save.IsEnabled = discard!.IsEnabled = draft.Count > 0; }

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Box(AwardField f)
        {
            string original = AwardValue(a, f);
            var box = new TextBox { Text = draft.GetValueOrDefault(f, original), Width = 80, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { string t = box.Text ?? ""; if (t == original) draft.Remove(f); else draft[f] = t; Changed(); };
            return box;
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 3, Margin = new Thickness(12, 8) };
        foreach (var f in Enum.GetValues<AwardField>())
        {
            int r = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Label(AwardLabel(f)); Grid.SetRow(l, r); grid.Children.Add(l);
            var box = Box(f); Grid.SetRow(box, r); Grid.SetColumn(box, 1); grid.Children.Add(box);
        }
        awardEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = a.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold }, Label($"{a.Id.ToString(Inv)}  ·  {a.ShortName}", "#94A3B8") }
        });
        awardEditor.Children.Add(new Border
        {
            Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                        Child = new TextBlock { Text = "AWARD", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } },
                    grid,
                }
            }
        });
        save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        save.Click += (_, _) => SaveAward(a, draft);
        discard.Click += (_, _) => { awardDrafts.Remove(a.Address); RenderAwardEditor(); };
        awardEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
    }

    void SaveAward(AwardRecord a, Dictionary<AwardField, string> draft)
    {
        var values = new Dictionary<AwardField, int>();
        foreach (var (f, text) in draft)
        {
            int max = f == AwardField.Reputation ? AwardWriter.MaxReputation : AwardWriter.MaxWinnerReputation;
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int v) || v < 0 || v > max)
            { status.Text = $"{AwardLabel(f)}: enter a whole number 0 to {max}"; return; }
            values[f] = v;
        }
        try
        {
            var w = backend.Session.AwardWriter;
            int count = draft.Count;
            backend.Edits.Execute(() => { foreach (var (f, v) in values) w.Set(a, f, v); });
            awardDrafts.Remove(a.Address);
            if (backend.Session.Awards.Reload(a.Address) is { } fresh)
            {
                selectedAward = fresh;
                awardRows = awardRows.Select(r => r.Award.Address == a.Address ? r with { Award = fresh, Reputation = fresh.Reputation } : r).ToList();
                FilterAwards();
                awardGrid.SelectedItem = (awardGrid.ItemsSource as IEnumerable<AwardRow>)?.FirstOrDefault(r => r.Award.Address == a.Address);
            }
            RenderAwardEditor();
            status.Text = $"Saved {count} change{(count == 1 ? "" : "s")} to {a.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
