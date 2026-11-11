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

        var removed = new List<RelationEntry>();
        var drafts = new List<DraftRow>();
        bool AnyChanged() => removed.Count > 0 || drafts.Count > 0 || edits.Any(e => !removed.Contains(e.Entry) && e.Changed());
        void Refresh() => save.IsEnabled = AnyChanged();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,80,80,160,Auto"), ColumnSpacing = 8, RowSpacing = 4, Margin = new Thickness(12, 8) };
        void Cell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
        void HideRow(int at) { foreach (var c in grid.Children.Where(c => Grid.GetRow(c) == at)) c.IsVisible = false; }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = ["Type", "Name", "Caps / Value", "Goals", "Info / Reason", ""];
        for (int i = 0; i < headers.Length; i++) Cell(new TextBlock { Text = headers[i], Foreground = Brush("#8A8DA3") }, 0, i);

        foreach (var entry in statuses.Entries)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { removed.Add(entry); HideRow(row); Refresh(); };
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

        // What a new row can point at, by the kind its type needs; built the first time a draft asks.
        var choices = new Dictionary<RelationTargetKind, IReadOnlyList<RelationTarget>>();
        IReadOnlyList<RelationTarget> TargetChoices(RelationTargetKind kind)
        {
            if (choices.TryGetValue(kind, out var known)) return known;
            var session = backend.Session;
            IEnumerable<RelationTarget> found = kind switch
            {
                RelationTargetKind.Nation => (nationCatalogue ??= session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList())
                    .Select(n => new RelationTarget(n.Id is { } id ? $"{n.DisplayName} ({id})" : n.DisplayName, n.Address)),
                RelationTargetKind.Club => (clubCache ??= session.LoadClubs(Players())).Where(c => c.Gender != Fm27Editor.Core.Offsets.Gender.Women).Select(c => new RelationTarget(c.DisplayName, c.Address)),
                RelationTargetKind.Team => (clubCache ??= session.LoadClubs(Players())).Where(c => c.FirstTeam is not null && c.Gender != Fm27Editor.Core.Offsets.Gender.Women).Select(c => new RelationTarget(c.DisplayName, c.FirstTeam!.Address)),
                RelationTargetKind.Person => Players().Where(p => p.Address != player.Address && p.Gender != Fm27Editor.Core.Offsets.Gender.Women && p.Club?.Gender != Fm27Editor.Core.Offsets.Gender.Women).Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", session.PersonOf(p))),
                _ => [],
            };
            return choices[kind] = found.ToList();
        }

        // Add puts a draft row in the table; the type decides which of its cells show. Nothing is written until Save.
        void AddDraft()
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var d = new DraftRow();
            var typeBox = new ComboBox { Width = 150, ItemsSource = RelationCatalog.All.Select(t => t.Name).ToArray(), SelectedIndex = -1, MaxDropDownHeight = 300 };
            var target = new AutoCompleteBox { MinimumPrefixLength = 2, FilterMode = AutoCompleteFilterMode.Custom, MaxDropDownHeight = 300, IsVisible = false,
                ItemFilter = (q, o) => o is RelationTarget t && Matches(t.Label, q ?? "") };
            var value = new TextBox { Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right, IsVisible = false };
            var goals = new TextBox { Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right, IsVisible = false };
            var info = new ComboBox { Width = 150, ItemsSource = infoCodes.Select(c => InformationLabels.Name(InformationLabels.NationalityInfos, c)).ToArray(), SelectedIndex = infoCodes.ToList().IndexOf(80), IsVisible = false };
            var removeDraft = new Button { Content = "Remove" };
            d.TargetBox = target; d.Value = value; d.Goals = goals; d.Info = info;
            Cell(typeBox, row, 0); Cell(target, row, 1); Cell(value, row, 2); Cell(goals, row, 3); Cell(info, row, 4); Cell(removeDraft, row, 5);
            typeBox.SelectionChanged += (_, _) =>
            {
                d.Type = typeBox.SelectedIndex < 0 ? null : RelationCatalog.All[typeBox.SelectedIndex];
                bool targeted = d.Type is { Target: not RelationTargetKind.None };
                target.IsVisible = targeted;
                target.Text = ""; target.SelectedItem = null;
                target.ItemsSource = targeted ? TargetChoices(d.Type!.Target) : null;
                value.IsVisible = d.Type is not null && d.Type.Code != OtherNationalityType;
                goals.IsVisible = d.Type?.Code is CapsType or YouthCapsType;
                info.IsVisible = d.Type?.Code == OtherNationalityType;
                value.Text = d.Type?.Value.ToString(Inv) ?? ""; goals.Text = "0";
                Refresh();
            };
            removeDraft.Click += (_, _) => { drafts.Remove(d); HideRow(row); Refresh(); };
            drafts.Add(d);
            Refresh();
        }
        var add = new Button { Content = "Add", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddDraft();

        var list = new StackPanel();
        list.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        list.Children.Add(new Border
        {
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, statuses.Entries.Count > 0 ? 1 : 0, 0, 0),
            Padding = new Thickness(12, 8),
            Child = add,
        });
        if (statuses.Holder != 0) panel.Children.Add(UiKit.Card("Relationships", list));
        RelationDraft ToDraft(DraftRow d)
        {
            var type = d.Type ?? throw new InvalidOperationException("Relationships: choose a type for every new row");
            ulong target = type.Target == RelationTargetKind.None ? 0
                : (d.TargetBox!.SelectedItem as RelationTarget)?.Address ?? throw new InvalidOperationException($"{type.Name}: choose a target");
            byte value = type.Value, flags = type.Flags;
            if (type.Code is CapsType or YouthCapsType)
            {
                if (!byte.TryParse(d.Value!.Text, out value) || value > 127 || !byte.TryParse(d.Goals!.Text, out flags) || flags > 127)
                    throw new InvalidOperationException("Other Nation Caps: Caps and Goals are 0–127");
            }
            else if (type.Code == OtherNationalityType) flags = (byte)infoCodes[d.Info!.SelectedIndex];
            else if (!byte.TryParse(d.Value!.Text, out value)) throw new InvalidOperationException($"{type.Name}: enter a value");
            return new RelationDraft(type, target, type.Plus8, value, flags);
        }
        save.Click += (_, _) =>
        {
            try
            {
                var session = backend.Session;
                var toAdd = drafts.Select(ToDraft).ToList();
                backend.Edits.Execute("Player", player.Name, "Page save", () =>
                {
                    var writer = new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase);
                    foreach (var e in edits.Where(x => !removed.Contains(x.Entry) && x.Changed()))
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
                    // Removing and adding move the list, so it is read again before each one.
                    foreach (var gone in removed)
                    {
                        var now = session.ReadStatuses(player).Entries.FirstOrDefault(x => x.Type == gone.Type && x.Target == gone.Target && x.Snapshot == gone.Snapshot)
                            ?? throw new StaleObjectException("The relationship is no longer in the list");
                        writer.RemoveEntry(player, now);
                    }
                    foreach (var draft in toAdd) writer.AddEntry(player, session.ReadStatuses(player), draft, session.AllocateGameBlock);
                });
                rerender();
                status.Text = "Saved relationships. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(save);
        return panel;
    }

    /// <summary>A row added with Add and not yet saved; its controls sit in the table like any other row.</summary>
    sealed class DraftRow
    {
        public RelationType? Type;
        public AutoCompleteBox? TargetBox;
        public TextBox? Value, Goals;
        public ComboBox? Info;
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
