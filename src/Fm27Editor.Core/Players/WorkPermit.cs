using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>A club (its team object) that was refused a work permit for the player: one "Cannot Apply for Work Permit" row.</summary>
public sealed record WorkPermitDenial(ulong Team, string? ClubName);

/// <summary>
/// The "Work Permit Denied" list of FMRTE 27's Work Permit tab. It is not a list of its own: each entry is a relationship row of
/// type 0x75 (FMRTE's "Cannot Apply for Work Permit") whose target is the refused club's team (target kind 4). Observed on
/// Álvaro Ginés (9/11/2026): after Man Utd's work-permit application was rejected the row
/// <c>88DE9324F3010000 2C00 04 75 00 00 00 FF</c> appeared in his relations and FMRTE listed "Manchester United" under
/// Work Permit Denied; FMRTE's Relationships tab names the same row "Cannot Apply for Work Permit". Remove erases the row like
/// any relationship (later rows move up, the end pointer moves back one record).
/// </summary>
public sealed class WorkPermitAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const byte CannotApplyType = 0x75;

    public IReadOnlyList<WorkPermitDenial> ReadDenied(PlayerRecord player, Func<ulong, string?> teamName)
    {
        var o = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        var owner = PersonOwner.Of(player, profile);
        if (!owner.IsValid(memory, moduleBase)) return [];
        ulong holder = memory.ReadUInt64(owner.At(o.Pointer));
        if (holder == 0) return [];
        ulong begin = memory.ReadUInt64(holder), end = memory.ReadUInt64(holder + 8);
        if (begin == 0 || end <= begin || (end - begin) % (ulong)o.RecordSize != 0 || (end - begin) / (ulong)o.RecordSize > 512) return [];
        var rows = memory.Read(begin, (int)(end - begin));
        var result = new List<WorkPermitDenial>();
        for (int i = 0; i < rows.Length; i += o.RecordSize)
            if (rows[i + o.Type] == CannotApplyType)
            {
                ulong team = BitConverter.ToUInt64(rows, i + o.Target);
                result.Add(new(team, teamName(team)));
            }
        return result;
    }

    /// <summary>Removes the denial of <paramref name="denial"/>'s club; false when the row is no longer there.</summary>
    public bool RemoveDenied(PlayerRecord player, WorkPermitDenial denial) =>
        new RelationshipWriter(memory, profile, moduleBase).RemoveTrainedRow(PersonOwner.Of(player, profile), denial.Team, CannotApplyType);
}

/// <summary>A work permit a nation has given a player: one 0x20-byte record of the game's sorted permit table.</summary>
public sealed record WorkPermitGrant(ulong Record, ulong Nation, string? NationName, int Year, DateOnly? ReviewFrom, bool Temporary)
{
    /// <summary>The complete record as read, used to reject changes before writing.</summary>
    public string Snapshot { get; init; } = "";
}

/// <summary>
/// The permits FMRTE 27's Work Permit tab lists. Measured with Frida against FMRTE's "+" and "−" (Álvaro Ginés, 9/11/2026):
/// the game keeps every permit in one vector of pointers to 0x20-byte records, <b>sorted by the person index</b> (the u32 at
/// <c>player+0x290</c>); a record is <c>+0 nation, +8 person index, +0x10 year (u16), +0x12 review date (4 bytes, 1/1/1900 = none),
/// +0x16 u16 (1 = temporary, with a review date)</c>. FMRTE adds a permit by creating a record (a game allocation made through code it
/// injects), inserting its pointer at the sorted place, moving the vector end up 8, and setting bit 0x04 of the first byte of
/// the 8 bytes at <c>player+0x2A0</c>; removing one erases the pointer and clears that bit. The vector header lives in a game object
/// whose address changes with every load, so it is found once per connection by shape (three pointers framing an array
/// of pointers to records whose nation is a nation object, sorted by person index).
/// </summary>
public sealed class WorkPermitTable(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, ulong? knownHeader = null)
{
    public const int PersonIndexOffset = 0x290, FlagsOffset = 0x2A0, RecordSize = 0x20;
    public const byte HasPermitBit = 0x04;
    static ulong? header;

    NationOffsets N => profile.Nation ?? throw new InvalidOperationException("Profile has no nation offsets");

