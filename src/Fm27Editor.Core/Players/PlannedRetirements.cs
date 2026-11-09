using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

public sealed record PlannedRetirement(uint PersonId, DateOnly? Date);
internal sealed record RetirementSnapshot(ulong Root, ulong Manager, byte[] ListHeader, byte[] IndexHeader,
    ulong ListBegin, ulong IndexBegin, byte[] Pointers, byte[] Index, IReadOnlyList<byte[]> Entries,
    IReadOnlyDictionary<uint, PlannedRetirement> ByPerson);

/// <summary>Read-only retirement index; the manager is named by the profile (measured on Game Pass and Steam 27.0.0+2493426).</summary>
public sealed class PlannedRetirements(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxEntries = 131072;

    /// <summary>True when the profile names the manager (slot and vtable); the structure checks below refuse anything that is not it.</summary>
    public bool IsSupported => profile.RetirementManagerRva is not null && profile.RetirementManagerVtable is not null;

    internal uint ManagerVtable => profile.RetirementManagerVtable ?? throw new InvalidOperationException("Planned retirements are not available for this build");

    public IReadOnlyDictionary<uint, PlannedRetirement> ReadAll() => Snapshot().ByPerson;

    internal RetirementSnapshot Snapshot()
    {
        if (!IsSupported)
            throw new InvalidOperationException("Planned retirements are not available for this build");
        ulong root = moduleBase + profile.RetirementManagerRva!.Value;
        ulong manager = memory.ReadUInt64(root);
        if (manager == 0 || memory.ReadUInt64(manager) != moduleBase + ManagerVtable)
            throw new StaleObjectException("Retirement manager is not available");
        var lists = memory.Read(manager + 0x30, 24);
        var index = memory.Read(manager + 0x90, 24);
        var (begin, count) = Header(lists, 8);
        var (indexBegin, indexCount) = Header(index, 16);
        if (count != indexCount) throw new StaleObjectException("Retirement lists have different lengths");
        var pointers = count == 0 ? [] : memory.Read(begin, checked(count * 8));
        var indexed = count == 0 ? [] : memory.Read(indexBegin, checked(count * 16));
        var byPointer = new Dictionary<ulong, PlannedRetirement>();
        var byPerson = new Dictionary<uint, PlannedRetirement>();
        var entries = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            ulong pointer = BitConverter.ToUInt64(pointers, i * 8);
            if (pointer == 0) throw new StaleObjectException("Retirement entry is null");
            byte[] entry = memory.Read(pointer, 24);
            entries.Add(entry);
            uint person = BitConverter.ToUInt32(entry, 0);
            var record = new PlannedRetirement(person, ReadDate(entry));
            if (person == 0 || !byPointer.TryAdd(pointer, record) || !byPerson.TryAdd(person, record))
                throw new StaleObjectException("Retirement list contains duplicate entries");
        }
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            uint person = BitConverter.ToUInt32(indexed, i * 16);
            ulong pointer = BitConverter.ToUInt64(indexed, i * 16 + 8);
            if (person <= previous || !byPointer.TryGetValue(pointer, out var record) || record.PersonId != person)
                throw new StaleObjectException("Retirement index does not match its entries");
            previous = person;
        }
        if (memory.ReadUInt64(root) != manager || memory.ReadUInt64(manager) != moduleBase + ManagerVtable ||
            !memory.Read(manager + 0x30, 24).AsSpan().SequenceEqual(lists) ||
            !memory.Read(manager + 0x90, 24).AsSpan().SequenceEqual(index) ||
            (count > 0 && (!memory.Read(begin, pointers.Length).AsSpan().SequenceEqual(pointers) ||
                           !memory.Read(indexBegin, indexed.Length).AsSpan().SequenceEqual(indexed))))
            throw new StaleObjectException("Retirement lists changed while reading");
        return new(root, manager, lists, index, begin, indexBegin, pointers, indexed, entries, byPerson);
    }

    public uint PersonId(PlayerRecord player)
    {
        if (player.Id is not > 0 || profile.Fields.Id is not { } offset ||
            memory.ReadUInt64(player.Address) != moduleBase + player.VtableRva ||
            !profile.PlayerVtableRvas.Contains(player.VtableRva) ||
            BitConverter.ToInt32(memory.Read(player.Address + (ulong)offset, 4)) != player.Id)
            throw new StaleObjectException("Player changed since it was read");
        return BitConverter.ToUInt32(memory.Read(player.Address + 0x290, 4));
    }

    static (ulong Begin, int Count) Header(byte[] bytes, int width)
    {
        ulong begin = BitConverter.ToUInt64(bytes, 0), end = BitConverter.ToUInt64(bytes, 8), capacity = BitConverter.ToUInt64(bytes, 16);
        if (end < begin || capacity < end || (begin == 0 && (end != 0 || capacity != 0)) ||
            (end - begin) % (ulong)width != 0 || (capacity - begin) % (ulong)width != 0 ||
            (end - begin) / (ulong)width > MaxEntries || (capacity - begin) / (ulong)width > MaxEntries)
            throw new StaleObjectException("Retirement list header is invalid");
        return (begin, (int)((end - begin) / (ulong)width));
    }

    static DateOnly? ReadDate(byte[] entry)
    {
        int day = BitConverter.ToUInt16(entry, 12) & 0x1FF;
        int year = BitConverter.ToUInt16(entry, 14);
        if (year <= 1900 || year > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }
}
