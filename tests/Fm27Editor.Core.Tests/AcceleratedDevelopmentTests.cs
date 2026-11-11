using System.Runtime.InteropServices;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The routine is run on this machine's own processor, inside a small harness that plays the game's part: it sets
/// <c>r15</c> to a fake player and <c>di</c> to a training gain, jumps into the routine, and returns <c>eax</c> (CA the
/// stolen instruction loaded) and <c>di</c> (the gain after the routine). Expected values are hand calculations.
/// The site bytes are the ones read from Steam <c>27.0.1+2494687</c> <c>game_plugin.dll</c> at RVA 0x368909A:
/// <c>41 0F B7 87 64 02 00 00  66 01 F8  41 0F B7 8F 66 02 00 00</c>.
/// </summary>
public class AcceleratedDevelopmentTests
{
    static readonly byte[] GameSiteBytes =
        [0x41, 0x0F, 0xB7, 0x87, 0x64, 0x02, 0x00, 0x00, 0x66, 0x01, 0xF8, 0x41, 0x0F, 0xB7, 0x8F, 0x66, 0x02, 0x00, 0x00];

    static OffsetProfile Profile() =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2494687-steam.json"));

    [Fact]
    public void Site_pattern_is_built_from_the_profile_and_matches_the_game_bytes()
    {
        Assert.Equal(GameSiteBytes, AcceleratedDevelopment.OriginalPattern(Profile()));
    }

    // ---------- Running the routine natively ----------

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate long Harness(nint player, int gain);

    sealed class NativeRoutine : IDisposable
    {
        const int HarnessOffset = AcceleratedDevelopment.BlockSize;
        public readonly nint Block;
        readonly nint player;
        readonly Harness run;

        public NativeRoutine(bool liftZeroGains, short currentAbility = 120)
        {
            Block = VirtualAlloc(0, HarnessOffset + 0x1000, 0x3000, 0x40);
            if (Block == 0) throw new InvalidOperationException("VirtualAlloc failed");
            player = Marshal.AllocHGlobal(0x400);
            for (int i = 0; i < 0x400; i++) Marshal.WriteByte(player, i, 0);
            Marshal.WriteInt16(player, 0x264, currentAbility);

            ulong cave = (ulong)Block, harness = cave + HarnessOffset;
            // push r15; push rdi; mov r15,rcx; mov edi,edx; jmp routine
            var h = new List<byte> { 0x41, 0x57, 0x57, 0x49, 0x89, 0xCF, 0x89, 0xD7, 0xE9 };
            long toRoutine = (long)(cave + 0x40) - (long)(harness + (ulong)h.Count + 4);
            h.AddRange(BitConverter.GetBytes((int)toRoutine));
            ulong resume = harness + (ulong)h.Count;
            // resume: shl rdi,32; or rax,rdi; pop rdi; pop r15; ret
            h.AddRange([0x48, 0xC1, 0xE7, 0x20, 0x48, 0x09, 0xF8, 0x5F, 0x41, 0x5F, 0xC3]);

            var block = AcceleratedDevelopment.BuildBlock(cave, resume - 8, GameSiteBytes[..8], liftZeroGains);
            Marshal.Copy(block, 0, Block, block.Length);
            Marshal.Copy(h.ToArray(), 0, (nint)harness, h.Count);
            run = Marshal.GetDelegateForFunctionPointer<Harness>((nint)harness);
        }

        public nint Player => player;

        public void Table(params (nint Address, int Multiplier)[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                nint row = Block + 0x200 + i * 0x10;
                Marshal.WriteInt64(row, rows[i].Address);
                Marshal.WriteInt16(row + 8, (short)rows[i].Multiplier);
            }
            Marshal.WriteInt32(Block + 0x28, rows.Length);
        }

