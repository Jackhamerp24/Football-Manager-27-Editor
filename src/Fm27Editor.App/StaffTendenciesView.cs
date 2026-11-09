using Fm27Editor.Core.History;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        var outer = new StackPanel { Spacing = 10, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        IReadOnlyList<StaffTendency> rows;
        try { rows = backend.Session.StaffTendencies.Read(staff); }
        catch (Exception ex) { outer.Children.Add(new TextBlock { Text = ex.Message }); return outer; }
        if (rows.Count == 0) return outer;

        var fields = new List<(StaffTendency Row, ComboBox Type, TextBox Box)>();
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        static byte Code(ComboBox box) => ((TendencyChoice)box.SelectedItem!).Code;
        bool Pending() => fields.Any(f => f.Box.Text != f.Row.Level.ToString(Inv) || Code(f.Type) != f.Row.Type);
        void Refresh() => save.IsEnabled = Pending();

        var list = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var row in rows)
        {
            var choices = TendencyChoices.ToList();
            if (choices.All(c => c.Code != row.Type)) choices.Insert(0, new(row.Type, StaffTendencies.TypeName(row.Type)));
            var type = new ComboBox { Width = 380, ItemsSource = choices, SelectedItem = choices.First(c => c.Code == row.Type), MaxDropDownHeight = 400 };
            var box = new TextBox { Width = 68, Text = row.Level.ToString(Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            var line = new DockPanel { LastChildFill = false, MinHeight = 26 };
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box);
            line.Children.Add(type);
            fields.Add((row, type, box));
            box.TextChanged += (_, _) => Refresh();
            type.SelectionChanged += (_, _) => Refresh();
            list.Children.Add(new Border
            {
                Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(12, 3),
                Child = line,
            });
        }

        outer.Children.Add(UiKit.Card("Tendencies", list));

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
                backend.Edits.Execute("Staff", staff.DisplayName, "Tendencies", () =>
                {
                    foreach (var (row, type, level) in changes)
                    {
                        // Level first: both setters check the row's two bytes as read.
                        if (level != row.Level) t.SetLevel(staff, row, level);
                        if (type != row.Type) t.SetType(staff, row with { Level = (byte)level }, type);
                        written++;
                    }
                }, StaffChangeLabels.ForTendencies(changes.Select(c => c.Row)));
                RenderStaffEditor(); status.Text = "Saved tendencies. Continue in FM to see updates.";
            }
            catch (Exception ex) { RenderStaffEditor(); status.Text = $"{written} saved; {ex.Message}"; }
        };
        outer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return outer;
    }
}
