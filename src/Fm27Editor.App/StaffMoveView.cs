using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly Stack<StaffMovePlan> staffMoveUndo = new();

    Control StaffMovePanel(StaffRecord staff)
    {
        var session = backend.Session;
        var service = new StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
        IReadOnlyList<ClubEntry> all = clubCache ?? [];
        StaffMovePlan? plan = null;
        var search = new TextBox { Watermark = "Club or ID", Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        var clubs = new ListBox { Width = 340, Height = 190, HorizontalAlignment = HorizontalAlignment.Left };
        var details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Brush("#F59E0B") };
        var apply = new Button { Content = "Confirm transfer", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last staff move", IsEnabled = staffMoveUndo.Count > 0 };

        void Invalidate()
        {
            plan = null; apply.IsEnabled = false; details.Text = ""; error.Text = "";
        }
        void Filter()
        {
            string query = Fold(search.Text?.Trim() ?? "");
            clubs.ItemsSource = all.Where(c => c.Address != staff.Club?.Address && c.Id is > 0 &&
                    (query.Length == 0 || Fold(c.DisplayName).Contains(query) || Fold(c.Name ?? "").Contains(query) || c.Id.ToString() == query))
                .OrderBy(c => c.DisplayName).Take(200).Select(c => new ClubOption(c)).ToArray();
            Invalidate();
        }
        void UpdateStaff(StaffRecord before)
        {
            var fresh = session.Staff.Reload(before.Address) ?? throw new InvalidOperationException("Staff no longer readable");
            staffRows = staffRows.Select(r => r.Staff.Address == before.Address
                ? r with { Staff = fresh, Club = fresh.Club?.DisplayName ?? "", Job = StaffLabels.Job(fresh.JobCode) } : r).ToList();
            updatingStaffFilters = true;
            staffClubFilter.SelectedIndex = 0;
            updatingStaffFilters = false;
            PopulateStaffFilters();
            FilterStaff();
            selectedStaff = fresh;
            staffGrid.SelectedItem = staffRows.FirstOrDefault(r => r.Staff.Address == fresh.Address);
            RenderStaffEditor();
        }

        search.TextChanged += (_, _) => Filter();
        clubs.SelectionChanged += (_, _) =>
        {
            Invalidate();
            if (clubs.SelectedItem is not ClubOption option) return;
            try
            {
                var from = all.Single(c => c.Address == staff.Club?.Address);
                plan = service.Plan(staff, from, option.Club, session.ReadGameDate());
                details.Text = $"{plan.From.DisplayName}  →  {plan.To.DisplayName}\n{string.Join(", ", plan.Departments)}";
                apply.IsEnabled = true;
            }
            catch (Exception ex) { error.Text = UserMessage.For(ex); }
        };
        apply.Click += (_, _) =>
        {
            if (plan is not { } selectedPlan) return;
            try
            {
                backend.Edits.Execute(() => service.Apply(selectedPlan));
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
                backend.Edits.Execute(() => service.Undo(last));
                staffMoveUndo.Pop();
                clubCache = null;
                UpdateStaff(last.Staff);
                status.Text = $"Undid move for {last.Staff.DisplayName}. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        if (clubCache is null)
        {
            _ = Task.Run(() => session.LoadClubs(session.Players.LoadAll())).ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                if (backend.State.Status != ConnectionStatus.Connected || !ReferenceEquals(backend.Session, session)) return;
                if (task.IsFaulted) { error.Text = UserMessage.For(task.Exception!.GetBaseException()); return; }
                clubCache = all = task.Result;
                Filter();
            }));
        }
        else Filter();

        return new StackPanel
        {
            Spacing = 8, Margin = new Thickness(0, 6), HorizontalAlignment = HorizontalAlignment.Left,
            Children = { new TextBlock { Text = staff.Club?.DisplayName ?? "" }, search, clubs, details, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, undo } } },
        };
    }
}
