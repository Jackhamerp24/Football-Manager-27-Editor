using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record KnowledgeTarget(ulong Team, string Name)
    {
        public override string ToString() => Name;
    }

    Control StaffKnowledgePanel(StaffRecord staff)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 580, HorizontalAlignment = HorizontalAlignment.Left };
        var session = backend.Session;
        nationCatalogue ??= session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<StaffKnowledgeRow> rows;
        try { rows = session.StaffKnowledge.Read(staff, nationCatalogue); }
        catch (Exception ex) { panel.Children.Add(new TextBlock { Text = ex.Message }); return panel; }
        var fields = new List<(StaffKnowledgeRow Row, TextBox Box)>();
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        bool Pending() => fields.Any(f => f.Box.Text != f.Row.Value.ToString(Inv));

        // Add and Remove write at once, like FMRTE's +/−; they refuse while edits are pending since the list moves.
        void Change(Action change, string done)
        {
            if (Pending()) { status.Text = "Save or discard the knowledge edits first"; return; }
            try { backend.Edits.Execute(change); status.Text = done; RenderStaffEditor(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var list = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var row in rows)
        {
            var line = new DockPanel { LastChildFill = true, MinHeight = 26 };
            var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += (_, _) => Change(() => session.StaffKnowledge.Remove(staff, rows, row), $"Removed {row.Nation}. Continue in FM to see updates.");
            var box = new TextBox { Width = 68, Text = row.Value.ToString(Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(remove, Dock.Right); line.Children.Add(remove);
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box);
            line.Children.Add(new TextBlock { Text = row.Nation, Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            fields.Add((row, box));
            box.TextChanged += (_, _) => save.IsEnabled = discard.IsEnabled = Pending();
            list.Children.Add(new Border
            {
                Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(12, 3),
                Child = line,
            });
        }

        // Add row: a new record copies the first row (FMRTE copies the selected one), so it needs at least one.
        Border? addRow = null;
        if (rows.Count > 0)
        {
            var listed = rows.Select(r => r.Nation).ToHashSet();
            var targets = session.StaffKnowledge.Catalogue(nationCatalogue).Where(t => !listed.Contains(t.Name)).Select(t => new KnowledgeTarget(t.Team, t.Name)).ToArray();
            var nation = new AutoCompleteBox
            {
                Width = 220, Watermark = "Add nation...", MinimumPrefixLength = 2,
                FilterMode = AutoCompleteFilterMode.Custom, MaxDropDownHeight = 300, ItemsSource = targets,
                ItemFilter = (q, o) => o is KnowledgeTarget t && Matches(t.Name, q ?? ""),
            };
            var value = new TextBox { Width = 68, Text = "100", HorizontalContentAlignment = HorizontalAlignment.Right };
            var add = new Button { Content = "Add", IsEnabled = false };
            nation.SelectionChanged += (_, _) => add.IsEnabled = nation.SelectedItem is KnowledgeTarget;
            add.Click += (_, _) =>
            {
                if (nation.SelectedItem is not KnowledgeTarget t) return;
                if (!int.TryParse(value.Text, out int v) || v is < 0 or > 100) { status.Text = "Knowledge must be between 0 and 100"; return; }
                Change(() => session.AddStaffKnowledge(staff, rows, t.Team, v),
                    $"Added {t.Name}. Its record lives outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
            };
            addRow = new Border
            {
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, list.Children.Count > 0 ? 1 : 0, 0, 0),
                Padding = new Thickness(12, 8),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { nation, value, add } },
            };
        }

        if (list.Children.Count == 0 && addRow is null)
            return panel;

        var cardStack = new StackPanel();
        if (list.Children.Count > 0) cardStack.Children.Add(list);
        if (addRow is not null) cardStack.Children.Add(addRow);
        panel.Children.Add(UiKit.Card("Scouting Knowledge", cardStack));

        save.Click += (_, _) =>
        {
            var changes = new List<(StaffKnowledgeRow Row, int Value)>();
            foreach (var (row, box) in fields)
            {
                if (!int.TryParse(box.Text, out int value) || value is < 0 or > 100) { status.Text = "Knowledge must be between 0 and 100"; return; }
                if (value != row.Value) changes.Add((row, value));
            }
            int written = 0;
            try
            {
                backend.Edits.Execute(() => { foreach (var (row, value) in changes) { session.StaffKnowledge.SetValue(staff, row, value); written++; } });
                RenderStaffEditor(); status.Text = "Saved knowledge. Continue in FM to see updates.";
            }
            catch (Exception ex) { RenderStaffEditor(); status.Text = $"{written} saved; {ex.Message}"; }
        };
        discard.Click += (_, _) => RenderStaffEditor();
        if (fields.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
