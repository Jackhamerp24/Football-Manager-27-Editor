using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.History;

/// <summary>Name of a changed value; <see cref="Divisor"/> turns the stored value into what the game shows (attributes are stored ×5).</summary>
/// <param name="Parts">A write that stores several equal-size numbers in one go (CA then PA, the three reputations) is shown as "a / b / c".</param>
/// <param name="Kind">How the stored bytes read: a number, a float, a game date (day-of-year in the low 9 bits, then the year) or a colour.</param>
/// <param name="PartNames">Names of the parts of one multi-value write (Rating, Min Age...); only the parts that changed are shown.</param>
public sealed record ChangeLabel(string Name, int Divisor = 1, int Parts = 1, ChangeValueKind Kind = ChangeValueKind.Number, string[]? PartNames = null);

/// <remarks><see cref="Flags"/>: one byte of checkboxes; <c>PartNames[bit]</c> names each bit and the entry shows the boxes that changed ("Is Covered on").</remarks>
public enum ChangeValueKind { Number, Float, Date, Colour, Signed, Flags }

/// <summary>One write to the game: the bytes before and after, and whether it has been put back.</summary>
public sealed class ChangeEntry(ulong address, byte[] before, byte[] after)
{
    public ulong Address { get; } = address;
    public byte[] Before { get; } = before;
    public byte[] After { get; internal set; } = after;
    public bool Undone { get; internal set; }
    public ChangeLabel? Label { get; internal set; }
    /// <summary>What the writer said it was changing (<see cref="ChangeContext"/>) when it wrote.</summary>
    public string? Hint { get; internal init; }
    public ChangeLabel? HintLabel { get; internal init; }
    /// <summary>The Core method that did the write ("Nation Kits: Colour"), the last resort before an address.</summary>
    public string? Caller { get; internal init; }

    public string Name => Label?.Name ?? $"0x{Address:X} ({Before.Length} bytes)";
    public string BeforeText => Label?.PartNames is { } names ? ChangedParts(names, Before, After, Label) : Format(Before, Label);
    public string AfterText => Label?.PartNames is { } names ? ChangedParts(names, After, Before, Label) : Format(After, Label);

    /// <summary>"Rating 12, Min Age 14": just the parts of a multi-value write that differ between <paramref name="mine"/> and <paramref name="other"/>.</summary>
    static string ChangedParts(string[] names, byte[] mine, byte[] other, ChangeLabel label)
    {
        if (label.Kind == ChangeValueKind.Flags && mine.Length == 1 && other.Length == 1)
        {
            var boxes = Enumerable.Range(0, Math.Min(8, names.Length))
                .Where(i => !string.IsNullOrEmpty(names[i]) && ((mine[0] ^ other[0]) & (1 << i)) != 0)
                .Select(i => $"{names[i]} {((mine[0] >> i) & 1) switch { 1 => "on", _ => "off" }}").ToList();
            return boxes.Count == 0 ? mine[0].ToString() : string.Join(", ", boxes);
        }
        if (mine.Length != other.Length || mine.Length % names.Length != 0) return Format(mine, label with { PartNames = null });
        int size = mine.Length / names.Length;
        var part = label with { PartNames = null, Parts = 1 };
        var shown = Enumerable.Range(0, names.Length)
            .Where(i => !mine.AsSpan(i * size, size).SequenceEqual(other.AsSpan(i * size, size)))
            .Select(i => $"{names[i]} {Format(mine[(i * size)..((i + 1) * size)], part)}").ToList();
        return shown.Count == 0 ? Format(mine, label with { PartNames = null }) : string.Join(", ", shown);
    }

