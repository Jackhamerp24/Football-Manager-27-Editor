using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>One entry of the clause table: int32 value followed by the int16 marker -1 and an int16 type code.</summary>
public sealed record ContractClause(int Type, int Value);

/// <summary>
/// Reads the clause table of an agreed-but-not-completed contract (player+0x328 on 27.0.0+2486695).
/// Observed on one deal only: value 10032 type 32, 7524 type 34, 5016 type 38. The table ends at the
/// first entry whose marker is not -1, so an unknown layout yields fewer clauses, never invented ones.
/// </summary>
public sealed class PendingContractReader(IProcessMemory memory, OffsetProfile profile)
{
    const int EntrySize = 8;
    const int MaxClauses = 64;

    public IReadOnlyList<ContractClause> ReadClauses(ulong pendingAddress)
    {
        if (pendingAddress == 0 || profile.PendingContract?.Clauses is not { } table) return [];
        var clauses = new List<ContractClause>();
        Span<byte> entry = stackalloc byte[EntrySize];
        for (int i = 0; i < MaxClauses; i++)
        {
            if (!memory.TryRead(pendingAddress + (ulong)(table + i * EntrySize), entry)) break;
            if (BitConverter.ToInt16(entry[4..]) != -1) break;
            clauses.Add(new ContractClause(BitConverter.ToInt16(entry[6..]), BitConverter.ToInt32(entry)));
        }
        return clauses;
    }
}
