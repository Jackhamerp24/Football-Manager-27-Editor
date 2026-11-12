using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Presets;

/// <summary>What the code patch needs from the process besides reads and writes.</summary>
public interface ICodePatchHost
{
    /// <summary>Executable memory close enough to <paramref name="near"/> for a rel32 jump.</summary>
    ulong AllocateNear(ulong near, int bytes);

    /// <summary>Changes page protection and returns the previous value.</summary>
    uint Protect(ulong address, int bytes, uint protect);

    /// <summary>Stops every thread of the process until the result is disposed, so no thread runs half-patched code.</summary>
    IDisposable Pause();

    /// <summary>Makes the processor see freshly written code.</summary>
    void FlushCode(ulong address, int bytes);
}

/// <summary>One player whose development is sped up, as the game process holds it.</summary>
/// <summary>One player whose development is sped up; <see cref="Multiplier"/> in steps of 0.1 (2.5 = two and a half times).</summary>
public sealed record AcceleratedPlayer(ulong Address, int PlayerId, double Multiplier);

/// <summary>Whether the patch is in place, how often it has run, and who it speeds up.</summary>
public sealed record AcceleratedState(ulong Site, ulong Cave, long Calls, long Hits, bool LiftZeroGains, IReadOnlyList<AcceleratedPlayer> Players)
{
    /// <summary>The last objects (r15) the game passed through the site, newest first; for checking the hook sees players.</summary>
    public IReadOnlyList<ulong> Recent { get; init; } = [];

    /// <summary>The block was written by an earlier editor version (whole-number multipliers); Install replaces it.</summary>
    public bool IsOlderVersion { get; init; }
}

/// <summary>
/// "Accelerate Player Development": the game adds a player's training gain to his Current Ability in one place
/// (<c>movzx eax, word [r15+CA]</c>, <c>add ax, di</c>, then CA is capped at PA). A jump there leads to a small routine
/// in the game process that looks the player (<c>r15</c>) up in a table and multiplies the gain (<c>di</c>) by that
/// player's multiplier. The game's own cap keeps CA at or below PA.
/// </summary>
/// <remarks>
/// <para>Idea from the Cheat Engine table preset of the same name (FM26). The site is found by its bytes, built from the
/// profile's CA and PA offsets, so no address is fixed for one build; exactly one match is required. The routine is
/// written here from scratch and filters only by the table: choosing players is done in the editor.</para>
/// <para>Table changes are ordinary writes into the routine's block, so the edit history can undo them. Installing and
/// removing the jump pause the game for the length of one write.</para>
/// </remarks>
public sealed class AcceleratedDevelopment(IProcessMemory memory, ICodePatchHost? host, OffsetProfile profile, ulong moduleBase, int moduleSize)
{
    /// <summary>Speeds are 1.1x to 20x in steps of 0.1; 1x (or 0) means normal speed.</summary>
    public const double MinMultiplier = 1.1, MaxMultiplier = 20;

    /// <summary>The largest gain one call can produce after multiplying; CA is 1–200, so this never wraps.</summary>
    public const int MaxGain = 200;

    public static readonly double[] OfferedMultipliers = [2, 3, 5, 10, 20];

    /// <summary>True for a speed the routine can hold: 1.1-20 with at most one decimal.</summary>
    public static bool IsAllowedMultiplier(double value) =>
        value >= MinMultiplier - 1e-9 && value <= MaxMultiplier + 1e-9 && Math.Abs(value * 10 - Math.Round(value * 10)) < 1e-6;

    // Block layout. Header fields are read and written by the editor; the routine only counts and reads.
    internal const int MagicOffset = 0x00, CallsOffset = 0x08, HitsOffset = 0x10, OriginalOffset = 0x18, SiteOffset = 0x20,
        CountOffset = 0x28, LiftZeroOffset = 0x30, RingIndexOffset = 0x38, CodeOffset = 0x40, RingOffset = 0x180, RingSize = 16,
        TableOffset = 0x200, EntrySize = 0x10;
    public const int BlockSize = 0x10000;
    public const int Capacity = (BlockSize - TableOffset) / EntrySize;
    /// <summary>Version 2 keeps multipliers in tenths; version 1 (an earlier editor) kept whole numbers.</summary>
    internal static readonly byte[] Magic = "FM27ACC2"u8.ToArray(), MagicV1 = "FM27ACC1"u8.ToArray();
    const int StolenLength = 8;
    const uint PageExecuteReadWrite = 0x40;