        /// <summary>Runs the routine; returns CA as the stolen instruction loaded it, and the gain left in di.</summary>
        public (int Ca, short Gain) Run(short gain)
        {
            long r = run(player, (ushort)gain);
            return ((int)(r & 0xFFFFFFFF), (short)((r >> 32) & 0xFFFF));
        }

        public long Calls => Marshal.ReadInt64(Block + 0x08);
        public long Hits => Marshal.ReadInt64(Block + 0x10);

        public void Dispose()
        {
            VirtualFree(Block, 0, 0x8000);
            Marshal.FreeHGlobal(player);
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern nint VirtualAlloc(nint address, nint size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFree(nint address, nint size, uint type);
    }

    [Fact]
    public void A_player_not_in_the_table_keeps_his_gain_and_the_stolen_load_still_reads_CA()
    {
        using var r = new NativeRoutine(liftZeroGains: true, currentAbility: 133);
        r.Table((r.Player + 0x1000, 5));
        Assert.Equal((133, (short)1), r.Run(1));
        Assert.Equal(1, r.Calls);
        Assert.Equal(0, r.Hits);
    }

    [Fact]
    public void A_player_in_the_table_gets_his_gain_multiplied()
    {
        using var r = new NativeRoutine(liftZeroGains: true);
        r.Table((r.Player - 0x400, 3), (r.Player, 5));      // second row is the player
        Assert.Equal((120, (short)10), r.Run(2));             // 2 x 5
        Assert.Equal(1, r.Hits);
    }

    [Theory]
    [InlineData(true, 0, 5)]     // lifted to 1, then x5
    [InlineData(false, 0, 0)]    // left alone
    [InlineData(true, -1, 5)]    // a decline is lifted to 1, then x5 - never multiplied
    [InlineData(false, -1, -1)]  // left alone
    public void Gains_below_one_follow_the_lift_setting(bool lift, short gain, short expected)
    {
        using var r = new NativeRoutine(lift);
        r.Table((r.Player, 5));
        Assert.Equal(expected, r.Run(gain).Gain);
    }

    [Theory]
    [InlineData(20, 15, 200)]    // 300 is capped at 200
    [InlineData(20, 2000, 200)]  // 40,000 overflows 16 bits: capped, not wrapped negative
    [InlineData(20, 10, 200)]    // exactly 200 is kept
    [InlineData(2, 99, 198)]
    public void The_multiplied_gain_never_exceeds_200(int multiplier, short gain, short expected)
    {
        using var r = new NativeRoutine(liftZeroGains: true);
        r.Table((r.Player, multiplier));
        Assert.Equal(expected, r.Run(gain).Gain);
    }

    [Fact]
    public void An_empty_table_changes_nothing()
    {
        using var r = new NativeRoutine(liftZeroGains: true);
        r.Table();
        Assert.Equal((short)3, r.Run(3).Gain);
        Assert.Equal(1, r.Calls);
    }

    // ---------- Install / table edits on fake memory ----------

    const ulong Module = 0x7FF0_0000_0000, CodeRva = 0x1000, SiteRva = 0x1500, Player = 0x2000_0000, OtherPlayer = 0x2000_1000;

    sealed class FakeHost(FakeMemory memory) : ICodePatchHost
    {
        public ulong Next = 0x7FF0_0100_0000;
        public int Pauses, Flushes;
        public ulong AllocateNear(ulong near, int bytes) { ulong at = Next; memory.Map(at, bytes); Next += 0x100000; return at; }
        public uint Protect(ulong address, int bytes, uint protect) => 0x20;
        public IDisposable Pause() { Pauses++; return new Done(); }
        public void FlushCode(ulong address, int bytes) => Flushes++;
        sealed class Done : IDisposable { public void Dispose() { } }
    }

    static (FakeMemory Memory, OffsetProfile Profile, byte[] Code) Module_(params int[] siteRvas)
    {
        var profile = Profile();
        var memory = new FakeMemory();
        var header = memory.Map(Module, 0x400);
        BitConverter.GetBytes(0x80).CopyTo(header, 0x3C);
        BitConverter.GetBytes((ushort)1).CopyTo(header, 0x80 + 6);       // one section
        BitConverter.GetBytes((ushort)0).CopyTo(header, 0x80 + 20);      // no optional header
        int s = 0x80 + 24;
        BitConverter.GetBytes(0x2000u).CopyTo(header, s + 8);            // virtual size
        BitConverter.GetBytes((uint)CodeRva).CopyTo(header, s + 12);
        BitConverter.GetBytes(0x60000020u).CopyTo(header, s + 36);       // code | execute | read
        var code = memory.Map(Module + CodeRva, 0x2000);
        foreach (int rva in siteRvas) GameSiteBytes.CopyTo(code, rva - (int)CodeRva);
        foreach (var (address, id) in new[] { (Player, 111), (OtherPlayer, 222) })
        {
            var p = memory.Map(address, 0x400);
            BitConverter.GetBytes(Module + profile.PlayerVtableRvas[0]).CopyTo(p, 0);
            BitConverter.GetBytes(id).CopyTo(p, profile.Fields.Id!.Value);
        }
        return (memory, profile, code);
    }

    static PlayerRecord Record(ulong address, int id) => new()
    {
        Address = address, VtableRva = 0, Id = id, CurrentAbility = 100, PotentialAbility = 150, HomeReputation = 1,
        CurrentReputation = 1, WorldReputation = 1, Height = 180, RawAttributes = [], AttributeScale = 5, Positions = [], Personality = [],
    };

    [Fact]
    public void Install_puts_a_jump_at_the_site_and_keeps_the_original_bytes_in_the_block()
    {
        var (memory, profile, code) = Module_((int)SiteRva);
        var host = new FakeHost(memory);
        var accel = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000);

        var state = accel.Install();

        ulong site = Module + SiteRva;
        Assert.Equal(site, state.Site);
        var patched = memory.Read(site, 8);
        Assert.Equal(0xE9, patched[0]);
        Assert.Equal(state.Cave + 0x40, site + 5 + (ulong)(long)BitConverter.ToInt32(patched, 1));
        Assert.Equal(new byte[] { 0x90, 0x90, 0x90 }, patched[5..8]);
        Assert.Equal(GameSiteBytes[8..], memory.Read(site + 8, GameSiteBytes.Length - 8));  // rest of the site untouched
        Assert.Equal(GameSiteBytes[..8], memory.Read(state.Cave + 0x18, 8));
        Assert.Equal(1, host.Pauses);
        Assert.Empty(state.Players);
    }

