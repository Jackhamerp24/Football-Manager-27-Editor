using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>Agreed playing time as FM stores it: the index of the value in this list.</summary>
public enum PlayingTime : byte
{
    Unset, Star, ImportantPlayer, RegularStarter, SquadPlayer, ImpactSub, SquadPlayer2, FringePlayer, FringePlayer2,
    EmergencyBackup, BreakthroughProspect, FutureProspect, HotProspect, Youngster, BTeamRegular,
}

public enum ContractType : short { PartTime = 0, FullTime = 1, Amateur = 2, Youth = 3, NonContract = 6 }

/// <summary>
/// One bonus or clause: int32 value (-1 when unused), int16 parameter (-1, games or years), int16 type.
/// Types seen: 11 one-year extension after league games, 13 wage after reaching club league games,
/// 22 optional extension by club, 32 appearance fee, 34 clean sheet bonus, 38 unused substitute fee, 39 assist bonus.
/// </summary>
public sealed record ContractTerm(int Index, int Value, short Parameter, short Type);

/// <summary>Snapshot of every contract field the profile knows; null where the profile has no offset.</summary>
public sealed record ContractDetails(
    ulong Address, int Wage, DateOnly? Expires, DateOnly? Signed, DateOnly? Started, DateOnly? JoinedClub,
    ContractType? Type, int? LoyaltyBonus, int? SquadNumber, PlayingTime? PlayingTime, PlayingTime? FuturePlayingTime,
    int? FuturePlayingTimeYear, int? Happiness, int? PlayingTimeHappiness, IReadOnlyList<ContractTerm> Terms);