    /// <summary>Little-endian signed number for 1, 2, 4 and 8 bytes, hex otherwise.</summary>
    public static string Format(byte[] bytes, ChangeLabel? label)
    {
        if (label is { Parts: > 1 } && bytes.Length % label.Parts == 0)
        {
            int size = bytes.Length / label.Parts;
            return string.Join(" / ", Enumerable.Range(0, label.Parts).Select(i => Format(bytes[(i * size)..((i + 1) * size)], label with { Parts = 1 })));
        }
        switch (label?.Kind)
        {
            case ChangeValueKind.Float when bytes.Length == 4:
                return BitConverter.ToSingle(bytes).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            case ChangeValueKind.Date when bytes.Length == 4:
            {
                int day = BitConverter.ToUInt16(bytes) & 0x1FF; short year = BitConverter.ToInt16(bytes, 2);
                if (year <= 1900 || year > 3000 || day is < 1 or > 366) return "none";
                return Settings.DateSetting.Format(new DateOnly(year, 1, 1).AddDays(day - 1));
            }
            case ChangeValueKind.Colour when bytes.Length is 3 or 4:
                return $"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";
            case ChangeValueKind.Signed when bytes.Length == 1:
                return ((sbyte)bytes[0]).ToString();
        }
        long? value = bytes.Length switch
        {
            1 => bytes[0],
            2 => BitConverter.ToInt16(bytes),
            4 => BitConverter.ToInt32(bytes),
            8 => BitConverter.ToInt64(bytes),
            _ => null,
        };
        if (value is null) return Convert.ToHexString(bytes.Length > 16 ? bytes[..16] : bytes) + (bytes.Length > 16 ? "…" : "");
        return label is { Divisor: > 1 } ? (value.Value / label.Divisor).ToString() : value.Value.ToString();
    }
}

/// <summary>
/// Changes made by one save or preset, for one thing (a player, a club, a nation...). <see cref="Kind"/> and
/// <see cref="Entity"/> are what the history window groups by.
/// </summary>
public sealed class ChangeGroup(long id, DateTimeOffset time, string kind, string entity, string title, Func<ulong, int, ChangeLabel?>? labeler)
{
    public long Id { get; } = id;
    public DateTimeOffset Time { get; internal set; } = time;
    public string Kind { get; } = kind;
    public string Entity { get; } = entity;
    public string Title { get; } = title;
    internal Func<ulong, int, ChangeLabel?>? Labeler { get; } = labeler;
    public List<ChangeEntry> Entries { get; } = [];
    public bool FullyUndone => Entries.Count > 0 && Entries.All(e => e.Undone);
}

/// <summary>
/// The session's edit history. While a group is open every write the editor makes is recorded with the bytes it replaced,
/// so any change, from any view, can be undone. Undo only writes the old bytes back while the game still holds the bytes
/// that were written (otherwise the game changed it since and the entry is refused).
/// </summary>
public sealed class ChangeJournal
{
    /// <summary>Saves of the same thing made within this time are shown as one group, like one Save click.</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(5);

    readonly object _gate = new();
    readonly List<ChangeGroup> _groups = [];
    readonly List<(ulong Start, ulong End)> _fresh = [];
    ChangeGroup? _open;
    int _depth, _suppress;
    long _nextId = 1;

    /// <summary>Counts every write recorded so far, so a caller can tell whether an action changed anything.</summary>
    public long Writes { get { lock (_gate) return _writes; } }
    long _writes;

    public event Action? Changed;

    public IReadOnlyList<ChangeGroup> Groups { get { lock (_gate) return _groups.ToArray(); } }

    /// <summary>
    /// Opens a group (or joins the one already open). Writes made until the returned scope is disposed are recorded; a scope that
    /// recorded nothing leaves no group.
    /// </summary>
    public IDisposable Begin(string kind, string entity, string title, Func<ulong, int, ChangeLabel?>? labeler = null)
    {
        lock (_gate)
        {
            if (_open is null) _open = new ChangeGroup(_nextId++, DateTimeOffset.Now, kind, entity, title, labeler);
            _depth++;
        }
        return new Scope(this);
    }

    sealed class Scope(ChangeJournal journal) : IDisposable
    {
        bool _done;
        public void Dispose() { if (_done) return; _done = true; journal.End(); }
    }

