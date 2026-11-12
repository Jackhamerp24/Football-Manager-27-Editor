using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    static readonly (string Label, InjuryTreatment Flag)[] TreatmentBoxes =
        [("By Physio", InjuryTreatment.Physio), ("By Specialist", InjuryTreatment.Specialist), ("By Injection", InjuryTreatment.Injection), ("By Isolation", InjuryTreatment.Isolation)];

    /// <summary>One injury card: the type, the two durations and the four treatments, for an existing injury or a new one.</summary>
    sealed class InjuryRow
    {
        public PlayerInjury? Injury;   // null for a new injury
        public ComboBox Type = null!;
        public TextBox Minimum = null!, Extra = null!;
        public CheckBox[] Boxes = null!;
        public Border Card = null!;
        public bool Removed;
    }

    Control InjuryPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var rowsHost = new StackPanel { Spacing = 8 };
        var rows = new List<InjuryRow>();
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var session = backend.Session;
        var existing = session.ReadInjuries(player);

        // Every kind of injury, and an injury to build new ones from, are found in the background (a short scan of the game).
        var allPlayers = Players();
        var catalogue = Task.Run(() =>
        {
            var template = session.InjuryTemplate(player, allPlayers);
            return (Template: template, Types: template is null ? session.InjuryTypes() : session.InjuryTypes(template));
        });

        InjuryTreatment Treatment(CheckBox[] boxes) =>
            boxes.Select((b, i) => b.IsChecked == true ? TreatmentBoxes[i].Flag : InjuryTreatment.None).Aggregate(InjuryTreatment.None, (a, f) => a | f);
        bool RowChanged(InjuryRow r) => r.Injury is not { } i
            || r.Minimum.Text != i.MinimumDays.ToString(Inv) || r.Extra.Text != i.ExtraDays.ToString(Inv) || Treatment(r.Boxes) != i.Treatment
            || r.Type.SelectedItem is InjuryType t && t.Id != i.TypeId;
        void Changed() => save.IsEnabled = rows.Any(r => r.Removed || RowChanged(r));

        InjuryRow BuildRow(PlayerInjury? injury)
        {
            var row = new InjuryRow { Injury = injury };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,24,Auto"), RowSpacing = 4, Margin = new Thickness(12, 8) };
            row.Minimum = new TextBox { Text = injury?.MinimumDays.ToString(Inv) ?? "", Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            row.Extra = new TextBox { Text = injury?.ExtraDays.ToString(Inv) ?? "", Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            row.Boxes = TreatmentBoxes.Select(t => new CheckBox { Content = t.Label, IsChecked = injury?.Treatment.HasFlag(t.Flag) ?? false }).ToArray();
            var current = injury is null ? null : new InjuryType(0, injury.TypeId, injury.Name);
            row.Type = new ComboBox { Width = 220, ItemsSource = current is null ? Array.Empty<InjuryType>() : new[] { current }, SelectedIndex = current is null ? -1 : 0,
                DisplayMemberBinding = new Avalonia.Data.Binding(nameof(InjuryType.Name)) };
            for (int r = 0; r < row.Boxes.Length; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int line = 0;
            foreach (var (label, input) in new (string, Control)[] { ("Type", row.Type), ("Minimum Days", row.Minimum), ("Extra Days", row.Extra) })
            {
                var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(text, line); Grid.SetRow(input, line); Grid.SetColumn(input, 1);
                grid.Children.Add(text); grid.Children.Add(input); line++;
                if (input is TextBox box) box.TextChanged += (_, _) => Changed();
            }
            // Picking another kind gives the injury that kind's own duration, as FMRTE does.
            row.Type.SelectionChanged += (_, _) =>
            {
                if (row.Type.SelectedItem is InjuryType t && t.Address != 0 && (row.Injury is null || t.Id != row.Injury.TypeId))
                { row.Minimum.Text = t.DefaultMinimumDays.ToString(Inv); row.Extra.Text = t.DefaultExtraDays.ToString(Inv); }
                else if (row.Type.SelectedItem is InjuryType same && row.Injury is { } own && same.Id == own.TypeId)
                { row.Minimum.Text = own.MinimumDays.ToString(Inv); row.Extra.Text = own.ExtraDays.ToString(Inv); }
                Changed();
            };
            for (int r = 0; r < row.Boxes.Length; r++)
            {
                Grid.SetRow(row.Boxes[r], r); Grid.SetColumn(row.Boxes[r], 3);
                grid.Children.Add(row.Boxes[r]);
                row.Boxes[r].IsCheckedChanged += (_, _) => Changed();
            }
            var remove = new Button { Content = injury is null ? "Remove" : "Remove injury" };
            remove.Click += (_, _) =>
            {
                if (injury is null) { rows.Remove(row); rowsHost.Children.Remove(row.Card); }
                else
                {
                    row.Removed = !row.Removed;
                    remove.Content = row.Removed ? "Undo remove" : "Remove injury";
                    row.Card.Opacity = row.Removed ? 0.45 : 1;
                }
                Changed();
            };
            row.Card = UiKit.Card(injury?.Name ?? "New Injury", grid, rightHeader: remove);
            rows.Add(row);
            rowsHost.Children.Add(row.Card);
            return row;
        }

        foreach (var injury in existing) BuildRow(injury);

        var add = new Button { Content = "Add injury", IsEnabled = false };
        add.Click += (_, _) =>
        {
            var row = BuildRow(null);
            if (catalogue.IsCompletedSuccessfully && catalogue.Result.Types.Count > 0)
            {
                row.Type.ItemsSource = catalogue.Result.Types;
                row.Type.SelectedIndex = 0;
            }
            Changed();
        };
        _ = catalogue.ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (task.IsFaulted || task.Result.Types.Count == 0) return;
            var all = task.Result.Types;
            foreach (var row in rows)
            {
                var selected = row.Type.SelectedItem as InjuryType;
                row.Type.ItemsSource = all;
                row.Type.SelectedItem = selected is null ? null : all.FirstOrDefault(t => t.Id == selected.Id);
            }
            add.IsEnabled = task.Result.Template is not null;
        }));

        save.Click += (_, _) =>
        {
            var (template, types) = catalogue.IsCompletedSuccessfully ? catalogue.Result : (null, []);
            foreach (var r in rows)
            {
                if (!ushort.TryParse(r.Minimum.Text, out ushort minimumDay) || minimumDay < 1 || !ushort.TryParse(r.Extra.Text, out _)) { status.Text = "Injury: Minimum Days is 1 or more, Extra Days 0 or more"; return; }
                if (r.Injury is null && r.Type.SelectedItem is not InjuryType { Address: not 0 }) { status.Text = "Injury: pick a type for the new injury"; return; }
            }
            if (rows.Any(r => r.Injury is null) && template is null) { status.Text = "Injury: no injury to build a new one from was found yet"; return; }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () =>
                {
                    var writer = new InjuryWriter(session.Memory, session.Profile, session.Game.ModuleBase);
                    PlayerInjury? Reread(ulong address) => session.ReadInjuries(player).FirstOrDefault(x => x.Address == address);

                    foreach (var r in rows.Where(r => r.Injury is not null && r.Removed))
                        writer.Remove(player, Reread(r.Injury!.Address) ?? throw new StaleObjectException("The injury is gone"));

                    foreach (var r in rows.Where(r => r.Injury is not null && !r.Removed && RowChanged(r)))
                    {
                        var current = Reread(r.Injury!.Address) ?? throw new StaleObjectException("The injury is gone");
                        if (r.Type.SelectedItem is InjuryType t && t.Id != current.TypeId && t.Address != 0)
                        {
                            writer.SetType(player, current, t);
                            current = Reread(current.Address)!;
                        }
                        ushort min = ushort.Parse(r.Minimum.Text!), extra = ushort.Parse(r.Extra.Text!);
                        if (min != current.MinimumDays || extra != current.ExtraDays)
                        {
                            writer.SetDuration(player, current, min, extra);
                            current = Reread(current.Address)!;
                        }
                        if (Treatment(r.Boxes) != current.Treatment) writer.SetTreatment(player, current, Treatment(r.Boxes));
                    }

                    foreach (var r in rows.Where(r => r.Injury is null))
                    {
                        var type = (InjuryType)r.Type.SelectedItem!;
                        var made = session.AddInjury(player, template!, type, Treatment(r.Boxes));
                        ushort min = ushort.Parse(r.Minimum.Text!), extra = ushort.Parse(r.Extra.Text!);
                        if (min != made.MinimumDays || extra != made.ExtraDays) writer.SetDuration(player, made, min, extra);
                    }
                });
                status.Text = "Saved injury. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, save } });
        panel.Children.Add(rowsHost);
        return panel;
    }
}
