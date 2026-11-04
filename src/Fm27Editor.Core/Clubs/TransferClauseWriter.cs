using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>A clause save stopped after a write may have changed memory.</summary>
public sealed class PartialClauseWriteException(Exception inner) : Exception("Some clause fields may have been saved; reload before retrying", inner);

/// <summary>
/// Changes transfer clauses read by <see cref="TransferClauseReader"/>. Each write checks identity, list membership and
/// the full record snapshot before writing and reading back the changed fields.
/// Adding and removing is limited to sell-ons: they are referenced only from the club's vector. Appearance fees (and
/// some competition bonuses) are also in a second game-wide list that is not decoded yet.
/// </summary>
public sealed class TransferClauseWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int SellOnSize = 0x28;
    TransferClauseOffsets T => profile.Club?.TransferClauses ?? throw new InvalidOperationException("Profile has no transfer clause offsets");

    public void SetPercentage(TransferClause c, int percent)
    {
        Require(c, TransferClauseKind.SellOn);
        Range(percent, 1, 100, "Sell-on percentage");
        Write(c, 0x22, BitConverter.GetBytes(c.Percentage!.Value), BitConverter.GetBytes((short)percent));
    }

    public void SetAmount(TransferClause c, int amount)
    {
        if (c.Kind == TransferClauseKind.SellOn) throw new InvalidOperationException("A sell-on has a percentage, not an amount");
        Range(amount, 0, int.MaxValue, "Amount");
        Write(c, c.Kind == TransferClauseKind.AppearanceFee ? 0x18 : 0x28, BitConverter.GetBytes(c.Amount!.Value), BitConverter.GetBytes(amount));
    }

    /// <summary>Appearances needed, int16 (the count so far is the int16 after it).</summary>
    public void SetAppearances(TransferClause c, int appearances)
    {
        Require(c, TransferClauseKind.AppearanceFee);
        Range(appearances, 1, 1000, "Appearances");
        Write(c, 0x1C, BitConverter.GetBytes((short)c.Appearances!.Value), BitConverter.GetBytes((short)appearances));
    }

    /// <summary>Fee the player was signed for, on a sell-on; null stores -1 (none).</summary>
    public void SetSignedFor(TransferClause c, int? fee)
    {
        Require(c, TransferClauseKind.SellOn);
        if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee));
        Write(c, 0x18, BitConverter.GetBytes(c.SignedFor ?? -1), BitConverter.GetBytes(fee ?? -1));
    }

    /// <summary>Competition of a competition bonus, by internal index (competition +0x8).</summary>
    public void SetCompetition(TransferClause c, int index)
    {
        Require(c, TransferClauseKind.CompetitionBonus);
        Range(index, 0, 100_000, "Competition index");
        Write(c, 0x24, BitConverter.GetBytes(c.CompetitionIndex!.Value), BitConverter.GetBytes(index));
    }

    /// <summary>End date of a competition bonus; null stores 1/1/1900 (no end), as the game does.</summary>
    public void SetEnds(TransferClause c, DateOnly? ends)
    {
        Require(c, TransferClauseKind.CompetitionBonus);
        if (ends is { Year: <= 1900 or > 2300 }) throw new ArgumentOutOfRangeException(nameof(ends));
        var current = memory.Read(c.Address + 0x10, 4);
        if (DecodeDate(current) != c.Ends) throw new StaleObjectException("The clause's end date changed since it was read");
        Write(c, 0x10, current, ends is { } d ? EncodeDate(d) : EncodeDate(new DateOnly(1900, 1, 1)));
    }

    /// <summary>Writes the main and optional extra field of one clause against the same full record snapshot.</summary>
    public void SetValues(TransferClause c, int? main, bool changeExtra, int? extraNumber, DateOnly? extraDate)
    {
        var writes = new List<(ulong Address, byte[] Expected, byte[] Value)>();
        CheckVtable(c);
        byte[] snapshot = CheckRecord(c);
        void Add(int offset, byte[] value) => writes.Add((c.Address + (ulong)offset, snapshot[offset..(offset + value.Length)], value));
        if (main is { } value)
        {
            if (c.Kind == TransferClauseKind.SellOn) { Range(value, 1, 100, "Sell-on percentage"); Add(0x22, BitConverter.GetBytes((short)value)); }
            else { Range(value, 0, int.MaxValue, "Amount"); Add(c.Kind == TransferClauseKind.AppearanceFee ? 0x18 : 0x28, BitConverter.GetBytes(value)); }
        }
        if (changeExtra)
        {
            if (c.Kind == TransferClauseKind.SellOn)
            {
                if (extraNumber < 0) throw new ArgumentOutOfRangeException(nameof(extraNumber));
                Add(0x18, BitConverter.GetBytes(extraNumber ?? -1));
            }
            else if (c.Kind == TransferClauseKind.AppearanceFee)
            {
                if (extraNumber is not { } appearances) throw new ArgumentNullException(nameof(extraNumber));
                Range(appearances, 1, 1000, "Appearances");
                Add(0x1C, BitConverter.GetBytes((short)appearances));
            }
            else
            {
                if (extraDate is { Year: <= 1900 or > 2300 }) throw new ArgumentOutOfRangeException(nameof(extraDate));
                Add(0x10, EncodeDate(extraDate ?? new DateOnly(1900, 1, 1)));
            }
        }
        if (writes.Count == 0) throw new InvalidOperationException("No clause fields were selected");
        for (int i = 0; i < writes.Count; i++)
        {
            bool attempted = false;
            try
            {
                CheckVtable(c);
                if (!memory.Read(c.Address, snapshot.Length).AsSpan().SequenceEqual(snapshot))
                    throw new StaleObjectException("The clause changed during save");
                var write = writes[i];
                if (!memory.Read(write.Address, write.Expected.Length).AsSpan().SequenceEqual(write.Expected))
                    throw new StaleObjectException("The clause changed during save");
                attempted = true;
                if (!memory.TryWrite(write.Address, write.Value) || !memory.Read(write.Address, write.Value.Length).AsSpan().SequenceEqual(write.Value))
                    throw new MemoryAccessException($"Write at 0x{write.Address:X} failed");
                write.Value.CopyTo(snapshot, (int)(write.Address - c.Address));
            }
            catch (Exception ex) when ((i > 0 || attempted) && (ex is StaleObjectException or MemoryAccessException))
            {
                throw new PartialClauseWriteException(ex);
            }
        }
    }

    /// <summary>Removes the sell-on's pointer from the club's vector (shift left, end − 8). The record itself stays allocated.</summary>
    public void RemoveSellOn(ulong club, TransferClause c)
    {
        Require(c, TransferClauseKind.SellOn);
        CheckVtable(c);
        if (c.Origin?.Club != club) throw new StaleObjectException("The clause belongs to a different club");
        CheckRecord(c);
        var (vector, begin, end, _) = Vector(club);
        var items = memory.Read(begin, (int)(end - begin));
        int n = items.Length / 8;
        int index = Enumerable.Range(0, n).FirstOrDefault(i => BitConverter.ToUInt64(items, i * 8) == c.Address, -1);
        if (index < 0) throw new StaleObjectException("The clause is no longer in the club's list");
        var writes = new List<(ulong, byte[], byte[])>();
        for (int i = index; i + 1 < n; i++) writes.Add((begin + (ulong)i * 8, items[(i * 8)..(i * 8 + 8)], items[((i + 1) * 8)..((i + 2) * 8)]));
        writes.Add((vector + 8, BitConverter.GetBytes(end), BitConverter.GetBytes(end - 8)));
        Apply(writes);
    }

    /// <summary>
    /// A new sell-on owed by <paramref name="club"/> to <paramref name="beneficiary"/> on the player's next sale.
    /// The record and, when the vector is full, a new vector buffer come from <paramref name="allocate"/>
    /// (outside the game's allocator).
    /// </summary>
    public ulong AddSellOn(ulong club, int playerId, ulong beneficiary, int percent, Func<int, ulong> allocate, int? signedFor = null)
    {
        Range(percent, 1, 100, "Sell-on percentage");
        var k = profile.Club!;
        if (memory.ReadUInt64(beneficiary) != moduleBase + k.ClubVtableRva) throw new StaleObjectException("Beneficiary is not a club object");
        if (beneficiary == club) throw new InvalidOperationException("A club cannot owe a sell-on to itself");
        var (vector, begin, end, cap) = Vector(club);
        var oldPointers = memory.Read(vector, 24);

        var record = new byte[SellOnSize];
        BitConverter.GetBytes(moduleBase + T.SellOnVtableRva).CopyTo(record, 0);
        BitConverter.GetBytes(playerId).CopyTo(record, 0x08);
        BitConverter.GetBytes(playerId).CopyTo(record, 0x0C);
        BitConverter.GetBytes(beneficiary).CopyTo(record, 0x10);
        if (signedFor < 0) throw new ArgumentOutOfRangeException(nameof(signedFor));
        BitConverter.GetBytes(signedFor ?? -1).CopyTo(record, 0x18);
        record[0x20] = 0xFF;
        BitConverter.GetBytes((short)percent).CopyTo(record, 0x22);

        ulong address = allocate(SellOnSize);
        var writes = new List<(ulong, byte[], byte[])> { (address, memory.Read(address, SellOnSize), record) };
        if (end < cap)
        {
            writes.Add((end, memory.Read(end, 8), BitConverter.GetBytes(address)));
            writes.Add((vector + 8, BitConverter.GetBytes(end), BitConverter.GetBytes(end + 8)));
        }
        else
        {
            var items = memory.Read(begin, (int)(end - begin));
            int capacity = Math.Max(4, (items.Length / 8 + 1) * 2);
            ulong buffer = allocate(capacity * 8);
            byte[] bytes = [.. items, .. BitConverter.GetBytes(address)];
            writes.Add((buffer, memory.Read(buffer, bytes.Length), bytes));
            writes.Add((vector, oldPointers,
                [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)bytes.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]));
        }
        Apply(writes);
        return address;
    }

    (ulong vector, ulong begin, ulong end, ulong cap) Vector(ulong club)
    {
        var k = profile.Club!;
        if (memory.ReadUInt64(club) != moduleBase + k.ClubVtableRva) throw new StaleObjectException($"0x{club:X} is no longer a club object");
        ulong holder = memory.ReadUInt64(club + (ulong)T.Pointer);
        if (holder == 0) throw new InvalidOperationException("The club has no clause list");
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8), cap = memory.ReadUInt64(holder + 16);
        if (end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0
            || (cap - begin) / 8 > 4096) throw new StaleObjectException("The club's clause list looks invalid");
        return (holder, begin, end, cap);
    }

    void Apply(List<(ulong Address, byte[] Expected, byte[] Value)> writes)
    {
        foreach (var w in writes)
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException("The clause list changed since it was read; nothing written");
        foreach (var w in writes)
            if (!memory.TryWrite(w.Address, w.Value) || !memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                throw new MemoryAccessException($"Write at 0x{w.Address:X} failed");
    }

    void Write(TransferClause c, int offset, byte[] expected, byte[] value)
    {
        CheckVtable(c);
        CheckRecord(c);
        Apply([(c.Address + (ulong)offset, expected, value)]);
    }

    byte[] CheckRecord(TransferClause c)
    {
        int size = c.Kind == TransferClauseKind.CompetitionBonus ? 0x38 : 0x28;
        if (c.Snapshot is not { } snapshot || snapshot.Length != size * 2)
            throw new StaleObjectException("The clause snapshot is unavailable");
        byte[] expected = Convert.FromHexString(snapshot);
        if (!memory.Read(c.Address, size).AsSpan().SequenceEqual(expected))
            throw new StaleObjectException("The clause changed since it was read");
        return expected;
    }

    void CheckVtable(TransferClause c)
    {
        uint? rva = c.Kind switch
        {
            TransferClauseKind.SellOn => T.SellOnVtableRva,
            TransferClauseKind.AppearanceFee => T.AppearanceFeeVtableRva,
            _ => T.CompetitionBonusVtableRva,
        };
        if (rva is null || memory.ReadUInt64(c.Address) != moduleBase + rva.Value)
            throw new StaleObjectException($"0x{c.Address:X} is no longer a {c.Kind} clause");
        if (BitConverter.ToInt32(memory.Read(c.Address + 8, 4)) != c.PlayerId)
            throw new StaleObjectException($"The player of the clause at 0x{c.Address:X} changed since it was read");
        if (c.Origin is not { } origin || profile.Club is not { } club
            || memory.ReadUInt64(origin.Club) != moduleBase + club.ClubVtableRva
            || memory.ReadUInt64(origin.Club + (ulong)T.Pointer) != origin.Holder
            || memory.ReadUInt64(origin.Holder) != origin.Begin
            || memory.ReadUInt64(origin.Holder + 8) != origin.End
            || memory.ReadUInt64(origin.Holder + 16) != origin.Capacity
            || origin.Slot < origin.Begin || origin.Slot >= origin.End
            || (origin.Slot - origin.Begin) % 8 != 0 || memory.ReadUInt64(origin.Slot) != c.Address)
            throw new StaleObjectException("The clause's containing list changed since it was read");
    }

    static void Require(TransferClause c, TransferClauseKind kind)
    {
        if (c.Kind != kind) throw new InvalidOperationException($"Not a {kind} clause");
    }

    static void Range(int value, int min, int max, string field)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(field, value, $"{field} must be {min}-{max}");
    }

    static byte[] EncodeDate(DateOnly d) => [.. BitConverter.GetBytes((short)d.DayOfYear), .. BitConverter.GetBytes((short)d.Year)];

    static DateOnly? DecodeDate(byte[] b)
    {
        short day = BitConverter.ToInt16(b), year = BitConverter.ToInt16(b, 2);
        if (year <= 1900 || year > 2300 || day < 1 || day > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(day - 1);
        return d.Year == year ? d : null;
    }
}
