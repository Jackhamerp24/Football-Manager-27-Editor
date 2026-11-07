using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record MassField(string Name, PlayerAttribute? Attribute, PlayerField? Scalar, PlayerQuality? Quality = null, PlayerRatingField? Rating = null, bool Wage = false)
    {
        public override string ToString() => Name;
    }
    sealed record MassOperation(MassField Field, MassEditMode Mode, int Value)
    {
        public override string ToString() => $"{Field}: {Mode} {Value}";
    }
    sealed record MassChange(PlayerMassChange? AttributeChange, PlayerPresetChange? ScalarChange, PlayerQualityChange? QualityChange = null, PlayerRatingChange? RatingChange = null, PlayerWageChange? WageChange = null)
    {
        public PlayerRecord Player => AttributeChange?.Player ?? ScalarChange?.Player ?? QualityChange?.Player ?? RatingChange?.Player ?? WageChange!.Player;
        public string Field => AttributeChange?.Attribute.ToString() ?? ScalarChange?.Field.ToString() ?? QualityChange?.Field.ToString() ?? RatingChange?.Field.ToString() ?? "Wage";
        public long Before => AttributeChange?.Before ?? ScalarChange?.Before ?? QualityChange?.Before ?? RatingChange?.Before ?? WageChange!.Before;
        public long After => AttributeChange?.After ?? ScalarChange?.After ?? QualityChange?.After ?? RatingChange?.After ?? WageChange!.After;
    }
    readonly Stack<List<MassChange>> massUndo = new();

    async Task ShowMassEditAsync()
    {
        if (grid.CollectionView is not { } view || module is not PlayersModule) return;
        var targets = view.OfType<PlayerRecord>().ToArray();
        var window = new Window { Title = "Mass Edit", Width = 660, Height = 600, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var fields = Enum.GetValues<PlayerAttribute>().Select(a => new MassField(a.ToString(), a, null))
            .Concat(PlayerScalarMassEdit.Fields.Where(backend.Session.PlayerFields.IsMapped).Select(f => new MassField(f.ToString(), null, f)))
            .Concat(Enum.GetValues<PlayerQuality>().Select(f => new MassField(f.ToString(), null, null, f)))
            .Concat(Enum.GetValues<PlayerPosition>().Select(p => new PlayerRatingField(Position: p)).Select(f => new MassField(f.ToString(), null, null, Rating: f)))
            .Concat(Enum.GetValues<PersonalityAttribute>().Select(p => new PlayerRatingField(Personality: p)).Select(f => new MassField(f.ToString(), null, null, Rating: f)))
            .Concat(backend.Session.Profile.Contract is null ? [] : [new MassField("Wage", null, null, Wage: true)]).ToArray();
        var attribute = new ComboBox { ItemsSource = fields, SelectedIndex = 0, Width = 176 };
        var mode = new ComboBox { ItemsSource = Enum.GetValues<MassEditMode>(), SelectedIndex = 0, Width = 104 };
        var value = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Value = 10, Width = 92, ShowButtonSpinner = false, HorizontalContentAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add" };
        var remove = new Button { Content = "Remove" };
        var preview = new Button { Content = "Preview" };
        var operations = new List<MassOperation>();
        var operationsList = new ListBox { MaxHeight = 100, MinHeight = 42, Margin = new Thickness(0, 8, 0, 0) };
        var apply = new Button { Content = "Apply", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last batch", IsEnabled = massUndo.Count > 0 };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => window.Close();
        var message = new TextBlock { Text = $"{targets.Length:N0} players", Margin = new Thickness(0, 8, 0, 8) };
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, RowHeight = 27 };
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding("Player.Id"), Width = new DataGridLength(96) };
        idCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(idCol);
        table.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding("Player.DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Field"), Width = new DataGridLength(130) });
        var beforeCol = new DataGridTextColumn { Header = "Before", Binding = new Binding("Before"), Width = new DataGridLength(86) };
        beforeCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(beforeCol);
        var afterCol = new DataGridTextColumn { Header = "After", Binding = new Binding("After"), Width = new DataGridLength(86) };
        afterCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(afterCol);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { attribute, mode, value, add, remove, preview } };
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, undo } });
        var top = new StackPanel { Children = { controls, operationsList, message } };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(table); window.Content = root;
        IReadOnlyList<MassChange> plan = [];
        bool running = false, targetsDirty = false;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };

        void Invalidate() { plan = []; table.ItemsSource = null; apply.IsEnabled = false; }
        add.Click += (_, _) =>
        {
            var entry = new MassOperation((MassField)attribute.SelectedItem!, (MassEditMode)mode.SelectedItem!, (int)(value.Value ?? 0));
            operations.RemoveAll(o => o.Field == entry.Field);
            operations.Add(entry);
            operationsList.ItemsSource = operations.ToArray();
            Invalidate();
        };
        remove.Click += (_, _) =>
        {
            if (operationsList.SelectedItem is not MassOperation entry) return;
            operations.Remove(entry);
            operationsList.ItemsSource = operations.ToArray();
            Invalidate();
        };
        preview.Click += async (_, _) =>
        {
            if (running) return;
            try
            {
                running = busy = true;
                apply.IsEnabled = undo.IsEnabled = preview.IsEnabled = add.IsEnabled = remove.IsEnabled = false;
                if (operations.Count == 0) throw new ArgumentException("Choose at least one operation");
                var session = backend.Session;
                var actions = operations.ToArray();
                if (targetsDirty)
                {
                    var prior = targets;
                    targets = await Task.Run(() => prior.Select(p =>
                    {
                        var current = session.Players.Reload(p.Address);
                        return current is not null && current.Id == p.Id ? current : throw new InvalidOperationException("Player list changed; reopen Mass Edit");
                    }).ToArray());
                    targetsDirty = false;
                }
                var selected = targets;
                int noContract = actions.Any(o => o.Field.Wage) ? selected.Count(p => p.Contract is null) : 0;
                plan = await Task.Run(() =>
                {
                    var changes = new List<MassChange>();
                    foreach (var operation in actions)
                    {
                        if (operation.Field.Attribute is { } a)
                            changes.AddRange(PlayerMassEdit.Plan(selected, a, operation.Mode, operation.Value).Select(c => new MassChange(c, null)));
                        else if (operation.Field.Scalar is { } f)
                            changes.AddRange(PlayerScalarMassEdit.Plan(selected, session.PlayerFields, f, operation.Mode, operation.Value).Select(c => new MassChange(null, c)));
                        else if (operation.Field.Quality is { } q)
                            changes.AddRange(PlayerQualityMassEdit.Plan(selected, q, operation.Mode, operation.Value).Select(c => new MassChange(null, null, c)));
                        else if (operation.Field.Rating is { } r)
                            changes.AddRange(PlayerRatingMassEdit.Plan(selected, r, operation.Mode, operation.Value).Select(c => new MassChange(null, null, RatingChange: c)));
                        else if (operation.Field.Wage)
                            changes.AddRange(PlayerWageMassEdit.Plan(selected.Where(p => p.Contract is not null), operation.Mode, operation.Value).Select(c => new MassChange(null, null, WageChange: c)));
                    }
                    return changes;
                });
                table.ItemsSource = plan;
                message.Text = $"{plan.Count:N0} changes / {selected.Length:N0} players" +
                    (noContract > 0 ? $" · Wage unavailable: {noContract:N0}" : "");
                apply.IsEnabled = plan.Count > 0;
            }
            catch (Exception ex) { Invalidate(); message.Text = ex.Message; }
            finally { running = busy = false; preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true; undo.IsEnabled = massUndo.Count > 0; }
        };

        async Task Run(bool isUndo)
        {
            if (running) return;
            var batch = isUndo ? massUndo.Peek() : new List<MassChange>();
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
                        bool written = false;
                        try
                        {
                            backend.Edits.Execute(() =>
                            {
                                void RecordWrite()
                                {
                                    if (isUndo) batch.Remove(change); else batch.Add(change);
                                    written = true;
                                    completed++;
                                }
                                if (change.AttributeChange is { } attributeChange)
                                {
                                    var p = isUndo ? PlayerMassEdit.Undo(attributeChange, backend.Session.Players, backend.Session.Writer, RecordWrite)
                                        : PlayerMassEdit.Apply(attributeChange, backend.Session.Players, backend.Session.Writer, RecordWrite);
                                    backend.AcceptFrozenEdit(p, attributeChange.Attribute.ToString());
                                }
                                else if (change.ScalarChange is { } scalarChange)
                                {
                                    if (isUndo) PlayerPresetBatch.Undo(scalarChange, backend.Session.Players, backend.Session.PlayerFields, RecordWrite);
                                    else PlayerPresetBatch.Apply(scalarChange, backend.Session.Players, backend.Session.PlayerFields, RecordWrite);
                                }
                                else if (change.QualityChange is { } qualityChange)
                                {
                                    if (isUndo) PlayerQualityMassEdit.Undo(qualityChange, backend.Session.Players, backend.Session.Writer, RecordWrite);
                                    else PlayerQualityMassEdit.Apply(qualityChange, backend.Session.Players, backend.Session.Writer, RecordWrite);
                                }
                                else if (change.RatingChange is { } ratingChange)
                                {
                                    if (isUndo) PlayerRatingMassEdit.Undo(ratingChange, backend.Session.Players, backend.Session.Writer, RecordWrite);
                                    else PlayerRatingMassEdit.Apply(ratingChange, backend.Session.Players, backend.Session.Writer, RecordWrite);
                                }
                                else if (change.WageChange is { } wageChange)
                                {
                                    if (isUndo) PlayerWageMassEdit.Undo(wageChange, backend.Session.Players, backend.Session.Contracts, RecordWrite);
                                    else PlayerWageMassEdit.Apply(wageChange, backend.Session.Players, backend.Session.Contracts, RecordWrite);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            if (written) backend.StopFreezer("A batch write succeeded but its player could not be reloaded; reconnect before freezing");
                            failure = ex; break;
                        }
                    }
                });
                if (isUndo && batch.Count == 0) massUndo.Pop();
                else if (!isUndo && batch.Count > 0) massUndo.Push(batch);
                if (completed > 0) targetsDirty = true;
                Invalidate();
                message.Text = failure is null ? $"{(isUndo ? "Undone" : "Applied")}: {completed:N0}" : $"{completed:N0} completed. {failure.Message}";
            }
            finally
            {
                running = busy = false;
                preview.IsEnabled = add.IsEnabled = remove.IsEnabled = true;
                undo.IsEnabled = massUndo.Count > 0;
            }
        }
        apply.Click += async (_, _) => await Run(false);
        undo.Click += async (_, _) => await Run(true);
        await window.ShowDialog(this);
        if (backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
    }
}
