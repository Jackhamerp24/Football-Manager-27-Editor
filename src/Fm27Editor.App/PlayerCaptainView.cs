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

    /// <summary>
    /// FMRTE's "Make Captain" / "Make Vice Captain": one click makes this player the role on his team, through the edit
    /// journal (undo works), with no dialog. A second click while it runs does nothing.
    /// </summary>
    async Task MakeCaptainAsync(PlayerRecord player, CaptainRole role)
    {
        if (busy || backend.State.Status != ConnectionStatus.Connected) return;
        var session = backend.Session;
        var writer = CaptainWriter(session);
        string label = TeamCaptainService.Label(role);
        try
        {
            busy = true;
            var plan = await Task.Run(() => writer.Plan(player, role));
            if (!ReferenceEquals(session, backend.Session)) return;
            if (plan.Previous == plan.Person) { status.Text = $"{player.DisplayName} is already {label}."; return; }
            await Task.Run(() => backend.Edits.Execute("Player", player.DisplayName, $"Make {label}", () => writer.Apply(plan)));
            captainUndo.Push(plan);
            status.Text = $"{player.DisplayName} is now {label} of {player.ClubName}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        finally { busy = false; }
        RenderEditor();
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
