using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Player + <see cref="Vector"/> is a vector of pointers to 16-byte records (Language*, value byte). Checked against FMRTE
/// 27's Languages tab on Kylian Mbappé (Spanish 8, English 5, Arabic 6) and Harry Kane (German 10); writing Spanish 9 and
/// German 11 showed in FMRTE as written.
/// </summary>
public sealed record LanguageOffsets(int Vector, int RecordSize, int Language, int Value, uint LanguageVtable, int LanguageId, int LanguageName)
{
    static readonly string[] Keys = ["vector", "recordSize", "language", "value", "languageVtable", "languageId", "languageName"];

    internal static LanguageOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.languages.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], (uint)v[4], v[5], v[6]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [Vector, RecordSize, Language, Value, LanguageVtable, LanguageId, LanguageName];
        w.WriteStartObject("languages");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>One language as FMRTE's Languages tab shows it (Name, Value).</summary>
public sealed record PlayerLanguage(ulong Address, int LanguageId, string? Name, int Value, string Snapshot = "", ulong Begin = 0, ulong End = 0, ulong LanguageObject = 0);

/// <summary>A language object the game knows, for the Add picker.</summary>
public sealed record LanguageEntry(ulong Address, int Id, string Name);

public sealed class LanguageReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxLanguages = 32, MaxName = 200;

    public IReadOnlyList<PlayerLanguage> Read(PlayerRecord player) => Read(PersonOwner.Of(player, profile));

    public IReadOnlyList<PlayerLanguage> Read(PersonOwner owner)
    {
        if (profile.Languages is not { } o || !owner.IsValid(memory, moduleBase)) return [];
        if (!Q(owner.At(o.Vector), out ulong begin) || !Q(owner.At(o.Vector) + 8, out ulong end) || begin == 0) return [];
        if (end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxLanguages) return [];
        var result = new List<PlayerLanguage>();
        var b = new byte[o.RecordSize];
        for (ulong p = begin; p < end; p += 8)
        {
            if (!Q(p, out ulong record) || record == 0 || !memory.TryRead(record, b)) continue;
            ulong language = BitConverter.ToUInt64(b, o.Language);
            if (!Q(language, out ulong languageVt) || languageVt != moduleBase + o.LanguageVtable) continue;
            var l = new byte[Math.Max(o.LanguageId + 4, o.LanguageName + 8)];
            if (!memory.TryRead(language, l)) continue;
            result.Add(new(record, BitConverter.ToInt32(l, o.LanguageId), StringAt(BitConverter.ToUInt64(l, o.LanguageName)), b[o.Value],
                Convert.ToHexString(b), begin, end, language));
        }
        return result;
    }

    /// <summary>Every language some loaded player speaks, by name: the choices for Add.</summary>
    public IReadOnlyList<LanguageEntry> Catalogue(IEnumerable<PlayerRecord> players) =>
        players.SelectMany(Read).Where(l => l.Name is not null).DistinctBy(l => l.LanguageObject)
            .Select(l => new LanguageEntry(l.LanguageObject, l.LanguageId, l.Name!)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

    string? StringAt(ulong p)
    {
        if (p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>
/// Sets a language's value. 1–10 is the range the game uses: of 37,666 records on 22/11/2026, all but 11 held 1–10
/// (10 the most common). Guarded: player vtable, unchanged vector, membership, whole record; read-back.
/// </summary>
public sealed class LanguageWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetValue(PlayerRecord player, PlayerLanguage language, int value) => SetValue(PersonOwner.Of(player, profile), language, value);

    public void SetValue(PersonOwner owner, PlayerLanguage language, int value)
    {
        if (value is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(value), "Language value is 1–10");
        var o = profile.Languages ?? throw new InvalidOperationException("Profile has no language offsets");
        var before = Convert.FromHexString(language.Snapshot);
        if (!owner.IsValid(memory, moduleBase)
            || language.Begin == 0 || language.End <= language.Begin || language.End - language.Begin > 32 * 8 || (language.End - language.Begin) % 8 != 0
            || before.Length != o.RecordSize
            || memory.ReadUInt64(owner.At(o.Vector)) != language.Begin || memory.ReadUInt64(owner.At(o.Vector) + 8) != language.End
            || !Enumerable.Range(0, (int)((language.End - language.Begin) / 8)).Any(i => memory.ReadUInt64(language.Begin + (ulong)i * 8) == language.Address)
            || !memory.Read(language.Address, o.RecordSize).AsSpan().SequenceEqual(before)
            || before[o.Value] != language.Value
            || memory.ReadUInt64(BitConverter.ToUInt64(before, o.Language)) != moduleBase + o.LanguageVtable)
            throw new StaleObjectException("The language changed since it was read");
        ulong at = language.Address + (ulong)o.Value;
        if (!memory.TryWrite(at, [(byte)value]) || memory.Read(at, 1)[0] != value)
            throw new MemoryAccessException("Could not write language value");
    }

    /// <summary>
    /// Adds a language the way FMRTE 27 does (diffed on Harry Kane, 2/11/2026): a fresh 16-byte record (Language*, value,
    /// zeros) and its pointer appended to the vector; a full vector moves to a new buffer. Both live outside the game's
    /// allocator.
    /// </summary>
    public void Add(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value, Func<int, ulong> allocate) =>
        Add(PersonOwner.Of(player, profile), current, language, value, allocate);

    public void Add(PersonOwner player, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value, Func<int, ulong> allocate)
    {
        if (value is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(value), "Language value is 1–10");
        var (o, vector, begin, end, cap) = Require(player, current);
        if (memory.ReadUInt64(language.Address) != moduleBase + o.LanguageVtable || BitConverter.ToInt32(memory.Read(language.Address + (ulong)o.LanguageId, 4)) != language.Id)
            throw new StaleObjectException("The language object changed since it was read");
        if (current.Any(l => l.LanguageObject == language.Address)) throw new InvalidOperationException($"The player already speaks {language.Name}");
        var record = new byte[o.RecordSize];
        BitConverter.GetBytes(language.Address).CopyTo(record, o.Language);
        record[o.Value] = (byte)value;
        ulong at = allocate(o.RecordSize);
        Write(at, record);
        if (begin != 0 && end < cap)
        {
            Write(end, BitConverter.GetBytes(at));
            Write(vector + 8, BitConverter.GetBytes(end + 8));
            return;
        }
        int count = current.Count, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        Write(buffer, [.. current.SelectMany(l => BitConverter.GetBytes(l.Address)), .. BitConverter.GetBytes(at)]);
        Write(vector, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)(count + 1) * 8), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    /// <summary>Removes a language like FMRTE 27: later pointers move up and the end pointer moves back; the record stays.</summary>
    public void Remove(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language) =>
        Remove(PersonOwner.Of(player, profile), current, language);

    public void Remove(PersonOwner player, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language)
    {
        var (_, vector, begin, end, _) = Require(player, current);
        int index = current.ToList().FindIndex(l => l.Address == language.Address);
        if (index < 0) throw new StaleObjectException("The language is no longer in the list");
        if (index < current.Count - 1) Write(begin + (ulong)index * 8, current.Skip(index + 1).SelectMany(l => BitConverter.GetBytes(l.Address)).ToArray());
        Write(vector + 8, BitConverter.GetBytes(end - 8));
    }

    /// <summary>The vector must hold exactly the records read, in order, each unchanged.</summary>
    (LanguageOffsets o, ulong vector, ulong begin, ulong end, ulong cap) Require(PersonOwner player, IReadOnlyList<PlayerLanguage> current)
    {
        var o = profile.Languages ?? throw new InvalidOperationException("Profile has no language offsets");
        if (!player.IsValid(memory, moduleBase)) throw new StaleObjectException($"{player.Name} changed since it was read");
        ulong vector = player.At(o.Vector);
        var p = memory.Read(vector, 24);
        ulong begin = BitConverter.ToUInt64(p, 0), end = BitConverter.ToUInt64(p, 8), cap = BitConverter.ToUInt64(p, 16);
        if (end < begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 != (ulong)current.Count
            || (current.Count > 0 && (current[0].Begin != begin || current[0].End != end))
            || current.Select((l, i) => memory.ReadUInt64(begin + (ulong)i * 8) != l.Address || !memory.Read(l.Address, o.RecordSize).AsSpan().SequenceEqual(Convert.FromHexString(l.Snapshot))).Any(bad => bad))
            throw new StaleObjectException("The player's languages changed since they were read");
        return (o, vector, begin, end, cap);
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}
