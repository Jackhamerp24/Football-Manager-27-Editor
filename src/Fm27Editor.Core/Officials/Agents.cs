using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Officials;

/// <summary>The five agent rates FMRTE-style editors expose; every one is a byte 1..20.</summary>
public enum AgentField { Scope, Patience, Business, AgencySize, ToutingClients }

/// <summary>
/// One agent from the game-wide agent pool. Measured on 27.0.0+2493426 (Game Pass), 8/11/2026: the pool is a
/// contiguous array of 0xE0-byte records whose first qword is vtable +0x4DDEB18, reached through the module slot at
/// +0x5685B30 → holder → chunk (chunk = [holder]). The record still uses the FM26 agent layout: first name +0x28,
/// last name +0x30, common name +0x38, clients vector +0x70/+0x78/+0x80, based nation +0xA0, scope +0xA8,
/// patience +0xB0, business +0xB1, size of agency +0xB2, touting clients +0xB3, row id int32 +0xC8,
/// unique id uint32 +0xCC (all observed ≥ 2,000,000,000). The first record read on the live save was
/// "Toufik Boufennara" with 34 clients, scope 1, patience 4, business 17, agency size 18 and touting 10.
/// </summary>
public sealed record AgentRecord(ulong Address, int Index, string? FirstName, string? LastName, string? CommonName,
    ulong BasedNation, int Scope, int Patience, int Business, int AgencySize, int ToutingClients, int Clients,
    int RowId, uint Uid, string Snapshot)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CommonName) ? $"{FirstName} {LastName}".Trim() : CommonName;
}

public sealed class AgentDirectory(IProcessMemory memory, ulong moduleBase, uint chunkSlotRva = AgentDirectory.ChunkSlotRva, uint? expectedVtableRva = null)
{
    /// <summary>Module slot whose value is a holder whose first qword is the agent chunk.</summary>
    public const int ChunkSlotRva = 0x5685B30;
    public const int Stride = 0xE0, MaxAgents = 5000, SnapshotSize = 0xE0;
    internal const int FirstName = 0x28, LastName = 0x30, CommonName = 0x38, Clients = 0x70, BasedNation = 0xA0;
    internal const int Scope = 0xA8, Patience = 0xB0, Business = 0xB1, AgencySize = 0xB2, ToutingClients = 0xB3;
    internal const int RowId = 0xC8, Uid = 0xCC;

    /// <summary>The pool chunk address, or zero when the slot or holder is unreadable.</summary>
    public ulong Chunk
    {
        get
        {
            if (!Q(moduleBase + chunkSlotRva, out var holder) || holder == 0) return 0;
            return Q(holder, out var chunk) ? chunk : 0;
        }
    }

    /// <summary>Every agent in the pool, in pool order; empty when the pool is unavailable.</summary>
    public IReadOnlyList<AgentRecord> ReadAll()
    {
        ulong chunk = Chunk;
        if (chunk == 0) return [];
        var head = new byte[8];
        if (!memory.TryRead(chunk, head)) return [];
        ulong vtable = BitConverter.ToUInt64(head);
        if (expectedVtableRva is { } expected && vtable != moduleBase + expected) return [];
        if (vtable < moduleBase) return [];
        var result = new List<AgentRecord>();
        for (int i = 0; i < MaxAgents; i++)
        {
            ulong at = chunk + (ulong)i * Stride;
            if (!memory.TryRead(at, head) || BitConverter.ToUInt64(head) != vtable) break;
            if (Read(at, i, vtable) is { } agent) result.Add(agent);
        }
        return result;
    }

    /// <summary>One agent at an address; null when the record does not start with the given vtable.</summary>
    public AgentRecord? Read(ulong address, int index, ulong vtable)
    {
        var bytes = new byte[SnapshotSize];
        if (!memory.TryRead(address, bytes) || BitConverter.ToUInt64(bytes) != vtable) return null;
        int clients = 0;
        if (Q(address + (ulong)Clients, out var begin) && Q(address + (ulong)Clients + 8, out var end) && end > begin && (end - begin) % 8 == 0
            && (end - begin) / 8 <= 10_000) clients = (int)((end - begin) / 8);
        return new(address, index, EntryString(bytes, FirstName), EntryString(bytes, LastName), EntryString(bytes, CommonName),
            BitConverter.ToUInt64(bytes, BasedNation), bytes[Scope], bytes[Patience], bytes[Business], bytes[AgencySize],
            bytes[ToutingClients], clients, BitConverter.ToInt32(bytes, RowId), BitConverter.ToUInt32(bytes, Uid),
            Convert.ToHexString(bytes));
    }

    string? EntryString(byte[] record, int offset)
    {
        ulong entry = BitConverter.ToUInt64(record, offset);
        return entry == 0 ? null : StringAt(EntryPointer(entry));
    }

    ulong EntryPointer(ulong entry)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(entry, b) ? BitConverter.ToUInt64(b) : 0;
    }

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

    bool Q(ulong at, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return memory.TryRead(at, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
}

/// <summary>Guarded writes for one agent rate: whole snapshot must still match, value 1..20, read back.</summary>
public sealed class AgentWriter(IProcessMemory memory)
{
    public void Set(AgentRecord agent, AgentField field, int value)
    {
        if (value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), $"{field} must be 1..20");
        int offset = field switch
        {
            AgentField.Scope => AgentDirectory.Scope,
            AgentField.Patience => AgentDirectory.Patience,
            AgentField.Business => AgentDirectory.Business,
            AgentField.AgencySize => AgentDirectory.AgencySize,
            AgentField.ToutingClients => AgentDirectory.ToutingClients,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var before = Convert.FromHexString(agent.Snapshot);
        var now = memory.Read(agent.Address, before.Length);
        if (!now.AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The agent changed since it was read; nothing written");
        var after = (byte[])before.Clone();
        after[offset] = (byte)value;
        if (!memory.TryWrite(agent.Address, after) || !memory.Read(agent.Address, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the agent at 0x{agent.Address:X}");
    }
}
