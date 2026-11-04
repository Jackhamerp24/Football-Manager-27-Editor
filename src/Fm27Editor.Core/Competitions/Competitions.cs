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
    uint Foreground, uint Background, uint Trim, uint WinterBallColour, DateOnly? WinterBallStart, DateOnly? WinterBallEnd)
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
            Colour(b, wb), Date(b, ws), Date(b, we));
    }

    static uint Colour(byte[] b, int at) => (uint)(b[at + 3] << 24 | b[at + 2] << 16 | b[at + 1] << 8 | b[at]);

    static DateOnly? Date(byte[] b, int at)
    {
        // Surveyed over all 8,213 competitions (3/11/2026): every day word is at most 0x1FF, so no flag bits are masked.
        int day = BitConverter.ToUInt16(b, at), year = BitConverter.ToInt16(b, at + 2);
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

/// <summary>
/// Sets Reputation. FMRTE 27 wrote the value to two adjacent int16 (Süper Lig 155 → 156 changed both), so both are written.
/// Guarded: competition vtable, both int16 still equal to the snapshot, read back.
/// </summary>
public sealed class CompetitionWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int MaxReputation = 200;

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
}
