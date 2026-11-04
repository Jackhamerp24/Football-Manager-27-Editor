using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record TendencyChoice(byte Code, string Name)
    {
        public override string ToString() => Name;
    }

    static readonly TendencyChoice[] TendencyChoices = StaffTendencies.Types.Select(t => new TendencyChoice(t.Code, t.Name)).ToArray();

    Control StaffTendenciesPanel(StaffRecord staff)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12, 8) };
        IReadOnlyList<StaffTendency> rows;
        try { rows = backend.Session.StaffTendencies.Read(staff); }
        catch (Exception ex) { panel.Children.Add(new TextBlock { Text = ex.Message }); return panel; }
        var fields = new List<(StaffTendency Row, ComboBox Type, TextBox Box)>();
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        static byte Code(ComboBox box) => ((TendencyChoice)box.SelectedItem!).Code;
        bool Pending() => fields.Any(f => f.Box.Text != f.Row.Level.ToString(Inv) || Code(f.Type) != f.Row.Type);
        void Refresh() => save.IsEnabled = discard.IsEnabled = Pending();
        foreach (var row in rows)
        {
            var choices = TendencyChoices.ToList();
            if (choices.All(c => c.Code != row.Type)) choices.Insert(0, new(row.Type, StaffTendencies.TypeName(row.Type)));
            var type = new ComboBox { Width = 420, ItemsSource = choices, SelectedItem = choices.First(c => c.Code == row.Type), MaxDropDownHeight = 400 };
            var box = new TextBox { Width = 70, Text = row.Level.ToString(Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            var line = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box);
            line.Children.Add(type);
            fields.Add((row, type, box));
            box.TextChanged += (_, _) => Refresh();
            type.SelectionChanged += (_, _) => Refresh();
            panel.Children.Add(line);
        }
        save.Click += (_, _) =>
        {
            var changes = new List<(StaffTendency Row, byte Type, int Level)>();
            foreach (var (row, type, box) in fields)
            {
                if (!int.TryParse(box.Text, out int value) || value is < 0 or > 20) { status.Text = "Tendency level must be between 0 and 20"; return; }
                if (value != row.Level || Code(type) != row.Type) changes.Add((row, Code(type), value));
            }
            int written = 0;
            try
            {
                var t = backend.Session.StaffTendencies;
                backend.Edits.Execute(() =>
                {
                    foreach (var (row, type, level) in changes)
                    {
                        // Level first: both setters check the row's two bytes as read.
                        if (level != row.Level) t.SetLevel(staff, row, level);
                        if (type != row.Type) t.SetType(staff, row with { Level = (byte)level }, type);
                        written++;
                    }
                });
                RenderStaffEditor(); status.Text = "Saved tendencies. Continue in FM to see updates.";
            }
            catch (Exception ex) { RenderStaffEditor(); status.Text = $"{written} saved; {ex.Message}"; }
        };
        discard.Click += (_, _) => RenderStaffEditor();
        if (fields.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
