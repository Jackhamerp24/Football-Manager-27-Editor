using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>Why a player was left out of a work-permit batch; never silently skipped.</summary>
public sealed record WorkPermitBatchSkip(ulong PlayerAddress, string? PlayerName, string Reason);

/// <summary>
/// What a permit batch would do: validated targets plus the players it refused with reasons.
/// <see cref="ExpectedPersons"/> carries the person index read at plan time, inside the plan
/// itself, so a copied plan without one is refused instead of granting to the wrong person.
/// </summary>
public sealed record WorkPermitBatchPlan(IReadOnlyList<PlayerRecord> Targets, IReadOnlyList<WorkPermitBatchSkip> Skipped,
    ulong Nation, int Year, IReadOnlyDictionary<ulong, int>? ExpectedPersons = null);

/// <summary>
/// Batch work-permit grants for a whole squad, built on <see cref="WorkPermitTable"/>.
/// Planning is read-only; applying re-checks every player right before writing, skips the
/// stale ones instead of throwing the batch away, and reports every refusal with a reason.
/// The permit table is snapshotted once per phase and only the 24-byte header is re-read
/// per write (re-snapshotted when it moved); there are no writes here, applying goes
/// through <see cref="WorkPermitTable.Add"/>.
/// </summary>
public static class WorkPermitBatch
{
    /// <summary>
    /// Plans "grant a permit from <paramref name="nation"/> for <paramref name="year"/>" for every
    /// player: unreadable players and players already holding that nation's permit are skipped
    /// with reasons. Never throws for a bad player; an invalid nation or year skips everyone.
    /// </summary>
    public static WorkPermitBatchPlan Plan(IProcessMemory memory, OffsetProfile profile, ulong moduleBase,
        IEnumerable<PlayerRecord> players, ulong nation, int year, Func<ulong, string?> nationName,
        ulong? knownHeader = null)
    {
        var list = players.ToArray();
        if (!ValidNation(memory, profile, moduleBase, nation))
            return new([], list.Select(p => new WorkPermitBatchSkip(p.Address, SafeName(p), "not a nation object")).ToArray(),
                nation, year, new Dictionary<ulong, int>());
        if (year is < 1900 or > 2300)
            return new([], list.Select(p => new WorkPermitBatchSkip(p.Address, SafeName(p), "year out of range (1900-2300)")).ToArray(),
                nation, year, new Dictionary<ulong, int>());
        TableSnapshot snapshot;
        try { snapshot = SnapshotTable(memory, profile, moduleBase, knownHeader); }
        catch (Exception ex)
        {
            return new([], list.Select(p => new WorkPermitBatchSkip(p.Address, SafeName(p), $"permit table unavailable: {ex.Message}")).ToArray(),
                nation, year, new Dictionary<ulong, int>());
        }
        var targets = new List<PlayerRecord>();
        var skipped = new List<WorkPermitBatchSkip>();
        var persons = new Dictionary<ulong, int>();
        var seen = new HashSet<ulong>();
        foreach (var p in list)
        {
            if (!seen.Add(p.Address)) { skipped.Add(new(p.Address, SafeName(p), "duplicate player in batch")); continue; }
            int person;
            try
            {
                RequirePlayer(memory, profile, moduleBase, p);
                person = PersonIndex(memory, p);
            }
            catch (Exception ex) { skipped.Add(new(p.Address, SafeName(p), $"unreadable: {ex.Message}")); continue; }
            if (snapshot.Has(nation, person))
            {
                skipped.Add(new(p.Address, SafeName(p),
                    $"already holds a permit from {nationName(nation) ?? "unknown nation"}"));
                continue;
            }
            persons[p.Address] = person;
            targets.Add(p);
        }
        return new(targets, skipped, nation, year, persons);
    }

