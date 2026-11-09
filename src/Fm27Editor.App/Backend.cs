using Fm27Editor.Core.Game;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Diagnostics;

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
    /// <summary>True when the game loaded another save than the one the editor read: every write is refused until Load Game reads it again.</summary>
    public bool SaveChanged { get; set; }
    public async Task ConnectAsync(CancellationToken ct)
    {
        Freezer = null;
        FreezeError = null;
        session?.Dispose(); session = null;
        try
        {
            var attached = await Task.Run(() => GameSession.Attach(GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets")), true), ct);
            session = attached;
            SaveChanged = false;
            var journal = new ChangeJournal();
            var recorder = FlightRecorder.Current;
            recorder?.Log("game", $"attached {attached.Profile.Build}; writable {attached.CanWrite}");
            attached.Process.OnWrite = (address, before, after) =>
            {
                journal.Record(address, before, after);
                recorder?.Write(address, before, after, attached.ModuleBase, attached.Game.ModuleEnd, ChangeContext.Current ?? ChangeContext.CallerName());
            };
            attached.Process.OnAllocate = journal.NoteAllocation;
            Edits = new EditService(attached.Writer, attached.Players, AcceptFrozenEdit, journal, p => PlayerChangeLabels.For(attached.Profile, p.Address)) { Blocked = () => SaveChanged };
            if (attached.CanWrite)
            {
                try { Freezer = await Task.Run(() => new FreezeRunner(attached, FreezePath), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { FreezeError = ex.Message; }
            }
            State = new(ConnectionStatus.Connected, attached.CanWrite ? attached.Profile.Build :
                $"{attached.Profile.Build} (read-only)");
        }
        catch (UnsupportedBuildException ex) { State = new(ConnectionStatus.UnsupportedBuild, UserMessage.For(ex)); FlightRecorder.Current?.Log("game", "unsupported build: " + ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { State = new(ConnectionStatus.Disconnected, UserMessage.For(ex)); FlightRecorder.Current?.Log("game", "not connected: " + ex.Message); }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void TickFreezer()
    {
        if (SaveChanged || Freezer is not { } freezer || (freezer.Players.Count == 0 && freezer.Clubs.Count == 0 && freezer.Staff.Count == 0)) return;
        try { Edits.ExecuteUnrecorded(() => freezer.Tick()); }
        catch (Exception ex) { Freezer = null; FreezeError = ex.Message; }
    }

    public void StopFreezer(string reason) { Freezer = null; FreezeError = reason; }

    public void AcceptFrozenEdit(PlayerRecord player, string field)
    {
        if (Freezer is not { } freezer || !Enum.TryParse<PlayerAttribute>(field, out var attribute)) return;
        try { freezer.AcceptPlayerEdit(player, attribute); }
        catch (Exception ex) { Freezer = null; FreezeError = ex.Message; }
    }

    public void AcceptFrozenStaffEdit(Fm27Editor.Core.Staff.StaffRecord staff, Fm27Editor.Core.Staff.StaffAttribute attribute)
    {
        if (Freezer is not { } freezer) return;
        try { freezer.AcceptStaffEdit(staff, attribute); }
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

/// <summary>A write was refused because the game now holds another save than the one the editor read.</summary>
public sealed class SaveChangedException() : Exception("A different save is loaded in the game. Press Load Game first.");

public sealed record EditRecord(ulong Entity, string Field, int OldValue, int NewValue, DateTimeOffset Time, bool Succeeded, string? Error);
public interface IEditService
{
    IReadOnlyList<EditRecord> History { get; }
    PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value);
    PlayerRecord Undo(PlayerRecord player, FieldDescriptor<PlayerRecord> field, EditRecord record);
    void Execute(Action action);

    /// <summary>The session's edit history; null for a service that keeps none.</summary>
    ChangeJournal? Journal => null;

    /// <summary>How many writes the history has recorded; unchanged across an action means the action wrote nothing.</summary>
    long WriteCount => Journal?.Writes ?? -1;

    /// <summary>Opens a history group (joining one already open); writes made until it is disposed are recorded under it.</summary>
    IDisposable Group(string kind, string entity, string title, Func<ulong, int, ChangeLabel?>? labeler = null) => NoGroup.Instance;

    /// <summary>Runs <paramref name="action"/> as one recorded save of <paramref name="entity"/> (a player, a club, a nation...).</summary>
    void Execute(string kind, string entity, string title, Action action, Func<ulong, int, ChangeLabel?>? labeler = null)
    {
        using (Group(kind, entity, title, labeler)) Execute(action);
    }

    /// <summary>Remembers how to name the bytes of <paramref name="entity"/> so its later saves show field names in the history.</summary>
    void RegisterLabels(string kind, string entity, Func<ulong, int, ChangeLabel?> labeler) { }

    /// <summary>Says what the user is editing now; a write made outside a named group is recorded under it instead of "Other".</summary>
    Func<(string Kind, string Entity)?>? ContextProvider { get => null; set { } }

    /// <summary>Runs <paramref name="action"/> without recording it (the freezer's ticks).</summary>
    void ExecuteUnrecorded(Action action) => Execute(action);
}

sealed class NoGroup : IDisposable
{
    public static readonly NoGroup Instance = new();
    public void Dispose() { }
}
public sealed class EditService(PlayerWriter writer, PlayerRepository players, Action<PlayerRecord, string>? onApplied = null, ChangeJournal? journal = null, Func<PlayerRecord, Func<ulong, int, ChangeLabel?>?>? playerLabels = null) : IEditService
{
    readonly object gate = new();
    /// <summary>When it returns true every write is refused (the game holds another save than the one read).</summary>
    public Func<bool>? Blocked { get; init; }
    void CheckAllowed() { if (Blocked?.Invoke() == true) throw new SaveChangedException(); }
    readonly List<EditRecord> history = [];
    public IReadOnlyList<EditRecord> History { get { lock (gate) return history.ToArray(); } }
    public ChangeJournal? Journal => journal;
    readonly Dictionary<(string, string), Func<ulong, int, ChangeLabel?>> labelers = [];
    public void RegisterLabels(string kind, string entity, Func<ulong, int, ChangeLabel?> labeler) { lock (labelers) labelers[(kind, entity)] = labeler; }
    public IDisposable Group(string kind, string entity, string title, Func<ulong, int, ChangeLabel?>? labeler = null) =>
        journal is null ? NoGroup.Instance : journal.Begin(kind, entity, title, labeler ?? Registered(kind, entity));
    Func<ulong, int, ChangeLabel?>? Registered(string kind, string entity) { lock (labelers) return labelers.GetValueOrDefault((kind, entity)); }
    public Func<(string Kind, string Entity)?>? ContextProvider { get; set; }
    public void Execute(Action action)
    {
        CheckAllowed();
        var (kind, entity) = ContextProvider?.Invoke() ?? ("Other", "Edits");
        using (Group(kind, entity, "Edit")) lock (gate) action();
    }
    public void ExecuteUnrecorded(Action action) { CheckAllowed(); lock (gate) action(); }
    public PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value)
    {
        CheckAllowed();
        using var scope = Group("Player", player.DisplayName, "Page save", playerLabels?.Invoke(player));
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
