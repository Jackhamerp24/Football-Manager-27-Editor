using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Competitions;

/// <summary>
/// A competition from the game-wide list (8,213 on 3/11/2026). Colours are packed 0xAARRGGBB (the bytes are B, G, R, A);
/// winter ball dates are null when the game stores 1900 ("none"). Measured on Premier League (11), Sky Bet Championship (12) and
/// Trendyol Süper Lig (130286) against FMRTE 27's Information page.
/// </summary>
public sealed record CompetitionRecord(ulong Address, int Index, int Id, string? Name, string? ShortName, short Reputation,
    uint Foreground, uint Background, uint Trim, uint WinterBallColour, DateOnly? WinterBallStart, DateOnly? WinterBallEnd,
    byte SubstitutionRules = 0xFF)
{
    public string DisplayName => Name ?? ShortName ?? "";
}

public sealed class CompetitionRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxCompetitions = 100_000, MaxName = 300;
    CompetitionOffsets C => profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");

    public IReadOnlyList<CompetitionRecord> LoadAll()
    {
        var c = C;
        if (c.ListRootRva is not { } rva || c.ListOffset is not { } off) throw new InvalidOperationException("Profile has no competition list");
        ulong root = memory.ReadUInt64(moduleBase + (ulong)rva);
        ulong begin = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)off), end = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)off + 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxCompetitions)
            throw new MemoryAccessException($"Competition list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        var raw = memory.Read(begin, (int)(end - begin));
        var result = new List<CompetitionRecord>(raw.Length / 8);
        for (int i = 0; i < raw.Length; i += 8)
            if (Reload(BitConverter.ToUInt64(raw, i)) is { } r) result.Add(r);
        return result;
    }

    public CompetitionRecord? Reload(ulong address)
    {
        var c = C;
        if (address == 0 || c.Index is not { } index || c.Id is not { } id || c.Reputation is not { } rep || c.Foreground is not { } fg
            || c.Background is not { } bg || c.Trim is not { } trim || c.WinterBallColour is not { } wb
            || c.WinterBallStart is not { } ws || c.WinterBallEnd is not { } we) return null;
        int size = new[] { c.Name, c.ShortName, index, id, rep, fg, bg, trim, wb, ws, we }.Max() + 8;
        var b = new byte[size];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + c.VtableRva) return null;
        return new(address, BitConverter.ToInt32(b, index), BitConverter.ToInt32(b, id), StringAt(BitConverter.ToUInt64(b, c.Name)),
            StringAt(BitConverter.ToUInt64(b, c.ShortName)), BitConverter.ToInt16(b, rep), Colour(b, fg), Colour(b, bg), Colour(b, trim),
            Colour(b, wb), Date(b, ws), Date(b, we),
            c.SubstitutionRules is { } subs && subs < size ? b[subs] : (byte)0xFF);
    }

    static uint Colour(byte[] b, int at) => (uint)(b[at + 3] << 24 | b[at + 2] << 16 | b[at + 1] << 8 | b[at]);

    static DateOnly? Date(byte[] b, int at)
    {
        // Every competition surveyed on 3/11/2026 had no flag bits, but the day word keeps the same 9-bit day as the
        // contract dates, so the writer preserves bits above it and the reader masks them.
        int day = BitConverter.ToUInt16(b, at) & 0x1FF, year = BitConverter.ToInt16(b, at + 2);
        if (year is <= 1900 or > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }

    string? StringAt(ulong field)
    {
        if (field == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(field, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(field + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}

public enum CompetitionColour { Foreground, Background, Trim, WinterBall }

/// <summary>
/// Sets Reputation. FMRTE 27 wrote the value to two adjacent int16 (Süper Lig 155 → 156 changed both), so both are written.
/// Guarded: competition vtable, both int16 still equal to the snapshot, read back.
/// </summary>
public sealed class CompetitionWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int MaxReputation = 200;

    /// <summary>FMRTE's "Do Not Override Sub Rules": the only value whose label has been confirmed twice.</summary>
    public const byte SubstitutionRulesNone = 0xFF;
    /// <summary>FMRTE's "5 from 9", observed when FMRTE wrote the Premier League on build 2493426.</summary>
    public const byte SubstitutionRulesFiveFromNine = 0x11;

    public void SetReputation(CompetitionRecord competition, short value)
    {
        var c = profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
        if (c.Reputation is not { } rep || c.ReputationMirror is not { } mirror) throw new InvalidOperationException("Profile has no reputation offsets");
        if (value is < 0 or > MaxReputation) throw new ArgumentOutOfRangeException(nameof(value), $"Reputation is 0–{MaxReputation}");
        if (memory.ReadUInt64(competition.Address) != moduleBase + c.VtableRva) throw new StaleObjectException("The competition object changed since it was read");
        ulong at = competition.Address + (ulong)rep, atMirror = competition.Address + (ulong)mirror;
        var before = BitConverter.GetBytes(competition.Reputation);
        if (!memory.Read(at, 2).AsSpan().SequenceEqual(before) || !memory.Read(atMirror, 2).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("Reputation changed since it was read");
        var after = BitConverter.GetBytes(value);
        foreach (ulong target in new[] { at, atMirror })
            if (!memory.TryWrite(target, after) || !memory.Read(target, 2).AsSpan().SequenceEqual(after))
                throw new MemoryAccessException($"Could not write 0x{target:X}");
    }

    /// <summary>
    /// Writes one byte of FMRTE's Substitution Rules dropdown. Only <see cref="SubstitutionRulesNone"/> and
    /// <see cref="SubstitutionRulesFiveFromNine"/> have been seen on this build, so other values are passed through raw.
    /// </summary>
    public void SetSubstitutionRules(CompetitionRecord competition, byte value)
    {
        var c = profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
        if (c.SubstitutionRules is not { } offset) throw new InvalidOperationException("Profile has no substitution rules offset");
        RequireSameObject(competition, c);
        ulong at = competition.Address + (ulong)offset;
        byte[] before = [competition.SubstitutionRules], after = [value];
        if (!memory.Read(at, 1).AsSpan().SequenceEqual(before)) throw new StaleObjectException("Substitution rules changed since they were read");
        if (!memory.TryWrite(at, after) || !memory.Read(at, 1).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write substitution rules at 0x{at:X}");
    }

    /// <summary>Writes one of the four colours FMRTE shows; the object holds B, G, R, A.</summary>
    public void SetColour(CompetitionRecord competition, CompetitionColour colour, uint argb)
    {
        var c = profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
        int offset = colour switch
        {
            CompetitionColour.Foreground => c.Foreground ?? throw new InvalidOperationException("Profile has no foreground colour"),
            CompetitionColour.Background => c.Background ?? throw new InvalidOperationException("Profile has no background colour"),
            CompetitionColour.Trim => c.Trim ?? throw new InvalidOperationException("Profile has no trim colour"),
            CompetitionColour.WinterBall => c.WinterBallColour ?? throw new InvalidOperationException("Profile has no winter ball colour"),
            _ => throw new ArgumentOutOfRangeException(nameof(colour)),
        };
        uint before = colour switch
        {
            CompetitionColour.Foreground => competition.Foreground,
            CompetitionColour.Background => competition.Background,
            CompetitionColour.Trim => competition.Trim,
            _ => competition.WinterBallColour,
        };
        RequireSameObject(competition, c);
        ulong at = competition.Address + (ulong)offset;
        var expected = BitConverter.GetBytes(before);
        var after = BitConverter.GetBytes(argb);
        if (!memory.Read(at, 4).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("The colour changed since it was read");
        if (!memory.TryWrite(at, after) || !memory.Read(at, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the colour at 0x{at:X}");
    }

    /// <summary>
    /// Writes a Winter Ball date (null clears it to 1/1/1900). The day word keeps the bits above the 9-bit day, as the
    /// contract dates do; every competition surveyed on 3/11/2026 had no such flags set.
    /// </summary>
    public void SetWinterBallDate(CompetitionRecord competition, bool start, DateOnly? value)
    {
        var c = profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
        int offset = (start ? c.WinterBallStart : c.WinterBallEnd) ?? throw new InvalidOperationException("Profile has no winter ball date");
        DateOnly? before = start ? competition.WinterBallStart : competition.WinterBallEnd;
        RequireSameObject(competition, c);
        ulong at = competition.Address + (ulong)offset;
        var expected = Encode(before, memory.Read(at, 4));
        var after = Encode(value, expected);
        if (!memory.Read(at, 4).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("The winter ball date changed since it was read");
        if (!memory.TryWrite(at, after) || !memory.Read(at, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the winter ball date at 0x{at:X}");
    }

    /// <summary>Keeps the flag bits of the existing day word; 1/1/1900 means "no date".</summary>
    static byte[] Encode(DateOnly? date, byte[] existing)
    {
        ushort flags = existing.Length == 4 ? (ushort)(BitConverter.ToUInt16(existing, 0) & ~0x1FF) : (ushort)0;
        if (date is not { } d) return [.. BitConverter.GetBytes(flags), .. BitConverter.GetBytes((short)1900)];
        ushort day = (ushort)(flags | (d.DayOfYear & 0x1FF));
        return [.. BitConverter.GetBytes(day), .. BitConverter.GetBytes((short)d.Year)];
    }

    void RequireSameObject(CompetitionRecord competition, CompetitionOffsets c)
    {
        if (memory.ReadUInt64(competition.Address) != moduleBase + c.VtableRva)
            throw new StaleObjectException("The competition object changed since it was read");
    }
}
