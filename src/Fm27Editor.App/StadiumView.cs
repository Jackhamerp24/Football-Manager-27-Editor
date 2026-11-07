using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
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
        /// <summary>Pending Seat Colour as #AARRGGBB, or null when unchanged.</summary>
        public string? SeatColour { get; set; }
        public int Count => Fields.Count + Flags.Count + Stands.Count + (IntSize is null ? 0 : 1) + (SeatColour is null ? 0 : 1);
    }

    readonly StadiumNavItem stadiumNavItem = new();
    readonly Grid stadiumViewRoot = new() { IsVisible = false };
    readonly TextBox stadiumSearch = new() { Watermark = "Search stadium, owner or ID..." };
    readonly DataGrid stadiumGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 28, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly ContentControl stadiumHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly StackPanel stadiumEditor = new() { Spacing = 12, Margin = new Thickness(12, 0, 12, 12) };
    readonly Dictionary<ulong, StadiumDraft> stadiumDrafts = [];
    IReadOnlyList<StadiumRow> stadiumRows = [];
    StadiumRecord? selectedStadium;
    Button? stadiumSaveButton;
    bool stadiumActive;
    Control stadiumDetailRoot = new Panel();

    void OpenSelectedStadium()
    {
        selectedStadium = (stadiumGrid.SelectedItem as StadiumRow)?.Stadium;
        if (selectedStadium is null) return;
        RenderStadiumEditor();
        OpenKindTab("stadium", selectedStadium.Name ?? selectedStadium.Address.ToString("X"), stadiumDetailRoot);
    }

    void RegisterStadiumView()
    {
        AddModuleTab("Stadiums", stadiumViewRoot, stadiumNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, stadiumNavItem];

        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "Stadium", Binding = new Binding(nameof(StadiumRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        stadiumGrid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding(nameof(StadiumRow.Owner)), Width = new DataGridLength(160) });
        var capCol = new DataGridTextColumn { Header = "Capacity", Binding = new Binding(nameof(StadiumRow.Capacity)) { StringFormat = "N0" }, Width = new DataGridLength(100) };
        capCol.CellStyleClasses.Add("numeric");
        stadiumGrid.Columns.Add(capCol);
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(StadiumRow.Id)), Width = new DataGridLength(96) };
        idCol.CellStyleClasses.Add("numeric");
        stadiumGrid.Columns.Add(idCol);

        stadiumSearch.TextChanged += (_, _) => FilterStadiums();
        stadiumGrid.SelectionChanged += (_, _) => selectedStadium = (stadiumGrid.SelectedItem as StadiumRow)?.Stadium;
        stadiumGrid.DoubleTapped += (_, _) => OpenSelectedStadium();
        stadiumGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedStadium(); e.Handled = true; }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = stadiumSearch,
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(stadiumGrid);
        stadiumViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = stadiumEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(stadiumHeaderHost, Dock.Top);
        detailDock.Children.Add(stadiumHeaderHost);
        detailDock.Children.Add(scroll);
        stadiumDetailRoot = detailDock;

        // Registered after Clubs, Staff and Nations: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toStadiums = ReferenceEquals(modules.SelectedItem, stadiumNavItem);
            if (toStadiums == stadiumActive) return;
            stadiumActive = toStadiums;
            if (toStadiums && stadiumRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadStadiumsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            stadiumRows = [];
            stadiumDrafts.Clear();
            selectedStadium = null;
            stadiumSaveButton = null;
            stadiumGrid.ItemsSource = null;
            stadiumHeaderHost.Content = null;
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
        string q = Fold((stadiumSearch.Text ?? "").Trim());
        stadiumGrid.ItemsSource = q.Length == 0
            ? stadiumRows
            : stadiumRows.Where(r =>
                Fold(r.Name).Contains(q, StringComparison.Ordinal) ||
                Fold(r.Owner).Contains(q, StringComparison.Ordinal) ||
                r.Id.ToString(Inv).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderStadiumEditor()
    {
        stadiumHeaderHost.Content = null;
        stadiumEditor.Children.Clear();
        if (selectedStadium is not { } s) { stadiumSaveButton = null; return; }
        if (!stadiumDrafts.TryGetValue(s.Address, out var draft)) stadiumDrafts[s.Address] = draft = new StadiumDraft();
        var values = backend.Session.StadiumWriter.ReadFields(s.Address);
        var offsets = backend.Session.Profile.Stadium!;
        var save = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        stadiumSaveButton = save;
        void Changed() => save.IsEnabled = discard.IsEnabled = draft.Count > 0;
        save.Click += (_, _) => SaveStadium(s, draft, values);
        discard.Click += (_, _) => { stadiumDrafts.Remove(s.Address); RenderStadiumEditor(); };

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 11.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        TextBox Box(string text, double width, Action<string> changed, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            var box = new TextBox { Text = text, Width = width, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = align, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => changed(box.Text ?? "");
            return box;
        }
        Control SeatColourControl(StadiumRecord stadium, StadiumDraft stDraft)
        {
            string original = $"#{stadium.SeatColour:X8}";
            var box = Box(stDraft.SeatColour ?? original, 106, text =>
            {
                string value = text.Trim();
                stDraft.SeatColour = value == original ? null : value;
                Changed();
            }, HorizontalAlignment.Left);
            return ColourInput(box);
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
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { on, length, new TextBlock { Text = "×", Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center }, width } };
        }
        Control FieldControl(StadiumField f, long original)
        {
            if (StadiumLabels.Choices.TryGetValue(f, out var choices))
            {
                var codes = choices.Keys.Union([original]).OrderBy(c => c).ToArray();
                var combo = new ComboBox { ItemsSource = codes.Select(c => StadiumLabels.Choice(f, c)).ToArray(), MinWidth = 156 };
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
            return Box(draft.Fields.GetValueOrDefault(f, text), StadiumLabels.IsDate(f) ? 104 : 88, t => { if (t == text) draft.Fields.Remove(f); else draft.Fields[f] = t; Changed(); });
        }

        StackPanel Rows(IEnumerable<(string Label, Control Value)> items)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var (label, value) in items)
            {
                var line = new DockPanel { MinHeight = 25, LastChildFill = true };
                DockPanel.SetDock(value, Dock.Right);
                line.Children.Add(value);
                line.Children.Add(Label(label));
                stack.Children.Add(new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 2, 10, 2),
                    Child = line,
                });
            }
            return stack;
        }

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {s.Id.ToString(Inv)}"));
        if (s.Owner is { } owner && !string.IsNullOrEmpty(owner.DisplayName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var ownerLink = UiKit.MetaLinkButton(owner.DisplayName, highlight: true);
            ownerLink.Click += async (_, _) => await OpenPlayerClubAsync(owner.Address);
            badges.Children.Add(ownerLink);
        }
        if (!string.IsNullOrEmpty(s.NearbyStadium))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(s.NearbyStadium));
        }

        long capVal = values.TryGetValue(StadiumField.Capacity, out var cv) ? cv : s.StandsTotal;
        var capText = new TextBlock
        {
            Text = capVal.ToString("N0", Inv),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#38BDF8"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("CAP", capText) },
        };
        var stadiumImg = graphics.Image("stadium", s.Id, 72);
        stadiumHeaderHost.Content = UiKit.EntityBanner(s.DisplayName, badges, headerActions, stadiumImg);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var col = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(col[0], 0); Grid.SetColumn(col[1], 2);
        columns.Children.Add(col[0]); columns.Children.Add(col[1]);

        var stadiumItems = StadiumLabels.Groups[0].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))).ToList();
        int afterOwner = stadiumItems.FindIndex(i => i.Label == "Owner Type") + 1;
        stadiumItems.Insert(afterOwner, ("Seat Colour", SeatColourControl(s, draft)));
        stadiumItems.Add(("Latitude / Longitude", Label($"{s.Latitude.ToString("0.000000", Inv)}, {s.Longitude.ToString("0.000000", Inv)}", "#F1F5F9")));
        foreach (var (flag, flagLabel) in StadiumLabels.Flags)
        {
            bool original = StadiumLabels.FlagOn(s, offsets, flag);
            var cb = new CheckBox { IsChecked = draft.Flags.GetValueOrDefault(flag, original) };
            cb.IsCheckedChanged += (_, _) => { bool on = cb.IsChecked == true; if (on == original) draft.Flags.Remove(flag); else draft.Flags[flag] = on; Changed(); };
            stadiumItems.Add((flagLabel, cb));
        }
        col[0].Children.Add(UiKit.Card("Stadium", Rows(stadiumItems)));
        col[0].Children.Add(UiKit.Card(StadiumLabels.Groups[1].Title, Rows(StadiumLabels.Groups[1].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))))));
        var pitchItems = StadiumLabels.Groups[2].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))).ToList();
        pitchItems.Insert(pitchItems.FindIndex(i => i.Label == "Pitch Width (m)") + 1, ("Int. Length / Width (m)", IntSizeControl()));
        col[1].Children.Add(UiKit.Card(StadiumLabels.Groups[2].Title, Rows(pitchItems)));

        var stands = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), RowSpacing = 4, ColumnSpacing = 10, Margin = new Thickness(12, 8) };
        void Cell(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); stands.Children.Add(c); }
        stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = ["Stand", "Capacity", "Roof", "Seats", "Tunnel"];
        for (int c = 0; c < heads.Length; c++)
        {
            var headLabel = Label(heads[c], "#94A3B8");
            headLabel.FontWeight = FontWeight.SemiBold;
            if (c == 1) headLabel.HorizontalAlignment = HorizontalAlignment.Right;
            else if (c >= 2) headLabel.HorizontalAlignment = HorizontalAlignment.Center;
            Cell(headLabel, 0, c);
        }
        foreach (var t in s.Stands)
        {
            int row = stands.RowDefinitions.Count; stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cur = draft.Stands.TryGetValue(t.Index, out var d) ? d : (t.Capacity.ToString(Inv), t.HasRoof, t.HasSeats, t.HasTunnel);
            var original = (t.Capacity.ToString(Inv), t.HasRoof, t.HasSeats, t.HasTunnel);
            void Update((string, bool, bool, bool) next) { if (next == original) draft.Stands.Remove(t.Index); else draft.Stands[t.Index] = next; Changed(); }
            var cap = Box(cur.Item1, 88, x => { cur.Item1 = x; Update(cur); });
            var roof = new CheckBox { IsChecked = cur.Item2, HorizontalAlignment = HorizontalAlignment.Center }; roof.IsCheckedChanged += (_, _) => { cur.Item2 = roof.IsChecked == true; Update(cur); };
            var seats = new CheckBox { IsChecked = cur.Item3, HorizontalAlignment = HorizontalAlignment.Center }; seats.IsCheckedChanged += (_, _) => { cur.Item3 = seats.IsChecked == true; Update(cur); };
            var tunnel = new CheckBox { IsChecked = cur.Item4, HorizontalAlignment = HorizontalAlignment.Center }; tunnel.IsCheckedChanged += (_, _) => { cur.Item4 = tunnel.IsChecked == true; Update(cur); };
            Cell(Label(StadiumLabels.Stands[t.Index]), row, 0); Cell(cap, row, 1); Cell(roof, row, 2); Cell(seats, row, 3); Cell(tunnel, row, 4);
        }
        int total = stands.RowDefinitions.Count; stands.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var totalValue = Label(s.StandsTotal.ToString("N0", Inv), "#F8FAFC");
        totalValue.FontWeight = FontWeight.Bold;
        totalValue.HorizontalAlignment = HorizontalAlignment.Right;
        var totalLabel = Label("Total", "#94A3B8");
        totalLabel.FontWeight = FontWeight.SemiBold;
        Cell(totalLabel, total, 0); Cell(totalValue, total, 1);
        col[1].Children.Add(UiKit.Card("Stands", stands));
        stadiumEditor.Children.Add(columns);
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
            uint? seatColour = null;
            if (draft.SeatColour is { } colourText)
            {
                if (!uint.TryParse(colourText.Trim().TrimStart('#'), System.Globalization.NumberStyles.HexNumber, Inv, out uint argb))
                { status.Text = "Seat Colour: enter #AARRGGBB"; return; }
                seatColour = argb;
            }
            backend.Edits.Execute(() =>
            {
                foreach (var (f, v) in fields) w.SetField(s.Address, f, snapshot[f], v);
                if (seatColour is { } colour) w.SetSeatColour(s, colour);
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