    [Fact]
    public void Install_twice_reuses_the_routine_already_in_the_game()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var host = new FakeHost(memory);
        var first = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000).Install();
        var second = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000).Install();   // editor restarted
        Assert.Equal(first.Cave, second.Cave);
        Assert.Equal(1, host.Pauses);
    }

    [Fact]
    public void A_site_that_appears_twice_is_refused_and_nothing_is_written()
    {
        var (memory, profile, _) = Module_((int)SiteRva, 0x1800);
        var host = new FakeHost(memory);
        var accel = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000);
        Assert.Throws<InvalidOperationException>(() => accel.Install());
        Assert.Equal(GameSiteBytes, memory.Read(Module + SiteRva, GameSiteBytes.Length));
        Assert.Equal(0, host.Pauses);
    }

    [Fact]
    public void A_build_without_the_site_is_refused()
    {
        var (memory, profile, _) = Module_();
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<InvalidOperationException>(() => accel.Install());
    }

    [Fact]
    public void Set_adds_changes_and_removes_rows()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        var a = Record(Player, 111);
        var b = Record(OtherPlayer, 222);

        Assert.True(accel.Set(a, 5));
        Assert.True(accel.Set(b, 3));
        Assert.False(accel.Set(a, 5));                       // nothing changed
        Assert.True(accel.Set(a, 10));
        Assert.Equal(10, accel.MultiplierOf(a));
        Assert.Equal([new AcceleratedPlayer(Player, 111, 10), new AcceleratedPlayer(OtherPlayer, 222, 3)], accel.Read()!.Players);

        Assert.True(accel.Set(a, 0));                        // the last row moves into the freed slot
        Assert.Equal([new AcceleratedPlayer(OtherPlayer, 222, 3)], accel.Read()!.Players);
        Assert.False(accel.Set(a, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    [InlineData(-2)]
    public void Multipliers_outside_2_to_20_are_refused(int multiplier)
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<ArgumentOutOfRangeException>(() => accel.Set(Record(Player, 111), multiplier));
        Assert.Equal(GameSiteBytes, memory.Read(Module + SiteRva, GameSiteBytes.Length));
    }

    [Fact]
    public void A_record_whose_object_is_another_player_is_refused()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<StaleObjectException>(() => accel.Set(Record(Player, 999), 5));
    }

    [Fact]
    public void Rows_whose_player_object_changed_are_removed()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        accel.Set(Record(Player, 111), 5);
        accel.Set(Record(OtherPlayer, 222), 3);
        memory.TryWrite(Player + (ulong)profile.Fields.Id!.Value, BitConverter.GetBytes(555));   // another save: a different player here

        Assert.Equal(1, accel.RemoveStale());
        Assert.Equal([new AcceleratedPlayer(OtherPlayer, 222, 3)], accel.Read()!.Players);
    }

    [Fact]
    public void Uninstall_puts_the_original_bytes_back()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var accel = new AcceleratedDevelopment(memory, new FakeHost(memory), profile, Module, 0x10000);
        accel.Set(Record(Player, 111), 5);
        accel.Uninstall();
        Assert.Equal(GameSiteBytes, memory.Read(Module + SiteRva, GameSiteBytes.Length));
        Assert.Null(accel.Read());
    }

    [Fact]
    public void The_routine_remembers_the_objects_that_passed_through_newest_last_in_a_ring_of_16()
    {
        using var r = new NativeRoutine(liftZeroGains: true);
        r.Table();
        for (int i = 0; i < 18; i++) r.Run(1);
        Assert.Equal(18, Marshal.ReadInt32(r.Block + 0x38));                 // ring index counts every call
        for (int slot = 0; slot < 16; slot++)
            Assert.Equal((long)r.Player, Marshal.ReadInt64(r.Block + 0x180 + slot * 8));
        Assert.Equal(0, Marshal.ReadInt64(r.Block + 0x200 + 0));             // the table right after the ring is untouched
    }

    [Fact]
    public void An_older_routine_in_the_game_is_replaced_by_a_new_block_keeping_the_table_and_counters()
    {
        var (memory, profile, _) = Module_((int)SiteRva);
        var host = new FakeHost(memory);
        var accel = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000);
        accel.Set(Record(Player, 111), 5);
        var old = accel.Read()!;
        memory.TryWrite(old.Cave + 0x08, BitConverter.GetBytes(1792L));       // calls made by the old routine
        memory.TryWrite(old.Cave + 0x40 + 15, new byte[] { 0xCC });           // the old routine differs from the current one

        var fresh = new AcceleratedDevelopment(memory, host, profile, Module, 0x10000).Install();

        Assert.NotEqual(old.Cave, fresh.Cave);
        Assert.Equal([new AcceleratedPlayer(Player, 111, 5)], fresh.Players);
        Assert.Equal(1792, fresh.Calls);
        var site = memory.Read(Module + SiteRva, 8);
        Assert.Equal(fresh.Cave + 0x40, Module + SiteRva + 5 + (ulong)(long)BitConverter.ToInt32(site, 1));
        Assert.Equal(GameSiteBytes[..8], memory.Read(fresh.Cave + 0x18, 8));
    }
}