    ulong? site;

    PlayerFieldOffsets Fields => profile.Fields;

    /// <summary>
    /// Bytes at the site before patching: <c>movzx eax, word [r15+CA]; add ax, di; movzx ecx, word [r15+PA]</c>.
    /// </summary>
    public static byte[] OriginalPattern(OffsetProfile profile)
    {
        var p = new List<byte> { 0x41, 0x0F, 0xB7, 0x87 };
        p.AddRange(BitConverter.GetBytes(profile.Fields.CurrentAbility));
        p.AddRange([0x66, 0x01, 0xF8, 0x41, 0x0F, 0xB7, 0x8F]);
        p.AddRange(BitConverter.GetBytes(profile.Fields.PotentialAbility));
        return [.. p];
    }

    /// <summary>The same bytes once patched: the first eight are a jump, the rest is untouched.</summary>
    static bool IsPatchedSite(ReadOnlySpan<byte> at, byte[] original) =>
        at.Length >= original.Length && at[0] == 0xE9 && at[5] == 0x90 && at[6] == 0x90 && at[7] == 0x90
        && at[StolenLength..original.Length].SequenceEqual(original.AsSpan(StolenLength));

    /// <summary>The site: the one place in the module's code holding <see cref="OriginalPattern"/>, patched or not.</summary>
    public ulong FindSite()
    {
        if (site is { } known) return known;
        var original = OriginalPattern(profile);
        var hits = new List<ulong>();
        foreach (var (start, size) in CodeSections())
        {
            const int Chunk = 1 << 20;
            var buffer = new byte[Chunk + original.Length];
            for (ulong offset = 0; offset < size; offset += Chunk)
            {
                int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                if (!memory.TryRead(start + offset, buffer.AsSpan(0, length))) continue;
                var span = buffer.AsSpan(0, length);
                int limit = Math.Min(Chunk, length - original.Length + 1);
                for (int i = 0; i < limit; i++)
                {
                    if (span[i] != 0x41 && span[i] != 0xE9) continue;
                    var here = span[i..];
                    if (here[..original.Length].SequenceEqual(original) || IsPatchedSite(here, original))
                        hits.Add(start + offset + (ulong)i);
                }
                if (hits.Count > 1) break;
            }
            if (hits.Count > 1) break;
        }
        if (hits.Count == 0) throw new InvalidOperationException("Accelerate Player Development: the training-gain code was not found in this build");
        if (hits.Count > 1) throw new InvalidOperationException("Accelerate Player Development: the training-gain code is not unique in this build");
        return (site = hits[0]).Value;
    }

    /// <summary>Executable sections of the loaded module, from its PE header.</summary>
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

    /// <summary>The current state, or null when the patch is not installed.</summary>
    public AcceleratedState? Read()
    {
        ulong at = FindSite();
        var bytes = memory.Read(at, StolenLength);
        if (bytes[0] != 0xE9) return null;
        ulong code = at + 5 + (ulong)(long)BitConverter.ToInt32(bytes, 1);
        ulong cave = code - CodeOffset;
        var head = memory.Read(cave, TableOffset);
        bool v1 = head.AsSpan(MagicOffset, MagicV1.Length).SequenceEqual(MagicV1);
        if (!v1 && !head.AsSpan(MagicOffset, Magic.Length).SequenceEqual(Magic) || BitConverter.ToUInt64(head, SiteOffset) != at)
            throw new InvalidOperationException("The training-gain code is patched by another tool");
        int count = BitConverter.ToInt32(head, CountOffset);
        if (count is < 0 or > Capacity) throw new InvalidOperationException("The accelerated player table is damaged");
        var players = new List<AcceleratedPlayer>(count);
        if (count > 0)
        {
            var rows = memory.Read(cave + TableOffset, count * EntrySize);
            for (int i = 0; i < count; i++)
                players.Add(new(BitConverter.ToUInt64(rows, i * EntrySize), BitConverter.ToInt32(rows, i * EntrySize + 12),
                    BitConverter.ToUInt16(rows, i * EntrySize + 8) / (v1 ? 1.0 : 10.0)));
        }
        uint ring = BitConverter.ToUInt32(head, RingIndexOffset);
        var recent = new List<ulong>(RingSize);
        for (int i = 1; i <= Math.Min(RingSize, (int)Math.Min(ring, RingSize)); i++)
            recent.Add(BitConverter.ToUInt64(head, RingOffset + (int)((ring - (uint)i) % RingSize) * 8));
        return new(at, cave, BitConverter.ToInt64(head, CallsOffset), BitConverter.ToInt64(head, HitsOffset), head[LiftZeroOffset] != 0, players)
            { Recent = recent, IsOlderVersion = v1 };
    }

