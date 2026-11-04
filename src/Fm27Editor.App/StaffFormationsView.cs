using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12, 8) };
        byte[] snapshot;
        try { snapshot = backend.Session.StaffFormations.Read(staff); }
        catch (Exception ex) { panel.Children.Add(new TextBlock { Text = ex.Message }); return panel; }
        var boxes = new ComboBox[8];
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        foreach (int index in new[] { 0, 4, 1, 5, 2, 6, 3, 7 })
        {
            var choices = FormationChoices.ToList();
            if (choices.All(c => c.Code != snapshot[index])) choices.Add(new(snapshot[index], $"Unknown ({snapshot[index]})"));
            var box = new ComboBox { Width = 220, ItemsSource = choices, SelectedItem = choices.Single(c => c.Code == snapshot[index]) };
            boxes[index] = box;
            var line = new DockPanel();
            DockPanel.SetDock(box, Dock.Right); line.Children.Add(box);
            line.Children.Add(new TextBlock { Text = FormationLabels[index], VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(line);
            box.SelectionChanged += (_, _) => save.IsEnabled = discard.IsEnabled =
                boxes.Select((b, i) => (Box: b, Index: i))
                    .Any(entry => entry.Box?.SelectedItem is FormationChoice c && c.Code != snapshot[entry.Index]);
        }
        save.Click += (_, _) =>
        {
            var values = boxes.Select(b => ((FormationChoice)b.SelectedItem!).Code).ToArray();
            try { backend.Edits.Execute(() => backend.Session.StaffFormations.Save(staff, snapshot, values)); RenderStaffEditor(); status.Text = "Saved formations. Continue in FM to see updates."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderStaffEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