    /// <summary>
    /// Grants the plan: re-checks address, vtable and person index of every target right before
    /// writing, skips stale players and existing permits, and returns how many grants were made.
    /// A failed player never stops the rest of the batch. Targets without a plan-time person
    /// index (a copied plan) are refused. Progress reports (done, total) after each target.
    /// </summary>
    public static int Apply(WorkPermitBatchPlan plan, IProcessMemory memory, OffsetProfile profile, ulong moduleBase,
        Func<int, ulong> allocate, Action<WorkPermitBatchSkip>? onSkip = null, ulong? knownHeader = null,
        Action<int, int>? progress = null)
    {
        int granted = 0;
        int done = 0;
        void Skip(PlayerRecord p, string reason) => onSkip?.Invoke(new(p.Address, SafeName(p), reason));
        void Report() => progress?.Invoke(++done, plan.Targets.Count);
        if (plan.Year is < 1900 or > 2300)
        {
            foreach (var p in plan.Targets) { Skip(p, "year out of range (1900-2300)"); Report(); }
            return granted;
        }
        TableSnapshot snapshot;
        try { snapshot = SnapshotTable(memory, profile, moduleBase, knownHeader); }
        catch (Exception ex)
        {
            foreach (var p in plan.Targets) { Skip(p, $"permit table unavailable: {ex.Message}"); Report(); }
            return granted;
        }
        var table = new WorkPermitTable(memory, profile, moduleBase, knownHeader);
        ulong header;
        try { header = knownHeader ?? table.Locate(); }
        catch (Exception ex)
        {
            foreach (var p in plan.Targets) { Skip(p, $"permit table unavailable: {ex.Message}"); Report(); }
            return granted;
        }
        ulong expectedEnd = snapshot.End;
        foreach (var p in plan.Targets)
        {
            int person;
            try
            {
                RequirePlayer(memory, profile, moduleBase, p);
                person = PersonIndex(memory, p);
            }
            catch (Exception ex) { Skip(p, $"player changed since plan: {ex.Message}"); Report(); continue; }
            if (plan.ExpectedPersons is not { } baseline || !baseline.TryGetValue(p.Address, out int was))
            { Skip(p, "no plan-time identity; re-plan the batch"); Report(); continue; }
            if (was != person) { Skip(p, "player changed since plan"); Report(); continue; }
            try
            {
                snapshot = snapshot.RefreshIfMoved(memory, header, expectedEnd);
                // After a re-read (the table moved, e.g. our own insert grew it into a new buffer) the snapshot's end is the
                // new reference; keeping the old one would force a full re-read for every remaining player.
                expectedEnd = snapshot.End;
            }
            catch (Exception ex) { Skip(p, $"permit table unavailable: {ex.Message}"); Report(); continue; }
            if (snapshot.Has(plan.Nation, person)) { Skip(p, "already holds a permit"); Report(); continue; }
            try
            {
                table.Add(p, plan.Nation, plan.Year, allocate);
                granted++;
                expectedEnd += 8;
                snapshot = snapshot.WithOwnInsert(plan.Nation, person);
            }
            catch (Exception ex) { Skip(p, $"write failed: {ex.Message}"); }
            finally { Report(); }
        }
        return granted;
    }

    /// <summary>One bulk read of the permit table: header triple plus every record's nation and person.</summary>
    sealed class TableSnapshot(ulong begin, ulong end, Dictionary<(ulong Nation, int Person), ulong> entries)
    {
        public ulong End { get; private set; } = end;
        readonly ulong _begin = begin;
        readonly Dictionary<(ulong Nation, int Person), ulong> _entries = entries;

        public bool Has(ulong nation, int person) => _entries.ContainsKey((nation, person));

        public TableSnapshot WithOwnInsert(ulong nation, int person)
        {
            _entries[(nation, person)] = 0;
            return this;
        }

        /// <summary>
        /// Re-reads only the 24-byte header; when begin/end differ from what this batch
        /// expects (someone else moved the table), re-reads the whole table once.
        /// </summary>
        public TableSnapshot RefreshIfMoved(IProcessMemory memory, ulong header, ulong expectedEnd)
        {
            var triple = memory.Read(header, 24);
            ulong b = BitConverter.ToUInt64(triple, 0), e = BitConverter.ToUInt64(triple, 8);
            if (b == _begin && e == expectedEnd) return this;
            return SnapshotTable(memory, header);
        }
    }

    static TableSnapshot SnapshotTable(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, ulong? knownHeader)
    {
        ulong at = knownHeader ?? new WorkPermitTable(memory, profile, moduleBase).Locate();
        return SnapshotTable(memory, at);
    }

    static TableSnapshot SnapshotTable(IProcessMemory memory, ulong header)
    {
        var triple = memory.Read(header, 24);
        ulong b = BitConverter.ToUInt64(triple, 0), e = BitConverter.ToUInt64(triple, 8);
        if (b == 0 || e < b || (e - b) % 8 != 0 || (e - b) / 8 > 200_000)
            throw new InvalidOperationException("The work permit table was not found");
        var pointers = memory.Read(b, (int)(e - b));
        var entries = new Dictionary<(ulong Nation, int Person), ulong>();
        for (int i = 0; i < pointers.Length; i += 8)
        {
            ulong record = BitConverter.ToUInt64(pointers, i);
            var raw = memory.Read(record, WorkPermitTable.RecordSize);
            entries[(BitConverter.ToUInt64(raw, 0), BitConverter.ToInt32(raw, 8))] = record;
        }
        return new(b, e, entries);
    }

    static bool ValidNation(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, ulong nation)
    {
        try
        {
            var nationOffsets = profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");
            return nation != 0 && memory.ReadUInt64(nation) == moduleBase + nationOffsets.VtableRva;
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException) { return false; }
    }

    static void RequirePlayer(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, PlayerRecord player)
    {
        ulong vtable;
        try { vtable = memory.ReadUInt64(player.Address); }
        catch (MemoryAccessException ex) { throw new InvalidOperationException("Player is no longer readable", ex); }
        if (!profile.PlayerVtableRvas.Any(r => vtable == moduleBase + r))
            throw new InvalidOperationException("Player changed since it was read");
    }

    static int PersonIndex(IProcessMemory memory, PlayerRecord player)
    {
        try { return BitConverter.ToInt32(memory.Read(player.Address + (ulong)WorkPermitTable.PersonIndexOffset, 4)); }
        catch (MemoryAccessException ex) { throw new InvalidOperationException("Player is no longer readable", ex); }
    }

    static string SafeName(PlayerRecord player)
    {
        try { return player.DisplayName; }
        catch { return $"player 0x{player.Address:X}"; }
    }
}
