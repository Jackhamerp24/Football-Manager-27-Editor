using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record SquadPresetStep(PlayerPresetChange? Scalar, InjuryRemovalPlan? Injury, UnhappinessRemovalPlan? Unhappiness = null, BanRemovalPlan? Ban = null, PlayerMassChange? Attribute = null, KeepPlayingPlan? Retirement = null)
    {
        public PlayerRecord Player => Scalar?.Player ?? Injury?.Player ?? Unhappiness?.Player ?? Ban?.Player ?? Attribute?.Player ?? Retirement!.Player;
        public string Field => Scalar?.Field.ToString() ?? Attribute?.Attribute.ToString() ?? (Injury is not null ? "Injuries" : Unhappiness is not null ? "Unhappiness" : Retirement is not null ? "Retirement" : "Bans");
        public long Before => Scalar?.Before ?? (long?)Attribute?.Before ?? Injury?.Injuries.Count ?? Unhappiness?.OldCount ?? Ban?.OldCount ?? 0;
        public long After => Scalar?.After ?? (long?)Attribute?.After ?? Unhappiness?.NewCount ?? Ban?.NewCount ?? 0;
        public string BeforeText => Retirement is { } retirement ? retirement.RetirementDate?.ToString("yyyy-MM-dd") ?? "None" : Before.ToString(Inv);
        public string AfterText => Retirement is not null ? "Continue" : After.ToString(Inv);
    }
    readonly Stack<List<SquadPresetStep>> squadPresetUndo = new();

    void AddSquadPresetButtons(IReadOnlyList<PlayerRecord> squad)
    {
        var fitness = new Button { Content = "Fitness" };
        var morale = new Button { Content = "Morale" };
        var heal = new Button { Content = "Heal all" };
        var unhappiness = new Button { Content = "Remove unhappiness" };
        var bans = new Button { Content = "Lift bans" };
        var max = new Button { Content = "Max attributes" };
        var retirement = new Button { Content = "Prevent retirement", IsEnabled = new PlannedRetirements(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase).IsSupported };
        var undo = new Button { Content = "Undo preset", IsEnabled = squadPresetUndo.Count > 0 };
        fitness.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, false, false, false);
        morale.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Morale, false, false, false, false);
        heal.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, true, false, false);
        unhappiness.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, false, true, false);
        bans.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, false, false, true);
        max.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, false, false, false, true);
        retirement.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, false, false, false, false, false, true);
        undo.Click += async (_, _) => await ShowSquadPresetAsync(squad, PlayerPreset.Fitness, true, false, false, false);
        clubEditorPanel.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { heal, fitness, morale, bans, unhappiness, max, retirement, undo } });
    }

    async Task ShowSquadPresetAsync(IReadOnlyList<PlayerRecord> squad, PlayerPreset preset, bool undoing, bool heal, bool removeUnhappiness, bool liftBans, bool maxAttributes = false, bool preventRetirement = false)
    {
        if (undoing && squadPresetUndo.Count == 0) return;
        List<SquadPresetStep> changes;
        int unavailable = 0;
        try
        {
            changes = undoing ? squadPresetUndo.Peek().AsEnumerable().Reverse().ToList()
                : heal || removeUnhappiness || liftBans || maxAttributes || preventRetirement ? [] : PlayerPresetBatch.Plan(squad, backend.Session.PlayerFields, preset).Select(c => new SquadPresetStep(c, null)).ToList();
            if (maxAttributes && !undoing)
            {
                var max = MaxAttributes.Plan(squad);
                unavailable = max.Unavailable;
                changes.AddRange(max.Changes.Select(c => new SquadPresetStep(null, null, Attribute: c)));
            }
            if (preventRetirement && !undoing)
            {
                if (busy) return;
                var session = backend.Session;
                if (squad.Any(p => p.Id is not > 0) || squad.Select(p => p.Id).Distinct().Count() != squad.Count)
                    throw new InvalidOperationException("Players must have unique positive IDs");
                busy = true;
                try
                {
                    var plans = await Task.Run(() =>
                    {
                        var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
                        return squad.Select(writer.TryPlan).Where(p => p is not null).Select(p => new SquadPresetStep(null, null, Retirement: p)).ToArray();
                    });
                    changes.AddRange(plans);
                }
                finally { busy = false; }
            }
            if (heal && !undoing)
            {
                var writer = new InjuryWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
                changes.InsertRange(0, squad.Select(writer.PlanRemoveAll).Where(p => p is not null).Select(p => new SquadPresetStep(null, p)).ToArray());
            }
            if (removeUnhappiness && !undoing)
            {
                var session = backend.Session;
                var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date is unavailable");
                var writer = new UnhappinessWriter(session.Memory, session.Profile, session.ModuleBase);
                changes.AddRange(squad.Select(p => writer.PlanRemoveActive(p, date)).Where(p => p is not null).Select(p => new SquadPresetStep(null, null, p)));
            }
            if (liftBans && !undoing)
            {
                var session = backend.Session;
                var writer = new BanWriter(session.Memory, session.Profile, session.ModuleBase);
                changes.AddRange(squad.Select(writer.PlanRemoveMatchBans).Where(p => p is not null).Select(p => new SquadPresetStep(null, null, null, p)));
            }
        }
        catch (Exception ex) { status.Text = $"Preset: {ex.Message}"; return; }
        var window = new Window { Title = undoing ? "Undo preset" : $"{(preventRetirement ? "Prevent retirement" : maxAttributes ? "Max attributes" : heal ? "Heal" : removeUnhappiness ? "Remove unhappiness" : liftBans ? "Lift bans" : preset.ToString())} — Preview", Width = 600, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, ItemsSource = changes, RowHeight = 27 };
        table.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Player.Id"), Width = new DataGridLength(90) });
        table.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding("Player.DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding("Field"), Width = new DataGridLength(120) });
        table.Columns.Add(new DataGridTextColumn { Header = undoing ? "Restore" : "Before", Binding = new Binding("BeforeText"), Width = new DataGridLength(95) });
        table.Columns.Add(new DataGridTextColumn { Header = undoing ? "Current" : "After", Binding = new Binding("AfterText"), Width = new DataGridLength(95) });
        var apply = new Button { Content = undoing ? "Undo" : "Apply", IsEnabled = changes.Count > 0, Classes = { "accent" } };
        var text = new TextBlock { Text = $"{changes.Count:N0} changes" + (unavailable > 0 ? $", {unavailable:N0} unavailable" : ""), VerticalAlignment = VerticalAlignment.Center };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0), Children = { apply, text } };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(table); window.Content = root;
        bool running = false;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };
        apply.Click += async (_, _) =>
        {
            if (running) return;
            running = busy = true; apply.IsEnabled = false;
            var batch = undoing ? squadPresetUndo.Peek() : new List<SquadPresetStep>();
            int count = 0;
            Exception? failure = null;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var change in changes)
                    {
                        try
                        {
                            backend.Edits.Execute(() =>
                            {
                                void Record() { if (undoing) batch.Remove(change); else batch.Add(change); count++; }
                                if (change.Scalar is { } scalar)
                                {
                                    if (undoing) PlayerPresetBatch.Undo(scalar, backend.Session.Players, backend.Session.PlayerFields, Record);
                                    else PlayerPresetBatch.Apply(scalar, backend.Session.Players, backend.Session.PlayerFields, Record);
                                }
                                else if (change.Injury is { } injury)
                                {
                                    var writer = new InjuryWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
                                    if (undoing) writer.UndoRemoval(injury, Record); else writer.ApplyRemoval(injury, Record);
                                }
                                else if (change.Unhappiness is { } unhappy)
                                {
                                    var writer = new UnhappinessWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
                                    if (undoing) writer.UndoRemoval(unhappy, Record); else writer.ApplyRemoval(unhappy, Record);
                                }
                                else if (change.Ban is { } ban)
                                {
                                    var writer = new BanWriter(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
                                    if (undoing) writer.UndoRemoval(ban, Record); else writer.ApplyRemoval(ban, Record);
                                }
                                else if (change.Attribute is { } attribute)
                                {
                                    var p = undoing ? PlayerMassEdit.Undo(attribute, backend.Session.Players, backend.Session.Writer, Record)
                                        : PlayerMassEdit.Apply(attribute, backend.Session.Players, backend.Session.Writer, Record);
                                    backend.AcceptFrozenEdit(p, attribute.Attribute.ToString());
                                }
                                else if (change.Retirement is { } retirement)
                                {
                                    var session = backend.Session;
                                    var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
                                    if (undoing) writer.Undo(retirement, Record);
                                    else
                                    {
                                        var current = writer.Replan(retirement);
                                        writer.Apply(current, () => { batch.Add(change with { Retirement = current }); count++; });
                                    }
                                }
                            });
                        }
                        catch (Exception ex) { failure = ex; break; }
                    }
                });
                if (undoing && batch.Count == 0) squadPresetUndo.Pop();
                else if (!undoing && batch.Count > 0) squadPresetUndo.Push(batch);
                text.Text = failure is null ? $"{count:N0} changes applied" : $"{count:N0} completed. {failure.Message}";
            }
            finally { running = busy = false; }
        };
        await window.ShowDialog(this);
        if (clubsActive && backend.State.Status == ConnectionStatus.Connected) RenderClubEditor();
    }
}
