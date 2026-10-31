using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public enum ConnectionStatus { Disconnected, UnsupportedBuild, Connected }
public sealed record ConnectionState(ConnectionStatus Status, string Message);
public interface IEditorBackend
{
    ConnectionState State { get; }
    DateOnly? GameDate { get; }
    event EventHandler? StateChanged;
    Task ConnectAsync(CancellationToken ct);
    PlayerRepository Players { get; }
    IEditService Edits { get; }
}

public sealed class EditorBackend : IEditorBackend, IDisposable
{
    GameSession? session;
    public GameSession Session => session ?? throw new InvalidOperationException("Not connected");
    public ConnectionState State { get; private set; } = new(ConnectionStatus.Disconnected, "Disconnected");
    public DateOnly? GameDate => session?.ReadGameDate();
    public event EventHandler? StateChanged;
    public PlayerRepository Players => session?.Players ?? throw new InvalidOperationException("Not connected");
    public IEditService Edits { get; private set; } = null!;
    public async Task ConnectAsync(CancellationToken ct)
    {
        session?.Dispose(); session = null;
        try
        {
            var attached = await Task.Run(() => GameSession.Attach(GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets")), true), ct);
            session = attached;
            Edits = new EditService(attached.Writer, attached.Players);
            State = new(ConnectionStatus.Connected, attached.Profile.Build);
        }
        catch (UnsupportedBuildException ex) { State = new(ConnectionStatus.UnsupportedBuild, UserMessage.For(ex)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { State = new(ConnectionStatus.Disconnected, UserMessage.For(ex)); }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => session?.Dispose();
}

public sealed record EditRecord(ulong Entity, string Field, int OldValue, int NewValue, DateTimeOffset Time, bool Succeeded, string? Error);
public interface IEditService
{
    IReadOnlyList<EditRecord> History { get; }
    PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value);
    PlayerRecord Undo(PlayerRecord player, FieldDescriptor<PlayerRecord> field, EditRecord record);
    void Execute(Action action);
}
public sealed class EditService(PlayerWriter writer, PlayerRepository players) : IEditService
{
    readonly object gate = new();
    readonly List<EditRecord> history = [];
    public IReadOnlyList<EditRecord> History { get { lock (gate) return history.ToArray(); } }
    public void Execute(Action action) { lock (gate) action(); }
    public PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value)
    {
        lock (gate)
        {
            int old = field.Read(player);
            try
            {
                if (field.Write is null) throw new InvalidOperationException("Field is read-only");
                field.Write(writer, player, value);
                history.Add(new(player.Address, field.Id, old, value, DateTimeOffset.Now, true, null));
            }
            catch (Exception ex)
            {
                history.Add(new(player.Address, field.Id, old, value, DateTimeOffset.Now, false, ex.Message));
                throw;
            }
            return players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer exists");
        }
    }
    public PlayerRecord Undo(PlayerRecord player, FieldDescriptor<PlayerRecord> field, EditRecord record)
    {
        lock (gate)
        {
            if (!record.Succeeded || record.Entity != player.Address || record.Field != field.Id || field.Read(player) != record.NewValue)
                throw new StaleObjectException("Cannot undo: the value or player has changed");
            return Apply(player, field, record.OldValue);
        }
    }
}
