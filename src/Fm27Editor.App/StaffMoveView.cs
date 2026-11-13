using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly Stack<StaffMovePlan> staffMoveUndo = new();

    sealed record RoleOption(byte Code, string Name) { public override string ToString() => Name; }

    Control StaffMovePanel(StaffRecord staff)
    {
        var session = backend.Session;
        var service = new StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
        IReadOnlyList<ClubEntry> all = clubCache ?? [];
        StaffMovePlan? plan = null;
        ClubOption? chosenClub = null;
        var pickClub = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = "Choose destination club…" };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.TextPrimary, FontSize = 13.5, IsVisible = false };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F59E0B"), FontSize = 13, IsVisible = false };
        // Role and weekly wage the member gets at the new club (the current ones to start with); the move itself takes effect today.
        var roles = StaffMoveService.SupportedJobs.Select(j => new RoleOption(j, StaffLabels.Job(j))).ToList();
        var currentRole = roles.FirstOrDefault(r => r.Code == staff.JobCode);
        if (currentRole is null && staff.JobCode is { } unlisted) roles.Insert(0, currentRole = new RoleOption((byte)unlisted, StaffLabels.Job(unlisted)));
        var roleBox = new ComboBox { ItemsSource = roles, SelectedItem = currentRole, Width = 220 };
        int? currentWage = null;
        try { currentWage = session.StaffContracts.Read(staff)?.Wage; } catch (Exception) { }
        var wageBox = new NumericUpDown { Minimum = 0, Maximum = 1_000_000_000, Increment = 100, FormatString = "0", Width = 140, Value = currentWage ?? 0, IsEnabled = currentWage is not null };
        var gameToday = session.ReadGameDate() ?? DateOnly.FromDateTime(DateTime.Today);
        // 30 June two years on: what the game gave two of the three free agents it signed in the traces.
        var expiryBox = new DateBox { Text = DateSetting.Format(new DateOnly(gameToday.Year + 2, 6, 30)), Width = 140 };
        var freeService = new FreeStaffSigning(session.Memory, session.Profile, session.ModuleBase);
        FreeStaffSigningPlan? freePlan = null;
        var apply = new Button { Content = "Confirm transfer", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last staff move", IsEnabled = staffMoveUndo.Count > 0, IsVisible = staffMoveUndo.Count > 0 };

        void Invalidate()
        {
            plan = null;
            freePlan = null;
            apply.IsEnabled = false;
            details.Text = ""; details.IsVisible = false;
            error.Text = ""; error.IsVisible = false;
        }
        void Filter()
        {
            Invalidate();
        }
        void UpdateStaff(StaffRecord before)
        {
            var fresh = session.Staff.Reload(before.Address) ?? throw new InvalidOperationException("Staff no longer readable");
            staffScreen.ReloadOpen();
            staffScreen.RefreshFilters(showAllClubs: true);
        }

        void Replan()
        {
            Invalidate();
            if (chosenClub is not { } option) return;
            if (staff.Club is null)
            {
                try
                {
                    if (roleBox.SelectedItem is not RoleOption role) { error.Text = "Choose the role."; error.IsVisible = true; return; }
                    if (wageBox.Value is not { } wage) { error.Text = "Enter the weekly wage."; error.IsVisible = true; return; }
                    if (!DateBox.TryParse(expiryBox.Text, out var expiry)) { error.Text = $"Enter the contract end as {DateSetting.Hint}"; error.IsVisible = true; return; }
                    freePlan = freeService.Plan(staff, option.Club, role.Code, (int)wage, session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"), expiry);
                    details.Text = $"Free agent  →  {option.Club.DisplayName}\n{role.Name}, {StaffMoveService.DepartmentOf(role.Code)}";
                    details.IsVisible = true;
                    apply.IsEnabled = true;
                }
                catch (Exception ex) { error.Text = UserMessage.For(ex); error.IsVisible = true; }
                return;
            }
            try
            {
                var from = all.FirstOrDefault(c => c.Address == staff.Club?.Address)
                    ?? throw new InvalidOperationException("The current club can't be found; reload the save");
                byte? newJob = roleBox.SelectedItem is RoleOption r && r.Code != staff.JobCode ? r.Code : null;
                int? newWage = currentWage is { } w && wageBox.Value is { } v && (int)v != w ? (int)v : null;
                plan = service.Plan(staff, from, option.Club, session.ReadGameDate(), newJob, newWage);
                details.Text = $"{plan.From.DisplayName}  →  {plan.To.DisplayName}\n{string.Join(", ", plan.Departments)}";
                details.IsVisible = true;
                apply.IsEnabled = true;
            }
            catch (Exception ex)
            {
                error.Text = UserMessage.For(ex);
                error.IsVisible = true;
            }
        }
        roleBox.SelectionChanged += (_, _) => Replan();
        wageBox.ValueChanged += (_, _) => Replan();
        expiryBox.LostFocus += (_, _) => Replan();
        apply.Click += (_, _) =>
        {
            if (freePlan is { } signing)
            {
                try
                {
                    backend.Edits.Execute("Staff", staff.DisplayName, "Move", () =>
                    {
                        if (session.ReadGameDate() != signing.Today) throw new StaleObjectException("The game date changed; nothing was written");
                        freeService.Apply(signing, session.AllocateGameBlock);
                    });
                    clubCache = null;
                    UpdateStaff(staff);
                    status.Text = $"{staff.DisplayName} joined {signing.To.DisplayName}. Continue in FM to see updates.";
                    apply.IsEnabled = false;
                }
                catch (Exception ex) { Invalidate(); status.Text = UserMessage.For(ex); }
                return;
            }
            if (plan is not { } selectedPlan) return;
            try
            {
                backend.Edits.Execute("Staff", selectedPlan.Staff.DisplayName, "Move", () => service.Apply(selectedPlan));
                staffMoveUndo.Push(selectedPlan);
                clubCache = null;
                UpdateStaff(staff);
                status.Text = $"Moved {staff.DisplayName} to {selectedPlan.To.DisplayName}. Continue in FM to see updates.";
            }
            catch (Exception ex) { Invalidate(); status.Text = UserMessage.For(ex); }
        };
        undo.Click += (_, _) =>
        {
            if (!staffMoveUndo.TryPeek(out var last)) return;
            try
            {
                backend.Edits.Execute("Staff", last.Staff.DisplayName, "Move: Undo", () => service.Undo(last));
                staffMoveUndo.Pop();
                clubCache = null;
                UpdateStaff(last.Staff);
                status.Text = $"Undid move for {last.Staff.DisplayName}. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        // A free agent has no contract: the wage must be entered, and the contract gets an end date.
        if (staff.Club is null) { wageBox.IsEnabled = true; wageBox.Value = null; }
        if (clubCache is null)
        {
            _ = Task.Run(() => session.LoadClubs(session.Players.LoadAll())).ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                if (backend.State.Status != ConnectionStatus.Connected || !ReferenceEquals(backend.Session, session)) return;
                if (task.IsFaulted)
                {
                    error.Text = UserMessage.For(task.Exception!.GetBaseException());
                    error.IsVisible = true;
                    return;
                }
                clubCache = all = task.Result;
                Filter();
            }));
        }
        else Filter();

        pickClub.Click += async (_, _) =>
        {
            var choices = all.Where(c => c.Address != staff.Club?.Address && c.Id is > 0 && c.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .OrderBy(c => c.DisplayName).Select(c => new ClubOption(c)).ToList();
            var chosen = await PickerDialog.Show<ClubOption>(this, "Choose destination club", q => choices.Where(o => PickerSearch.Hit(q, o.Club.DisplayName, o.Club.ShortName, o.Club.Id)).Select(o => (o.Club.DisplayName, o)).ToList());
            if (chosen is null) return;
            chosenClub = chosen;
            pickClub.Content = chosen.Club.DisplayName;
            Replan();
        };
        var curRow = new DockPanel { LastChildFill = true };
        var curVal = new TextBlock { Text = staff.Club?.DisplayName ?? "None", FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary };
        DockPanel.SetDock(curVal, Dock.Right);
        curRow.Children.Add(curVal);
        curRow.Children.Add(new TextBlock { Text = "Current Club", Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });

        var leftCard = UiKit.Card("Destination Club", new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 10),
            Children = { curRow, pickClub },
        });
        var rightCard = UiKit.Card("Transfer Summary", new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12, 10),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Role", Width = 50, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary }, roleBox } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Wages", Width = 50, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary }, wageBox } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = staff.Club is null, Children = { new TextBlock { Text = "Until", Width = 50, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary }, expiryBox } },
                details,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, undo } },
            },
        });

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("356,*"),
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        split.Children.Add(leftCard);
        split.Children.Add(rightCard);
        return split;
    }
}
