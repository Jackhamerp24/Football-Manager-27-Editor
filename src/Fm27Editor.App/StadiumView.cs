using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Stadiums;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record StadiumNavItem
    {
        public string Title => "Stadiums";
        public override string ToString() => Title;
    }

    public sealed record StadiumRow(StadiumRecord Stadium, string Name, int Id, string Owner, int Capacity);

    /// <summary>Pending stadium edits: field texts, flags, and per-stand (capacity text, roof, seats, tunnel).</summary>
    sealed class StadiumDraft
    {
        public Dictionary<StadiumField, string> Fields { get; } = [];
        public Dictionary<StadiumFlag, bool> Flags { get; } = [];
        public Dictionary<int, (string Capacity, bool Roof, bool Seats, bool Tunnel)> Stands { get; } = [];
        /// <summary>Pending Int. Length / Width: ticked flag and the two texts, or null when unchanged.</summary>
        public (bool On, string Length, string Width)? IntSize { get; set; }
        public int Count => Fields.Count + Flags.Count + Stands.Count + (IntSize is null ? 0 : 1);
    }

    readonly StadiumNavItem stadiumNavItem = new();
    readonly Grid stadiumViewRoot = new() { IsVisible = false };
    readonly TextBox stadiumSearch = new() { Watermark = "Search stadium..." };
    readonly DataGrid stadiumGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 29, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly StackPanel stadiumEditor = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, StadiumDraft> stadiumDrafts = [];
    IReadOnlyList<StadiumRow> stadiumRows = [];
    StadiumRecord? selectedStadium;
    bool stadiumActive;

    void RegisterStadiumView(Grid playersSplit)
    {
        if (playersSplit.Parent is not Grid host) return;
        host.Children.Add(stadiumViewRoot);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, stadiumNavItem];

        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "Stadium", Binding = new Binding(nameof(StadiumRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding(nameof(StadiumRow.Owner)), Width = new DataGridLength(140) });
        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "Capacity", Binding = new Binding(nameof(StadiumRow.Capacity)) { StringFormat = "N0" }, Width = new DataGridLength(96) });
        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(StadiumRow.Id)), Width = new DataGridLength(90) });
        stadiumSearch.TextChanged += (_, _) => FilterStadiums();
        stadiumGrid.SelectionChanged += (_, _) => { selectedStadium = (stadiumGrid.SelectedItem as StadiumRow)?.Stadium; RenderStadiumEditor(); };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border { Background = Brush("#141722"), BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = stadiumSearch };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(stadiumGrid);
        stadiumViewRoot.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(600)) { MinWidth = 420 },
            new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 460 },
        };
        Grid.SetColumn(left, 0);
        stadiumViewRoot.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        stadiumViewRoot.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = stadiumEditor, Background = Brush("#0D0F17"), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 2);
        stadiumViewRoot.Children.Add(scroll);

        // Registered after Clubs, Staff and Nations: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toStadiums = ReferenceEquals(modules.SelectedItem, stadiumNavItem);
            if (toStadiums == stadiumActive) return;
            stadiumActive = toStadiums;
            stadiumViewRoot.IsVisible = toStadiums;
            if (toStadiums) playersSplit.IsVisible = false;
            else if (!clubsActive && !staffActive && !nationActive) playersSplit.IsVisible = true;
            if (toStadiums && stadiumRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadStadiumsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            stadiumRows = [];
            stadiumDrafts.Clear();
            selectedStadium = null;
            stadiumGrid.ItemsSource = null;
            stadiumEditor.Children.Clear();
            if (stadiumActive && backend.State.Status == ConnectionStatus.Connected) await LoadStadiumsAsync();
        });
    }

    async Task LoadStadiumsAsync()
    {
        status.Text = "Loading stadiums...";
        try
        {
            var session = backend.Session;
            stadiumRows = await Task.Run(() =>
            {
                var w = session.StadiumWriter;
                return session.Stadiums.LoadAll()
                    .Select(s => new StadiumRow(s, s.DisplayName, s.Id, s.Owner?.DisplayName ?? "",
                        (int)(w.ReadFields(s.Address).TryGetValue(StadiumField.Capacity, out var c) ? c : 0)))
                    .OrderByDescending(r => r.Capacity).ToList();
            });
            FilterStadiums();
            status.Text = $"{stadiumRows.Count:N0} stadiums";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterStadiums()
    {
        string q = Fold(stadiumSearch.Text ?? "");
        stadiumGrid.ItemsSource = q.Length == 0 ? stadiumRows : stadiumRows.Where(r => Fold(r.Name).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderStadiumEditor()
    {
        stadiumEditor.Children.Clear();
        if (selectedStadium is not { } s) return;
        if (!stadiumDrafts.TryGetValue(s.Address, out var draft)) stadiumDrafts[s.Address] = draft = new StadiumDraft();
        var values = backend.Session.StadiumWriter.ReadFields(s.Address);
        var offsets = backend.Session.Profile.Stadium!;
        Button? save = null, discard = null;
        void Changed() { if (save is not null) save.IsEnabled = discard!.IsEnabled = draft.Count > 0; }

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        TextBox Box(string text, double width, Action<string> changed)
        {
            var box = new TextBox { Text = text, Width = width, MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => changed(box.Text ?? "");
            return box;
        }
        Control IntSizeControl()
        {
            var orig = (On: s.IntLength is not null, Length: s.IntLength is { } l ? (l / 10).ToString(Inv) : "", Width: s.IntWidth is { } wd ? (wd / 10).ToString(Inv) : "");
            var cur = draft.IntSize ?? orig;
            var length = Box(cur.Length, 52, _ => { }); var width = Box(cur.Width, 52, _ => { });
            var on = new CheckBox { IsChecked = cur.On, VerticalAlignment = VerticalAlignment.Center };
            void Update()
            {
                length.IsEnabled = width.IsEnabled = on.IsChecked == true;
                var next = (On: on.IsChecked == true, Length: length.Text ?? "", Width: width.Text ?? "");
                draft.IntSize = next == orig ? null : next;
                Changed();
            }
            length.TextChanged += (_, _) => Update(); width.TextChanged += (_, _) => Update();
            on.IsCheckedChanged += (_, _) =>
            {
                if (on.IsChecked == true && string.IsNullOrWhiteSpace(length.Text)) { length.Text = (values.TryGetValue(StadiumField.PitchLength, out var pl) ? pl : 105).ToString(Inv); width.Text = (values.TryGetValue(StadiumField.PitchWidth, out var pw) ? pw : 68).ToString(Inv); }
                Update();
            };
            length.IsEnabled = width.IsEnabled = cur.On;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { on, length, new TextBlock { Text = "x", VerticalAlignment = VerticalAlignment.Center }, width } };
        }
        Control FieldControl(StadiumField f, long original)
        {
            if (StadiumLabels.Choices.TryGetValue(f, out var choices))
            {
                var codes = choices.Keys.Union([original]).OrderBy(c => c).ToArray();
                var combo = new ComboBox { ItemsSource = codes.Select(c => StadiumLabels.Choice(f, c)).ToArray(), MinWidth = 150 };
                long current = draft.Fields.TryGetValue(f, out var d) ? long.Parse(d) : original;
                combo.SelectedIndex = Array.IndexOf(codes, current);
                combo.SelectionChanged += (_, _) =>
                {
                    long code = codes[combo.SelectedIndex];
                    if (code == original) draft.Fields.Remove(f); else draft.Fields[f] = code.ToString(Inv);
                    Changed();
                };
                return combo;
            }
            string text = StadiumLabels.Format(f, original);
            return Box(draft.Fields.GetValueOrDefault(f, text), StadiumLabels.IsDate(f) ? 100 : 84, t => { if (t == text) draft.Fields.Remove(f); else draft.Fields[f] = t; Changed(); });
        }
        Border Panel(string title, Control body) => new()
        {
            Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 6),
                        Child = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") } },
                    body,
                }
            }
        };
        Grid Rows(IEnumerable<(string Label, Control Value)> items, string columns = "*,Auto")
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), RowSpacing = 3, Margin = new Thickness(12, 8) };
            foreach (var (label, value) in items)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var l = Label(label); Grid.SetRow(l, r); g.Children.Add(l);
                Grid.SetRow(value, r); Grid.SetColumn(value, 1); g.Children.Add(value);
            }
            return g;
        }

        stadiumEditor.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = s.DisplayName, FontSize = 20, FontWeight = FontWeight.Bold },
                Label(string.Join("  ·  ", new[] { s.Id.ToString(Inv), s.Owner?.DisplayName, s.NearbyStadium }.Where(x => !string.IsNullOrEmpty(x))), "#94A3B8"),
            }
        });
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var col = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(col[0], 0); Grid.SetColumn(col[1], 2);
        columns.Children.Add(col[0]); columns.Children.Add(col[1]);

        var stadiumItems = StadiumLabels.Groups[0].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))).ToList();
        int afterOwner = stadiumItems.FindIndex(i => i.Label == "Owner Type") + 1;
        stadiumItems.Insert(afterOwner, ("Seat Colour", new Border { Width = 70, Height = 16, Background = new SolidColorBrush(Color.FromUInt32(s.SeatColour)), BorderBrush = Brush("#3A3F55"), BorderThickness = new Thickness(1) }));
        stadiumItems.Add(("Latitude / Longitude", Label($"{s.Latitude.ToString("0.000000", Inv)}  {s.Longitude.ToString("0.000000", Inv)}")));
        foreach (var (flag, flagLabel) in StadiumLabels.Flags)
        {
            bool original = StadiumLabels.FlagOn(s, offsets, flag);
            var cb = new CheckBox { IsChecked = draft.Flags.GetValueOrDefault(flag, original) };
            cb.IsCheckedChanged += (_, _) => { bool on = cb.IsChecked == true; if (on == original) draft.Flags.Remove(flag); else draft.Flags[flag] = on; Changed(); };
            stadiumItems.Add((flagLabel, cb));
        }
        col[0].Children.Add(Panel("Stadium", Rows(stadiumItems)));
        col[0].Children.Add(Panel(StadiumLabels.Groups[1].Title, Rows(StadiumLabels.Groups[1].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))))));
        var pitchItems = StadiumLabels.Groups[2].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))).ToList();
        pitchItems.Insert(pitchItems.FindIndex(i => i.Label == "Pitch Width (m)") + 1, ("Int. Length / Width (m)", IntSizeControl()));
        col[1].Children.Add(Panel(StadiumLabels.Groups[2].Title, Rows(pitchItems)));

        var stands = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), RowSpacing = 3, ColumnSpacing = 10, Margin = new Thickness(12, 8) };
        void Cell(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); stands.Children.Add(c); }
        stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = ["", "Capacity", "Roof", "Seats", "Tunnel"];
        for (int c = 1; c < heads.Length; c++) Cell(Label(heads[c], "#94A3B8"), 0, c);
        foreach (var t in s.Stands)
        {
            int row = stands.RowDefinitions.Count; stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cur = draft.Stands.TryGetValue(t.Index, out var d) ? d : (t.Capacity.ToString(Inv), t.HasRoof, t.HasSeats, t.HasTunnel);
            var original = (t.Capacity.ToString(Inv), t.HasRoof, t.HasSeats, t.HasTunnel);
            void Update((string, bool, bool, bool) next) { if (next == original) draft.Stands.Remove(t.Index); else draft.Stands[t.Index] = next; Changed(); }
            var cap = Box(cur.Item1, 84, x => { cur.Item1 = x; Update(cur); });
            var roof = new CheckBox { IsChecked = cur.Item2 }; roof.IsCheckedChanged += (_, _) => { cur.Item2 = roof.IsChecked == true; Update(cur); };
            var seats = new CheckBox { IsChecked = cur.Item3 }; seats.IsCheckedChanged += (_, _) => { cur.Item3 = seats.IsChecked == true; Update(cur); };
            var tunnel = new CheckBox { IsChecked = cur.Item4 }; tunnel.IsCheckedChanged += (_, _) => { cur.Item4 = tunnel.IsChecked == true; Update(cur); };
            Cell(Label(StadiumLabels.Stands[t.Index]), row, 0); Cell(cap, row, 1); Cell(roof, row, 2); Cell(seats, row, 3); Cell(tunnel, row, 4);
        }
        int total = stands.RowDefinitions.Count; stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(Label("Total", "#94A3B8"), total, 0); Cell(Label(s.StandsTotal.ToString("N0", Inv), "#F1F5F9"), total, 1);
        col[1].Children.Add(Panel("Stands", stands));
        stadiumEditor.Children.Add(columns);

        save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        save.Click += (_, _) => SaveStadium(s, draft, values);
        discard.Click += (_, _) => { stadiumDrafts.Remove(s.Address); RenderStadiumEditor(); };
        stadiumEditor.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
    }

    void SaveStadium(StadiumRecord s, StadiumDraft draft, IReadOnlyDictionary<StadiumField, long> snapshot)
    {
        var fields = new Dictionary<StadiumField, long>();
        foreach (var (f, text) in draft.Fields)
        {
            if (!StadiumLabels.TryParse(f, text, out long v, out var error)) { status.Text = error; return; }
            fields[f] = v;
        }
        var stands = new Dictionary<int, (int, bool, bool, bool)>();
        foreach (var (i, (cap, roof, seats, tunnel)) in draft.Stands)
        {
            if (!int.TryParse(cap.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int c) || c is < 0 or > 200_000)
            { status.Text = $"{StadiumLabels.Stands[i]}: enter a capacity 0–200000"; return; }
            stands[i] = (c, roof, seats, tunnel);
        }
        (bool On, int Length, int Width)? intSize = null;
        if (draft.IntSize is { } pendingSize)
        {
            if (!pendingSize.On) intSize = (false, 0, 0);
            else if (int.TryParse(pendingSize.Length.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int il) && int.TryParse(pendingSize.Width.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int iw)
                && il is >= 80 and <= 130 && iw is >= 40 and <= 100) intSize = (true, il, iw);
            else { status.Text = "Int. Length / Width: enter 80–130 and 40–100"; return; }
        }
        try
        {
            var w = backend.Session.StadiumWriter;
            int count = draft.Count;
            backend.Edits.Execute(() =>
            {
                foreach (var (f, v) in fields) w.SetField(s.Address, f, snapshot[f], v);
                var current = s;
                foreach (var (flag, on) in draft.Flags) { w.SetFlag(current, flag, on); current = backend.Session.Stadiums.Reload(s.Address)!; }
                if (intSize is { } requested) { w.SetInternationalSize(current, requested.On ? (requested.Length, requested.Width) : null); current = backend.Session.Stadiums.Reload(s.Address)!; }
                foreach (var (i, (c, roof, seats, tunnel)) in stands) w.SetStand(current, i, c, roof, seats, tunnel);
            });
            stadiumDrafts.Remove(s.Address);
            if (backend.Session.Stadiums.Reload(s.Address) is { } fresh)
            {
                selectedStadium = fresh;
                stadiumRows = stadiumRows.Select(r => r.Stadium.Address == s.Address ? r with { Stadium = fresh } : r).ToList();
            }
            RenderStadiumEditor();
            status.Text = $"Saved {count} change{(count == 1 ? "" : "s")} to {s.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
