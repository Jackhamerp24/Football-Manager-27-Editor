using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly Stack<CaptainPlan> captainUndo = new();

    static TeamCaptainService CaptainWriter(GameSession session) =>
        new(session.Memory, session.Profile, session.ModuleBase);

    async Task ShowSetCaptainAsync(PlayerRecord player, CaptainRole role)
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected) return;
        var session = backend.Session;
        var writer = CaptainWriter(session);
        CaptainPlan plan;
        try
        {
            busy = true;
            plan = await Task.Run(() => writer.Plan(player, role));
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        finally { busy = false; }
        if (backend.State.Status != ConnectionStatus.Connected || !ReferenceEquals(session, backend.Session)) return;
        var window = new Window { Title = $"Set {TeamCaptainService.Label(role)}", Width = 390, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = new Button { Content = "Apply", Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel" };
        var message = new TextBlock
        {
            Text = $"{player.DisplayName}\n{player.ClubName}\n{TeamCaptainService.Label(role)}: 0x{plan.Previous:X}  →  0x{plan.Person:X}",
        };
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
                captainUndo.Push(plan);
                status.Text = $"{player.DisplayName} is now {TeamCaptainService.Label(role)}. Continue in FM to see updates.";
                running = false;
                window.Close();
                RenderEditor();
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

    async Task UndoCaptainAsync()
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected || !captainUndo.TryPeek(out var plan)) return;
        var session = backend.Session;
        try
        {
            busy = true;
            await Task.Run(() => backend.Edits.Execute("Player", "Captain change", "Undo", () => CaptainWriter(session).Undo(plan)));
            captainUndo.Pop();
            status.Text = $"Restored {TeamCaptainService.Label(plan.Role)} for {plan.Player.DisplayName}. Continue in FM to see updates.";
            RenderEditor();
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
        finally { busy = false; }
    }
}
