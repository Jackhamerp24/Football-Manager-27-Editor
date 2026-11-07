using Avalonia;
using Avalonia.Controls;
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

    Control RelationshipsPanel(PlayerRecord player) => RelationshipsPanel(backend.Session.Owner(player), RenderEditor);

    /// <summary>FMRTE's Relationships tab for a player or a staff member (same holder on the person).</summary>
    Control RelationshipsPanel(PersonOwner player, Action rerender)
    {
        var panel = new StackPanel { Spacing = 8 };
        var statuses = backend.Session.ReadStatuses(player);
        var fields = new List<(PlayerRelationship Row, TextBox Value, ComboBox Reason, CheckBox? Permanent)>();
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        bool Changed((PlayerRelationship Row, TextBox Value, ComboBox Reason, CheckBox? Permanent) f) =>
            f.Value.Text != f.Row.Value.ToString(Inv) || f.Reason.SelectedIndex != f.Row.Reason || (f.Permanent is { } p && p.IsChecked != (f.Row.Flags != 0));
        // Other Nationality rows: only Nationality Info is editable (the record's +0xD byte).
        var infos = new List<(PlayerRelationship Row, ComboBox Info)>();
        var infoCodes = RelationshipWriter.OtherNationalityInfos;
        bool InfoChanged((PlayerRelationship Row, ComboBox Info) f) => f.Info.SelectedIndex >= 0 && infoCodes[f.Info.SelectedIndex] != f.Row.Flags;
        bool AnyChanged() => fields.Any(Changed) || infos.Any(InfoChanged);
        void Refresh() => save.IsEnabled = discard.IsEnabled = AnyChanged();

        // Add and Remove write at once, like FMRTE's +/−; they refuse while edits are pending since the list moves.
        void Change(Action change, string done)
        {
            if (AnyChanged()) { status.Text = "Save or discard the relationship edits first"; return; }
            try { backend.Edits.Execute(change); status.Text = done; rerender(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,160,Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4, Margin = new Thickness(12, 8) };
        void Cell(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = ["Type", "Name", "Value", "Reason", "Permanent"];
        for (int i = 0; i < headers.Length; i++) Cell(new TextBlock { Text = headers[i], Foreground = Brush("#8A8DA3") }, 0, i);
        foreach (var r in statuses.Relationships)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var removeRow = new Button { Content = "Remove" };
            removeRow.Click += (_, _) => Change(() => new RelationshipWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.Game.ModuleBase).Remove(player, r),
                $"Removed {r.Name}. Continue in FM to see updates.");
            if (r.Kind == RelationshipKind.OtherNationality)
            {
                var names = infoCodes.Select(c => InformationLabels.Name(InformationLabels.NationalityInfos, c)).ToList();
                int at = infoCodes.ToList().IndexOf(r.Flags);
                if (at < 0) { names.Add($"Code {r.Flags}"); at = names.Count - 1; }
                var info = new ComboBox { Width = 150, ItemsSource = names, SelectedIndex = at };
                info.SelectionChanged += (_, _) => Refresh();
                infos.Add((r, info));
                Cell(new TextBlock { Text = RelationshipLabels.KindName(r.Kind), VerticalAlignment = VerticalAlignment.Center }, row, 0);
                Cell(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center }, row, 1);
                Cell(info, row, 3);
                Cell(removeRow, row, 5);
                continue;
            }
            var value = new TextBox { Text = r.Value.ToString(Inv), Width = 64, HorizontalContentAlignment = HorizontalAlignment.Right };
            var reasons = RelationshipLabels.Reasons(r.Kind).ToList();
            if (r.Reason >= reasons.Count) reasons.AddRange(Enumerable.Range(reasons.Count, r.Reason - reasons.Count + 1).Select(c => $"Code {c}"));
            var reason = new ComboBox { Width = 150, ItemsSource = reasons, SelectedIndex = r.Reason };
            CheckBox? permanent = r.Kind == RelationshipKind.LikesTeam ? null : new CheckBox { IsChecked = r.Flags != 0 };
            Cell(new TextBlock { Text = RelationshipLabels.KindName(r.Kind), VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Cell(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center }, row, 1);
            Cell(value, row, 2);
            Cell(reason, row, 3);
            if (permanent is not null) Cell(permanent, row, 4);
            Cell(removeRow, row, 5);
            var f = (r, value, reason, permanent);
            fields.Add(f);
            value.TextChanged += (_, _) => Refresh();
            reason.SelectionChanged += (_, _) => Refresh();
            if (permanent is not null) permanent.IsCheckedChanged += (_, _) => Refresh();
        }

        // Add row: type, person or club (search by name), value, reason, permanent.
        var kinds = new[] { RelationshipKind.LikesPerson, RelationshipKind.DislikesPerson, RelationshipKind.LikesTeam, RelationshipKind.OtherNationality };
        var addKind = new ComboBox { Width = 130, ItemsSource = kinds.Select(RelationshipLabels.KindName).ToArray(), SelectedIndex = 0 };
        var search = new TextBox { Width = 160, Watermark = "Name" };
        var targets = new ComboBox { Width = 240 };
        var addValue = new TextBox { Width = 64, Text = "50", HorizontalContentAlignment = HorizontalAlignment.Right };
        var addReason = new ComboBox { Width = 150 };
        var addPermanent = new CheckBox();
        var addNationalityInfo = new ComboBox { Width = 150, ItemsSource = infoCodes.Select(c => InformationLabels.Name(InformationLabels.NationalityInfos, c)).ToArray(), SelectedIndex = infoCodes.ToList().IndexOf(80), IsVisible = false };
        var add = new Button { Content = "Add", IsEnabled = false };
        RelationshipKind Kind() => kinds[Math.Max(0, addKind.SelectedIndex)];
        void Find()
        {
            string q = (search.Text ?? "").Trim();
            if (q.Length < 2) { targets.ItemsSource = null; return; }
            IEnumerable<RelationTarget> found = Kind() == RelationshipKind.OtherNationality
                ? (nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList())
                    .Where(n => Matches(n.DisplayName, q)).Select(n => new RelationTarget(n.Id is { } id ? $"{n.DisplayName} ({id})" : n.DisplayName, n.Address))
                : Kind() == RelationshipKind.LikesTeam
                ? (clubCache ??= backend.Session.LoadClubs(Players()))
                    .Where(c => Matches(c.DisplayName, q) || Matches(c.Name, q)).Select(c => new RelationTarget(c.DisplayName, c.Address))
                : Players().Where(p => p.Address != player.Address && Matches(p.DisplayName, q))
                    .Select(p => new RelationTarget($"{p.DisplayName} ({p.ClubName})", backend.Session.PersonOf(p)));
            targets.ItemsSource = found.Take(40).ToArray();
            targets.SelectedIndex = targets.ItemCount > 0 ? 0 : -1;
        }
        void KindChanged()
        {
            addReason.ItemsSource = RelationshipLabels.Reasons(Kind());
            addReason.SelectedIndex = 0;
            bool nationality = Kind() == RelationshipKind.OtherNationality;
            addValue.IsVisible = addReason.IsVisible = !nationality;
            addPermanent.IsVisible = !nationality && Kind() != RelationshipKind.LikesTeam;
            addNationalityInfo.IsVisible = nationality;
            Find();
        }
        addKind.SelectionChanged += (_, _) => KindChanged();
        search.TextChanged += (_, _) => Find();
        targets.SelectionChanged += (_, _) => add.IsEnabled = targets.SelectedItem is RelationTarget;
        KindChanged();
        add.Click += (_, _) =>
        {
            if (targets.SelectedItem is not RelationTarget t) return;
            if (Kind() == RelationshipKind.OtherNationality)
            {
                if (addNationalityInfo.SelectedIndex < 0) return;
                Change(() => backend.Session.AddOtherNationality(player, statuses, t.Address, infoCodes[addNationalityInfo.SelectedIndex]),
                    $"Added {t.Label}. A full list moves to memory outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
                return;
            }
            if (!int.TryParse(addValue.Text, out var value) || value is < 1 or > 100) { status.Text = "Relationships: enter a value 1–100"; return; }
            Change(() => backend.Session.AddRelationship(player, statuses, Kind(), t.Address, value, addReason.SelectedIndex, addPermanent.IsChecked == true),
                $"Added {t.Label}. A full list moves to memory outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
        };

        var list = new StackPanel();
        if (statuses.Relationships.Count > 0) list.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        list.Children.Add(new Border
        {
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, statuses.Relationships.Count > 0 ? 1 : 0, 0, 0),
            Padding = new Thickness(12, 8),
            Child = new WrapPanel { Orientation = Orientation.Horizontal, Children = { addKind, search, targets, addValue, addReason, addPermanent, addNationalityInfo, add } },
        });
        if (statuses.Holder != 0) panel.Children.Add(UiKit.Card("Relationships", list));
        save.Click += (_, _) =>
        {
            var changes = new List<((PlayerRelationship Row, TextBox Value, ComboBox Reason, CheckBox? Permanent) Field, int Value)>();
            foreach (var f in fields.Where(Changed))
            {
                if (!int.TryParse(f.Value.Text, out var value) || value is < 1 or > 100) { status.Text = "Relationships: enter a value 1–100"; return; }
                changes.Add((f, value));
            }
            try
            {
                var session = backend.Session;
                backend.Edits.Execute(() =>
                {
                    var writer = new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase);
                    foreach (var (f, value) in changes)
                    {
                        bool reasonChanged = f.Reason.SelectedIndex != f.Row.Reason;
                        bool? permanent = f.Permanent is { } p && p.IsChecked != (f.Row.Flags != 0) ? p.IsChecked == true : null;
                        if (!reasonChanged && permanent is null) writer.SetValue(player, f.Row, value);
                        else writer.SetDetails(player, f.Row, f.Reason.SelectedIndex, permanent, value);
                    }
                    foreach (var f in infos.Where(InfoChanged)) writer.SetNationalityInfo(player, f.Row, infoCodes[f.Info.SelectedIndex]);
                });
                status.Text = "Saved relationships. Continue in FM to see updates.";
                rerender();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => rerender();
        if (statuses.Relationships.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
