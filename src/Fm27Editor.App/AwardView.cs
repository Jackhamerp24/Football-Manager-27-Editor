using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
    readonly TextBox awardSearch = new() { Watermark = "Search award or ID..." };
    readonly DataGrid awardGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 28, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly ContentControl awardHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly StackPanel awardEditor = new() { Spacing = 12, Margin = new Thickness(12, 0, 12, 12) };
    readonly Dictionary<ulong, Dictionary<AwardField, string>> awardDrafts = [];
    IReadOnlyList<AwardRow> awardRows = [];
    AwardRecord? selectedAward;
    Button? awardSaveButton;
    bool awardActive;
    Control awardDetailRoot = new Panel();

    void OpenSelectedAward()
    {
        selectedAward = (awardGrid.SelectedItem as AwardRow)?.Award;
        if (selectedAward is null) return;
        RenderAwardEditor();
        OpenKindTab("award", selectedAward.Name ?? selectedAward.Address.ToString("X"), awardDetailRoot);
    }

    void RegisterAwardView()
    {
        AddModuleTab("Awards", awardViewRoot, awardNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, awardNavItem];

        awardGrid.Columns.Add(new DataGridTextColumn { Header = "Award", Binding = new Binding(nameof(AwardRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var repCol = new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(AwardRow.Reputation)), Width = new DataGridLength(100) };
        repCol.CellStyleClasses.Add("numeric");
        awardGrid.Columns.Add(repCol);
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(AwardRow.Id)), Width = new DataGridLength(100) };
        idCol.CellStyleClasses.Add("numeric");
        awardGrid.Columns.Add(idCol);

        awardSearch.TextChanged += (_, _) => FilterAwards();
        awardGrid.SelectionChanged += (_, _) => selectedAward = (awardGrid.SelectedItem as AwardRow)?.Award;
        awardGrid.DoubleTapped += (_, _) => OpenSelectedAward();
        awardGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedAward(); e.Handled = true; }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = awardSearch,
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(awardGrid);
        awardViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = awardEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(awardHeaderHost, Dock.Top);
        detailDock.Children.Add(awardHeaderHost);
        detailDock.Children.Add(scroll);
        awardDetailRoot = detailDock;

        // Registered after Clubs, Staff, Nations, Stadiums, Cities and Competitions: those show the players screen when none is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toAwards = ReferenceEquals(modules.SelectedItem, awardNavItem);
            if (toAwards == awardActive) return;
            awardActive = toAwards;
            if (toAwards && awardRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadAwardsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            awardRows = [];
            awardDrafts.Clear();
            selectedAward = null;
            awardSaveButton = null;
            awardGrid.ItemsSource = null;
            awardHeaderHost.Content = null;
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
        string q = Fold((awardSearch.Text ?? "").Trim());
        awardGrid.ItemsSource = q.Length == 0
            ? awardRows
            : awardRows.Where(r =>
                Fold(r.Name).Contains(q, StringComparison.Ordinal) ||
                r.Id.ToString(Inv).Contains(q, StringComparison.Ordinal)).ToList();
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
        awardHeaderHost.Content = null;
        awardEditor.Children.Clear();
        if (selectedAward is not { } a) { awardSaveButton = null; return; }
        if (!awardDrafts.TryGetValue(a.Address, out var draft)) awardDrafts[a.Address] = draft = [];
        var save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        awardSaveButton = save;
        void Changed() => save.IsEnabled = discard.IsEnabled = draft.Count > 0;
        save.Click += (_, _) => SaveAward(a, draft);
        discard.Click += (_, _) => { awardDrafts.Remove(a.Address); RenderAwardEditor(); };

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 11.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Box(AwardField f)
        {
            string original = AwardValue(a, f);
            var box = new TextBox { Text = draft.GetValueOrDefault(f, original), Width = 96, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { string t = box.Text ?? ""; if (t == original) draft.Remove(f); else draft[f] = t; Changed(); };
            return box;
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

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {a.Id.ToString(Inv)}"));
        if (!string.IsNullOrEmpty(a.ShortName) && !string.Equals(a.ShortName, a.DisplayName, StringComparison.Ordinal))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(a.ShortName, highlight: true));
        }

        var repText = new TextBlock
        {
            Text = a.Reputation.ToString(Inv),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#C084FC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("REP", repText) },
        };
        var awardImg = graphics.Image("award", a.Id, 72);
        awardHeaderHost.Content = UiKit.EntityBanner(a.DisplayName, badges, headerActions, awardImg);

        var idCard = CardRows("Identification", [
            ("Unique ID", Label(a.Id.ToString(Inv), "#F1F5F9")),
            ("Full Name", Label(a.Name ?? a.DisplayName, "#F1F5F9")),
            ("Short Name", Label(a.ShortName ?? "", "#F1F5F9")),
        ]);
        var repCard = CardRows("Reputation", Enum.GetValues<AwardField>().Select(f => (AwardLabel(f), Box(f))));

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
        };
        Grid.SetColumn(idCard, 0);
        Grid.SetColumn(repCard, 1);
        columns.Children.Add(idCard);
        columns.Children.Add(repCard);
        awardEditor.Children.Add(columns);
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
