using Fm27Editor.Core.Game;
using Fm27Editor.Core.Clubs;
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
    public FreezeRunner? Freezer { get; private set; }
    public string? FreezeError { get; private set; }
    static string FreezePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "frozen.json");
    public GameSession Session => session ?? throw new InvalidOperationException("Not connected");
    public ConnectionState State { get; private set; } = new(ConnectionStatus.Disconnected, "Disconnected");
    public DateOnly? GameDate => session?.ReadGameDate();
    public event EventHandler? StateChanged;
    public PlayerRepository Players => session?.Players ?? throw new InvalidOperationException("Not connected");
    public IEditService Edits { get; private set; } = null!;
    public async Task ConnectAsync(CancellationToken ct)
    {
        Freezer = null;
        FreezeError = null;
        session?.Dispose(); session = null;
        try
        {
            var attached = await Task.Run(() => GameSession.Attach(GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets")), true), ct);
            session = attached;
            Edits = new EditService(attached.Writer, attached.Players, AcceptFrozenEdit);
            if (attached.CanWrite)
            {
                try { Freezer = await Task.Run(() => new FreezeRunner(attached, FreezePath), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { FreezeError = ex.Message; }
            }
            State = new(ConnectionStatus.Connected, attached.CanWrite ? attached.Profile.Build :
                $"{attached.Profile.Build} (read-only)");
        }
        catch (UnsupportedBuildException ex) { State = new(ConnectionStatus.UnsupportedBuild, UserMessage.For(ex)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { State = new(ConnectionStatus.Disconnected, UserMessage.For(ex)); }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void TickFreezer()
    {
        if (Freezer is not { } freezer || (freezer.Players.Count == 0 && freezer.Clubs.Count == 0)) return;
        try { Edits.Execute(() => freezer.Tick()); }
        catch (Exception ex) { Freezer = null; FreezeError = ex.Message; }
    }

    public void StopFreezer(string reason) { Freezer = null; FreezeError = reason; }

    public void AcceptFrozenEdit(PlayerRecord player, string field)
    {
        if (Freezer is not { } freezer || !Enum.TryParse<PlayerAttribute>(field, out var attribute)) return;
        try { freezer.AcceptPlayerEdit(player, attribute); }
        catch (Exception ex) { Freezer = null; FreezeError = ex.Message; }
    }

    public void AcceptFrozenClubEdit(ClubEntry club, ClubField field)
    {
        if (field != ClubField.Balance || Freezer is not { } freezer ||
            freezer.Clubs.FirstOrDefault(c => c.Id == club.Id) is not { } existing) return;
        try { freezer.SetClub(club, existing.Mode); }
        catch (Exception ex) { Freezer = null; FreezeError = ex.Message; }
    }

    public void Dispose() { Freezer = null; session?.Dispose(); session = null; }
}

public sealed record EditRecord(ulong Entity, string Field, int OldValue, int NewValue, DateTimeOffset Time, bool Succeeded, string? Error);
public interface IEditService
{
    IReadOnlyList<EditRecord> History { get; }
    PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value);
    PlayerRecord Undo(PlayerRecord player, FieldDescriptor<PlayerRecord> field, EditRecord record);
    void Execute(Action action);
}
public sealed class EditService(PlayerWriter writer, PlayerRepository players, Action<PlayerRecord, string>? onApplied = null) : IEditService
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
            var fresh = players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer exists");
            onApplied?.Invoke(fresh, field.Id);
            return fresh;
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