    void End()
    {
        bool raise = false;
        lock (_gate)
        {
            if (--_depth > 0 || _open is not { } group) return;
            _open = null;
            if (group.Entries.Count == 0) return;
            foreach (var entry in group.Entries) entry.Label = group.Labeler?.Invoke(entry.Address, entry.Before.Length);
            // A write nothing names is called after what the save was doing ("Sponsor: Total"), not by its address.
            foreach (var entry in group.Entries.Where(e => e.Label is null && e.Hint is not null)) entry.Label = entry.HintLabel ?? new ChangeLabel(entry.Hint!);
            if (group.Title is not ("Page save" or "Edit"))
            {
                var unnamed = group.Entries.Where(e => e.Label is null).ToList();
                for (int i = 0; i < unnamed.Count; i++)
                    unnamed[i].Label = new ChangeLabel(unnamed.Count == 1 ? group.Title : $"{group.Title} ({i + 1})");
            }
            foreach (var entry in group.Entries.Where(e => e.Label is null && e.Caller is not null)) entry.Label = new ChangeLabel(entry.Caller!);
            if (_groups.Count > 0 && _groups[^1] is { } last && last.Kind == group.Kind && last.Entity == group.Entity && last.Title == group.Title
                && group.Time - last.Time <= MergeWindow && !last.Entries.Any(e => e.Undone))
            {
                foreach (var entry in group.Entries) Merge(last, entry);
                last.Time = group.Time;
            }
            else _groups.Add(group);
            raise = true;
        }
        if (raise) Changed?.Invoke();
    }

    /// <summary>Called for every successful write of the editor (see <c>WindowsProcessMemory.OnWrite</c>).</summary>
    public void Record(ulong address, byte[] before, byte[] after)
    {
        lock (_gate)
        {
            if (_suppress > 0 || _open is null || before.AsSpan().SequenceEqual(after)) return;
            if (_fresh.Any(r => address >= r.Start && address < r.End)) return;
            _writes++;
            Merge(_open, new ChangeEntry(address, before, after) { Hint = ChangeContext.Current, HintLabel = ChangeContext.CurrentLabel, Caller = ChangeContext.Current is null ? ChangeContext.CallerName() : null });
        }
    }

    /// <summary>Memory the editor allocated itself (a bigger vector): writes into it are part of a change elsewhere, not changes.</summary>
    public void NoteAllocation(ulong address, int bytes)
    {
        lock (_gate) _fresh.Add((address, address + (ulong)bytes));
    }

    static void Merge(ChangeGroup group, ChangeEntry entry)
    {
        var same = group.Entries.FirstOrDefault(e => e.Address == entry.Address && e.Before.Length == entry.Before.Length);
        if (same is null) { group.Entries.Add(entry); return; }
        same.After = entry.After;
        if (same.Before.AsSpan().SequenceEqual(same.After)) group.Entries.Remove(same);
    }

    /// <summary>Puts one entry back. False with a reason when the game no longer holds the written bytes.</summary>
    public bool Undo(ChangeEntry entry, IProcessMemory memory, out string? error)
    {
        error = null;
        lock (_gate)
        {
            if (entry.Undone) { error = "Already undone"; return false; }
            var current = new byte[entry.After.Length];
            if (!memory.TryRead(entry.Address, current)) { error = $"Cannot read 0x{entry.Address:X}"; return false; }
            if (!current.AsSpan().SequenceEqual(entry.After)) { error = "The value changed since this edit"; return false; }
            _suppress++;
            try
            {
                if (!memory.TryWrite(entry.Address, entry.Before) || !memory.Read(entry.Address, entry.Before.Length).AsSpan().SequenceEqual(entry.Before))
                { error = $"Could not write 0x{entry.Address:X}"; return false; }
            }
            finally { _suppress--; }
            // A code patch undone must also leave the instruction cache behind, or a thread may run the patched bytes.
            if (memory is WindowsProcessMemory windows) windows.FlushInstructionCache(entry.Address, entry.Before.Length);
            entry.Undone = true;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts a whole group back, newest write first; returns how many entries were restored and why others were not.</summary>
    public (int Restored, List<string> Errors) Undo(ChangeGroup group, IProcessMemory memory)
    {
        int restored = 0;
        var errors = new List<string>();
        foreach (var entry in group.Entries.AsEnumerable().Reverse().Where(e => !e.Undone).ToList())
        {
            if (Undo(entry, memory, out var error)) restored++;
            else errors.Add($"{entry.Name}: {error}");
        }
        return (restored, errors);
    }

    public void Clear()
    {
        lock (_gate) _groups.Clear();
        Changed?.Invoke();
    }
}
