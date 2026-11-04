using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly Stack<ReleasePlan> releaseUndo = new();

    static ReleaseService ReleaseWriter(GameSession session) => new(session.Memory, session.Profile, session.ModuleBase,
        () => session.Staff.LoadAll());

    async Task ShowReleasePlayerAsync(PlayerRecord player)
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected) return;
        var session = backend.Session;
        var writer = ReleaseWriter(session);
        ReleasePlan plan;
        try
        {
            busy = true;
            plan = await Task.Run(() => writer.Plan(player));
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        finally { busy = false; }
        if (backend.State.Status != ConnectionStatus.Connected || !ReferenceEquals(session, backend.Session)) return;
        var window = new Window { Title = "Release Player", Width = 390, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = new Button { Content = "Release", Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel" };
        var message = new TextBlock { Text = $"{player.DisplayName}\n{player.ClubName}  →  Free agent\nSquads: {plan.Squads}   Captaincies: {plan.Captaincies}" };
        var problem = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        bool running = false;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };
        cancel.Click += (_, _) => window.Close();
        confirm.Click += async (_, _) =>
        {
            if (running || busy) return;
            running = busy = true; confirm.IsEnabled = cancel.IsEnabled = false;
            try
            {
                await Task.Run(() => backend.Edits.Execute(() => writer.Apply(plan)));
                releaseUndo.Push(plan);
                var fresh = session.Players.Reload(player.Address) ?? throw new InvalidOperationException("Player no longer readable");
                selected = fresh; ReplaceRow(fresh); RenderEditor();
                status.Text = $"Released {player.DisplayName}. Continue in FM to see updates.";
                running = false;
                window.Close();
            }
            catch (Exception ex) { problem.Text = UserMessage.For(ex); }
            finally { running = busy = false; confirm.IsEnabled = cancel.IsEnabled = true; }
        };
        window.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 12,
            Children = { message, problem, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } } },
        };
        await window.ShowDialog(this);
    }

    async Task UndoReleaseAsync()
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected || !releaseUndo.TryPeek(out var plan)) return;
        var session = backend.Session;
        try
        {
            busy = true;
            await Task.Run(() => backend.Edits.Execute(() => ReleaseWriter(session).Undo(plan)));
            releaseUndo.Pop();
            var fresh = session.Players.Reload(plan.Player.Address) ?? throw new InvalidOperationException("Player no longer readable");
            selected = fresh; ReplaceRow(fresh); RenderEditor();
            status.Text = $"Undid release for {plan.Player.DisplayName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
        finally { busy = false; }
    }
}
