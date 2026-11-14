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

    /// <summary>Opens at most one at a time: a second click while it is loading or open does nothing.</summary>
    async Task ShowReleasePlayerAsync(PlayerRecord player)
    {
        if (!openers.Add("ShowReleasePlayerAsync")) return;
        try { await ShowReleasePlayerAsyncOnce(player); }
        finally { openers.Remove("ShowReleasePlayerAsync"); }
    }

    async Task ShowReleasePlayerAsyncOnce(PlayerRecord player)
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
        var window = new Window { Title = AppLanguage.Get("Ui_Release_Player"), Width = 390, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = new Button { Content = AppLanguage.Get("Label_Release"), Classes = { "accent" } };
        var cancel = new Button { Content = AppLanguage.Get("Label_Cancel") };
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
                await Task.Run(() => backend.Edits.Execute("Player", player.DisplayName, "Page save", () => writer.Apply(plan)));
                releaseUndo.Push(plan);
                var fresh = session.Players.Reload(player.Address) ?? throw new InvalidOperationException("Player no longer readable");
                selected = fresh; ReplaceRow(fresh); RenderEditor();
                status.Text = string.Format(AppLanguage.T("Released {0}. Continue in FM to see updates."), player.DisplayName);
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
            await Task.Run(() => backend.Edits.Execute("Player", "Release", "Undo", () => ReleaseWriter(session).Undo(plan)));
            releaseUndo.Pop();
            var fresh = session.Players.Reload(plan.Player.Address) ?? throw new InvalidOperationException("Player no longer readable");
            selected = fresh; ReplaceRow(fresh); RenderEditor();
            status.Text = string.Format(AppLanguage.T("Undid release for {0}. Continue in FM to see updates."), plan.Player.DisplayName);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
        finally { busy = false; }
    }
}
