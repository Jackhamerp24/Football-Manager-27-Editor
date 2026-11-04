using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Players;

/// <summary>Freeze state for one attached save. The caller serializes this with other game edits.</summary>
public sealed class FreezeRunner
{
    readonly GameSession session;
    readonly string path;
    readonly string key;
    readonly ulong rootBegin;
    readonly List<FrozenPlayer> players;
    readonly List<ClubBalanceFreezer> clubs;

    public IReadOnlyList<FrozenPlayer> Players => players;
    public IReadOnlyList<ClubBalanceFreezer> Clubs => clubs;

    public FreezeRunner(GameSession session, string path)
    {
        if (!session.CanWrite) throw new InvalidOperationException("Freeze requires a writable game connection");
        this.session = session;
        this.path = path;
        key = FreezeStore.SaveKey(session);
        rootBegin = CurrentRoot();
        if (rootBegin == 0) throw new StaleObjectException("Player list is not available");
        var all = session.Players.LoadAll();
        var saved = FreezeStore.Load(path, key);
        var entries = FreezeStore.Resolve(saved, all, session.LoadClubs(all));
        if (entries.Players.Count != saved.Players.Length || entries.Clubs.Count != saved.Clubs.Length ||
            saved.Players.Select(p => p.Id).Distinct().Count() != saved.Players.Length ||
            saved.Clubs.Select(c => c.Id).Distinct().Count() != saved.Clubs.Length)
            throw new StaleObjectException("Some frozen player or club IDs are missing or ambiguous; freeze was not started");
        players = entries.Players.ToList();
        clubs = entries.Clubs.ToList();
        CheckSave();
    }

    ulong CurrentRoot() => session.Memory.ReadUInt64(session.ModuleBase + session.Profile.PlayerListRootRva + (ulong)session.Profile.PlayerListBeginOffset);
    void CheckSave()
    {
        if (CurrentRoot() != rootBegin) throw new StaleObjectException("Save changed; reconnect before freezing");
    }

    void Persist() => FreezeStore.Save(path, key, FreezeStore.Snapshot(players, clubs));

    public void SetPlayer(PlayerRecord player, IEnumerable<(PlayerAttribute Attribute, FreezeMode Mode)> attributes)
    {
        CheckSave();
        var current = session.Players.Reload(player.Address);
        if (current?.Id != player.Id || current?.Id is not > 0) throw new StaleObjectException("Player changed");
        var entry = FrozenPlayer.Capture(current, attributes);
        players.RemoveAll(p => p.Id == entry.Id);
        players.Add(entry);
        Persist();
    }

    public void RemovePlayer(int id) { CheckSave(); players.RemoveAll(p => p.Id == id); Persist(); }

    /// <summary>A deliberate editor change becomes the new frozen value for that attribute.</summary>
    public void AcceptPlayerEdit(PlayerRecord player, PlayerAttribute attribute)
    {
        CheckSave();
        var old = players.FirstOrDefault(p => p.Id == player.Id);
        if (old is not null && old.AcceptEdit(player, attribute)) Persist();
    }

    public void SetClub(ClubEntry club, FreezeMode mode)
    {
        CheckSave();
        int idOffset = session.Profile.Club?.Id ?? throw new InvalidOperationException("Profile has no club ID");
        if (club.Id is not > 0 || BitConverter.ToInt32(session.Memory.Read(club.Address + (ulong)idOffset, 4)) != club.Id)
            throw new StaleObjectException("Club changed");
        var entry = ClubBalanceFreezer.Capture(club, session.ClubFields, mode);
        clubs.RemoveAll(c => c.Id == entry.Id);
        clubs.Add(entry);
        Persist();
    }

    public void RemoveClub(int id) { CheckSave(); clubs.RemoveAll(c => c.Id == id); Persist(); }

    /// <summary>Applies one pass. Any changed save or object stops the pass with a visible error.</summary>
    public int Tick()
    {
        CheckSave();
        var before = FreezeStore.Snapshot(players, clubs);
        int writes = 0;
        foreach (var player in players) { CheckSave(); writes += player.Reconcile(session.Players, session.Writer); }
        foreach (var club in clubs) { CheckSave(); if (club.Reconcile(session.Memory, session.Profile, session.ClubFields)) writes++; }
        var after = FreezeStore.Snapshot(players, clubs);
        if (!SameValues(before, after)) FreezeStore.Save(path, key, after);
        return writes;
    }

    static bool SameValues(SavedFreezes a, SavedFreezes b) =>
        a.Players.Length == b.Players.Length && a.Clubs.SequenceEqual(b.Clubs) &&
        a.Players.Zip(b.Players).All(pair => pair.First.Id == pair.Second.Id && pair.First.Fields.SequenceEqual(pair.Second.Fields));
}