/// <summary>
/// Reads and writes the player's current contract. Same guards as <see cref="PlayerWriter"/>: the contract
/// object must still be the player's and carry the contract vtable, the field must still hold the snapshot
/// value, and the write is read back.
/// </summary>
public sealed class ContractWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int DayMask = 0x1FF, MaxClauses = 64;
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");

    public ContractDetails? Read(PlayerRecord player)
    {
        if (player.Contract is not { } info) return null;
        ulong a = Require(player).Address;
        var c = C;
        short I16(int o) => BitConverter.ToInt16(memory.Read(a + (ulong)o, 2));
        int I32(int o) => BitConverter.ToInt32(memory.Read(a + (ulong)o, 4));
        byte U8(int o) => memory.Read(a + (ulong)o, 1)[0];
        DateOnly? Date(int? o) => o is { } x ? ToDate(I16(x) & DayMask, I16(x + 2)) : null;
        return new ContractDetails(
            a, info.Wage, Date(c.ExpiryDate), Date(c.JoinedDate), Date(c.Started), Date(c.JoinedClub),
            c.Type is { } t ? (ContractType)I16(t) : null,
            c.LoyaltyBonus is { } l ? I32(l) : null,
            c.SquadNumber is { } n ? I32(n) : null,
            c.PlayingTime is { } p ? (PlayingTime)U8(p) : null,
            c.FuturePlayingTime is { } fp ? (PlayingTime)U8(fp) : null,
            c.FuturePlayingTimeYear is { } fy ? I16(fy) : null,
            c.Happiness is { } h ? U8(h) : null,
            c.PlayingTimeHappiness is { } ph ? U8(ph) : null,
            ReadTerms(a));
    }

    IReadOnlyList<ContractTerm> ReadTerms(ulong contract)
    {
        if (C.Clauses is not { } o) return [];
        ulong begin = memory.ReadUInt64(contract + (ulong)o), end = memory.ReadUInt64(contract + (ulong)o + 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxClauses) return [];
        var raw = memory.Read(begin, (int)(end - begin));
        return Enumerable.Range(0, raw.Length / 8)
            .Select(i => new ContractTerm(i, BitConverter.ToInt32(raw, i * 8), BitConverter.ToInt16(raw, i * 8 + 4), BitConverter.ToInt16(raw, i * 8 + 6)))
            .ToList();
    }

    // Overloads on PlayerRecord: the snapshot comes from the player read (wage, expiry) or a fresh read.
    public void SetWage(PlayerRecord player, int weekly)
    {
        RequireRange(weekly, 0, 10_000_000, "Wage");
        var contract = Require(player);
        Write(contract.Address + (ulong)C.Wage, BitConverter.GetBytes(contract.Wage), BitConverter.GetBytes(weekly));
    }

    public void SetExpiry(PlayerRecord player, DateOnly date)
    {
        var details = Read(player) ?? throw new StaleObjectException($"0x{player.Address:X} has no contract");
        if (player.Contract!.Expires is { } snap && details.Expires != snap) throw new StaleObjectException("Expiry changed since it was read");
        SetExpiry(player, details, date);
    }

    public void SetPlayingTime(PlayerRecord player, PlayingTime value)
    {
        var details = Read(player) ?? throw new StaleObjectException($"0x{player.Address:X} has no contract");
        SetPlayingTime(player, details, value);
    }

    public PlayingTime? ReadPlayingTime(PlayerRecord player) => player.Contract is null ? null : Read(player)?.PlayingTime;

    // Setters against a ContractDetails snapshot.
    // Contract dates: int16 day of year in the low 9 bits (the bits above are flags, kept as they are) + int16 year.
    public void SetExpiry(PlayerRecord player, ContractDetails s, DateOnly date) => WriteDate(At(player, s, C.ExpiryDate), s.Expires, date);
    public void SetSigned(PlayerRecord player, ContractDetails s, DateOnly date) => WriteDate(At(player, s, C.JoinedDate), s.Signed, date);
    public void SetStarted(PlayerRecord player, ContractDetails s, DateOnly date) => WriteDate(At(player, s, C.Started), s.Started, date);
    public void SetJoinedClub(PlayerRecord player, ContractDetails s, DateOnly date) => WriteDate(At(player, s, C.JoinedClub), s.JoinedClub, date);

    /// <summary><paramref name="expected"/> null means the field held "no date" when it was read.</summary>
    void WriteDate(ulong address, DateOnly? expected, DateOnly date)
    {
        RequireRange(date.Year, 1901, 2300, "Year");
        var current = memory.Read(address, 4);
        short raw = BitConverter.ToInt16(current);
        if (ToDate(raw & DayMask, BitConverter.ToInt16(current, 2)) != expected)
            throw new StaleObjectException($"Date at 0x{address:X} changed since it was read");
        short day = (short)((raw & ~DayMask) | date.DayOfYear);
        Write(address, current, [.. BitConverter.GetBytes(day), .. BitConverter.GetBytes((short)date.Year)]);
    }

    public void SetType(PlayerRecord player, ContractDetails s, ContractType type) =>
        Write(At(player, s, C.Type), BitConverter.GetBytes((short)Need(s.Type, "type")), BitConverter.GetBytes((short)type));

    public void SetLoyaltyBonus(PlayerRecord player, ContractDetails s, int weekly)
    {
        RequireRange(weekly, 0, 10_000_000, "Loyalty bonus");
        Write(At(player, s, C.LoyaltyBonus), BitConverter.GetBytes(Need(s.LoyaltyBonus, "loyalty bonus")), BitConverter.GetBytes(weekly));
    }

    /// <summary>-1 clears the number.</summary>
    public void SetSquadNumber(PlayerRecord player, ContractDetails s, int number)
    {
        RequireRange(number, -1, 99, "Squad number");
        Write(At(player, s, C.SquadNumber), BitConverter.GetBytes(Need(s.SquadNumber, "squad number")), BitConverter.GetBytes(number));
    }

    public void SetPlayingTime(PlayerRecord player, ContractDetails s, PlayingTime value) =>
        Write(At(player, s, C.PlayingTime), [(byte)Need(s.PlayingTime, "playing time")], [(byte)value]);

    public void SetFuturePlayingTime(PlayerRecord player, ContractDetails s, PlayingTime value, int year)
    {
        RequireRange(year, 1900, 2300, "Year");
        Write(At(player, s, C.FuturePlayingTime), [(byte)Need(s.FuturePlayingTime, "future playing time")], [(byte)value]);
        Write(At(player, s, C.FuturePlayingTimeYear), BitConverter.GetBytes((short)Need(s.FuturePlayingTimeYear, "future year")), BitConverter.GetBytes((short)year));
    }

    public void SetHappiness(PlayerRecord player, ContractDetails s, int value)
    {
        RequireRange(value, 0, 100, "Happiness");
        Write(At(player, s, C.Happiness), [(byte)Need(s.Happiness, "happiness")], [(byte)value]);
    }

    public void SetPlayingTimeHappiness(PlayerRecord player, ContractDetails s, int value)
    {
        RequireRange(value, 0, 100, "Playing time happiness");
        Write(At(player, s, C.PlayingTimeHappiness), [(byte)Need(s.PlayingTimeHappiness, "playing time happiness")], [(byte)value]);
    }

    /// <summary>Changes value and parameter of an existing clause; the type stays.</summary>
    public void SetTerm(PlayerRecord player, ContractDetails s, int index, int value, short parameter)
    {
        Require(player, s);
        if (index < 0 || index >= s.Terms.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var old = s.Terms[index];
        ulong begin = memory.ReadUInt64(s.Address + (ulong)(C.Clauses ?? throw new InvalidOperationException("Profile has no clause offset")));
        Write(begin + (ulong)index * 8,
            [.. BitConverter.GetBytes(old.Value), .. BitConverter.GetBytes(old.Parameter), .. BitConverter.GetBytes(old.Type)],
            [.. BitConverter.GetBytes(value), .. BitConverter.GetBytes(parameter), .. BitConverter.GetBytes(old.Type)]);
    }

    /// <summary>
    /// Appends a bonus or clause. Fills the vector in place when it has room; otherwise copies it into a
    /// buffer from <paramref name="allocate"/> (outside the game's allocator, as for transfers) with double capacity.
    /// </summary>
    public void AddTerm(PlayerRecord player, ContractDetails s, short type, int value, short parameter, Func<int, ulong>? allocate)
    {
        Require(player, s);
        var (vector, begin, end, cap) = TermVector(s);
        if ((end - begin) / 8 != (ulong)s.Terms.Count) throw new StaleObjectException("Bonuses and clauses changed since they were read");
        if (s.Terms.Any(t => t.Type == type && TypeIsUnique(type))) throw new InvalidOperationException($"The contract already has clause {type}");
        byte[] entry = [.. BitConverter.GetBytes(value), .. BitConverter.GetBytes(parameter), .. BitConverter.GetBytes(type)];
        var pointers = memory.Read(vector, 24);
        if (end < cap)
        {
            Write(end, memory.Read(end, 8), entry);
            Write(vector + 8, pointers[8..16], BitConverter.GetBytes(end + 8));
            return;
        }
        if (allocate is null) throw new InvalidOperationException("The clause list is full; an allocator is needed");
        int count = s.Terms.Count, capacity = Math.Max(4, count * 2);
        ulong buffer = allocate(capacity * 8);
        byte[] items = count > 0 ? [.. memory.Read(begin, count * 8), .. entry] : entry;
        Write(buffer, memory.Read(buffer, items.Length), items);
        Write(vector, pointers, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)items.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
    }

    /// <summary>Removes one bonus or clause: later entries move up, the end pointer moves back.</summary>
    public void RemoveTerm(PlayerRecord player, ContractDetails s, int index)
    {
        Require(player, s);
        if (index < 0 || index >= s.Terms.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var (vector, begin, end, _) = TermVector(s);
        if ((end - begin) / 8 != (ulong)s.Terms.Count) throw new StaleObjectException("Bonuses and clauses changed since they were read");
        var all = memory.Read(begin, (int)(end - begin));
        var expected = s.Terms.SelectMany(t => (byte[])[.. BitConverter.GetBytes(t.Value), .. BitConverter.GetBytes(t.Parameter), .. BitConverter.GetBytes(t.Type)]).ToArray();
        if (!all.AsSpan().SequenceEqual(expected)) throw new StaleObjectException("Bonuses and clauses changed since they were read");
        if (index < s.Terms.Count - 1)
            Write(begin + (ulong)index * 8, all[(index * 8)..], all[((index + 1) * 8)..]);
        Write(vector + 8, BitConverter.GetBytes(end), BitConverter.GetBytes(end - 8));
    }

    /// <summary>Clause types that can only appear once in a contract (the game keys option clauses by type).</summary>
    static bool TypeIsUnique(short type) => type is not 13;

    (ulong vector, ulong begin, ulong end, ulong cap) TermVector(ContractDetails s)
    {
        ulong vector = s.Address + (ulong)(C.Clauses ?? throw new InvalidOperationException("Profile has no clause offset"));
        var p = memory.Read(vector, 24);
        return (vector, BitConverter.ToUInt64(p, 0), BitConverter.ToUInt64(p, 8), BitConverter.ToUInt64(p, 16));
    }

    ulong At(PlayerRecord player, ContractDetails s, int? offset)
    {
        Require(player, s);
        return s.Address + (ulong)(offset ?? throw new InvalidOperationException("Profile has no offset for this contract field"));
    }

    static T Need<T>(T? value, string field) where T : struct =>
        value ?? throw new InvalidOperationException($"Profile has no offset for {field}");

    void Require(PlayerRecord player, ContractDetails s)
    {
        if (Require(player).Address != s.Address) throw new StaleObjectException("The player's contract changed since it was read");
    }

    ContractInfo Require(PlayerRecord player)
    {
        var contract = player.Contract ?? throw new StaleObjectException($"0x{player.Address:X} has no contract");
        if (memory.ReadUInt64(player.Address + (ulong)C.PlayerContract) != contract.Address)
            throw new StaleObjectException("The player's contract changed since it was read");
        if (memory.ReadUInt64(contract.Address) != moduleBase + C.ContractVtableRva)
            throw new StaleObjectException($"0x{contract.Address:X} is no longer a contract object");
        return contract;
    }

    /// <summary>1 January 1900 is how FM stores "no date".</summary>
    static DateOnly? ToDate(int day, short year)
    {
        if (year <= 1900 || year > 2300 || day < 1 || day > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(day - 1);
        return d.Year == year ? d : null;
    }


    static void RequireRange(int value, int min, int max, string field)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(field, value, $"{field} must be {min}-{max}");
    }

    void Write(ulong address, byte[] expected, byte[] value)
    {
        if (!memory.Read(address, expected.Length).AsSpan().SequenceEqual(expected))
            throw new StaleObjectException($"Value at 0x{address:X} changed since it was read");
        if (!memory.TryWrite(address, value))
            throw new MemoryAccessException($"Write at 0x{address:X} failed");
        if (!memory.Read(address, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Write at 0x{address:X} did not stick");
    }
}