    /// <summary>Puts the jump and the routine in place (once per game run); returns the state.</summary>
    public AcceleratedState Install(bool liftZeroGains = true)
    {
        if (Read() is { } installed)
            return RoutineIsCurrent(installed) || host is null ? installed : Migrate(installed);
        if (host is null) throw new InvalidOperationException("Accelerate Player Development needs a writable game process");
        ulong at = FindSite();
        var original = memory.Read(at, StolenLength);
        if (!original.AsSpan().SequenceEqual(OriginalPattern(profile).AsSpan(0, StolenLength)))
            throw new StaleObjectException("The training-gain code changed while it was being read");

        ulong cave = host.AllocateNear(at, BlockSize);
        var block = BuildBlock(cave, at, original, liftZeroGains);
        Put(cave, block);
        host.FlushCode(cave, block.Length);

        var patch = BuildPatch(at, cave + CodeOffset);
        using (host.Pause())
        {
            if (!memory.Read(at, StolenLength).AsSpan().SequenceEqual(original))
                throw new StaleObjectException("The training-gain code changed before the patch");
            uint previous = host.Protect(at, StolenLength, PageExecuteReadWrite);
            try { Put(at, patch); }
            finally { host.Protect(at, StolenLength, previous); }
            host.FlushCode(at, StolenLength);
        }
        return Read() ?? throw new MemoryAccessException("The patch did not stay in place");
    }

    bool RoutineIsCurrent(AcceleratedState state)
    {
        if (state.IsOlderVersion) return false;
        var stolen = memory.Read(state.Cave + OriginalOffset, StolenLength);
        var expected = BuildRoutine(state.Cave + CodeOffset, state.Cave, state.Site + StolenLength, stolen);
        return memory.Read(state.Cave + CodeOffset, expected.Length).AsSpan().SequenceEqual(expected);
    }

    /// <summary>
    /// The game holds an older routine (an earlier editor version wrote it): a new block gets the current routine, the
    /// counters and the table, then the jump is pointed at it. The old block is left alone, since a paused thread may be
    /// inside it; it finishes there and returns to the game as before.
    /// </summary>
    AcceleratedState Migrate(AcceleratedState old)
    {
        var stolen = memory.Read(old.Cave + OriginalOffset, StolenLength);
        ulong cave = host!.AllocateNear(old.Site, BlockSize);
        var block = BuildBlock(cave, old.Site, stolen, old.LiftZeroGains);
        BitConverter.GetBytes(old.Calls).CopyTo(block, CallsOffset);
        BitConverter.GetBytes(old.Hits).CopyTo(block, HitsOffset);
        BitConverter.GetBytes(old.Players.Count).CopyTo(block, CountOffset);
        for (int i = 0; i < old.Players.Count; i++)
            EntryBytes(old.Players[i].Address, old.Players[i].PlayerId, old.Players[i].Multiplier).CopyTo(block, TableOffset + i * EntrySize);  // Read already turned v1 whole numbers into multipliers
        Put(cave, block);
        host.FlushCode(cave, block.Length);
        var patch = BuildPatch(old.Site, cave + CodeOffset);
        using (host.Pause())
        {
            var current = memory.Read(old.Site, StolenLength);
            if (current[0] != 0xE9 || old.Site + 5 + (ulong)(long)BitConverter.ToInt32(current, 1) != old.Cave + CodeOffset)
                throw new StaleObjectException("The training-gain patch changed while it was being updated");
            uint previous = host.Protect(old.Site, StolenLength, PageExecuteReadWrite);
            try { Put(old.Site, patch); }
            finally { host.Protect(old.Site, StolenLength, previous); }
            host.FlushCode(old.Site, StolenLength);
        }
        return Read() ?? throw new MemoryAccessException("The patch did not stay in place");
    }

