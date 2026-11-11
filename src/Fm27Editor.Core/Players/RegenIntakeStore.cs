using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fm27Editor.Core.Players;

/// <summary>
/// What one store file holds: the scans kept for each save and timeline, and the once-only rows that go with them.
/// Both lists carry their save and timeline on every entry, which is what keeps two saves — or two timelines of one
/// save — apart even when they contain the same player id.
/// </summary>
public sealed record RegenStoreDocument(
    IReadOnlyList<RegenSnapshot> Snapshots,
    IReadOnlyList<RegenLedgerEntry> Entries)
{
    public static readonly RegenStoreDocument Empty = new([], []);

    /// <summary>The scan kept for one save, timeline and phase, or nothing when none was kept.</summary>
    public RegenSnapshot? Snapshot(string saveId, string branchId, string? phase = null) =>
        Snapshots.FirstOrDefault(s => string.Equals(s.SaveId, saveId, StringComparison.Ordinal)
            && string.Equals(s.BranchId, branchId, StringComparison.Ordinal)
            && (phase is null || string.Equals(s.Phase, phase, StringComparison.Ordinal)));

    public RegenLedger ToLedger() => new(Entries);
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RegenStoreDocument))]
internal partial class RegenJsonContext : JsonSerializerContext;

/// <summary>
/// The store on disk: one JSON file holding the scans and the ledger. It is diagnostics and restart protection, not
/// a database — SQLite stays out until enumeration and save identity are verified on FM27.
/// </summary>
public static class RegenIntakeStore
{
    /// <summary>
    /// Reads the store. A file that cannot be read is an error rather than an empty store, because an empty store
    /// would look like "nobody has been processed yet" and invite a second pass over players already handled.
    /// </summary>
    public static RegenStoreDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required", nameof(path));
        if (!File.Exists(path)) return RegenStoreDocument.Empty;

        RegenStoreDocument? document;
        try { document = JsonSerializer.Deserialize(File.ReadAllText(path), RegenJsonContext.Default.RegenStoreDocument); }
        catch (JsonException ex) { throw new InvalidDataException($"The regen store at {path} is not readable", ex); }
        if (document is null) throw new InvalidDataException($"The regen store at {path} holds nothing");

        var loaded = document with
        {
            Snapshots = document.Snapshots ?? [],
            Entries = document.Entries ?? [],
        };
        RejectDuplicates(loaded, path);
        return loaded;
    }

    /// <summary>
    /// Writes the store, merging into whatever is already there: a scan replaces the scan of the same save and
    /// timeline, a ledger row replaces the row of the same key — which is how a row moves from one state to the
    /// next. The file is written to the side and moved into place, so a crash mid-write cannot leave a half file
    /// that later reads as an empty store.
    /// </summary>
    public static void Save(string path, RegenStoreDocument document)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required", nameof(path));
        ArgumentNullException.ThrowIfNull(document);
        var incoming = document with { Snapshots = document.Snapshots ?? [], Entries = document.Entries ?? [] };
        RejectDuplicates(incoming, "the document being saved");

        var merged = Load(path);
        var snapshots = new List<RegenSnapshot>(merged.Snapshots);
        foreach (var snapshot in incoming.Snapshots)
        {
            int at = snapshots.FindIndex(s => string.Equals(s.SaveId, snapshot.SaveId, StringComparison.Ordinal)
                && string.Equals(s.BranchId, snapshot.BranchId, StringComparison.Ordinal)
                && string.Equals(s.Phase, snapshot.Phase, StringComparison.Ordinal));
            if (at < 0) snapshots.Add(snapshot); else snapshots[at] = snapshot;
        }
        var entries = new List<RegenLedgerEntry>(merged.Entries);
        foreach (var entry in incoming.Entries)
        {
            int at = entries.FindIndex(e => string.Equals(e.SaveId, entry.SaveId, StringComparison.Ordinal)
                && string.Equals(e.BranchId, entry.BranchId, StringComparison.Ordinal)
                && string.Equals(e.PersonUid, entry.PersonUid, StringComparison.Ordinal)
                && string.Equals(e.Feature, entry.Feature, StringComparison.Ordinal));
            if (at < 0) entries.Add(entry); else entries[at] = entry;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(new RegenStoreDocument(snapshots, entries), RegenJsonContext.Default.RegenStoreDocument);
            File.WriteAllText(temporary, json);
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Two rows for one key, or two scans of one save and timeline, mean the file does not describe one history.
    /// Overwriting one of them would pick a winner by accident, so the file is refused instead.
    /// </summary>
    static void RejectDuplicates(RegenStoreDocument document, string where)
    {
        var seenSnapshots = new HashSet<(string, string, string)>();
        foreach (var snapshot in document.Snapshots)
            if (!seenSnapshots.Add((snapshot.SaveId, snapshot.BranchId, snapshot.Phase)))
                throw new InvalidDataException($"{where} holds the {snapshot.Phase} scan of {snapshot.SaveId} / {snapshot.BranchId} twice");

        var seenEntries = new HashSet<(string, string, string, string)>();
        foreach (var entry in document.Entries)
            if (!seenEntries.Add((entry.SaveId, entry.BranchId, entry.PersonUid, entry.Feature)))
                throw new InvalidDataException($"{where} holds a row for {entry.PersonUid} on {entry.BranchId} under {entry.Feature} twice");
    }
}
