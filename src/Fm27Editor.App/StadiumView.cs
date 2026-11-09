using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Stadiums;

namespace Fm27Editor.App;

internal sealed record StadiumRow(StadiumRecord Stadium, string Name, int Id, string Owner, int Capacity);

/// <summary>Pending stadium edits: field texts, flags, and per-stand (capacity text, roof, seats, tunnel).</summary>
internal sealed class StadiumDraft
{
    public Dictionary<StadiumField, string> Fields { get; } = [];
    public Dictionary<StadiumFlag, bool> Flags { get; } = [];
    public Dictionary<int, (string Capacity, bool Roof, bool Seats, bool Tunnel)> Stands { get; } = [];
    /// <summary>Pending Int. Length / Width: ticked flag and the two texts, or null when unchanged.</summary>
    public (bool On, string Length, string Width)? IntSize { get; set; }
    /// <summary>Pending Seat Colour as #AARRGGBB, or null when unchanged.</summary>
    public string? SeatColour { get; set; }
    /// <summary>Pending Owner (a club) and Location (a city), or null when unchanged.</summary>
    public Fm27Editor.Core.Clubs.ClubEntry? Owner { get; set; }
    public Fm27Editor.Core.Players.CityRecord? Location { get; set; }
    public StadiumRecord? Nearby { get; set; }
    /// <summary>Pending Latitude / Longitude texts, or null when unchanged.</summary>
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public int Count => (Latitude is null ? 0 : 1) + (Longitude is null ? 0 : 1) + Fields.Count + Flags.Count + Stands.Count + (IntSize is null ? 0 : 1) + (SeatColour is null ? 0 : 1) + (Owner is null ? 0 : 1) + (Location is null ? 0 : 1) + (Nearby is null ? 0 : 1);
}