    /// <summary>Puts the game's original bytes back. The routine's block stays allocated: a paused thread may still be in it.</summary>
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

    /// <summary>
    /// Sets the player's speed (<see cref="MinMultiplier"/>-<see cref="MaxMultiplier"/> in steps of 0.1), or takes him out of
    /// the table with 0 or 1 (normal speed). Returns false when nothing changed. Installs the patch on first use.
    /// </summary>
    public bool Set(PlayerRecord player, double multiplier)
    {
        if (multiplier == 1) multiplier = 0;
        if (multiplier != 0 && !IsAllowedMultiplier(multiplier))
            throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, $"Speed must be {MinMultiplier}-{MaxMultiplier} with at most one decimal, or 1 for normal");
        if (player.Id is not > 0) throw new InvalidOperationException($"{player.DisplayName}: the player has no readable id");
        RequirePlayer(player);
        var state = multiplier == 0 ? Read() : Install();
        if (state is null) return false;
        int index = state.Players.ToList().FindIndex(p => p.Address == player.Address);
        if (multiplier == 0)
        {
            if (index < 0) return false;
            int last = state.Players.Count - 1;
            if (index != last) Put(Entry(state.Cave, index), memory.Read(Entry(state.Cave, last), EntrySize));
            Put(state.Cave + CountOffset, BitConverter.GetBytes(last));
            return true;
        }
        if (index >= 0)
        {
            if (Math.Abs(state.Players[index].Multiplier - multiplier) < 1e-9 && state.Players[index].PlayerId == player.Id) return false;
            Put(Entry(state.Cave, index), EntryBytes(player.Address, player.Id.Value, multiplier));
            return true;
        }
        if (state.Players.Count >= Capacity) throw new InvalidOperationException($"At most {Capacity} players can be accelerated at once");
        // The row goes in before the count grows, so the routine never sees a half-written row.
        Put(Entry(state.Cave, state.Players.Count), EntryBytes(player.Address, player.Id.Value, multiplier));
        Put(state.Cave + CountOffset, BitConverter.GetBytes(state.Players.Count + 1));
        return true;
    }

    /// <summary>The player's multiplier, or 0 when he is not accelerated (or the patch is not installed).</summary>
    public double MultiplierOf(PlayerRecord player) =>
        Read()?.Players.FirstOrDefault(p => p.Address == player.Address)?.Multiplier ?? 0;

    /// <summary>Takes out every row whose object is no longer that player (another save was loaded into the same game run).</summary>
    public int RemoveStale()
    {
        if (Read() is not { } state) return 0;
        int removed = 0;
        foreach (var row in state.Players.Reverse())
        {
            if (StillThePlayer(row)) continue;
            var fresh = Read()!;
            int index = fresh.Players.ToList().FindIndex(p => p.Address == row.Address);
            int last = fresh.Players.Count - 1;
            if (index != last) Put(Entry(fresh.Cave, index), memory.Read(Entry(fresh.Cave, last), EntrySize));
            Put(fresh.Cave + CountOffset, BitConverter.GetBytes(last));
            removed++;
        }
        return removed;
    }

    bool StillThePlayer(AcceleratedPlayer row)
    {
        if (Fields.Id is not { } id) return false;
        var b = new byte[8];
        if (!memory.TryRead(row.Address, b)) return false;
        ulong vtable = BitConverter.ToUInt64(b);
        if (!profile.PlayerVtableRvas.Any(r => vtable == moduleBase + r)) return false;
        return memory.TryRead(row.Address + (ulong)id, b.AsSpan(0, 4)) && BitConverter.ToInt32(b, 0) == row.PlayerId;
    }

    void RequirePlayer(PlayerRecord player)
    {
        if (!StillThePlayer(new(player.Address, player.Id ?? 0, 0)))
            throw new StaleObjectException($"{player.DisplayName}: the player object changed since it was read");
    }

    static ulong Entry(ulong cave, int index) => cave + TableOffset + (ulong)(index * EntrySize);

    static byte[] EntryBytes(ulong address, int playerId, double multiplier)
    {
        var row = new byte[EntrySize];
        BitConverter.GetBytes(address).CopyTo(row, 0);
        BitConverter.GetBytes((ushort)Math.Round(multiplier * 10)).CopyTo(row, 8);   // tenths
        BitConverter.GetBytes(playerId).CopyTo(row, 12);
        return row;
    }

    void Put(ulong address, byte[] bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write 0x{address:X}");
    }

    /// <summary>The jump that replaces the eight stolen bytes: <c>jmp rel32</c> and three nops.</summary>
    public static byte[] BuildPatch(ulong from, ulong to)
    {
        long delta = (long)to - (long)(from + 5);
        if (delta is < int.MinValue or > int.MaxValue) throw new InvalidOperationException("The routine is out of reach of a rel32 jump");
        var patch = new byte[StolenLength];
        patch[0] = 0xE9;
        BitConverter.GetBytes((int)delta).CopyTo(patch, 1);
        patch[5] = patch[6] = patch[7] = 0x90;
        return patch;
    }

    /// <summary>
    /// The whole block: header, routine and an empty table. The routine, in order:
    /// <code>
    /// pushfq; push rax; push rcx; push rdx
    /// lea rcx,[block]; inc qword [rcx+Calls]
    /// mov edx,[rcx+Count]; lea rax,[rcx+Table]
    /// next: test edx,edx; jz done; cmp [rax],r15; je hit; add rax,16; dec edx; jmp next
    /// hit:  test di,di; jg grow; cmp byte [rcx+LiftZero],0; je done; mov di,1
    /// grow: edx = word [rax+8] (multiplier in tenths); edx = (di * edx + 5) / 10; at most MaxGain
    /// keep: mov di,dx; inc qword [rcx+Hits]
    /// done: pop rdx; pop rcx; pop rax; popfq
    ///       (the eight stolen bytes: movzx eax, word [r15+CA])
    ///       jmp back to the instruction after them
    /// </code>
    /// A gain below one is only lifted to one when <paramref name="liftZeroGains"/> is set (the Cheat Engine preset
    /// always does); a negative gain (decline) is lifted the same way, never multiplied.
    /// </summary>
    public static byte[] BuildBlock(ulong cave, ulong site, byte[] stolen, bool liftZeroGains)
    {
        if (stolen.Length != StolenLength) throw new ArgumentException("Eight stolen bytes are expected", nameof(stolen));
        var block = new byte[BlockSize];
        Magic.CopyTo(block, MagicOffset);
        stolen.CopyTo(block, OriginalOffset);
        BitConverter.GetBytes(site).CopyTo(block, SiteOffset);
        block[LiftZeroOffset] = liftZeroGains ? (byte)1 : (byte)0;
        var code = BuildRoutine(cave + CodeOffset, cave, site + StolenLength, stolen);
        if (code.Length > RingOffset - CodeOffset) throw new InvalidOperationException("The routine does not fit its slot");
        code.CopyTo(block, CodeOffset);
        return block;
    }

    internal static byte[] BuildRoutine(ulong at, ulong cave, ulong resume, byte[] stolen)
    {
        var a = new MiniAssembler(at);
        a.Emit(0x9C, 0x50, 0x51, 0x52);                                   // pushfq; push rax; push rcx; push rdx
        a.LeaRipRcx(cave);                                                // lea rcx,[rip+cave]
        a.Emit(0x48, 0xFF, 0x41, CallsOffset);                            // inc qword [rcx+Calls]
        a.Emit(0x8B, 0x41, RingIndexOffset);                              // mov eax,[rcx+RingIndex]
        a.Emit(0x83, 0xE0, RingSize - 1);                                 // and eax,15
        a.Emit(0x4C, 0x89, 0xBC, 0xC1); a.Int32(RingOffset);              // mov [rcx+rax*8+Ring],r15
        a.Emit(0xFF, 0x41, RingIndexOffset);                              // inc dword [rcx+RingIndex]
        a.Emit(0x8B, 0x51, CountOffset);                                  // mov edx,[rcx+Count]
        a.Emit(0x48, 0x8D, 0x81); a.Int32(TableOffset);                   // lea rax,[rcx+Table]
        a.Label("next");
        a.Emit(0x85, 0xD2); a.Jump8(0x74, "done");                        // test edx,edx; jz done
        a.Emit(0x4C, 0x39, 0x38); a.Jump8(0x74, "hit");                   // cmp [rax],r15; je hit
        a.Emit(0x48, 0x83, 0xC0, EntrySize);                              // add rax,16
        a.Emit(0xFF, 0xCA); a.Jump8(0xEB, "next");                        // dec edx; jmp next
        a.Label("hit");
        a.Emit(0x66, 0x85, 0xFF); a.Jump8(0x7F, "grow");                  // test di,di; jg grow
        a.Emit(0x80, 0x79, LiftZeroOffset, 0x00); a.Jump8(0x74, "done");  // cmp byte [rcx+LiftZero],0; je done
        a.Emit(0x66, 0xBF, 0x01, 0x00);                                   // mov di,1
        a.Label("grow");
        a.Emit(0x0F, 0xB7, 0x50, 0x08);                                   // movzx edx,word [rax+8]   (tenths)
        a.Emit(0x0F, 0xBF, 0xC7);                                         // movsx eax,di             (gain > 0 here)
        a.Emit(0x0F, 0xAF, 0xC2);                                         // imul eax,edx
        a.Emit(0x83, 0xC0, 0x05);                                         // add eax,5                (round)
        a.Emit(0xBA); a.Int32(unchecked((int)0xCCCCCCCD));                // mov edx,0xCCCCCCCD
        a.Emit(0xF7, 0xE2);                                               // mul edx
        a.Emit(0xC1, 0xEA, 0x03);                                         // shr edx,3                (edx = eax / 10)
        a.Emit(0x81, 0xFA); a.Int32(MaxGain); a.Jump8(0x7E, "keep");      // cmp edx,MaxGain; jle keep
        a.Emit(0xBA); a.Int32(MaxGain);                                   // mov edx,MaxGain
        a.Label("keep");
        a.Emit(0x66, 0x89, 0xD7);                                         // mov di,dx
        a.Emit(0x48, 0xFF, 0x41, HitsOffset);                             // inc qword [rcx+Hits]
        a.Label("done");
        a.Emit(0x5A, 0x59, 0x58, 0x9D);                                   // pop rdx; pop rcx; pop rax; popfq
        a.Emit(stolen);                                                   // movzx eax, word [r15+CA]
        a.Jump32(resume);                                                 // jmp back
        return a.Finish();
    }

    /// <summary>Just enough of an x64 assembler for the routine above: raw bytes, rel8 jumps to labels, rel32 jumps out.</summary>
    sealed class MiniAssembler(ulong origin)
    {
        readonly List<byte> code = [];
        readonly Dictionary<string, int> labels = [];
        readonly List<(int At, string Label)> fixups = [];

        public void Emit(params int[] bytes) { foreach (int b in bytes) code.Add(checked((byte)b)); }
        public void Emit(byte[] bytes) => code.AddRange(bytes);
        public void Int32(int value) => code.AddRange(BitConverter.GetBytes(value));
        public void Int16(int value) => code.AddRange(BitConverter.GetBytes((short)value));
        public void Label(string name) => labels[name] = code.Count;

        public void Jump8(byte opcode, string label)
        {
            code.Add(opcode);
            fixups.Add((code.Count, label));
            code.Add(0);
        }

        public void LeaRipRcx(ulong target)
        {
            code.AddRange([0x48, 0x8D, 0x0D]);
            long delta = (long)target - (long)(origin + (ulong)code.Count + 4);
            code.AddRange(BitConverter.GetBytes(checked((int)delta)));
        }

        public void Jump32(ulong target)
        {
            code.Add(0xE9);
            long delta = (long)target - (long)(origin + (ulong)code.Count + 4);
            code.AddRange(BitConverter.GetBytes(checked((int)delta)));
        }

        public byte[] Finish()
        {
            foreach (var (at, label) in fixups)
            {
                int delta = labels[label] - (at + 1);
                code[at] = unchecked((byte)checked((sbyte)delta));
            }
            return [.. code];
        }
    }
}