    /// <summary>Forgets the located header (a new save moves it).</summary>
    public static void Forget() => header = null;

    /// <summary>Address of the vector header (begin, end, capacity), found by scanning the heap on first use.</summary>
    public ulong Locate()
    {
        if (knownHeader is { } given) return given;
        if (header is { } known && Valid(known)) return known;
        if (memory is not WindowsProcessMemory windows) throw new InvalidOperationException("The permit table can only be found in a live game");
        ulong best = 0; int bestCount = 0;
        var buffer = new byte[1 << 22];
        foreach (var region in windows.EnumerateWritableRegions(moduleBase, moduleBase + 1).Where(r => !r.IsModuleData))
            for (ulong offset = 0; offset < region.Size; offset += (ulong)buffer.Length - 32)
            {
                int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
                if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
                for (int i = 0; i + 24 <= length; i += 8)
                {
                    ulong b = BitConverter.ToUInt64(buffer, i), e = BitConverter.ToUInt64(buffer, i + 8), c = BitConverter.ToUInt64(buffer, i + 16);
                    if (b < 0x10000 || e <= b || c < e || (e - b) % 8 != 0 || (e - b) / 8 < 100 || (e - b) / 8 > 200_000 || (c - b) / 8 > 400_000) continue;
                    ulong at = region.Start + offset + (ulong)i;
                    if (Valid(at, out int count) && count > bestCount) { best = at; bestCount = count; }
                }
            }
        if (best == 0) throw new InvalidOperationException("The work permit table was not found; is a save loaded?");
        return (header = best).Value;
    }

    bool Valid(ulong at) => Valid(at, out _);

    bool Valid(ulong at, out int count)
    {
        try { return ValidCore(at, out count); }
        catch (MemoryAccessException) { count = 0; return false; }
    }

    bool ValidCore(ulong at, out int count)
    {
        count = 0;
        if (!memory.TryRead(at, new byte[24])) return false;
        ulong b = memory.ReadUInt64(at), e = memory.ReadUInt64(at + 8);
        if (b == 0 || e <= b || (e - b) % 8 != 0) return false;
        int total = (int)((e - b) / 8);
        count = total;
        long previous = -1;
        foreach (int k in new[] { 0, 1, 2, 3, total / 2, total - 2, total - 1 }.Distinct().Where(k => k >= 0 && k < total))
        {
            ulong record = memory.ReadUInt64(b + (ulong)k * 8);
            if (record == 0 || !memory.TryRead(record, new byte[RecordSize])) return false;
            ulong nation = memory.ReadUInt64(record);
            if (nation == 0 || !memory.TryRead(nation, new byte[8]) || memory.ReadUInt64(nation) != moduleBase + N.VtableRva) return false;
            long person = BitConverter.ToUInt32(memory.Read(record + 8, 4));
            if (person < previous) return false;
            previous = person;
        }
        return true;
    }

    public IReadOnlyList<WorkPermitGrant> Read(PlayerRecord player, Func<ulong, string?> nationName)
    {
        ulong at = Locate();
        ulong b = memory.ReadUInt64(at), e = memory.ReadUInt64(at + 8);
        uint person = BitConverter.ToUInt32(memory.Read(player.Address + PersonIndexOffset, 4));
        var pointers = memory.Read(b, (int)(e - b));
        var result = new List<WorkPermitGrant>();
        // The table is kept sorted by person index (the game's own order; Add inserts in place), so the player's records are
        // one run found by binary search instead of reading every record in the game.
        for (int i = FirstAtLeast(pointers, person) * 8; i < pointers.Length; i += 8)
        {
            var record = memory.Read(BitConverter.ToUInt64(pointers, i), RecordSize);
            if (BitConverter.ToUInt32(record, 8) != person) break;
            ulong nation = BitConverter.ToUInt64(record);
            result.Add(new WorkPermitGrant(BitConverter.ToUInt64(pointers, i), nation, nationName(nation), BitConverter.ToUInt16(record, 0x10),
                FutureTransferReader.DateAt(record, 0x12), BitConverter.ToUInt16(record, 0x16) == 1) { Snapshot = Convert.ToHexString(record) });
        }
        return result;
    }

    /// <summary>FMRTE's "+": a new permanent permit from <paramref name="nation"/> for <paramref name="year"/>.</summary>
    uint PersonAt(byte[] pointers, int index) =>
        BitConverter.ToUInt32(memory.Read(BitConverter.ToUInt64(pointers, index * 8) + 8, 4));

