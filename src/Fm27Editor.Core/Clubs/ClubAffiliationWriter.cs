using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Edits observed affiliation fields only after validating both clubs, vector membership and record snapshot.</summary>
public sealed class ClubAffiliationWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetAnnualFee(ulong club, ClubAffiliationEntry entry, int fee)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationAnnualFee is not { } offset || offset < 0 || offset > layout.AffiliationSize - 4 || entry.AnnualFee is null)
            throw new InvalidOperationException("Annual fee is unavailable for this build or record");
        if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee), "Negative affiliation fees are unverified");
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, BitConverter.GetBytes(fee));
    }

    public void SetEndDate(ulong club, ClubAffiliationEntry entry, DateOnly date)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationEnd is not { } offset || offset < 0 || offset > layout.AffiliationSize - 4 || entry.EndDate is null)
            throw new InvalidOperationException("End date is unavailable for this build or record");
        SetDate(club, entry, date, offset);
    }

    public void SetStartDate(ulong club, ClubAffiliationEntry entry, DateOnly date)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        int offset = layout.AffiliationStart;
        if (!layout.AffiliationStartEditable || offset < 0 || offset > layout.AffiliationSize - 4 || entry.StartDate is null)
            throw new InvalidOperationException("Start date is unavailable for this build or record");
        SetDate(club, entry, date, offset);
    }

    public void SetFriendlyChance(ulong club, ClubAffiliationEntry entry, int value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationFriendlyChance is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.FriendlyChance is null)
            throw new InvalidOperationException("Friendly chance is unavailable for this build or record");
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, [(byte)value]);
    }

    public void SetParentWagePercent(ulong club, ClubAffiliationEntry entry, int value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationParentWagePercent is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.ParentWagePercent is null)
            throw new InvalidOperationException("Parent wage percentage is unavailable for this build or record");
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, [(byte)value]);
    }

    public void SetPlayFriendlies(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationPlayFriendlies is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.PlayFriendlies is null)
            throw new InvalidOperationException("Play Friendlies is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x10 : original & ~0x10)]);
    }

    public void SetFirstOption(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationFirstOption is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.FirstOption is null)
            throw new InvalidOperationException("First Option To Buy is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x08 : original & ~0x08)]);
    }

    public void SetPlayersOnLoan(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationPlayersOnLoan is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.PlayersOnLoan is null)
            throw new InvalidOperationException("Players Go On Loan is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x04 : original & ~0x04)]);
    }

    public void SetScoutingShared(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationScoutingShared is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.ScoutingShared is null)
            throw new InvalidOperationException("Scouting Knowledge Shared is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x40 : original & ~0x40)]);
    }

    void SetDate(ulong club, ClubAffiliationEntry entry, DateOnly date, int offset)
    {
        if (date.Year is <= 1900 or > 2300) throw new ArgumentOutOfRangeException(nameof(date));
        RequireCurrent(club, entry);
        byte[] original = Convert.FromHexString(entry.Snapshot!.Bytes);
        ushort day = (ushort)((BitConverter.ToUInt16(original, offset) & 0xFE00) | date.DayOfYear);
        byte[] bytes = new byte[4];
        BitConverter.GetBytes(day).CopyTo(bytes, 0);
        BitConverter.GetBytes((short)date.Year).CopyTo(bytes, 2);
        Write(entry.Address + (ulong)offset, bytes);
    }

    void RequireCurrent(ulong club, ClubAffiliationEntry entry)
    {
        if (entry.Snapshot is not { } snapshot || snapshot.Club != club
            || (entry.ParentClub?.Address != club && entry.FeederClub?.Address != club))
            throw new StaleObjectException("Affiliation snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadAffiliations(club).FirstOrDefault(r => r.Address == entry.Address);
        if (current != entry) throw new StaleObjectException("The affiliation or its containing list changed since it was read");
    }

    void Write(ulong address, byte[] bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write affiliation field at 0x{address:X}");
    }

    /// <summary>Removes one affiliation: the pointer entries after it shift left and the vector's end shrinks by one.</summary>
    public void Remove(ulong club, ClubAffiliationEntry entry)
    {
        var snapshot = RequireCurrentSnapshot(club, entry);
        ulong other = entry.ParentClub!.Address == club ? entry.FeederClub!.Address : entry.ParentClub.Address;
        ClubAffiliationSnapshot? reciprocal = null;
        if (other != club)
        {
            var matches = new ClubListReader(memory, profile, moduleBase).ReadAffiliations(other)
                .Where(r => r.Address == entry.Address).ToArray();
            if (matches.Length != 1) throw new StaleObjectException("The other club must contain exactly one matching affiliation");
            reciprocal = RequireCurrentSnapshot(other, matches[0]);
        }
        RemoveFromVector(club, snapshot);
        if (reciprocal is not null) RemoveFromVector(other, reciprocal);
    }

    void RemoveFromVector(ulong club, ClubAffiliationSnapshot snapshot)
    {
        ulong slot = snapshot.Slot, next = slot + 8;
        if (next < snapshot.End)
        {
            var tail = memory.Read(next, (int)(snapshot.End - next));
            if (!memory.TryWrite(slot, tail) || !memory.Read(slot, tail.Length).AsSpan().SequenceEqual(tail))
                throw new MemoryAccessException($"Could not move affiliation pointers at 0x{slot:X}");
        }
        var end = BitConverter.GetBytes(snapshot.End - 8);
        ulong header = HeaderFor(club);
        if (!memory.TryWrite(header + 8, end) || !memory.Read(header + 8, 8).AsSpan().SequenceEqual(end))
            throw new MemoryAccessException($"Could not shrink the affiliation vector at 0x{header:X}");
    }

    /// <summary>
    /// Adds a link by copying an existing affiliation record into a new object and appending its pointer; the pointer
    /// array grows outside the game's allocator when it is full.
    /// </summary>
    public ClubAffiliationEntry? Duplicate(ulong club, ClubAffiliationEntry entry, Func<int, ulong>? allocate)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        var snapshot = RequireCurrentSnapshot(club, entry);
        ulong other = entry.ParentClub!.Address == club ? entry.FeederClub!.Address : entry.ParentClub.Address;
        ClubAffiliationSnapshot? reciprocal = null;
        if (other != club)
        {
            var matches = new ClubListReader(memory, profile, moduleBase).ReadAffiliations(other)
                .Where(r => r.Address == entry.Address).ToArray();
            if (matches.Length != 1) throw new StaleObjectException("The other club must contain exactly one matching affiliation");
            reciprocal = RequireCurrentSnapshot(other, matches[0]);
        }
        if (allocate is null) throw new StaleObjectException("No allocator is available to add an affiliation");
        var bytes = Convert.FromHexString(snapshot.Bytes);
        ulong record = allocate(layout.AffiliationSize);
        if (record == 0) throw new MemoryAccessException("Could not allocate an affiliation record");
        if (!memory.TryWrite(record, bytes) || !memory.Read(record, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write the new affiliation at 0x{record:X}");
        Append(club, snapshot, record, allocate);
        if (reciprocal is not null) Append(other, reciprocal, record, allocate);
        return new ClubListReader(memory, profile, moduleBase).ReadAffiliations(club).LastOrDefault(r => r.Address == record);
    }

    void Append(ulong club, ClubAffiliationSnapshot snapshot, ulong record, Func<int, ulong> allocate)
    {
        ulong header = HeaderFor(club);
        ulong begin = snapshot.Begin, end = snapshot.End, cap = snapshot.Capacity;
        if (end + 8 <= cap)
        {
            Pointers(end, record);
            Grow(header + 8, end + 8);
        }
        else
        {
            int oldBytes = (int)(end - begin), newBytes = oldBytes + 16;
            ulong buffer = allocate(newBytes);
            if (buffer == 0) throw new MemoryAccessException("Could not allocate an affiliation pointer array");
            var copy = memory.Read(begin, oldBytes);
            if (!memory.TryWrite(buffer, copy) || !memory.Read(buffer, copy.Length).AsSpan().SequenceEqual(copy))
                throw new MemoryAccessException($"Could not copy affiliation pointers to 0x{buffer:X}");
            Pointers(buffer + (ulong)oldBytes, record);
            var head = new byte[24];
            BitConverter.GetBytes(buffer).CopyTo(head, 0);
            BitConverter.GetBytes(buffer + (ulong)oldBytes + 8).CopyTo(head, 8);
            BitConverter.GetBytes(buffer + (ulong)newBytes).CopyTo(head, 16);
            if (!memory.TryWrite(header, head) || !memory.Read(header, head.Length).AsSpan().SequenceEqual(head))
                throw new MemoryAccessException($"Could not point the affiliation vector at 0x{buffer:X}");
        }
    }

    ClubAffiliationSnapshot RequireCurrentSnapshot(ulong club, ClubAffiliationEntry entry)
    {
        if (entry.Snapshot is not { } snapshot || snapshot.Club != club
            || (entry.ParentClub?.Address != club && entry.FeederClub?.Address != club))
            throw new StaleObjectException("Affiliation snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadAffiliations(club).FirstOrDefault(r => r.Address == entry.Address);
        if (current != entry) throw new StaleObjectException("The affiliation or its containing list changed since it was read");
        return snapshot;
    }

    /// <summary>The vector header is inline in the club object (begin / end / capacity at +0, +8, +16).</summary>
    ulong HeaderFor(ulong club)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.Affiliations < 0) throw new StaleObjectException("Profile has no affiliation vector offset");
        return club + (ulong)layout.Affiliations;
    }

    void Pointers(ulong slot, ulong record)
    {
        var pointer = BitConverter.GetBytes(record);
        if (!memory.TryWrite(slot, pointer) || !memory.Read(slot, 8).AsSpan().SequenceEqual(pointer))
            throw new MemoryAccessException($"Could not write the affiliation pointer at 0x{slot:X}");
    }

    void Grow(ulong address, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 8).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not grow the affiliation vector at 0x{address:X}");
    }
}
