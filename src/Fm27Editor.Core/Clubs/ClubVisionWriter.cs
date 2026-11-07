using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Changes observed culture fields after validating team, holder, vector membership and the full record.</summary>
public sealed class ClubVisionWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetImportance(ulong team, ClubVisionEntry entry, int importance)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (!layout.VisionImportanceEditable || layout.Importance < 0 || layout.Importance >= layout.CultureSize)
            throw new InvalidOperationException("Club vision importance is unavailable for this build");
        if (importance is < byte.MinValue or > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(importance));
        if (entry.Snapshot is not { } snapshot || snapshot.Team != team)
            throw new StaleObjectException("Club vision snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadClubVision(team).FirstOrDefault(r => r.Address == entry.Address);
        if (current?.Snapshot != snapshot) throw new StaleObjectException("The club vision or its containing list changed since it was read");
        ulong address = entry.Address + (ulong)layout.Importance;
        byte[] value = [(byte)importance];
        if (!memory.TryWrite(address, value) || !memory.Read(address, 1).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write club vision importance at 0x{address:X}");
    }

    /// <summary>Changes the culture type code after validating the row still matches its snapshot.</summary>
    public void SetType(ulong team, ClubVisionEntry entry, byte type)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (layout.VisionType < 0 || layout.VisionType >= layout.CultureSize)
            throw new InvalidOperationException("Club vision type is unavailable for this build");
        Require(team, entry);
        byte[] value = [type];
        ulong address = entry.Address + (ulong)layout.VisionType;
        if (!memory.TryWrite(address, value) || !memory.Read(address, 1).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write club vision type at 0x{address:X}");
    }

    /// <summary>Board-objective target (int32 at the profile's <c>visionTarget</c> offset).</summary>
    public void SetTarget(ulong team, ClubVisionEntry entry, int target)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (layout.VisionTarget < 0 || layout.VisionTarget > layout.CultureSize - 4)
            throw new InvalidOperationException("Club vision target is unavailable for this build");
        Require(team, entry);
        Write(entry.Address + (ulong)layout.VisionTarget, BitConverter.GetBytes(target), 4, "target");
    }

    /// <summary>Board-objective status byte (values 1 and 2 observed in RAM; the profile's <c>visionStatus</c> offset).</summary>
    public void SetStatus(ulong team, ClubVisionEntry entry, byte status)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (layout.VisionStatus < 0 || layout.VisionStatus >= layout.CultureSize)
            throw new InvalidOperationException("Club vision status is unavailable for this build");
        Require(team, entry);
        Write(entry.Address + (ulong)layout.VisionStatus, [status], 1, "status");
    }

    void Write(ulong address, byte[] value, int length, string what)
    {
        if (!memory.TryWrite(address, value) || !memory.Read(address, length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write club vision {what} at 0x{address:X}");
    }

    /// <summary>Writes the two dates of one vision row, keeping the flag bits above the 9-bit day.</summary>
    public void SetDates(ulong team, ClubVisionEntry entry, DateOnly start, DateOnly end)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (layout.VisionStart < 0 || layout.VisionEnd < 0
            || layout.VisionStart > layout.CultureSize - 4 || layout.VisionEnd > layout.CultureSize - 4)
            throw new InvalidOperationException("Club vision dates are unavailable for this build");
        if (start.Year is <= 1900 or > 2300 || end.Year is <= 1900 or > 2300)
            throw new ArgumentOutOfRangeException(nameof(start), "Club vision date cannot be read back by this profile");
        Require(team, entry);
        WriteDate(entry.Address + (ulong)layout.VisionStart, start);
        WriteDate(entry.Address + (ulong)layout.VisionEnd, end);
    }

    void WriteDate(ulong address, DateOnly date)
    {
        var current = memory.Read(address, 4);
        if (current.Length < 4) throw new MemoryAccessException($"Could not read club vision date at 0x{address:X}");
        ushort raw = BitConverter.ToUInt16(current, 0);
        var value = new byte[4];
        BitConverter.GetBytes((ushort)((raw & 0xFE00) | date.DayOfYear)).CopyTo(value, 0);
        BitConverter.GetBytes((short)date.Year).CopyTo(value, 2);
        if (!memory.TryWrite(address, value) || !memory.Read(address, 4).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write club vision date at 0x{address:X}");
    }

    /// <summary>Removes one vision row: the pointer entries after it shift left and the vector's end shrinks by one.</summary>
    public void Remove(ulong team, ClubVisionEntry entry)
    {
        var (layout, snapshot) = Require(team, entry);
        ulong header = snapshot.Vector, slot = snapshot.Slot;
        ulong next = slot + 8;
        if (next < snapshot.End)
        {
            var tail = memory.Read(next, (int)(snapshot.End - next));
            if (!memory.TryWrite(slot, tail) || !memory.Read(slot, tail.Length).AsSpan().SequenceEqual(tail))
                throw new MemoryAccessException($"Could not move club vision pointers at 0x{slot:X}");
        }
        var end = BitConverter.GetBytes(snapshot.End - 8);
        if (!memory.TryWrite(header + 8, end) || !memory.Read(header + 8, 8).AsSpan().SequenceEqual(end))
            throw new MemoryAccessException($"Could not shrink the club vision vector at 0x{header:X}");
    }

    /// <summary>
    /// Adds a row by copying an existing one into a new culture object (same type, importance, dates and club item) and
    /// appending its pointer; the pointer array grows outside the game's allocator when it is full.
    /// </summary>
    public ClubVisionEntry? Duplicate(ulong team, ClubVisionEntry entry, Func<int, ulong>? allocate)
    {
        var (layout, snapshot) = Require(team, entry);
        if (allocate is null) throw new StaleObjectException("No allocator is available to add a vision row");
        var bytes = Convert.FromHexString(snapshot.Bytes);
        ulong culture = allocate(layout.CultureSize);
        if (culture == 0) throw new MemoryAccessException("Could not allocate a vision row");
        if (!memory.TryWrite(culture, bytes) || !memory.Read(culture, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write the new vision row at 0x{culture:X}");
        ulong header = snapshot.Vector, begin = snapshot.Begin, end = snapshot.End, cap = snapshot.Capacity;
        if (end + 8 <= cap)
        {
            WritePointers(end, culture);
            Grow(header + 8, end + 8);
        }
        else
        {
            int oldBytes = (int)(end - begin), newBytes = oldBytes + 16;
            ulong buffer = allocate(newBytes);
            if (buffer == 0) throw new MemoryAccessException("Could not allocate a vision pointer array");
            var copy = memory.Read(begin, oldBytes);
            if (!memory.TryWrite(buffer, copy) || !memory.Read(buffer, copy.Length).AsSpan().SequenceEqual(copy))
                throw new MemoryAccessException($"Could not copy vision pointers to 0x{buffer:X}");
            WritePointers(buffer + (ulong)oldBytes, culture);
            var head = new byte[24];
            BitConverter.GetBytes(buffer).CopyTo(head, 0);
            BitConverter.GetBytes(buffer + (ulong)oldBytes + 8).CopyTo(head, 8);
            BitConverter.GetBytes(buffer + (ulong)newBytes).CopyTo(head, 16);
            if (!memory.TryWrite(header, head) || !memory.Read(header, head.Length).AsSpan().SequenceEqual(head))
                throw new MemoryAccessException($"Could not point the vision vector at 0x{buffer:X}");
        }
        return new ClubListReader(memory, profile, moduleBase).ReadClubVision(team).LastOrDefault(r => r.Address == culture);
    }

    (ClubListOffsets Layout, ClubVisionSnapshot Snapshot) Require(ulong team, ClubVisionEntry entry)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (entry.Snapshot is not { } snapshot || snapshot.Team != team)
            throw new StaleObjectException("Club vision snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadClubVision(team).FirstOrDefault(r => r.Address == entry.Address);
        if (current?.Snapshot != snapshot) throw new StaleObjectException("The club vision or its containing list changed since it was read");
        return (layout, snapshot);
    }

    void WritePointers(ulong slot, ulong culture)
    {
        var pointer = BitConverter.GetBytes(culture);
        if (!memory.TryWrite(slot, pointer) || !memory.Read(slot, 8).AsSpan().SequenceEqual(pointer))
            throw new MemoryAccessException($"Could not write the vision pointer at 0x{slot:X}");
    }

    void Grow(ulong address, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, 8).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not grow the vision vector at 0x{address:X}");
    }
}