/// <summary>Stadiums: information, pitch, and the eight stands.</summary>
internal sealed class StadiumScreen(IScreenHost host)
    : EntityScreen<StadiumRecord, StadiumRow, StadiumDraft>(host, "stadium", "Stadiums", "stadiums", "Search stadium, owner or ID...")
{
    string sectionKey = "Information";
    IReadOnlyDictionary<StadiumField, long> snapshot = new Dictionary<StadiumField, long>();

    protected override bool HasRail => true;

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Stadium", Binding = new Binding(nameof(StadiumRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding(nameof(StadiumRow.Owner)), Width = new DataGridLength(184) });
        var capacity = new DataGridTextColumn { Header = "Capacity", Binding = new Binding(nameof(StadiumRow.Capacity)) { StringFormat = "N0" }, Width = new DataGridLength(115) };
        capacity.CellStyleClasses.Add("numeric");
        grid.Columns.Add(capacity);
        var id = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(StadiumRow.Id)), Width = new DataGridLength(110) };
        id.CellStyleClasses.Add("numeric");
        grid.Columns.Add(id);
    }

    protected override IReadOnlyList<StadiumRow> LoadRows(GameSession session)
    {
        var writer = session.StadiumWriter;
        return session.Stadiums.LoadAll()
            .Select(s => new StadiumRow(s, s.DisplayName, s.Id, s.Owner?.DisplayName ?? "",
                (int)(writer.ReadFields(s.Address).TryGetValue(StadiumField.Capacity, out var c) ? c : 0)))
            .OrderByDescending(r => r.Capacity).ToList();
    }

    protected override bool Matches(StadiumRow row, string q) =>
        Fold(row.Name).Contains(q, StringComparison.Ordinal) || Fold(row.Owner).Contains(q, StringComparison.Ordinal)
        || row.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);

    protected override StadiumRecord RecordOf(StadiumRow row) => row.Stadium;
    protected override ulong AddressOf(StadiumRecord record) => record.Address;
    protected override string NameOf(StadiumRecord record) => record.Name ?? record.Address.ToString("X");
    protected override Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(StadiumRecord record) =>
        Fm27Editor.Core.History.EntityChangeLabels.ForStadium(Host.Backend.Session.Memory, Host.Backend.Session.Profile, record.Address);
    protected override StadiumDraft NewDraft() => new();
    protected override bool HasChanges(StadiumRecord record, StadiumDraft draft) => draft.Count > 0;
    protected override StadiumRecord? Reload(StadiumRecord record) => Host.Backend.Session.Stadiums.Reload(record.Address);
    protected override StadiumRow Refresh(StadiumRow row, StadiumRecord fresh) => row with { Stadium = fresh };

    static StackPanel ItemRows(IEnumerable<(string Label, Control Value)> items)
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

    static TextBox TextInput(string text, double width, Action<string> changed, HorizontalAlignment align = HorizontalAlignment.Right, bool date = false)
    {
        var box = date
            ? new DateBox { Text = text, Width = width, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = align, VerticalContentAlignment = VerticalAlignment.Center }
            : new TextBox { Text = text, Width = width, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = align, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => changed(box.Text ?? "");
        return box;
    }

    protected override void BuildEditor(StadiumRecord s, StadiumDraft draft, Action changed)
    {
        var values = Host.Backend.Session.StadiumWriter.ReadFields(s.Address);
        snapshot = values;
        var offsets = Host.Backend.Session.Profile.Stadium!;

        Control SeatColourControl()
        {
            string original = $"#{s.SeatColour:X8}";
            var box = TextInput(draft.SeatColour ?? original, 106, text =>
            {
                string value = text.Trim();
                draft.SeatColour = value == original ? null : value;
                changed();
            }, HorizontalAlignment.Left);
            return ColourInputControl.Create(box);
        }

        Control IntSizeControl()
        {
            var orig = (On: s.IntLength is not null, Length: s.IntLength is { } l ? (l / 10).ToString(Inv) : "", Width: s.IntWidth is { } wd ? (wd / 10).ToString(Inv) : "");
            var cur = draft.IntSize ?? orig;
            var length = TextInput(cur.Length, 52, _ => { });
            var width = TextInput(cur.Width, 52, _ => { });
            var on = new CheckBox { IsChecked = cur.On, VerticalAlignment = VerticalAlignment.Center };
            void Update()
            {
                length.IsEnabled = width.IsEnabled = on.IsChecked == true;
                var next = (On: on.IsChecked == true, Length: length.Text ?? "", Width: width.Text ?? "");
                draft.IntSize = next == orig ? null : next;
                changed();
            }
            length.TextChanged += (_, _) => Update();
            width.TextChanged += (_, _) => Update();
            on.IsCheckedChanged += (_, _) =>
            {
                if (on.IsChecked == true && string.IsNullOrWhiteSpace(length.Text))
                {
                    length.Text = (values.TryGetValue(StadiumField.PitchLength, out var pl) ? pl : 105).ToString(Inv);
                    width.Text = (values.TryGetValue(StadiumField.PitchWidth, out var pw) ? pw : 68).ToString(Inv);
                }
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
                    changed();
                };
                return combo;
            }
            string text = StadiumLabels.Format(f, original);
            return TextInput(draft.Fields.GetValueOrDefault(f, text), StadiumLabels.IsDate(f) ? 130 : 88, t => { if (t == text) draft.Fields.Remove(f); else draft.Fields[f] = t; changed(); }, date: StadiumLabels.IsDate(f));
        }

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {s.Id.ToString(Inv)}"));
        if (s.Owner is { } owner && !string.IsNullOrEmpty(owner.DisplayName))
        {
            badges.Children.Add(UiKit.MetaDot());
            var ownerLink = UiKit.MetaLinkButton(owner.DisplayName, highlight: true);
            ownerLink.Click += async (_, _) => await Host.OpenClubAsync(owner.Address);
            badges.Children.Add(ownerLink);
        }

        long capVal = values.TryGetValue(StadiumField.Capacity, out var cv) ? cv : s.StandsTotal;
        var capText = new TextBlock
        {
            Text = capVal.ToString("N0", Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#38BDF8"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
        };
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("CAP", capText) },
        };
        HeaderHost.Content = UiKit.EntityBanner(s.DisplayName, badges, headerActions, Host.Graphics.Image("stadium", s.Id, 72));

        // Section 1: Information
        var stadiumItems = new List<(string Label, Control Value)>
        {
            ("Unique ID", Label(s.Id.ToString(Inv), "#F1F5F9")),
            ("Name", Label(s.DisplayName, "#F1F5F9")),
        };
        if (values.ContainsKey(StadiumField.Environment))
            stadiumItems.Add(("Environment", FieldControl(StadiumField.Environment, values[StadiumField.Environment])));
        var stadiums = Host.Backend.Session.Stadiums;
        ulong locationNow = stadiums.LocationPointer(s.Address);
        stadiumItems.Add(("Location", PickerRow(draft.Location?.Name ?? stadiums.LocationOf(s.Address)?.Name ?? "", locationNow != 0 && draft.Location is null ? () => Host.OpenCity(locationNow) : null, draft.Location is not null, async () =>
        {
            var city = await ChooseCityAsync();
            if (city is null) return;
            draft.Location = city.Address == stadiums.LocationPointer(s.Address) ? null : city;
            changed(); Reopen();
        })));
        stadiumItems.Add(("Owner", PickerRow(draft.Owner?.DisplayName ?? s.Owner?.DisplayName ?? "", s.Owner is { } stadiumOwner && draft.Owner is null ? () => _ = Host.OpenClubAsync(stadiumOwner.Address) : null, draft.Owner is not null, async () =>
        {
            var club = await ChooseClubAsync();
            if (club is null) return;
            draft.Owner = club.Address == (s.Owner?.Address ?? 0) ? null : club;
            changed(); Reopen();
        })));
        if (values.ContainsKey(StadiumField.OwnerType))
            stadiumItems.Add(("Owner Type", FieldControl(StadiumField.OwnerType, values[StadiumField.OwnerType])));
        ulong nearbyNow = stadiums.NearbyPointer(s.Address);
        stadiumItems.Add(("Nearby Stadium", PickerRow(draft.Nearby?.DisplayName ?? s.NearbyStadium ?? "", nearbyNow != 0 && draft.Nearby is null ? () => Host.OpenStadium(nearbyNow) : null, draft.Nearby is not null, async () =>
        {
            var other = await ChooseStadiumAsync(s);
            if (other is null) return;
            draft.Nearby = other.Address == nearbyNow ? null : other;
            changed(); Reopen();
        })));
        stadiumItems.Add(("Seat Colour", SeatColourControl()));
        if (values.ContainsKey(StadiumField.Decay))
            stadiumItems.Add(("Decay", FieldControl(StadiumField.Decay, values[StadiumField.Decay])));
        if (values.ContainsKey(StadiumField.BuildDate))
            stadiumItems.Add(("Build Date", FieldControl(StadiumField.BuildDate, values[StadiumField.BuildDate])));
        if (values.ContainsKey(StadiumField.RebuildDate))
            stadiumItems.Add(("Re-Build Date", FieldControl(StadiumField.RebuildDate, values[StadiumField.RebuildDate])));
        TextBox CoordinateBox(string original, Func<string?> get, Action<string?> set)
        {
            var box = new TextBox { Text = get() ?? original, Width = 110, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { string t = box.Text ?? ""; set(t == original ? null : t); changed(); };
            return box;
        }
        stadiumItems.Add(("Latitude / Longitude", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                CoordinateBox(s.Latitude.ToString("0.000000", Inv), () => draft.Latitude, v => draft.Latitude = v),
                CoordinateBox(s.Longitude.ToString("0.000000", Inv), () => draft.Longitude, v => draft.Longitude = v),
            },
        }));
        foreach (var (flag, flagLabel) in StadiumLabels.Flags)
        {
            bool original = StadiumLabels.FlagOn(s, offsets, flag);
            var box = new CheckBox { IsChecked = draft.Flags.GetValueOrDefault(flag, original) };
            box.IsCheckedChanged += (_, _) => { bool on = box.IsChecked == true; if (on == original) draft.Flags.Remove(flag); else draft.Flags[flag] = on; changed(); };
            stadiumItems.Add((flagLabel, box));
        }

        var pitchItems = StadiumLabels.Groups[2].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))).ToList();
        pitchItems.Insert(pitchItems.FindIndex(i => i.Label == "Pitch Width (m)") + 1, ("Int. Length / Width (m)", IntSizeControl()));

        var informationPanel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var columns = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(columns[0], 0);
        Grid.SetColumn(columns[1], 2);
        informationPanel.Children.Add(columns[0]);
        informationPanel.Children.Add(columns[1]);
        columns[0].Children.Add(UiKit.Card("Stadium", ItemRows(stadiumItems)));
        columns[1].Children.Add(UiKit.Card(StadiumLabels.Groups[1].Title, ItemRows(StadiumLabels.Groups[1].Items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, FieldControl(i.Field, values[i.Field]))))));
        columns[1].Children.Add(UiKit.Card(StadiumLabels.Groups[2].Title, ItemRows(pitchItems)));

        // Section 2: Stadium Details
        var detailsCard = UiKit.Card("Stadium Details", ItemRows([
            ("Capacity", FieldControl(StadiumField.Capacity, values.TryGetValue(StadiumField.Capacity, out var capacity) ? capacity : s.StandsTotal)),
        ]));

        Control StandCard(int index, bool horizontal = false)
        {
            var stand = s.Stands.FirstOrDefault(x => x.Index == index) ?? new StadiumStand(index, 0, false, false, false, 0);
            var current = draft.Stands.TryGetValue(index, out var d) ? d : (stand.Capacity.ToString(Inv), stand.HasRoof, stand.HasSeats, stand.HasTunnel);
            var original = (stand.Capacity.ToString(Inv), stand.HasRoof, stand.HasSeats, stand.HasTunnel);
            void Update((string, bool, bool, bool) next) { if (next == original) draft.Stands.Remove(index); else draft.Stands[index] = next; changed(); }

            string headerColour = index switch
            {
                4 => "#0284C7", // Top / North
                0 => "#EA580C", // Bottom / South
                2 => "#7C3AED", // Left / West
                6 => "#16A34A", // Right / East
                _ => "#475569", // Corners
            };

            var capacityBox = TextInput(current.Item1, 72, x => { current.Item1 = x; Update(current); }, HorizontalAlignment.Center);
            capacityBox.FontWeight = FontWeight.Bold;
            capacityBox.FontSize = 13.5;

            var header = new Border
            {
                Background = UiKit.Brush(headerColour), CornerRadius = new CornerRadius(3, 3, 0, 0), Padding = new Thickness(6, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = StadiumLabels.Stands[index], FontSize = 12.5, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#FFFFFF"), VerticalAlignment = VerticalAlignment.Center },
                        capacityBox,
                    },
                },
            };

            var roof = new CheckBox { Content = "Has Roof", IsChecked = current.Item2, FontSize = 12.5 };
            roof.IsCheckedChanged += (_, _) => { current.Item2 = roof.IsChecked == true; Update(current); };
            var seats = new CheckBox { Content = "Has seats", IsChecked = current.Item3, FontSize = 12.5 };
            seats.IsCheckedChanged += (_, _) => { current.Item3 = seats.IsChecked == true; Update(current); };
            var tunnel = new CheckBox { Content = "Has Tunnel", IsChecked = current.Item4, FontSize = 12.5 };
            tunnel.IsCheckedChanged += (_, _) => { current.Item4 = tunnel.IsChecked == true; Update(current); };

            Control checks = horizontal
                ? new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Children = { roof, seats, tunnel } }
                : new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { roof, seats, tunnel } };

            var body = new Border
            {
                Background = UiKit.Brush("#141824"), BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(1, 0, 1, 1),
                CornerRadius = new CornerRadius(0, 0, 3, 3), Padding = new Thickness(8, 6), Child = checks,
            };
            return new StackPanel { Spacing = 0, Children = { header, body } };
        }

        var pitch = PitchSurface.CreateHorizontal(380, 240);
        var layout = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("150,Auto,150"),
            RowSpacing = 8, ColumnSpacing = 8, Margin = new Thickness(8, 12),
        };
        void Place(Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); layout.Children.Add(control); }

        Place(StandCard(3), 0, 0);
        var top = StandCard(4, horizontal: true); top.HorizontalAlignment = HorizontalAlignment.Center; Place(top, 0, 1);
        Place(StandCard(5), 0, 2);
        var west = StandCard(2); west.VerticalAlignment = VerticalAlignment.Center; Place(west, 1, 0);
        Place(pitch, 1, 1);
        var east = StandCard(6); east.VerticalAlignment = VerticalAlignment.Center; Place(east, 1, 2);
        Place(StandCard(1), 2, 0);
        var bottom = StandCard(0, horizontal: true); bottom.HorizontalAlignment = HorizontalAlignment.Center; Place(bottom, 2, 1);
        Place(StandCard(7), 2, 2);

        var detailsPanel = new StackPanel { Spacing = 12, Children = { detailsCard, UiKit.Card("Stands", layout) } };
        var sections = new SectionHost.Section[]
        {
            new("Information", "Information", informationPanel),
            new("Stadium Details", "Stadium Details", detailsPanel),
        };
        Editor.Children.Add(SectionHost.Build("stadium", sections, sectionKey, key => sectionKey = key, RailHost));
    }

    /// <summary>A value with a Change button, as FMRTE shows Owner and Location (a link to the owner when there is one).</summary>
    Control PickerRow(string text, Action? open, bool pending, Action change)
    {
        Control value = open is null
            ? new TextBlock { Text = text, FontSize = 13, Foreground = UiKit.Brush(pending ? "#C4B5FD" : "#F1F5F9"), VerticalAlignment = VerticalAlignment.Center }
            : UiKit.MetaLinkButton(text, highlight: true);
        if (open is not null && value is Button link) link.Click += (_, _) => open();
        var button = new Button { Content = "Change", Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => change();
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { value, button } };
    }

    Task<Fm27Editor.Core.Clubs.ClubEntry?> ChooseClubAsync() => PickerDialog.Show(Host.Window, "Change Owner", q =>
    {
        string folded = Fold(q);
        return Host.Clubs().Where(c => c.FirstTeam is not null && (Fold(c.Name ?? "").Contains(folded, StringComparison.Ordinal) || Fold(c.ShortName ?? "").Contains(folded, StringComparison.Ordinal)))
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).Take(200).Select(c => (c.Name ?? c.DisplayName, c)).ToList();
    });

    Task<StadiumRecord?> ChooseStadiumAsync(StadiumRecord self) => PickerDialog.Show(Host.Window, "Change Nearby Stadium", q =>
    {
        string folded = Fold(q);
        return Rows.Select(r => r.Stadium).Where(x => x.Address != self.Address && Fold(x.DisplayName).Contains(folded, StringComparison.Ordinal))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).Take(200).Select(x => (x.DisplayName, x)).ToList();
    });

    IReadOnlyList<Fm27Editor.Core.Players.CityRecord>? cities;

    async Task<Fm27Editor.Core.Players.CityRecord?> ChooseCityAsync()
    {
        var session = Host.Backend.Session;
        var all = cities ??= await Task.Run(() => session.Cities.LoadAll());
        var nations = Host.NationCatalogue.GroupBy(n => n.Address).ToDictionary(g => g.Key, g => g.First().DisplayName);
        return await PickerDialog.Show(Host.Window, "Change Stadium Location", q =>
        {
            string folded = Fold(q);
            return all.Where(c => Fold(c.Name).Contains(folded, StringComparison.Ordinal)).Take(200)
                .Select(c => (nations.TryGetValue(c.Nation, out var nation) ? $"{c.Name}, {nation}" : c.Name, c)).ToList();
        });
    }

    protected override string? Validate(StadiumRecord s, StadiumDraft draft, out Func<int> write)
    {
        write = () => 0;
        int count = draft.Count;
        var fields = new Dictionary<StadiumField, long>();
        foreach (var (f, text) in draft.Fields)
        {
            if (!StadiumLabels.TryParse(f, text, out long v, out var error)) return error;
            fields[f] = v;
        }
        var stands = new Dictionary<int, (int, bool, bool, bool)>();
        foreach (var (i, (cap, roof, seats, tunnel)) in draft.Stands)
        {
            if (!int.TryParse(cap.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int c) || c is < 0 or > 200_000)
                return $"{StadiumLabels.Stands[i]}: enter a capacity 0–200000";
            stands[i] = (c, roof, seats, tunnel);
        }
        (bool On, int Length, int Width)? intSize = null;
        if (draft.IntSize is { } pending)
        {
            if (!pending.On) intSize = (false, 0, 0);
            else if (int.TryParse(pending.Length.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int il)
                && int.TryParse(pending.Width.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int iw)
                && il is >= 80 and <= 130 && iw is >= 40 and <= 100) intSize = (true, il, iw);
            else return "Int. Length / Width: enter 80–130 and 40–100";
        }
        uint? seatColour = null;
        if (draft.SeatColour is { } colourText)
        {
            if (!uint.TryParse(colourText.Trim().TrimStart('#'), System.Globalization.NumberStyles.HexNumber, Inv, out uint argb))
                return "Seat Colour: enter #AARRGGBB";
            seatColour = argb;
        }

        float? latitude = null, longitude = null;
        if (draft.Latitude is { } latText)
        {
            if (!float.TryParse(latText.Trim(), System.Globalization.NumberStyles.Float, Inv, out float lat) || lat is < -90 or > 90) return "Latitude: enter -90 to 90";
            latitude = lat;
        }
        if (draft.Longitude is { } lonText)
        {
            if (!float.TryParse(lonText.Trim(), System.Globalization.NumberStyles.Float, Inv, out float lon) || lon is < -180 or > 180) return "Longitude: enter -180 to 180";
            longitude = lon;
        }

        var session = Host.Backend.Session;
        var writer = session.StadiumWriter;
        var before = snapshot;
        var flags = draft.Flags.ToArray();
        var pendingOwner = draft.Owner;
        var pendingLocation = draft.Location;
        var pendingNearby = draft.Nearby;
        ulong locationBefore = session.Stadiums.LocationPointer(s.Address), nearbyBefore = session.Stadiums.NearbyPointer(s.Address);
        write = () =>
        {
            foreach (var (f, v) in fields) writer.SetField(s.Address, f, before[f], v);
            if (seatColour is { } colour) writer.SetSeatColour(s, colour);
            if (latitude is not null || longitude is not null) writer.SetCoordinates(s, latitude, longitude);
            var current = s;
            foreach (var (flag, on) in flags) { writer.SetFlag(current, flag, on); current = session.Stadiums.Reload(s.Address)!; }
            if (intSize is { } requested) { writer.SetInternationalSize(current, requested.On ? (requested.Length, requested.Width) : null); current = session.Stadiums.Reload(s.Address)!; }
            foreach (var (i, (c, roof, seats, tunnel)) in stands) writer.SetStand(current, i, c, roof, seats, tunnel);
            if (pendingLocation is { } city) { writer.SetLocation(current, locationBefore, city.Address); current = session.Stadiums.Reload(s.Address)!; }
            if (pendingNearby is { } near) { writer.SetNearby(current, nearbyBefore, near.Address); current = session.Stadiums.Reload(s.Address)!; }
            if (pendingOwner is { } club) writer.SetOwner(current, club.Address);
            return count;
        };
        return null;
    }
}
