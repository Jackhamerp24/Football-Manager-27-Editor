using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    static readonly (string Label, InjuryTreatment Flag)[] TreatmentBoxes =
        [("By Physio", InjuryTreatment.Physio), ("By Specialist", InjuryTreatment.Specialist), ("By Injection", InjuryTreatment.Injection), ("By Isolation", InjuryTreatment.Isolation)];

    Control InjuryPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var fields = new List<(PlayerInjury Injury, TextBox Minimum, TextBox Extra, CheckBox[] Boxes)>();
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        InjuryTreatment Treatment(CheckBox[] boxes) =>
            boxes.Select((b, i) => b.IsChecked == true ? TreatmentBoxes[i].Flag : InjuryTreatment.None).Aggregate(InjuryTreatment.None, (a, f) => a | f);
        void Changed() => save.IsEnabled = discard.IsEnabled = fields.Any(f => f.Minimum.Text != f.Injury.MinimumDays.ToString(Inv)
            || f.Extra.Text != f.Injury.ExtraDays.ToString(Inv) || Treatment(f.Boxes) != f.Injury.Treatment);
        foreach (var injury in backend.Session.ReadInjuries(player))
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,24,Auto"), RowSpacing = 4, Margin = new Thickness(12, 8) };
            var minimum = new TextBox { Text = injury.MinimumDays.ToString(Inv), Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            var extra = new TextBox { Text = injury.ExtraDays.ToString(Inv), Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            var boxes = TreatmentBoxes.Select(t => new CheckBox { Content = t.Label, IsChecked = injury.Treatment.HasFlag(t.Flag) }).ToArray();
            for (int r = 0; r < boxes.Length; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int row = 0;
            foreach (var (label, input) in new[] { ("Minimum Days", minimum), ("Extra Days", extra) })
            {
                var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(text, row); Grid.SetRow(input, row); Grid.SetColumn(input, 1);
                grid.Children.Add(text); grid.Children.Add(input); row++;
                input.TextChanged += (_, _) => Changed();
            }
            for (int r = 0; r < boxes.Length; r++)
            {
                Grid.SetRow(boxes[r], r); Grid.SetColumn(boxes[r], 3);
                grid.Children.Add(boxes[r]);
                boxes[r].IsCheckedChanged += (_, _) => Changed();
            }
            fields.Add((injury, minimum, extra, boxes));
            panel.Children.Add(new Border { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1),
                Child = new StackPanel { Children = { new Border { Background = Brush("#1D1F2D"), Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = injury.Name, FontSize = 13 } }, grid } } });
        }
        if (fields.Count == 0) return panel;
        save.Click += (_, _) =>
        {
            var changes = new List<(PlayerInjury Injury, int Min, int Extra, InjuryTreatment Treatment)>();
            foreach (var f in fields)
            {
                if (!ushort.TryParse(f.Minimum.Text, out var min) || !ushort.TryParse(f.Extra.Text, out var extra)) { status.Text = "Injury: enter whole days 0–65535"; return; }
                var treatment = Treatment(f.Boxes);
                if (min != f.Injury.MinimumDays || extra != f.Injury.ExtraDays || treatment != f.Injury.Treatment) changes.Add((f.Injury, min, extra, treatment));
            }
            try
            {
                var session = backend.Session;
                backend.Edits.Execute(() =>
                {
                    var writer = new InjuryWriter(session.Memory, session.Profile, session.Game.ModuleBase);
                    foreach (var c in changes)
                    {
                        // Each write re-checks the whole record, so the second one needs the record as the first left it.
                        var current = c.Injury;
                        if (c.Min != current.MinimumDays || c.Extra != current.ExtraDays)
                        {
                            writer.SetDuration(player, current, c.Min, c.Extra);
                            current = session.ReadInjuries(player).Single(i => i.Address == current.Address);
                        }
                        if (c.Treatment != current.Treatment) writer.SetTreatment(player, current, c.Treatment);
                    }
                });
                status.Text = "Saved injury. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
