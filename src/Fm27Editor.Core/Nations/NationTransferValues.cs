using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// A nation's transfer-value tiers: 20 int32 amounts. FM Studio 1.1.1 keeps the same table
/// (<c>Nation.TransferValuesCount</c> 20, <c>TransferValueStride</c> 4) at
/// <c>[nation + SecondaryPointer] + 424</c>; on build 27.0.0+2493426 the men's national-team object is at
/// nation + 0x108 and the array pointer at that object + 0x1A8 (women's team at nation + 0x110). Read-only snapshot
/// carries the 80 bytes so a write can refuse when anything changed.
/// </summary>
public sealed record NationTransferValues(ulong ArrayAddress, IReadOnlyList<int> Values, string Snapshot);

public sealed class NationTransferValuesReader(IProcessMemory memory)
{
    public const int MenTeamPointer = 0x108, WomenTeamPointer = 0x110, ArrayOffset = 0x1A8, Count = 20;
    const int Bytes = Count * 4;

    /// <summary>The tiers, or null when the nation, the team object or the array is not readable.</summary>
    public NationTransferValues? Read(ulong nationAddress, bool women = false)
    {
        if (!Q(nationAddress + (ulong)(women ? WomenTeamPointer : MenTeamPointer), out var team) || team == 0) return null;
        if (!Q(team + ArrayOffset, out var array) || array == 0) return null;
        var raw = new byte[Bytes];
        if (!memory.TryRead(array, raw)) return null;
        return new(array, Enumerable.Range(0, Count).Select(i => BitConverter.ToInt32(raw, i * 4)).ToArray(), Convert.ToHexString(raw));
    }

    bool Q(ulong at, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return memory.TryRead(at, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
}

/// <summary>
/// Guarded write of one tier: the whole 80-byte table must still match the snapshot, the amount must be a
/// non-negative int32, then read back.
/// </summary>
public sealed class NationTransferValuesWriter(IProcessMemory memory)
{
    public const int MaxValue = 2_000_000_000;

    public void SetMany(ulong nationAddress, ulong nationVtable, ulong teamVtable, NationTransferValues snapshot, IReadOnlyDictionary<int, int> values)
    {
        foreach (var (index, value) in values)
        {
            if (index < 0 || index >= NationTransferValuesReader.Count) throw new ArgumentOutOfRangeException(nameof(values));
            if (value < 0 || value > MaxValue) throw new ArgumentOutOfRangeException(nameof(values));
        }
        var reader = new NationTransferValuesReader(memory);
        var expected = Convert.FromHexString(snapshot.Snapshot);
        ulong team = memory.ReadUInt64(nationAddress + NationTransferValuesReader.MenTeamPointer);
        NationTransferValues Current()
        {
            if (nationVtable == 0 || teamVtable == 0 || team == 0 || memory.ReadUInt64(nationAddress) != nationVtable ||
                memory.ReadUInt64(nationAddress + NationTransferValuesReader.MenTeamPointer) != team || memory.ReadUInt64(team) != teamVtable)
                throw new StaleObjectException("Nation or national team changed since it was read");
            var current = reader.Read(nationAddress) ?? throw new StaleObjectException("Transfer values are no longer readable");
            if (current.ArrayAddress != snapshot.ArrayAddress || !Convert.FromHexString(current.Snapshot).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException("Transfer values changed since they were read");
            return current;
        }
        var current = Current();
        foreach (var (index, value) in values)
        {
            using (Fm27Editor.Core.History.ChangeContext.Describe($"Transfer Value {index + 1}")) Set(current, index, value);
            BitConverter.GetBytes(value).CopyTo(expected, index * sizeof(int));
            current = Current();
        }
    }

    public void Set(NationTransferValues snapshot, int index, int value)
    {
        if (index < 0 || index >= NationTransferValuesReader.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"index must be 0..{NationTransferValuesReader.Count - 1}");
        if (value < 0 || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), $"value must be 0..{MaxValue}");
        var before = Convert.FromHexString(snapshot.Snapshot);
        if (!memory.Read(snapshot.ArrayAddress, before.Length).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The transfer values changed since they were read; nothing written");
        var after = (byte[])before.Clone();
        BitConverter.GetBytes(value).CopyTo(after, index * 4);
        if (!memory.TryWrite(snapshot.ArrayAddress, after) ||
            !memory.Read(snapshot.ArrayAddress, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the transfer values at 0x{snapshot.ArrayAddress:X}");
    }
}
