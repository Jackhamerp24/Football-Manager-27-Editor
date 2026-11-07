using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record FormationChoice(byte Code, string Name)
    {
        public override string ToString() => Name;
    }
    static readonly FormationChoice[] FormationChoices = StaffFormations.Codes.Select(c => new FormationChoice(c.Code, c.Name)).ToArray();
    static readonly string[] FormationLabels =
    ["Preferred Formation", "Second Preferred Formation", "Defensive Formation", "Attacking Formation",
     "Preferred Formation (Out of Possession)", "Second Preferred Formation (Out of Possession)",
     "Defensive Formation (Out of Possession)", "Attacking Formation (Out of Possession)"];

    Control StaffFormationsPanel(StaffRecord staff)
    {
        var outer = new StackPanel { Spacing = 10, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        byte[] snapshot;
        try { snapshot = backend.Session.StaffFormations.Read(staff); }
        catch (Exception ex) { outer.Children.Add(new TextBlock { Text = ex.Message }); return outer; }
        var boxes = new ComboBox[8];
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };

        var rows = new StackPanel { Spacing = 0 };
        int rowIdx = 0;
        foreach (int index in new[] { 0, 4, 1, 5, 2, 6, 3, 7 })
        {
            var choices = FormationChoices.ToList();
            if (choices.All(c => c.Code != snapshot[index])) choices.Add(new(snapshot[index], $"Unknown ({snapshot[index]})"));
            var box = new ComboBox { Width = 220, ItemsSource = choices, SelectedItem = choices.Single(c => c.Code == snapshot[index]) };
            boxes[index] = box;
            var line = new DockPanel { MinHeight = 26 };
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box);
            line.Children.Add(new TextBlock { Text = FormationLabels[index], Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            rows.Children.Add(new Border
            {
                Background = rowIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(12, 3),
                Child = line,
            });
            box.SelectionChanged += (_, _) => save.IsEnabled = discard.IsEnabled =
                boxes.Select((b, i) => (Box: b, Index: i))
                    .Any(entry => entry.Box?.SelectedItem is FormationChoice c && c.Code != snapshot[entry.Index]);
        }

        outer.Children.Add(UiKit.Card("Preferred Formations", rows));

        save.Click += (_, _) =>
        {
            var values = boxes.Select(b => ((FormationChoice)b.SelectedItem!).Code).ToArray();
            try { backend.Edits.Execute(() => backend.Session.StaffFormations.Save(staff, snapshot, values)); RenderStaffEditor(); status.Text = "Saved formations. Continue in FM to see updates."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderStaffEditor();
        outer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return outer;
    }
}
