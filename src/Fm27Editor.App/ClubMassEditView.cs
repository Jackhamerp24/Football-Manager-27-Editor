using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record ClubMassChoice(ClubFieldMeta Meta)
    {
        public override string ToString() => $"{Meta.Group}: {Meta.Label}";
    }
    sealed record ClubMassOperation(ClubMassChoice Choice, MassEditMode Mode, long Value)
    {
        public override string ToString() => $"{Choice}: {Mode} {Value}";
    }
    readonly Stack<List<ClubMassChange>> clubMassUndo = new();

    async Task ShowClubMassEditAsync()
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected || clubPickerList.ItemsSource is not IEnumerable<ClubPickerItem> items) return;
        var targets = items.Select(i => i.Club).ToArray();
        var session = backend.Session;
        var choices = ClubFieldInfo.All.Where(m => m.Options is null && m.DisplayKind is not (ClubDisplayKind.Bool or ClubDisplayKind.Date or ClubDisplayKind.Colour) &&
            ClubMassEdit.IsSupported(session.Profile, m.Field)).Select(m => new ClubMassChoice(m)).ToArray();
        if (choices.Length == 0) { status.Text = "No club fields available"; return; }
        var window = new Window { Title = "Mass Edit Clubs", Width = 680, Height = 600, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var field = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Width = 190 };
        var mode = new ComboBox { ItemsSource = Enum.GetValues<MassEditMode>(), SelectedIndex = 0, Width = 100 };
        var value = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Value = 10, Width = 90 };
        var add = new Button { Content = "Add" };
        var remove = new Button { Content = "Remove" };
        var preview = new Button { Content = "Preview" };
        var apply = new Button { Content = "Apply", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last batch", IsEnabled = clubMassUndo.Count > 0 };
        var message = new TextBlock { Text = $"{targets.Length:N0} clubs", Margin = new Thickness(0, 8, 0, 8) };
        var operations = new List<ClubMassOperation>();
        var operationsList = new ListBox { MaxHeight = 100, MinHeight = 42 };
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, RowHeight = 27 };
        table.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Club.Id"), Width = new DataGridLength(85) });
        table.Columns.Add(new DataGridTextColumn { Header = "Club", Binding = new Binding("Club.DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Field"), Width = new DataGridLength(140) });
        table.Columns.Add(new DataGridTextColumn { Header = "Before", Binding = new Binding("Before"), Width = new DataGridLength(95) });
        table.Columns.Add(new DataGridTextColumn { Header = "After", Binding = new Binding("After"), Width = new DataGridLength(95) });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field, mode, value, add, remove, preview } };
        var top = new StackPanel { Children = { controls, operationsList, message } };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { apply, undo } };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(table); window.Content = root;
        IReadOnlyList<ClubMassChange> plan = [];
        bool running = false;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };
        void Invalidate() { plan = []; table.ItemsSource = null; apply.IsEnabled = false; }
        add.Click += (_, _) =>
        {
            var operation = new ClubMassOperation((ClubMassChoice)field.SelectedItem!, (MassEditMode)mode.SelectedItem!, (long)(value.Value ?? 0));
            operations.RemoveAll(o => o.Choice.Meta.Field == operation.Choice.Meta.Field);
            operations.Add(operation); operationsList.ItemsSource = operations.ToArray(); Invalidate();
        };
        remove.Click += (_, _) =>
        {
            if (operationsList.SelectedItem is not ClubMassOperation operation) return;
            operations.Remove(operation); operationsList.ItemsSource = operations.ToArray(); Invalidate();
        };
        preview.Click += async (_, _) =>
        {
            if (running) return;
            try
            {
                running = busy = true; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = false;
                if (operations.Count == 0) throw new ArgumentException("Choose at least one operation");
                var actions = operations.ToArray();
                plan = await Task.Run(() => actions.SelectMany(o => ClubMassEdit.Plan(targets, o.Choice.Meta.Field, o.Mode, o.Value,
                    o.Choice.Meta.Min, o.Choice.Meta.Max, session.Memory, session.Profile, session.ModuleBase)).ToArray());
                table.ItemsSource = plan;
                message.Text = $"{plan.Count:N0} changes / {targets.Length:N0} clubs";
                apply.IsEnabled = plan.Count > 0;
            }
            catch (Exception ex) { Invalidate(); message.Text = ex.Message; }
            finally { running = busy = false; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true; }
        };
        async Task Run(bool isUndo)
        {
            if (running) return;
            var batch = isUndo ? clubMassUndo.Peek() : new List<ClubMassChange>();
            var work = isUndo ? batch.AsEnumerable().Reverse().ToArray() : plan.ToArray();
            running = busy = true; apply.IsEnabled = undo.IsEnabled = preview.IsEnabled = add.IsEnabled = remove.IsEnabled = false;
            int completed = 0;
            Exception? failure = null;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var change in work)
                    {
                        try
                        {
                            backend.Edits.Execute(() =>
                            {
                                void Record() { if (isUndo) batch.Remove(change); else batch.Add(change); completed++; }
                                if (isUndo) ClubMassEdit.Undo(change, session.Memory, session.Profile, session.ModuleBase, Record);
                                else ClubMassEdit.Apply(change, session.Memory, session.Profile, session.ModuleBase, Record);
                                backend.AcceptFrozenClubEdit(change.Club, change.Field);
                            });
                        }
                        catch (Exception ex) { failure = ex; break; }
                    }
                });
                if (isUndo && batch.Count == 0) clubMassUndo.Pop();
                else if (!isUndo && batch.Count > 0) clubMassUndo.Push(batch);
                Invalidate();
                message.Text = failure is null ? $"{(isUndo ? "Undone" : "Applied")}: {completed:N0}" : $"{completed:N0} completed. {failure.Message}";
            }
            finally { running = busy = false; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true; undo.IsEnabled = clubMassUndo.Count > 0; }
        }
        apply.Click += async (_, _) => await Run(false);
        undo.Click += async (_, _) => await Run(true);
        await window.ShowDialog(this);
        if (clubsActive && backend.State.Status == ConnectionStatus.Connected) RenderClubEditor();
    }
}
