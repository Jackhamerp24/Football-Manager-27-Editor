using System.Text;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Officials;

/// <summary>Editable referee numbers and flags. Names and birth date are read only.</summary>
public enum RefereeField
{
    CurrentAbility, PotentialAbility, Reputation,
    AllowingFlow, Discipline, ImportantMatches, Pressure, Refereeing, RunningLine, TimeKeeping,
    EliteList, Continental, EuropeanCategory,
}

/// <summary>
/// One referee from the game-wide officials array. Measured on 27.0.0+2493426 (Game Pass) with Daniel Richardson,
/// Daniel Barclay and Neil Hair, 8/11/2026: the array of pointers is 2,078 objects, every object carries vtable
/// +0x4F11568 and the FM26 referee layout still holds — first name +0x28, last name +0x30, common name +0x38,
/// nation +0x40, city +0x58, birth day-of-year +0x60 and year +0x62, CA int16 +0x11C, PA +0x11E, reputation int16
/// +0x120, then bytes at +0x122 Allowing Flow, +0x123 Discipline, +0x124 Important Matches, +0x125 Pressure,
/// +0x126 Refereeing, +0x127 Running Line, +0x128 Time Keeping, +0x129 Elite List, +0x12A Continental,
/// +0x12B European Category; unique id int32 +0x144. Values observed in the 1..20 range for the six skills, 0/1 for
/// the three flags and 0..4 for the European category.
/// </summary>
public sealed record RefereeRecord(ulong Address, uint VtableRva, string? FirstName, string? LastName, string? CommonName,
    ulong NationAddress, ulong CityAddress, DateOnly? DateOfBirth, int CurrentAbility, int PotentialAbility, int Reputation,
    int AllowingFlow, int Discipline, int ImportantMatches, int Pressure, int Refereeing, int RunningLine, int TimeKeeping,
    int EliteList, int Continental, int EuropeanCategory, int UniqueId, string Snapshot)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CommonName) ? $"{FirstName} {LastName}".Trim() : CommonName;
}

/// <summary>Reads the officials array found through <see cref="GlobalArrayFinder"/>; read only.</summary>
public sealed class RefereeDirectory(IProcessMemory memory, ulong moduleBase, ulong moduleSize)
{
    internal const int FirstName = 0x28, LastName = 0x30, CommonName = 0x38, Nation = 0x40, City = 0x58;
    internal const int BirthDay = 0x60, BirthYear = 0x62;
    internal const int CurrentAbility = 0x11C, PotentialAbility = 0x11E, Reputation = 0x120;
    internal const int AllowingFlow = 0x122, Discipline = 0x123, ImportantMatches = 0x124, Pressure = 0x125;
    internal const int Refereeing = 0x126, RunningLine = 0x127, TimeKeeping = 0x128, EliteList = 0x129;
    internal const int Continental = 0x12A, EuropeanCategory = 0x12B, UniqueId = 0x144;
    internal const int SnapshotSize = 0x148;
    const int MaxReferees = 20_000;

    /// <summary>Every referee the array holds; empty when the anchor or the array is not readable.</summary>
    public IReadOnlyList<RefereeRecord> ReadAll()
    {
        if (GlobalArrayFinder.Read(memory, moduleBase, moduleSize) is not { } arrays) return [];
        int count = GlobalArrays.Count(arrays.RefereesBegin, arrays.RefereesEnd);
        if (count is 0 or > MaxReferees) return [];
        var pointers = new byte[count * 8];
        if (!memory.TryRead(arrays.RefereesBegin, pointers)) return [];
        var result = new List<RefereeRecord>(count);
        for (int i = 0; i < count; i++)
            if (Read(BitConverter.ToUInt64(pointers, i * 8)) is { } referee) result.Add(referee);
        return result;
    }

