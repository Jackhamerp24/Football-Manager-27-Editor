using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    Control BuildForceAcceptTransferSection(GameSession session)
    {
        var hook = session.ForceTransferOffersHook;
        var panel = new StackPanel { Spacing = 6 };

        var heading = new TextBlock
        {
            Text = "Force Accept Transfer (Experimental)",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(UiKit.TitleWithInfo(
            heading,
            "Clubs accept transfer offers made by the club you manage. Works while active without affecting offers made by AI clubs."));

        var on = new CheckBox { Content = "On", VerticalAlignment = VerticalAlignment.Center };
        var refreshBtn = new Button
        {
            Content = "Refresh",
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(10, 0),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var removeBtn = new Button
        {
            Content = "Remove",
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(10, 0),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = false,
        };
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { on, refreshBtn, removeBtn },
        });

        var stateText = new TextBlock
        {
            Text = "Checking…",
            FontSize = 12.5,
            Foreground = UiKit.Brush("#8FA3B8"),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(stateText);

        var status = new TextBlock
        {
            Text = "",
            FontSize = 12,
            Foreground = UiKit.Brush("#8FA3B8"),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        panel.Children.Add(status);

        bool syncing = false;
        bool busy = false;

        IReadOnlyList<(int Id, string Name)> ReadManagedClubs()
        {
            if (session.Profile.Club?.Id is not { } idOff) return [];
            var result = new List<(int Id, string Name)>();
            var seen = new HashSet<int>();
            foreach (var staff in session.Staff.LoadAll())
            {
                if (staff.ManagerAttributes is null || staff.Club is not { } club) continue;
                var raw = new byte[4];
                if (!session.Memory.TryRead(club.Address + (ulong)idOff, raw)) continue;
                int id = BitConverter.ToInt32(raw, 0);
                if (id == 0 || id == -1 || !seen.Add(id)) continue;
                result.Add((id, string.IsNullOrWhiteSpace(club.Name) ? $"club {id}" : club.Name));
            }
            return result;
        }

        void RenderState(ForceTransferOffersState? state, IReadOnlyList<(int Id, string Name)> clubs)
        {
            syncing = true;
            try
            {
                on.IsChecked = state?.Enabled == true;
                removeBtn.IsEnabled = state is not null;
                if (state is null)
                {
                    stateText.Text = clubs.Count == 0
                        ? "Not installed · no managed club found"
                        : $"Not installed · managed clubs: {string.Join(", ", clubs.Select(c => $"{c.Name} ({c.Id})"))}";
                }
                else
                {
                    var nameMap = clubs.ToDictionary(c => c.Id, c => c.Name);
                    string clubLabel = state.ClubIds.Count == 0
                        ? "none"
                        : string.Join(", ", state.ClubIds.Select(id => nameMap.TryGetValue(id, out var n) ? $"{n} ({id})" : $"club {id}"));
                    stateText.Text = $"{(state.Enabled ? "On" : "Off")} · offers seen {state.Calls} · accepted {state.Forced} · clubs: {clubLabel}";
                }
            }
            finally { syncing = false; }
        }

        async Task RefreshSectionAsync()
        {
            if (busy) return;
            busy = true;
            try
            {
                var (state, clubs) = await Task.Run(() => (hook.Read(), ReadManagedClubs()));
                RenderState(state, clubs);
            }
            catch (Exception ex)
            {
                status.Text = UserMessage.For(ex);
                status.IsVisible = true;
            }
            finally { busy = false; }
        }

        on.IsCheckedChanged += async (_, _) =>
        {
            if (syncing || busy) return;
            bool enable = on.IsChecked == true;
            busy = true;
            status.IsVisible = false;
            try
            {
                var result = await Task.Run(() =>
                {
                    var clubs = ReadManagedClubs();
                    if (enable)
                    {
                        if (clubs.Count == 0)
                            throw new InvalidOperationException("No managed club found in the loaded save.");
                        var s = hook.Configure(true, clubs.Select(c => c.Id));
                        return (s, clubs);
                    }
                    else
                    {
                        var existing = hook.Read();
                        var ids = existing?.ClubIds ?? clubs.Select(c => c.Id).ToList();
                        var s = existing is null ? null : hook.Configure(false, ids);
                        return (s, clubs);
                    }
                });
                RenderState(result.s, result.clubs);
                status.Text = enable ? "Force Accept Transfer enabled." : "Force Accept Transfer turned off.";
                status.IsVisible = true;
            }
            catch (Exception ex)
            {
                syncing = true;
                on.IsChecked = !enable;
                syncing = false;
                status.Text = UserMessage.For(ex);
                status.IsVisible = true;
            }
            finally { busy = false; }
        };

        removeBtn.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            status.IsVisible = false;
            try
            {
                var clubs = await Task.Run(() =>
                {
                    hook.Uninstall();
                    return ReadManagedClubs();
                });
                RenderState(null, clubs);
                status.Text = "Force Accept Transfer removed.";
                status.IsVisible = true;
            }
            catch (Exception ex)
            {
                status.Text = UserMessage.For(ex);
                status.IsVisible = true;
            }
            finally { busy = false; }
        };

        refreshBtn.Click += async (_, _) =>
        {
            status.IsVisible = false;
            await RefreshSectionAsync();
        };

        _ = RefreshSectionAsync();
        return panel;
    }
}
