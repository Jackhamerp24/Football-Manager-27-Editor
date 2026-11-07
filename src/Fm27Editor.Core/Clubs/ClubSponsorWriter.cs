using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Edits one existing sponsor record (annual value and its two dates) while the club, list vector and record still
/// match the snapshot. Adding a sponsor copies the first record as a template, so unknown bytes keep the layout the
/// game wrote; the new record and any grown pointer array come from <c>VirtualAllocEx</c>, outside the game allocator.
/// </summary>
public sealed class ClubSponsorWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase,
    Func<int, ulong>? allocate = null)
{
    public void SetTotal(ulong club, ClubSponsorEntry entry, int value)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.SponsorTotal < 0 || f.SponsorTotal > f.SponsorSize - 4)
            throw new InvalidOperationException("Sponsor value offset is unavailable for this build");
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Sponsor value cannot be negative");
        RequireCurrent(club, entry);
        byte[] bytes = BitConverter.GetBytes(value);
        ulong address = entry.Address + (ulong)f.SponsorTotal;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write sponsor value at 0x{address:X}");
    }

    public void SetDates(ulong club, ClubSponsorEntry entry, DateOnly start, DateOnly end)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.SponsorStart < 0 || f.SponsorEnd < 0 || f.SponsorStart > f.SponsorSize - 4 || f.SponsorEnd > f.SponsorSize - 4)
            throw new InvalidOperationException("Sponsor date offsets are unavailable for this build");
        // Bounds the reader can decode back; not a claimed FMRTE limit.
        if (start.Year is <= 1900 or > 2300 || end.Year is <= 1900 or > 2300)
            throw new ArgumentOutOfRangeException(nameof(start), "Sponsor date cannot be read back by this profile");
        RequireCurrent(club, entry);
        var startBytes = DateBytes(entry.Address, f.SponsorStart, start);
        var endBytes = DateBytes(entry.Address, f.SponsorEnd, end);
        Write(entry.Address + (ulong)f.SponsorStart, startBytes);
        Write(entry.Address + (ulong)f.SponsorEnd, endBytes);
    }

    byte[] DateBytes(ulong record, int offset, DateOnly date)
    {
        byte[] current = memory.Read(record + (ulong)offset, 4);
        ushort original = BitConverter.ToUInt16(current, 0);
        var bytes = new byte[4];
        BitConverter.GetBytes((ushort)((original & 0xFE00) | date.DayOfYear)).CopyTo(bytes, 0);
        BitConverter.GetBytes((short)date.Year).CopyTo(bytes, 2);
        return bytes;
    }

    void Write(ulong address, byte[] bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write sponsor date at 0x{address:X}");
    }

    /// <summary>Income type codes seen in FMRTE's dropdown and in save data; other codes stay unsupported.</summary>
    public static bool SupportedIncomeType(byte value) => value is 1 or 3 or 4 or 6 or 11 or 13 or 18 or 19 or 22 or 23;

    public void SetIncomeType(ulong club, ClubSponsorEntry entry, byte value)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.SponsorType < 0 || f.SponsorType >= f.SponsorSize)
            throw new InvalidOperationException("Sponsor type offset is unavailable for this build");
        if (!SupportedIncomeType(value) || !SupportedIncomeType(entry.IncomeTypeCode))
            throw new ArgumentOutOfRangeException(nameof(value), "Income type is not verified");
        RequireCurrent(club, entry);
        byte[] bytes = [value];
        ulong address = entry.Address + (ulong)f.SponsorType;
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 1).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write sponsor income type at 0x{address:X}");
    }

    /// <summary>FMRTE's Renew Income and Fixed Value checkboxes: one byte each, 1 = ticked.</summary>
    public void SetFlags(ulong club, ClubSponsorEntry entry, bool renewIncome, bool fixedValue)
    {
        var f = profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");
        if (f.SponsorRenew is not { } renew || f.SponsorFixed is not { } fixedOffset
            || renew < 0 || fixedOffset < 0 || renew >= f.SponsorSize || fixedOffset >= f.SponsorSize)
            throw new InvalidOperationException("Sponsor flags are not mapped in this profile");
        if (entry.RenewIncome is null || entry.FixedValue is null) throw new StaleObjectException("Sponsor flags are unavailable");
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)renew, [renewIncome ? (byte)1 : (byte)0]);
        Write(entry.Address + (ulong)fixedOffset, [fixedValue ? (byte)1 : (byte)0]);
    }

    ClubFinanceListOffsets Finance() =>
        profile.Club?.FinanceLists ?? throw new InvalidOperationException("Profile has no finance list offsets");

    /// <summary>Appends a sponsor record built from the first existing record; returns the new entry.</summary>
    public ClubSponsorEntry Add(ulong club, byte incomeType, int total, DateOnly start, DateOnly end,
        bool renewIncome = false, bool fixedValue = false)
    {
        var f = Finance();
        if (allocate is null) throw new InvalidOperationException("This session cannot allocate memory");
        if (!SupportedIncomeType(incomeType)) throw new ArgumentOutOfRangeException(nameof(incomeType), "Income type is not verified");
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "Sponsor value cannot be negative");
        if (start.Year is <= 1900 or > 2300 || end.Year is <= 1900 or > 2300)
            throw new ArgumentOutOfRangeException(nameof(start), "Sponsor date cannot be read back by this profile");

        var reader = new ClubListReader(memory, profile, moduleBase);
        var existing = reader.ReadSponsors(club);
        if (existing.Count == 0) throw new InvalidOperationException("A sponsor record is needed as the template");

        ulong holder = PointerAt(club, f.SponsorsPointer);
        if (holder == 0 || !VectorState(holder, out ulong begin, out ulong endPointer, out ulong capacity))
            throw new StaleObjectException("The sponsor list changed since it was read");
        int count = (int)((endPointer - begin) / 8);
        if (count <= 0 || endPointer > capacity) throw new StaleObjectException("The sponsor list is inconsistent");
        if (memory.Read(existing[0].Address, f.SponsorSize) is not { Length: > 0 } template || template.Length < f.SponsorSize)
            throw new MemoryAccessException("Could not read the template sponsor record");

        var record = new byte[f.SponsorSize];
        template.CopyTo(record, 0);
        record[f.SponsorType] = incomeType;
        BitConverter.GetBytes(total).CopyTo(record, f.SponsorTotal);
        SetDate(record, f.SponsorStart, start);
        SetDate(record, f.SponsorEnd, end);
        if (f.SponsorRenew is { } renew) record[renew] = renewIncome ? (byte)1 : (byte)0;
        if (f.SponsorFixed is { } fixedValueOffset) record[fixedValueOffset] = fixedValue ? (byte)1 : (byte)0;

        ulong slot = allocate(f.SponsorSize);
        if (slot == 0 || !memory.TryWrite(slot, record))
            throw new MemoryAccessException("Could not write the new sponsor record");

        if (endPointer + 8 <= capacity)
        {
            Write(endPointer, BitConverter.GetBytes(slot));
            Write(holder + 8, BitConverter.GetBytes(endPointer + 8));
        }
        else
        {
            // begin/end/capacity are absolute addresses, so the new array is sized from the entry count, not from capacity.
            int newSlots = Math.Max(4, count * 2);
            ulong buffer = allocate(newSlots * 8);
            if (buffer == 0) throw new MemoryAccessException("Could not allocate a larger sponsor list");
            if (count > 0)
            {
                var pointers = memory.Read(begin, count * 8);
                if (pointers.Length < count * 8 || !memory.TryWrite(buffer, pointers))
                    throw new MemoryAccessException("Could not copy the sponsor list");
            }
            Write(buffer + (ulong)(count * 8), BitConverter.GetBytes(slot));
            Write(holder, BitConverter.GetBytes(buffer));
            Write(holder + 8, BitConverter.GetBytes(buffer + (ulong)((count + 1) * 8)));
            Write(holder + 0x10, BitConverter.GetBytes(buffer + (ulong)(newSlots * 8)));
        }

        var after = reader.ReadSponsors(club);
        var added = after.FirstOrDefault(e => e.Address == slot)
            ?? throw new MemoryAccessException("The new sponsor is not in the list after writing");
        return added;
    }

    /// <summary>Drops the list entry that points at this record; the record bytes are left untouched.</summary>
    public void Remove(ulong club, ClubSponsorEntry entry)
    {
        var f = Finance();
        ulong holder = PointerAt(club, f.SponsorsPointer);
        if (holder == 0 || !VectorState(holder, out ulong begin, out ulong endPointer, out _))
            throw new StaleObjectException("The sponsor list changed since it was read");
        int count = (int)((endPointer - begin) / 8);
        var pointers = memory.Read(begin, count * 8);
        if (pointers.Length < count * 8) throw new MemoryAccessException("Could not read the sponsor list");
        int index = -1;
        for (int i = 0; i < count; i++)
            if (BitConverter.ToUInt64(pointers, i * 8) == entry.Address) { index = i; break; }
        if (index < 0) throw new StaleObjectException("The sponsor is no longer in the list");

        var shifted = new byte[(count - index - 1) * 8];
        Array.Copy(pointers, (index + 1) * 8, shifted, 0, shifted.Length);
        if (shifted.Length > 0) Write(begin + (ulong)(index * 8), shifted);
        Write(holder + 8, BitConverter.GetBytes(endPointer - 8));

        if (new ClubListReader(memory, profile, moduleBase).ReadSponsors(club).Any(e => e.Address == entry.Address))
            throw new MemoryAccessException("The sponsor is still in the list after writing");
    }

    static void SetDate(byte[] record, int offset, DateOnly date)
    {
        ushort current = BitConverter.ToUInt16(record, offset);
        BitConverter.GetBytes((ushort)((current & 0xFE00) | date.DayOfYear)).CopyTo(record, offset);
        BitConverter.GetBytes((short)date.Year).CopyTo(record, offset + 2);
    }

    ulong PointerAt(ulong address, int offset)
    {
        var raw = memory.Read(address + (ulong)offset, 8);
        return raw.Length < 8 ? 0 : BitConverter.ToUInt64(raw, 0);
    }

    /// <summary>Vector header: begin, end and capacity are three pointers at the holder itself.</summary>
    bool VectorState(ulong holder, out ulong begin, out ulong end, out ulong capacity)
    {
        begin = end = capacity = 0;
        var raw = memory.Read(holder, 0x18);
        if (raw.Length < 0x18) return false;
        begin = BitConverter.ToUInt64(raw, 0);
        end = BitConverter.ToUInt64(raw, 8);
        capacity = BitConverter.ToUInt64(raw, 0x10);
        return begin != 0 && end >= begin && capacity >= end;
    }

    void RequireCurrent(ulong club, ClubSponsorEntry entry)
    {
        var current = new ClubListReader(memory, profile, moduleBase).ReadSponsors(club).FirstOrDefault(e => e.Address == entry.Address);
        if (current != entry) throw new StaleObjectException("The sponsor or its containing list changed since it was read");
    }
}