    /// <summary>One referee; null when the object is not a referee (vtable differs from the first one seen).</summary>
    public RefereeRecord? Read(ulong address)
    {
        if (address == 0) return null;
        var bytes = new byte[SnapshotSize];
        if (!memory.TryRead(address, bytes)) return null;
        ulong vtable = BitConverter.ToUInt64(bytes);
        if (vtable < moduleBase || vtable >= moduleBase + moduleSize) return null;
        int day = BitConverter.ToInt16(bytes, BirthDay), year = BitConverter.ToInt16(bytes, BirthYear);
        DateOnly? born = year is >= 1900 and <= 2100 && day is >= 1 and <= 366
            ? DateOnly.FromDayNumber(new DateOnly(year, 1, 1).DayNumber + day - 1) : null;
        return new(address, (uint)(vtable - moduleBase), EntryString(bytes, FirstName), EntryString(bytes, LastName),
            EntryString(bytes, CommonName), BitConverter.ToUInt64(bytes, Nation), BitConverter.ToUInt64(bytes, City), born,
            BitConverter.ToInt16(bytes, CurrentAbility), BitConverter.ToInt16(bytes, PotentialAbility),
            BitConverter.ToInt16(bytes, Reputation), bytes[AllowingFlow], bytes[Discipline], bytes[ImportantMatches],
            bytes[Pressure], bytes[Refereeing], bytes[RunningLine], bytes[TimeKeeping], bytes[EliteList],
            bytes[Continental], bytes[EuropeanCategory], BitConverter.ToInt32(bytes, UniqueId), Convert.ToHexString(bytes));
    }

    string? EntryString(byte[] objectBytes, int offset)
    {
        ulong entry = BitConverter.ToUInt64(objectBytes, offset);
        return entry == 0 ? null : StringAt(EntryPointer(entry));
    }

    ulong EntryPointer(ulong entry)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(entry, b) ? BitConverter.ToUInt64(b) : 0;
    }

    /// <summary>Length-prefixed UTF-8, the game's name string form.</summary>
    string? StringAt(ulong address)
    {
        if (address == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(address, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n <= 0 || n > 200) return null;
        var text = new byte[n];
        return memory.TryRead(address + 4, text) ? Encoding.UTF8.GetString(text) : null;
    }
}

/// <summary>
/// Guarded writes for one referee: the whole 0x148-byte snapshot must still match, the value must be inside the
/// range FMRTE/game use, then read back. Names and the pointer fields are never written.
/// </summary>
public sealed class RefereeWriter(IProcessMemory memory, ulong moduleBase)
{
    public void Set(RefereeRecord referee, RefereeField field, int value)
    {
        (int offset, int min, int max) = field switch
        {
            RefereeField.CurrentAbility or RefereeField.PotentialAbility => (field == RefereeField.CurrentAbility
                ? RefereeDirectory.CurrentAbility : RefereeDirectory.PotentialAbility, 1, 200),
            RefereeField.Reputation => (RefereeDirectory.Reputation, 0, 10_000),
            RefereeField.AllowingFlow => (RefereeDirectory.AllowingFlow, 1, 20),
            RefereeField.Discipline => (RefereeDirectory.Discipline, 1, 20),
            RefereeField.ImportantMatches => (RefereeDirectory.ImportantMatches, 1, 20),
            RefereeField.Pressure => (RefereeDirectory.Pressure, 1, 20),
            RefereeField.Refereeing => (RefereeDirectory.Refereeing, 1, 20),
            RefereeField.RunningLine => (RefereeDirectory.RunningLine, 1, 20),
            RefereeField.TimeKeeping => (RefereeDirectory.TimeKeeping, 1, 20),
            RefereeField.EliteList => (RefereeDirectory.EliteList, 0, 1),
            RefereeField.Continental => (RefereeDirectory.Continental, 0, 1),
            RefereeField.EuropeanCategory => (RefereeDirectory.EuropeanCategory, 0, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(nameof(value), $"{field} must be {min}..{max}");
        var before = Convert.FromHexString(referee.Snapshot);
        if (before.Length != RefereeDirectory.SnapshotSize || BitConverter.ToUInt64(before) != moduleBase + referee.VtableRva)
            throw new StaleObjectException("The referee snapshot has an invalid vtable");
        var now = memory.Read(referee.Address, before.Length);
        if (!now.AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The referee changed since it was read; nothing written");

        var after = (byte[])before.Clone();
        if (field is RefereeField.CurrentAbility or RefereeField.PotentialAbility or RefereeField.Reputation)
            BitConverter.GetBytes((short)value).CopyTo(after, offset);
        else after[offset] = (byte)value;
        int size = field is RefereeField.CurrentAbility or RefereeField.PotentialAbility or RefereeField.Reputation ? 2 : 1;
        if (!memory.TryWrite(referee.Address + (ulong)offset, after.AsSpan(offset, size)) ||
            !memory.Read(referee.Address, after.Length).AsSpan().SequenceEqual(after))
            throw new InvalidOperationException($"Could not write the referee at 0x{referee.Address:X}");
    }
}