    /// <summary>First index whose record's person index is at least <paramref name="person"/> (binary search; the table is sorted).</summary>
    int FirstAtLeast(byte[] pointers, uint person)
    {
        int lo = 0, hi = pointers.Length / 8;
        while (lo < hi) { int mid = (lo + hi) / 2; if (PersonAt(pointers, mid) < person) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>First index whose record's person index is above <paramref name="person"/>: where a new record goes.</summary>
    int FirstAbove(byte[] pointers, uint person)
    {
        int lo = 0, hi = pointers.Length / 8;
        while (lo < hi) { int mid = (lo + hi) / 2; if (PersonAt(pointers, mid) <= person) lo = mid + 1; else hi = mid; }
        return lo;
    }

    public void Add(PlayerRecord player, ulong nation, int year, Func<int, ulong> allocate)
    {
        if (year is < 1900 or > 2300) throw new ArgumentOutOfRangeException(nameof(year));
        if (memory.ReadUInt64(nation) != moduleBase + N.VtableRva) throw new ArgumentException("Not a nation", nameof(nation));
        RequirePlayer(player);
        ulong at = Locate();
        ulong b = memory.ReadUInt64(at), e = memory.ReadUInt64(at + 8), c = memory.ReadUInt64(at + 16);
        uint person = BitConverter.ToUInt32(memory.Read(player.Address + PersonIndexOffset, 4));
        var pointers = memory.Read(b, (int)(e - b));
        int position = FirstAbove(pointers, person);
        var record = new byte[RecordSize];
        BitConverter.GetBytes(nation).CopyTo(record, 0);
        BitConverter.GetBytes(person).CopyTo(record, 8);
        BitConverter.GetBytes((ushort)year).CopyTo(record, 0x10);
        new byte[] { 0x01, 0x00, 0x6C, 0x07 }.CopyTo(record, 0x12);   // 1/1/1900: no review date
        ulong address = allocate(RecordSize);
        Put(address, record);

        if (e + 8 <= c)
        {
            var tail = new byte[pointers.Length - position * 8 + 8];
            BitConverter.GetBytes(address).CopyTo(tail, 0);
            Buffer.BlockCopy(pointers, position * 8, tail, 8, pointers.Length - position * 8);
            Put(b + (ulong)position * 8, tail);
            Put(at + 8, BitConverter.GetBytes(e + 8));
        }
        else
        {
            int capacity = Math.Max(pointers.Length / 8 * 2, 64);
            ulong buffer = allocate(capacity * 8);
            var next = new byte[pointers.Length + 8];
            Buffer.BlockCopy(pointers, 0, next, 0, position * 8);
            BitConverter.GetBytes(address).CopyTo(next, position * 8);
            Buffer.BlockCopy(pointers, position * 8, next, position * 8 + 8, pointers.Length - position * 8);
            Put(buffer, next);
            Put(at, [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)next.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)]);
        }
        SetFlag(player, on: true);
    }

    /// <summary>FMRTE's "−": the permit leaves the table; the player's flag is cleared when it was his last.</summary>
    public void Remove(PlayerRecord player, WorkPermitGrant grant, Func<ulong, string?> nationName)
    {
        RequirePlayer(player);
        ulong at = Locate();
        ulong b = memory.ReadUInt64(at), e = memory.ReadUInt64(at + 8);
        var pointers = memory.Read(b, (int)(e - b));
        int index = -1;
        for (int i = 0; i < pointers.Length; i += 8) if (BitConverter.ToUInt64(pointers, i) == grant.Record) { index = i / 8; break; }
        if (index < 0) throw new StaleObjectException("The work permit is no longer in the table");
        var record = memory.Read(grant.Record, RecordSize);
        if (BitConverter.ToUInt32(record, 8) != BitConverter.ToUInt32(memory.Read(player.Address + PersonIndexOffset, 4)))
            throw new StaleObjectException("The work permit belongs to another player");
        if (grant.Snapshot.Length != RecordSize * 2 || !record.AsSpan().SequenceEqual(Convert.FromHexString(grant.Snapshot)))
            throw new StaleObjectException("The work permit changed since it was read");
        if (index < pointers.Length / 8 - 1) Put(b + (ulong)index * 8, pointers[((index + 1) * 8)..]);
        Put(at + 8, BitConverter.GetBytes(e - 8));
        if (Read(player, nationName).Count == 0) SetFlag(player, on: false);
    }

