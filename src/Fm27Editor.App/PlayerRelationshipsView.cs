using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record RelationTarget(string Label, ulong Address)
    {
        public override string ToString() => Label;
    }

    static bool Matches(string? text, string query) => text is not null && Fold(text).Contains(Fold(query), StringComparison.Ordinal);
    static RelationshipKind KindFor(byte type) => type switch { 0x01 => RelationshipKind.LikesPerson, 0x02 => RelationshipKind.DislikesPerson, _ => RelationshipKind.LikesTeam };

    Control RelationshipsPanel(PlayerRecord player) => RelationshipsPanel(backend.Session.Owner(player), RenderEditor);

    /// <summary>
    /// FMRTE's Relationships tab for a player or a staff member: every relation type in RelationCatalog, including the
    /// Other Nation Caps / Youth Caps rows, whose +0xC byte is Caps and +0xD is Goals (Frida trace on Haaland, 10/11/2026;
    /// Caps and Goals are clamped to 127 by FMRTE).
    /// </summary>
    Control RelationshipsPanel(PersonOwner player, Action rerender)
    {
        const byte CapsType = 0x68, YouthCapsType = 0x6B, OtherNationalityType = 0x09;
        var panel = new StackPanel { Spacing = 8 };
        var statuses = backend.Session.ReadStatuses(player);
        var edits = new List<EntryEdit>();
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var infoCodes = RelationshipWriter.OtherNationalityInfos;

        bool AnyChanged() => edits.Any(e => e.Changed());
        void Refresh() => save.IsEnabled = AnyChanged();

        void Change(Action change, string done)
        {
            if (AnyChanged()) { status.Text = "Save the relationship edits first"; return; }
            try { backend.Edits.Execute("Person", player.Name, "Relationships", change); status.Text = done; rerender(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,80,80,160,Auto"), ColumnSpacing = 8, RowSpacing = 4, Margin = new Thickness(12, 8) };
        void Cell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = ["Type", "Name", "Caps / Value", "Goals", "Info / Reason", ""];
        for (int i = 0; i < headers.Length; i++) Cell(new TextBlock { Text = headers[i], Foreground = Brush("#8A8DA3") }, 0, i);

        foreach (var entry in statuses.Entries)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => Change(() => new RelationshipWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.Game.ModuleBase).RemoveEntry(player, entry),
                $"Removed {RelationCatalog.NameOf(entry.Type)}. Continue in FM to see updates.");
            Cell(new TextBlock { Text = RelationCatalog.NameOf(entry.Type), VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Cell(new TextBlock { Text = entry.TargetName ?? "", VerticalAlignment = VerticalAlignment.Center }, row, 1);

            var edit = new EntryEdit(entry);
            if (entry.Type is CapsType or YouthCapsType)
            {
                var caps = new TextBox { Text = entry.Value.ToString(Inv), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
                var goals = new TextBox { Text = entry.Flags.ToString(Inv), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
                caps.TextChanged += (_, _) => Refresh();
                goals.TextChanged += (_, _) => Refresh();
                edit.Caps = caps; edit.Goals = goals;
                Cell(caps, row, 2); Cell(goals, row, 3);
            }
            else if (entry.Type == OtherNationalityType)
            {
                var names = infoCodes.Select(c => InformationLabels.Name(InformationLabels.NationalityInfos, c)).ToList();
                int at = infoCodes.ToList().IndexOf(entry.Flags);
                if (at < 0) { names.Add($"Code {entry.Flags}"); at = names.Count - 1; }
                var info = new ComboBox { Width = 150, ItemsSource = names, SelectedIndex = at };
                info.SelectionChanged += (_, _) => Refresh();
                edit.Info = info;
                Cell(info, row, 4);
            }
            else if (RelationCatalog.HasLikeValue(entry.Type))
            {
                var value = new TextBox { Text = entry.Value.ToString(Inv), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
                value.TextChanged += (_, _) => Refresh();
                var reasons = RelationshipLabels.Reasons(KindFor(entry.Type)).ToList();
                int at = entry.Plus8;
                if (at >= reasons.Count) reasons.AddRange(Enumerable.Range(reasons.Count, at - reasons.Count + 1).Select(c => $"Code {c}"));
                var reason = new ComboBox { Width = 150, ItemsSource = reasons, SelectedIndex = Math.Clamp(at, 0, reasons.Count - 1) };
                reason.SelectionChanged += (_, _) => Refresh();
                edit.Caps = value; edit.Reason = reason;
                Cell(value, row, 2); Cell(reason, row, 4);
            }
            else
            {
                Cell(new TextBlock { Text = entry.Value.ToString(Inv), Width = 70, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, row, 2);
            }
            Cell(remove, row, 5);
            edits.Add(edit);
        }

        // Add row: any catalog type, target by kind, Caps/Goals for the two caps types.
        var addType = new ComboBox { Width = 320, MinWidth = 320, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = RelationCatalog.All.Select(t => t.Name).ToArray(), SelectedIndex = -1, PlaceholderText = "Select type...", MaxDropDownHeight = 260, ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, MaxWidth = 284, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }) };
        var search = new TextBox { Width = 150, Watermark = "Name" };
        var targets = new ComboBox { Width = 230, MinWidth = 230, MaxWidth = 230, ItemTemplate = new FuncDataTemplate<RelationTarget>((target, _) => new TextBlock { Text = target.Label, MaxWidth = 194, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }) };
        var addCaps = new TextBox { Text = "0", Width = 64, HorizontalContentAlignment = HorizontalAlignment.Right, IsVisible = false };
        var addGoals = new TextBox { Text = "0", Width = 64, HorizontalContentAlignment = HorizontalAlignment.Right, IsVisible = false };
        var addValue = new TextBox { Text = "50", Width = 64, HorizontalContentAlignment = HorizontalAlignment.Right };
        var addInfo = new ComboBox { Width = 150, ItemsSource = infoCodes.Select(c => InformationLabels.Name(InformationLabels.NationalityInfos, c)).ToArray(), SelectedIndex = infoCodes.ToList().IndexOf(80), IsVisible = false };
        var add = new Button { Content = "Add", IsEnabled = false };

        RelationType? AddType() => addType.SelectedIndex < 0 ? null : RelationCatalog.All[addType.SelectedIndex];
        void Find()
        {
            string q = (search.Text ?? "").Trim();
            if (q.Length < 2) { targets.ItemsSource = null; return; }
            var type = AddType();
            if (type is null) { targets.ItemsSource = null; return; }
            IEnumerable<RelationTarget> found = type.Target switch
            {
                RelationTargetKind.Nation => (nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList())
                    .Where(n => Matches(n.DisplayName, q)).Select(n => new RelationTarget(n.Id is { } id ? $"{n.DisplayName} ({id})" : n.DisplayName, n.Address)),
                RelationTargetKind.Club => (clubCache ??= backend.Session.LoadClubs(Players()))
                    .Where(c => Matches(c.DisplayName, q) || Matches(c.Name, q)).Select(c => new RelationTarget(c.DisplayName, c.Address)),
                RelationTargetKind.Team => (clubCache ??= backend.Session.LoadClubs(Players()))
                    .Where(c => c.FirstTeam is not null && (Matches(c.DisplayName, q) || Matches(c.Name, q))).Select(c => new RelationTarget(c.DisplayName, c.FirstTeam!.Address)),
                RelationTargetKind.Person => Players().Where(p => p.Address != player.Address && Matches(p.DisplayName, q))
                    .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", backend.Session.PersonOf(p))),
                _ => [],
            };
            targets.ItemsSource = found.Take(40).ToArray();
            targets.SelectedIndex = targets.ItemCount > 0 ? 0 : -1;
        }
        void TypeChanged()
        {
            var type = AddType();
            if (type is null)
            {
                addCaps.IsVisible = addGoals.IsVisible = addValue.IsVisible = addInfo.IsVisible = false;
                search.IsVisible = targets.IsVisible = false;
                add.IsEnabled = false;
                return;
            }
            bool caps = type.Code is CapsType or YouthCapsType;
            bool nationality = type.Code == OtherNationalityType;
            addCaps.IsVisible = addGoals.IsVisible = caps;
            addValue.IsVisible = !caps && !nationality;
            addInfo.IsVisible = nationality;
            search.IsVisible = targets.IsVisible = type.Target != RelationTargetKind.None;
            addValue.Text = "";
            addValue.Watermark = "Value"; addCaps.Watermark = "Caps"; addGoals.Watermark = "Goals";
            Find();
            add.IsEnabled = type.Target == RelationTargetKind.None || targets.SelectedItem is RelationTarget;
        }
        addType.SelectionChanged += (_, _) => TypeChanged();
        search.TextChanged += (_, _) => Find();
        targets.SelectionChanged += (_, _) => add.IsEnabled = AddType() is { } t && (t.Target == RelationTargetKind.None || targets.SelectedItem is RelationTarget);
        TypeChanged();
        add.Click += (_, _) =>
        {
            if (AddType() is not { } type) return;
            ulong target = type.Target == RelationTargetKind.None ? 0 : (targets.SelectedItem as RelationTarget)?.Address ?? 0;
            byte value = type.Value, flags = type.Flags;
            if (type.Code is CapsType or YouthCapsType)
            {
                if (!byte.TryParse(addCaps.Text, out value) || value > 127 || !byte.TryParse(addGoals.Text, out flags) || flags > 127)
                { status.Text = "Other Nation Caps: Caps and Goals are 0–127"; return; }
            }
            else if (type.Code == OtherNationalityType)
            {
                if (addInfo.SelectedIndex < 0) return;
                flags = (byte)infoCodes[addInfo.SelectedIndex];
            }
            else if (!byte.TryParse(addValue.Text, out value)) { status.Text = "Relationships: enter a value"; return; }
            Change(() => new RelationshipWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.Game.ModuleBase)
                    .AddEntry(player, statuses, new RelationDraft(type, target, type.Plus8, value, flags), backend.Session.AllocateGameBlock),
                $"Added {type.Name}. Continue in FM to see updates.");
        };

        var list = new StackPanel();
        if (statuses.Entries.Count > 0) list.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var addRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        addRow.Children.Add(addType); addRow.Children.Add(search); addRow.Children.Add(targets);
        addRow.Children.Add(addCaps); addRow.Children.Add(addGoals); addRow.Children.Add(addValue); addRow.Children.Add(addInfo); addRow.Children.Add(add);
        list.Children.Add(new Border
        {
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, statuses.Entries.Count > 0 ? 1 : 0, 0, 0),
            Padding = new Thickness(12, 8),
            Child = addRow,
        });
        if (statuses.Holder != 0) panel.Children.Add(UiKit.Card("Relationships", list));
        save.Click += (_, _) =>
        {
            try
            {
                var session = backend.Session;
                backend.Edits.Execute("Player", player.Name, "Page save", () =>
                {
                    var writer = new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase);
                    foreach (var e in edits.Where(x => x.Changed()))
                    {
                        var entry = e.Entry;
                        if (entry.Type is CapsType or YouthCapsType)
                        {
                            if (!byte.TryParse(e.Caps!.Text, out var caps) || caps > 127 || !byte.TryParse(e.Goals!.Text, out var goals) || goals > 127)
                                throw new InvalidOperationException("Other Nation Caps: Caps and Goals are 0–127");
                            writer.UpdateEntry(player, entry, entry.Plus8, caps, goals);
                        }
                        else if (entry.Type == OtherNationalityType)
                        {
                            writer.UpdateEntry(player, entry, entry.Plus8, entry.Value, (byte)infoCodes[e.Info!.SelectedIndex]);
                        }
                        else
                        {
                            if (!byte.TryParse(e.Caps!.Text, out var value) || value is < 1 or > 100)
                                throw new InvalidOperationException("Relationships: enter a value 1–100");
                            writer.UpdateEntry(player, entry, e.Reason!.SelectedIndex, value, entry.Flags);
                        }
                    }
                });
                status.Text = "Saved relationships. Continue in FM to see updates.";
                rerender();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        if (statuses.Entries.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return panel;
    }

    sealed class EntryEdit(RelationEntry entry)
    {
        public RelationEntry Entry { get; } = entry;
        public TextBox? Caps { get; set; }
        public TextBox? Goals { get; set; }
        public ComboBox? Info { get; set; }
        public ComboBox? Reason { get; set; }
        public bool Changed()
        {
            if (Entry.Type is 0x68 or 0x6B)
                return Caps is not null && (Caps.Text != Entry.Value.ToString(CultureInfo.InvariantCulture) || Goals?.Text != Entry.Flags.ToString(CultureInfo.InvariantCulture));
            if (Entry.Type == 0x09)
                return Info?.SelectedIndex is { } i && (byte)RelationshipWriter.OtherNationalityInfos[i] != Entry.Flags;
            if (Caps is null) return false;   // read-only row (no editable control on the grid)
            return Caps.Text != Entry.Value.ToString(CultureInfo.InvariantCulture) || (Reason is not null && Reason.SelectedIndex != Entry.Plus8);
        }
    }
}
