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
    /// <summary>Exact game bytes for guarded date writes, including sentinel representation and flag bits.</summary>
    public uint? WinterBallStartRaw { get; init; }
    public uint? WinterBallEndRaw { get; init; }
}

public sealed class CompetitionRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxCompetitions = 100_000, MaxName = 300;
    CompetitionOffsets C => profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");

    /// <param name="players">Supplies the players for the team fallback; called only when the list slot is unusable.</param>
    public IReadOnlyList<CompetitionRecord> LoadAll(Func<IReadOnlyList<PlayerRecord>>? players = null)
    {
        var c = C;
        if (c.ListRootRva is not { } rva || c.ListOffset is not { } off) throw new InvalidOperationException("Profile has no competition list");
        ulong root = TryPointer(moduleBase + (ulong)rva) ?? 0;
        ulong begin = root == 0 ? 0 : TryPointer(root + (ulong)off) ?? 0, end = root == 0 ? 0 : TryPointer(root + (ulong)off + 8) ?? 0;
        var result = new List<CompetitionRecord>();
        MemoryAccessException? listProblem = null;
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxCompetitions)
            listProblem = new MemoryAccessException($"Competition list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        else
        {
            var raw = memory.Read(begin, (int)(end - begin));
            for (int i = 0; i < raw.Length; i += 8)
                if (Reload(BitConverter.ToUInt64(raw, i)) is { } r) result.Add(r);
        }
        if (result.Count > 0) return result;
        // The list slot is missing or points at something else (the Game Pass slot held squad-rule containers, 502 of them).
        // FM Studio 1.1.1 does not use a list at all: it takes [team + TeamCompetition] of every team and walks the
        // competition objects next to each hit at the object size, which found all 8,213 on Game Pass 2493426.
        var found = DiscoverFromTeams(players);
        if (found.Count > 0) return found;
        return listProblem is null ? result : throw listProblem;
    }

    /// <summary>Size of one competition object, also the distance between neighbours in the game's arrays.</summary>
    public const int ObjectStride = 0x1B0;

    /// <summary>Competitions reached from the teams of every loaded player, plus their neighbours in memory, ordered by index.</summary>
    IReadOnlyList<CompetitionRecord> DiscoverFromTeams(Func<IReadOnlyList<PlayerRecord>>? players)
    {
        var c = C;
        if (profile.Team is not { } team) return [];
        ulong vtable = moduleBase + c.VtableRva;
        var teams = new HashSet<ulong>();
        foreach (var player in players?.Invoke() ?? new PlayerRepository(memory, profile, moduleBase).LoadAll())
        {
            if (TryPointer(player.Address + (ulong)team.PlayerTeam) is { } playing) teams.Add(playing);
            if (profile.Contract is { } contract && player.Contract is { } k && TryPointer(k.Address + (ulong)contract.Team) is { } contractTeam)
                teams.Add(contractTeam);
        }
        bool IsCompetition(ulong address) => address >= 0x10000 && TryPointer(address) == vtable;
        var seeds = new HashSet<ulong>();
        foreach (ulong t in teams)
            if (TryPointer(t + (ulong)c.TeamCompetition) is { } p && IsCompetition(p)) seeds.Add(p);
        var all = new HashSet<ulong>(seeds);
        foreach (ulong seed in seeds)
            for (int direction = -1; direction <= 1; direction += 2)
                for (ulong at = seed; ;)
                {
                    at = direction > 0 ? at + ObjectStride : at - ObjectStride;
                    if (!IsCompetition(at) || !all.Add(at)) break;
                }
        var found = new List<CompetitionRecord>(all.Count);
        foreach (ulong address in all)
            if (Reload(address) is { } record) found.Add(record);
        return found.OrderBy(r => r.Index).ThenBy(r => r.Id).ToList();
    }

    ulong? TryPointer(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return at >= 0x10000 && memory.TryRead(at, b) && BitConverter.ToUInt64(b) is var v && v != 0 ? v : null;
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
            c.SubstitutionRules is { } subs && subs < size ? b[subs] : (byte)0xFF)
            { WinterBallStartRaw = BitConverter.ToUInt32(b, ws), WinterBallEndRaw = BitConverter.ToUInt32(b, we) };
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
        uint raw = (start ? competition.WinterBallStartRaw : competition.WinterBallEndRaw)
            ?? throw new InvalidOperationException("The competition snapshot has no raw winter ball date");
        RequireSameObject(competition, c);
        ulong at = competition.Address + (ulong)offset;
        var expected = BitConverter.GetBytes(raw);
        var after = Encode(value, expected);
        if (!memory.Read(at, 4).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("The winter ball date changed since it was read");
        if (!memory.TryWrite(at, after) || !memory.Read(at, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the winter ball date at 0x{at:X}");
    }

    /// <summary>Keeps the flag bits of the existing day word; 1/1/1900 means "no date".</summary>
    static byte[] Encode(DateOnly? date, byte[] existing)
    {
        ushort flags = existing.Length == 4 ? (ushort)(BitConverter.ToUInt16(existing, 0) & ~0x1FF) : (ushort)0;
        if (date is not { } d) return [.. BitConverter.GetBytes((ushort)(flags | 1)), .. BitConverter.GetBytes((short)1900)];
        ushort day = (ushort)(flags | (d.DayOfYear & 0x1FF));
        return [.. BitConverter.GetBytes(day), .. BitConverter.GetBytes((short)d.Year)];
    }

    void RequireSameObject(CompetitionRecord competition, CompetitionOffsets c)
    {
        if (memory.ReadUInt64(competition.Address) != moduleBase + c.VtableRva)
            throw new StaleObjectException("The competition object changed since it was read");
    }
}
