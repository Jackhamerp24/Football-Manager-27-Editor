using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record StaffMassChoice(string Name, StaffMassField? Field = null, StaffAttribute? Attribute = null, StaffPersonality? Personality = null, bool Wage = false)
    {
        public override string ToString() => Name;
    }
    sealed record StaffMassOperation(StaffMassChoice Field, MassEditMode Mode, int Value)
    {
        public override string ToString() => $"{Field}: {Mode} {Value}";
    }
    sealed record StaffBatchChange(StaffMassChange? Scalar, StaffAttributeMassChange? Attribute, StaffPersonalityMassChange? Personality = null, StaffWageChange? Wage = null)
    {
        public StaffRecord Staff => Scalar?.Staff ?? Attribute?.Staff ?? Personality?.Staff ?? Wage!.Staff;
        public string Field => Scalar?.Field.ToString() ?? Attribute?.Field.ToString() ?? (Personality is not null ? $"Personality: {Personality.Field}" : "Wage");
        public int Before => Scalar?.Before ?? Attribute?.Before ?? Personality?.Before ?? Wage!.Before;
        public int After => Scalar?.After ?? Attribute?.After ?? Personality?.After ?? Wage!.After;
    }
    readonly Stack<List<StaffBatchChange>> staffMassUndo = new();

    async Task ShowStaffMassEditAsync()
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected || staffGrid.CollectionView is not { } view) return;
        var targets = view.OfType<StaffRow>().Select(r => r.Staff).ToArray();
        var window = new Window { Title = "Mass Edit Staff", Width = 680, Height = 600, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var choices = Enum.GetValues<StaffMassField>().Select(f => new StaffMassChoice(f.ToString(), Field: f))
            .Concat(Enum.GetValues<StaffAttribute>().Select(a => new StaffMassChoice(a.ToString(), Attribute: a)))
            .Concat(Enum.GetValues<StaffPersonality>().Select(p => new StaffMassChoice($"Personality: {p}", Personality: p)))
            .Append(new StaffMassChoice("Wage", Wage: true)).ToArray();
        var field = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Width = 176 };
        var mode = new ComboBox { ItemsSource = Enum.GetValues<MassEditMode>(), SelectedIndex = 0, Width = 104 };
        var value = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Value = 100, Width = 92, ShowButtonSpinner = false, HorizontalContentAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add" };
        var remove = new Button { Content = "Remove" };
        var preview = new Button { Content = "Preview" };
        var apply = new Button { Content = "Apply", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last batch", IsEnabled = staffMassUndo.Count > 0 };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => window.Close();
        var message = new TextBlock { Text = $"{targets.Length:N0} staff", Margin = new Thickness(0, 8, 0, 8) };
        var operations = new List<StaffMassOperation>();
        var operationsList = new ListBox { MaxHeight = 100, MinHeight = 42, Margin = new Thickness(0, 8, 0, 0) };
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, RowHeight = 27 };
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding("Staff.Id"), Width = new DataGridLength(96) };
        idCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(idCol);
        table.Columns.Add(new DataGridTextColumn { Header = "Staff", Binding = new Binding("Staff.DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Field"), Width = new DataGridLength(150) });
        var beforeCol = new DataGridTextColumn { Header = "Before", Binding = new Binding("Before"), Width = new DataGridLength(84) };
        beforeCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(beforeCol);
        var afterCol = new DataGridTextColumn { Header = "After", Binding = new Binding("After"), Width = new DataGridLength(84) };
        afterCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(afterCol);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field, mode, value, add, remove, preview } };
        var top = new StackPanel { Children = { controls, operationsList, message } };
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, undo } });
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(table); window.Content = root;
        IReadOnlyList<StaffBatchChange> plan = [];
        bool running = false, targetsDirty = false;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };
        void Invalidate() { plan = []; table.ItemsSource = null; apply.IsEnabled = false; }
        add.Click += (_, _) =>
        {
            var operation = new StaffMassOperation((StaffMassChoice)field.SelectedItem!, (MassEditMode)mode.SelectedItem!, (int)(value.Value ?? 0));
            operations.RemoveAll(o => o.Field == operation.Field);
            operations.Add(operation);
            operationsList.ItemsSource = operations.ToArray();
            Invalidate();
        };
        remove.Click += (_, _) =>
        {
            if (operationsList.SelectedItem is not StaffMassOperation operation) return;
            operations.Remove(operation); operationsList.ItemsSource = operations.ToArray(); Invalidate();
        };
        preview.Click += async (_, _) =>
        {
            if (running) return;
            try
            {
                running = busy = true;
                preview.IsEnabled = add.IsEnabled = remove.IsEnabled = false;
                if (operations.Count == 0) throw new ArgumentException("Choose at least one operation");
                if (targetsDirty)
                {
                    var prior = targets;
                    targets = await Task.Run(() => prior.Select(s =>
                    {
                        var current = backend.Session.Staff.Reload(s.Address);
                        return current is not null && current.Id == s.Id ? current : throw new InvalidOperationException("Staff list changed; reopen Mass Edit");
                    }).ToArray());
                    targetsDirty = false;
                }
                var actions = operations.ToArray();
                var result = await Task.Run(() =>
                {
                    var changes = new List<StaffBatchChange>();
                    int unavailable = 0;
                    foreach (var operation in actions)
                    {
                        if (operation.Field.Field is { } scalar)
                            changes.AddRange(StaffMassEdit.Plan(targets, scalar, operation.Mode, operation.Value).Select(c => new StaffBatchChange(c, null)));
                        else if (operation.Field.Attribute is { } attribute)
                            changes.AddRange(StaffAttributeMassEdit.Plan(targets, attribute, operation.Mode, operation.Value, backend.Session.StaffWriter).Select(c => new StaffBatchChange(null, c)));
                        else if (operation.Field.Personality is { } personality)
                            changes.AddRange(StaffPersonalityMassEdit.Plan(targets, personality, operation.Mode, operation.Value).Select(c => new StaffBatchChange(null, null, c)));
                        else if (operation.Field.Wage)
                        {
                            var wages = StaffWageMassEdit.Plan(targets, backend.Session.StaffContracts, operation.Mode, operation.Value);
                            unavailable += wages.Unavailable;
                            changes.AddRange(wages.Changes.Select(c => new StaffBatchChange(null, null, Wage: c)));
                        }
                    }
                    return (Changes: changes, Unavailable: unavailable);
                });
                plan = result.Changes;
                table.ItemsSource = plan;
                message.Text = $"{plan.Count:N0} changes / {targets.Length:N0} staff" + (result.Unavailable > 0 ? $", {result.Unavailable:N0} unavailable" : "");
                apply.IsEnabled = plan.Count > 0;
            }
            catch (Exception ex) { Invalidate(); message.Text = ex.Message; }
            finally { running = busy = false; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true; }
        };
        async Task Run(bool isUndo)
        {
            if (running) return;
            var batch = isUndo ? staffMassUndo.Peek() : new List<StaffBatchChange>();
            var work = isUndo ? batch.AsEnumerable().Reverse().ToArray() : plan.ToArray();
            running = busy = true;
            apply.IsEnabled = undo.IsEnabled = preview.IsEnabled = add.IsEnabled = remove.IsEnabled = false;
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
                                if (change.Scalar is { } scalar)
                                {
                                    if (isUndo) StaffMassEdit.Undo(scalar, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                    else StaffMassEdit.Apply(scalar, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                }
                                else if (change.Attribute is { } attribute)
                                {
                                    if (isUndo) StaffAttributeMassEdit.Undo(attribute, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                    else StaffAttributeMassEdit.Apply(attribute, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                }
                                else if (change.Personality is { } personality)
                                {
                                    if (isUndo) StaffPersonalityMassEdit.Undo(personality, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                    else StaffPersonalityMassEdit.Apply(personality, backend.Session.Staff, backend.Session.StaffWriter, Record);
                                }
                                else if (change.Wage is { } wage)
                                {
                                    if (isUndo) StaffWageMassEdit.Undo(wage, backend.Session.Staff, backend.Session.StaffContracts, Record);
                                    else StaffWageMassEdit.Apply(wage, backend.Session.Staff, backend.Session.StaffContracts, Record);
                                }
                            });
                        }
                        catch (Exception ex) { failure = ex; break; }
                    }
                });
                if (isUndo && batch.Count == 0) staffMassUndo.Pop();
                else if (!isUndo && batch.Count > 0) staffMassUndo.Push(batch);
                if (completed > 0) targetsDirty = true;
                Invalidate();
                message.Text = failure is null ? $"{(isUndo ? "Undone" : "Applied")}: {completed:N0}" : $"{completed:N0} completed. {failure.Message}";
            }
            finally { running = busy = false; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true; undo.IsEnabled = staffMassUndo.Count > 0; }
        }
        apply.Click += async (_, _) => await Run(false);
        undo.Click += async (_, _) => await Run(true);
        await window.ShowDialog(this);
        if (backend.State.Status == ConnectionStatus.Connected) await LoadStaffAsync();
    }
}
