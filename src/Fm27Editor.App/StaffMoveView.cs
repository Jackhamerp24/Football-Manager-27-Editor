using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        var search = new TextBox { Watermark = "Filter club name or ID...", HorizontalAlignment = HorizontalAlignment.Stretch };
        var clubs = new ListBox
        {
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UiKit.SurfaceInput,
            BorderBrush = UiKit.BorderControl,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.TextPrimary, FontSize = 12, IsVisible = false };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#F59E0B"), FontSize = 11.5, IsVisible = false };
        var apply = new Button { Content = "Confirm transfer", IsEnabled = false, Classes = { "accent" } };
        var undo = new Button { Content = "Undo last staff move", IsEnabled = staffMoveUndo.Count > 0, IsVisible = staffMoveUndo.Count > 0 };

        void Invalidate()
        {
            plan = null;
            apply.IsEnabled = false;
            details.Text = ""; details.IsVisible = false;
            error.Text = ""; error.IsVisible = false;
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
                details.IsVisible = true;
                apply.IsEnabled = true;
            }
            catch (Exception ex)
            {
                error.Text = UserMessage.For(ex);
                error.IsVisible = true;
            }
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

        var curRow = new DockPanel { LastChildFill = true };
        var curVal = new TextBlock { Text = staff.Club?.DisplayName ?? "None", FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary };
        DockPanel.SetDock(curVal, Dock.Right);
        curRow.Children.Add(curVal);
        curRow.Children.Add(new TextBlock { Text = "Current Club", Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });

        var leftCard = UiKit.Card("Destination Club", new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 10),
            Children = { curRow, search, clubs },
        });
        var rightCard = UiKit.Card("Transfer Summary", new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12, 10),
            Children =
            {
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
