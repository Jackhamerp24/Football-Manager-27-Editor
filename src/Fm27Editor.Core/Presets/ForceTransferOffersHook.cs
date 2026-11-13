using System.Diagnostics;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Presets;

/// <summary>
/// One evaluation seen by the transfer-offer hook at runtime, newest first when read back.
/// </summary>
public sealed record ForceTransferOfferHit(
    ulong Proposal,
    ulong BuyerClub,
    int BuyerClubId,
    byte OriginalStatus,
    byte NewStatus,
    bool Forced);

/// <summary>
/// Live snapshot of the installed transfer-offer hook: counters, controlled club ids and the recent evaluations ring.
/// </summary>
public sealed record ForceTransferOffersState(
    ulong Site,
    ulong Cave,
    bool Enabled,
    long Calls,
    long Forced,
    IReadOnlyList<int> ClubIds,
    IReadOnlyList<ForceTransferOfferHit> Recent);

/// <summary>
/// Guarded trampoline hook for "Force Other Clubs to Accept Your Transfer Offers" (<c>glpr_yjli</c> /
/// <c>movzx r13d, byte [rdi+0x800]; mov r14, [rsp+0x68]; test r14, r14</c>).
/// Unlike older CE scripts that relied on a volatile <c>r8</c> pointer, the cave resolves the buying club through
/// the proposal pointer at <c>[rsp+0x68]</c> (<c>+0x38</c> pushed = <c>[rsp+0xA0]</c>), verifies the buying team is
/// a club team (<c>byte [buyerTeam+0x2F] == 0</c>), matches <c>Club.Id</c> against the configured managed club list,
/// and only then rewrites <c>r13d = 3</c> (accepted).
/// </summary>
public sealed class ForceTransferOffersHook(
    IProcessMemory memory,
    ICodePatchHost? host,
    OffsetProfile profile,
    ulong moduleBase,
    int moduleSize)
{
    public const int BlockSize = 0x1000;
    public const int StolenLength = 8;
    public const int MaxIds = 32;
    public const int RingSize = 64;
    public const int RecordSize = 32;

    const int MagicOffset = 0x00;
    const int CallsOffset = 0x08;
    const int ForcedOffset = 0x10;
    const int OriginalOffset = 0x18;
    const int SiteOffset = 0x20;
    const int EnabledOffset = 0x28;
    const int IdCountOffset = 0x2C;
    const int RingIndexOffset = 0x30;
    const int CodeOffset = 0x40;
    const int IdsOffset = 0x300;
    const int RingOffset = 0x400;

    const int BuyerSentinelOffset = 0x033;
    const int TeamClubSentinelOffset = 0x045;
    const int ClubIdSentinelOffset = 0x050;

    const uint PageExecuteReadWrite = 0x40;

    static ReadOnlySpan<byte> Magic => "FM27XFR1"u8;

    // movzx r13d, byte ptr [rdi+0x800] (8) ; mov r14, [rsp+0x68] (5) ; test r14, r14 (3)
    static readonly byte[] OriginalBytes =
    [
        0x44, 0x0F, 0xB6, 0xAF, 0x00, 0x08, 0x00, 0x00,
        0x4C, 0x8B, 0x74, 0x24, 0x68,
        0x4D, 0x85, 0xF6,
    ];

    // Pre-assembled x64 routine (230 bytes, verified with Iced Assembler), followed by 5-byte jmp rel32 to site+8.
    // +0x000..+0x008 : stolen movzx r13d, byte ptr [rdi+0x800]
    // +0x008..+0x010 : pushfq; push rax; push rcx; push rdx; push rsi; push rbx; push r8  (rsp shifted +0x38)
    // +0x010..+0x017 : lea rcx, [rip - 0x57] -> cave base
    // +0x017..+0x01C : lock inc qword ptr [rcx + CallsOffset]
    // +0x01C..+0x023 : xor eax, eax; xor edx, edx; xor r8d, r8d
    // +0x023..+0x091 : walk [rsp+0xA0] (proposal) -> Buyer -> [rbx+0x2F]==0 -> TeamClub -> Club.Id -> match Ids[0..IdCount]
    // +0x091..+0x0DE : record 32-byte ring entry; if r8b!=0 set r13d=3
    // +0x0DE..+0x0E6 : pop r8; pop rbx; pop rsi; pop rdx; pop rcx; pop rax; popfq
    static readonly byte[] RoutinePrefix =
    [
        0x44, 0x0F, 0xB6, 0xAF, 0x00, 0x08, 0x00, 0x00, 0x9C, 0x50, 0x51, 0x52, 0x56, 0x53, 0x41, 0x50,
        0x48, 0x8D, 0x0D, 0xA9, 0xFF, 0xFF, 0xFF, 0xF0, 0x48, 0xFF, 0x41, 0x08, 0x31, 0xC0, 0x31, 0xD2,
        0x45, 0x31, 0xC0, 0x48, 0x8B, 0xB4, 0x24, 0xA0, 0x00, 0x00, 0x00, 0x48, 0x85, 0xF6, 0x74, 0x61,
        0x48, 0x8B, 0x9E, 0x44, 0x33, 0x22, 0x11, 0x48, 0x85, 0xDB, 0x74, 0x55, 0x80, 0x7B, 0x2F, 0x00,
        0x75, 0x4F, 0x48, 0x8B, 0x83, 0x55, 0x44, 0x33, 0x22, 0x48, 0x85, 0xC0, 0x74, 0x43, 0x8B, 0x90,
        0x66, 0x55, 0x44, 0x33, 0x80, 0x79, 0x28, 0x00, 0x74, 0x37, 0x85, 0xD2, 0x74, 0x33, 0x83, 0xFA,
        0xFF, 0x74, 0x2E, 0x8B, 0x59, 0x2C, 0x85, 0xDB, 0x7E, 0x27, 0x83, 0xFB, 0x20, 0x76, 0x05, 0xBB,
        0x20, 0x00, 0x00, 0x00, 0x48, 0x8D, 0xB1, 0x00, 0x03, 0x00, 0x00, 0x39, 0x16, 0x74, 0x0A, 0x48,
        0x83, 0xC6, 0x04, 0xFF, 0xCB, 0x75, 0xF4, 0xEB, 0x08, 0xF0, 0x48, 0xFF, 0x41, 0x10, 0x41, 0xB0,
        0x01, 0xBB, 0x01, 0x00, 0x00, 0x00, 0xF0, 0x0F, 0xC1, 0x59, 0x30, 0x83, 0xE3, 0x3F, 0xC1, 0xE3,
        0x05, 0x48, 0x8D, 0x9C, 0x19, 0x00, 0x04, 0x00, 0x00, 0x48, 0x8B, 0xB4, 0x24, 0xA0, 0x00, 0x00,
        0x00, 0x48, 0x89, 0x33, 0x48, 0x89, 0x43, 0x08, 0x89, 0x53, 0x10, 0x44, 0x88, 0x6B, 0x14, 0x45,
        0x84, 0xC0, 0x74, 0x06, 0x41, 0xBD, 0x03, 0x00, 0x00, 0x00, 0x44, 0x88, 0x6B, 0x15, 0x44, 0x88,
        0x43, 0x16, 0xC6, 0x43, 0x17, 0x00, 0x48, 0xC7, 0x43, 0x18, 0x00, 0x00, 0x00, 0x00, 0x41, 0x58,
        0x5B, 0x5E, 0x5A, 0x59, 0x58, 0x9D,
    ];

    ulong? site;

    /// <summary>The 16-byte instruction pattern at the hook site across supported builds.</summary>
    public static ReadOnlySpan<byte> OriginalPattern() => OriginalBytes;

    /// <summary>True when <paramref name="here"/> holds our trampoline jump followed by the trailing 8 anchor bytes.</summary>
    public static bool IsPatchedSite(ReadOnlySpan<byte> here) =>
        here.Length >= OriginalBytes.Length &&
        here[0] == 0xE9 &&
        here[5] == 0x90 && here[6] == 0x90 && here[7] == 0x90 &&
        here.Slice(8, 8).SequenceEqual(OriginalBytes.AsSpan(8, 8));

    /// <summary>Locates the unique site in executable PE sections, or throws if missing/ambiguous.</summary>
    public ulong FindSite()
    {
        if (site is { } known) return known;
        var hits = new List<ulong>();
        foreach (var (start, size) in CodeSections())
        {
            const int Chunk = 1 << 20;
            var buffer = new byte[Chunk + OriginalBytes.Length];
            for (ulong offset = 0; offset < size; offset += Chunk)
            {
                int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                if (!memory.TryRead(start + offset, buffer.AsSpan(0, length))) continue;
                var span = buffer.AsSpan(0, length);
                int limit = Math.Min(Chunk, length - OriginalBytes.Length + 1);
                for (int i = 0; i < limit; i++)
                {
                    if (span[i] != 0x44 && span[i] != 0xE9) continue;
                    var here = span[i..];
                    if (here[..OriginalBytes.Length].SequenceEqual(OriginalBytes) || IsPatchedSite(here))
                        hits.Add(start + offset + (ulong)i);
                }
                if (hits.Count > 1) break;
            }
            if (hits.Count > 1) break;
        }
        if (hits.Count == 0) throw new InvalidOperationException("Force Accept Transfer: the transfer-offer code was not found in this build");
        if (hits.Count > 1) throw new InvalidOperationException("Force Accept Transfer: the transfer-offer code is not unique in this build");
        return (site = hits[0]).Value;
    }

    /// <summary>Current hook state, or null when the site still holds the game's original bytes.</summary>
    public ForceTransferOffersState? Read()
    {
        ulong at = FindSite();
        var bytes = memory.Read(at, OriginalBytes.Length);
        if (bytes.AsSpan().SequenceEqual(OriginalBytes)) return null;
        if (!IsPatchedSite(bytes))
            throw new InvalidOperationException("Force Accept Transfer: the transfer-offer site holds unexpected bytes");

        ulong code = at + 5 + (ulong)(long)BitConverter.ToInt32(bytes, 1);
        if (code < CodeOffset)
            throw new InvalidOperationException("Force Accept Transfer: the transfer-offer code is patched by another tool");
        ulong cave = code - CodeOffset;
        var head = new byte[IdsOffset + MaxIds * 4];
        if (!memory.TryRead(cave, head) ||
            !head.AsSpan(MagicOffset, Magic.Length).SequenceEqual(Magic) ||
            BitConverter.ToUInt64(head, SiteOffset) != at)
            throw new InvalidOperationException("Force Accept Transfer: the transfer-offer code is patched by another tool");

        int idCount = BitConverter.ToInt32(head, IdCountOffset);
        if (idCount is < 0 or > MaxIds)
            throw new InvalidOperationException("Force Accept Transfer: the club filter table is damaged");

        var ids = new int[idCount];
        for (int i = 0; i < idCount; i++)
            ids[i] = BitConverter.ToInt32(head, IdsOffset + i * 4);

        uint ring = BitConverter.ToUInt32(head, RingIndexOffset);
        int recentCount = (int)Math.Min(ring, (uint)RingSize);
        var recent = new List<ForceTransferOfferHit>(recentCount);
        if (recentCount > 0)
        {
            var ringBytes = memory.Read(cave + RingOffset, RingSize * RecordSize);
            for (int i = 1; i <= recentCount; i++)
            {
                int slot = (int)((ring - (uint)i) % RingSize);
                int off = slot * RecordSize;
                recent.Add(new(
                    Proposal: BitConverter.ToUInt64(ringBytes, off + 0x00),
                    BuyerClub: BitConverter.ToUInt64(ringBytes, off + 0x08),
                    BuyerClubId: BitConverter.ToInt32(ringBytes, off + 0x10),
                    OriginalStatus: ringBytes[off + 0x14],
                    NewStatus: ringBytes[off + 0x15],
                    Forced: ringBytes[off + 0x16] != 0));
            }
        }

        return new(
            Site: at,
            Cave: cave,
            Enabled: head[EnabledOffset] != 0,
            Calls: BitConverter.ToInt64(head, CallsOffset),
            Forced: BitConverter.ToInt64(head, ForcedOffset),
            ClubIds: ids,
            Recent: recent);
    }

    /// <summary>Installs the trampoline with <c>Enabled = false</c> (once per game session) or migrates an older routine.</summary>
    public ForceTransferOffersState Install()
    {
        if (Read() is { } installed)
            return RoutineIsCurrent(installed) || host is null ? installed : Migrate(installed);
        if (host is null) throw new InvalidOperationException("Force Accept Transfer needs a writable game process");
        RequireOffsets(profile);

        ulong at = FindSite();
        var original = memory.Read(at, StolenLength);
        if (!original.AsSpan().SequenceEqual(OriginalBytes.AsSpan(0, StolenLength)))
            throw new StaleObjectException("The transfer-offer code changed while it was being read");

        ulong cave = host.AllocateNear(at, BlockSize);
        var block = BuildBlock(cave, at, original, profile);
        Put(cave, block);
        host.FlushCode(cave, block.Length);

        var patch = BuildPatch(at, cave + CodeOffset);
        using (host.Pause())
        {
            if (!memory.Read(at, StolenLength).AsSpan().SequenceEqual(original))
                throw new StaleObjectException("The transfer-offer code changed before the patch");
            uint previous = host.Protect(at, StolenLength, PageExecuteReadWrite);
            try { Put(at, patch); }
            finally { host.Protect(at, StolenLength, previous); }
            host.FlushCode(at, StolenLength);
        }
        return Read() ?? throw new MemoryAccessException("The transfer-offer patch did not stay in place");
    }

    /// <summary>Installs if needed and configures the managed club filter list and active toggle.</summary>
    public ForceTransferOffersState Configure(bool enabled, IEnumerable<int> clubIds)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        int[] unique = [.. clubIds.Where(id => id != 0 && id != -1).Distinct()];
        if (unique.Length > MaxIds)
            throw new ArgumentOutOfRangeException(nameof(clubIds), unique.Length, $"At most {MaxIds} club ids can be watched");
        if (host is null && Read() is null)
            throw new InvalidOperationException("Force Accept Transfer needs a writable game process");

        var state = Install();
        // Disable cleanly while updating the table so no game thread observes a torn count/array.
        Put(state.Cave + EnabledOffset, [0]);
        var table = new byte[MaxIds * 4];
        for (int i = 0; i < unique.Length; i++)
            BitConverter.GetBytes(unique[i]).CopyTo(table, i * 4);
        Put(state.Cave + IdsOffset, table);
        Put(state.Cave + IdCountOffset, BitConverter.GetBytes(unique.Length));
        Put(state.Cave + EnabledOffset, [enabled ? (byte)1 : (byte)0]);
        return Read() ?? state;
    }

    /// <summary>Restores the game's original 8 bytes at the site; leaves the cave allocated for safety.</summary>
    public void Uninstall()
    {
        if (Read() is not { } state || host is null) return;
        var original = memory.Read(state.Cave + OriginalOffset, StolenLength);
        using (host.Pause())
        {
            uint previous = host.Protect(state.Site, StolenLength, PageExecuteReadWrite);
            try { Put(state.Site, original); }
            finally { host.Protect(state.Site, StolenLength, previous); }
            host.FlushCode(state.Site, StolenLength);
        }
    }

    bool RoutineIsCurrent(ForceTransferOffersState state)
    {
        var stolen = memory.Read(state.Cave + OriginalOffset, StolenLength);
        if (stolen.Length != StolenLength) return false;
        var expected = BuildRoutine(state.Cave + CodeOffset, state.Site + StolenLength, stolen, profile);
        return memory.Read(state.Cave + CodeOffset, expected.Length).AsSpan().SequenceEqual(expected);
    }

    ForceTransferOffersState Migrate(ForceTransferOffersState old)
    {
        RequireOffsets(profile);
        var stolen = memory.Read(old.Cave + OriginalOffset, StolenLength);
        ulong cave = host!.AllocateNear(old.Site, BlockSize);
        var block = BuildBlock(cave, old.Site, stolen, profile);
        BitConverter.GetBytes(old.Calls).CopyTo(block, CallsOffset);
        BitConverter.GetBytes(old.Forced).CopyTo(block, ForcedOffset);
        block[EnabledOffset] = old.Enabled ? (byte)1 : (byte)0;
        int count = Math.Min(old.ClubIds.Count, MaxIds);
        BitConverter.GetBytes(count).CopyTo(block, IdCountOffset);
        for (int i = 0; i < count; i++)
            BitConverter.GetBytes(old.ClubIds[i]).CopyTo(block, IdsOffset + i * 4);
        Put(cave, block);
        host.FlushCode(cave, block.Length);

        var patch = BuildPatch(old.Site, cave + CodeOffset);
        using (host.Pause())
        {
            var current = memory.Read(old.Site, StolenLength);
            if (!IsPatchedSite([.. current, .. memory.Read(old.Site + StolenLength, 8)]) ||
                old.Site + 5 + (ulong)(long)BitConverter.ToInt32(current, 1) != old.Cave + CodeOffset)
                throw new StaleObjectException("The transfer-offer patch changed while it was being updated");
            uint previous = host.Protect(old.Site, StolenLength, PageExecuteReadWrite);
            try { Put(old.Site, patch); }
            finally { host.Protect(old.Site, StolenLength, previous); }
            host.FlushCode(old.Site, StolenLength);
        }
        return Read() ?? throw new MemoryAccessException("The transfer-offer patch did not stay in place");
    }

    /// <summary>Assembles the entire 4 KB cave block for tests and installation.</summary>
    public static byte[] BuildBlock(ulong cave, ulong site, byte[] stolen, OffsetProfile profile)
    {
        if (stolen.Length != StolenLength) throw new ArgumentException("Eight stolen bytes are expected", nameof(stolen));
        var block = new byte[BlockSize];
        Magic.CopyTo(block.AsSpan(MagicOffset));
        stolen.CopyTo(block, OriginalOffset);
        BitConverter.GetBytes(site).CopyTo(block, SiteOffset);
        var routine = BuildRoutine(cave + CodeOffset, site + StolenLength, stolen, profile);
        if (routine.Length > IdsOffset - CodeOffset)
            throw new InvalidOperationException("The transfer-offer routine does not fit its slot");
        routine.CopyTo(block, CodeOffset);
        return block;
    }

    internal static byte[] BuildRoutine(ulong codeAddress, ulong resume, byte[] stolen, OffsetProfile profile)
    {
        if (stolen.Length != StolenLength) throw new ArgumentException("Eight stolen bytes are expected", nameof(stolen));
        var (buyer, teamClub, clubId) = RequireOffsets(profile);

        var code = new byte[RoutinePrefix.Length + 5];
        RoutinePrefix.CopyTo(code, 0);
        stolen.CopyTo(code, 0);

        Debug.Assert(BitConverter.ToUInt32(code, BuyerSentinelOffset) == 0x11223344u);
        Debug.Assert(BitConverter.ToUInt32(code, TeamClubSentinelOffset) == 0x22334455u);
        Debug.Assert(BitConverter.ToUInt32(code, ClubIdSentinelOffset) == 0x33445566u);

        BitConverter.GetBytes(buyer).CopyTo(code, BuyerSentinelOffset);
        BitConverter.GetBytes(teamClub).CopyTo(code, TeamClubSentinelOffset);
        BitConverter.GetBytes(clubId).CopyTo(code, ClubIdSentinelOffset);

        code[RoutinePrefix.Length] = 0xE9;
        long jmpDelta = (long)resume - (long)(codeAddress + (ulong)code.Length);
        BitConverter.GetBytes(checked((int)jmpDelta)).CopyTo(code, RoutinePrefix.Length + 1);
        return code;
    }

    static byte[] BuildPatch(ulong site, ulong target)
    {
        long delta = (long)target - (long)(site + 5);
        var patch = new byte[StolenLength];
        patch[0] = 0xE9;
        BitConverter.GetBytes(checked((int)delta)).CopyTo(patch, 1);
        patch[5] = 0x90;
        patch[6] = 0x90;
        patch[7] = 0x90;
        return patch;
    }

    static (int Buyer, int TeamClub, int ClubId) RequireOffsets(OffsetProfile profile)
    {
        var proposals = profile.Proposals ?? throw new InvalidOperationException("Transfer proposal offsets are missing in this build profile");
        var contract = profile.Contract ?? throw new InvalidOperationException("Contract/TeamClub offsets are missing in this build profile");
        int clubId = profile.Club?.Id ?? throw new InvalidOperationException("Club id offset is missing in this build profile");
        return (proposals.Buyer, contract.TeamClub, clubId);
    }

    IEnumerable<(ulong Start, ulong Size)> CodeSections()
    {
        int peOffset = BitConverter.ToInt32(memory.Read(moduleBase + 0x3C, 4));
        var header = memory.Read(moduleBase + (ulong)peOffset, 24);
        int sections = BitConverter.ToUInt16(header, 6), optional = BitConverter.ToUInt16(header, 20);
        var table = memory.Read(moduleBase + (ulong)peOffset + 24 + (ulong)optional, sections * 40);
        for (int i = 0; i < sections; i++)
        {
            uint size = BitConverter.ToUInt32(table, i * 40 + 8), rva = BitConverter.ToUInt32(table, i * 40 + 12);
            uint flags = BitConverter.ToUInt32(table, i * 40 + 36);
            if ((flags & 0x20000000) == 0 || rva + (ulong)size > (ulong)moduleSize) continue;
            yield return (moduleBase + rva, size);
        }
    }

    void Put(ulong address, byte[] data)
    {
        if (!memory.TryWrite(address, data))
            throw new MemoryAccessException($"Cannot write {data.Length} bytes at 0x{address:X}");
    }
}