    /// <summary>
    /// "Make permanent" (Clayton Taylor, 9/11/2026): the permit leaves a second vector that lists the temporary permits
    /// (header 0x60 after the main one in this game object), then its review date becomes 1/1/1900 and its +0x16 flag 0.
    /// </summary>
    public void MakePermanent(PlayerRecord player, WorkPermitGrant grant)
    {
        if (!grant.Temporary) throw new InvalidOperationException("The permit is already permanent");
        RequirePlayer(player);
        RequireMember(grant.Record);
        var record = memory.Read(grant.Record, RecordSize);
        if (BitConverter.ToUInt16(record, 0x16) != 1 || BitConverter.ToUInt64(record) != grant.Nation ||
            BitConverter.ToUInt32(record, 8) != BitConverter.ToUInt32(memory.Read(player.Address + PersonIndexOffset, 4)))
            throw new StaleObjectException("The work permit changed since it was read");
        ulong main = Locate(), review = main + 0x60;
        ulong b = memory.ReadUInt64(review), e = memory.ReadUInt64(review + 8);
        if (b == 0 || e < b || (e - b) % 8 != 0 || (e - b) / 8 > 100_000) throw new InvalidOperationException("The list of temporary permits was not found");
        var pointers = memory.Read(b, (int)(e - b));
        int index = -1;
        for (int i = 0; i < pointers.Length; i += 8) if (BitConverter.ToUInt64(pointers, i) == grant.Record) { index = i / 8; break; }
        if (index < 0) throw new InvalidOperationException("The permit is not in the list of temporary permits");
        if (index < pointers.Length / 8 - 1) Put(b + (ulong)index * 8, pointers[((index + 1) * 8)..]);
        Put(review + 8, BitConverter.GetBytes(e - 8));
        Put(grant.Record + 0x12, [0x01, 0x00, 0x6C, 0x07]);
        Put(grant.Record + 0x16, [0x00]);
    }

    /// <summary>Changes the year of a permit (one 2-byte write, as FMRTE does).</summary>
    public void SetYear(PlayerRecord player, WorkPermitGrant grant, int year)
    {
        if (year is < 1900 or > 2300) throw new ArgumentOutOfRangeException(nameof(year));
        RequirePlayer(player);
        RequireMember(grant.Record);
        var record = memory.Read(grant.Record, RecordSize);
        if (grant.Snapshot.Length != RecordSize * 2 || !record.AsSpan().SequenceEqual(Convert.FromHexString(grant.Snapshot)) ||
            BitConverter.ToUInt32(record, 8) != BitConverter.ToUInt32(memory.Read(player.Address + PersonIndexOffset, 4)))
            throw new StaleObjectException("The work permit changed since it was read");
        Put(grant.Record + 0x10, BitConverter.GetBytes((ushort)year));
    }

    void RequireMember(ulong record)
    {
        ulong at = Locate();
        ulong begin = memory.ReadUInt64(at), end = memory.ReadUInt64(at + 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 100_000)
            throw new StaleObjectException("The work permit table changed since it was read");
        var pointers = memory.Read(begin, (int)(end - begin));
        for (int i = 0; i < pointers.Length; i += 8)
            if (BitConverter.ToUInt64(pointers, i) == record) return;
        throw new StaleObjectException("The work permit is no longer in the table");
    }

    void SetFlag(PlayerRecord player, bool on)
    {
        ulong at = player.Address + FlagsOffset;
        byte current = memory.Read(at, 1)[0];
        byte next = (byte)(on ? current | HasPermitBit : current & ~HasPermitBit);
        if (next != current) Put(at, [next]);
    }

    void RequirePlayer(PlayerRecord player)
    {
        if (!profile.PlayerVtableRvas.Any(r => memory.ReadUInt64(player.Address) == moduleBase + r))
            throw new StaleObjectException($"{player.DisplayName} changed since it was read");
    }

    void Put(ulong address, byte[] data)
    {
        if (!memory.TryWrite(address, data) || !memory.Read(address, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{address:X}");
    }
}
