using System.Runtime.InteropServices;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The regen routine is run on this machine's processor in a small harness that plays the game's part: it sets
/// <c>rsi</c> to a fake new player, jumps into the routine and returns <c>eax</c> (PA as the stolen instruction loaded it).
/// The site bytes were read from Steam <c>27.0.1+2494687</c> <c>game_plugin.dll</c> at RVA 0x30B3D2F:
/// <c>0F BF 86 66 02 00 00  B9 09 00 00 00</c> (<c>movsx eax, word [rsi+0x266]; mov ecx, 9</c>). Expected values are hand
/// calculations from the rules in the class summary.
/// </summary>
public class GoldenGenerationHookTests
{
    static readonly byte[] GameSiteBytes = [0x0F, 0xBF, 0x86, 0x66, 0x02, 0x00, 0x00, 0xB9, 0x09, 0x00, 0x00, 0x00];

    static OffsetProfile Profile() =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2494687-steam.json"));

    [Fact]
    public void Site_pattern_is_built_from_the_profile_and_matches_the_game_bytes()
    {
        Assert.Equal(GameSiteBytes, GoldenGenerationHook.OriginalPattern(Profile()));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate int Harness(nint player, nint record);

    const int Ca = 0x264, Pa = 0x266, ClubId = 680, NationId = 765, ContinentId = 3;

    sealed class NativeRoutine : IDisposable
    {
        const int HarnessOffset = GoldenGenerationHook.BlockSize;
        public readonly nint Block;
        readonly nint player, record, club, nation, continent;
        readonly Harness run;

        public NativeRoutine()
        {
            Block = VirtualAlloc(0, HarnessOffset + 0x1000, 0x3000, 0x40);
            if (Block == 0) throw new InvalidOperationException("VirtualAlloc failed");
            player = Alloc(0x400); record = Alloc(0x110); club = Alloc(0x20); nation = Alloc(0x100); continent = Alloc(0x20);
            // The generated-player record (rdi): club at +0x78, the club's nation at +0x68; the nation's continent at the
            // profile's nation continent offset (0xF0 on Steam 27.0.1); every object's id at +0xC.
            Marshal.WriteInt64(record, 0x78, club);
            Marshal.WriteInt64(record, 0x68, nation);
            Marshal.WriteInt64(nation, 0xF0, continent);
            Marshal.WriteInt32(club, 0xC, ClubId);
            Marshal.WriteInt32(nation, 0xC, NationId);
            Marshal.WriteInt32(continent, 0xC, ContinentId);

            ulong cave = (ulong)Block, harness = cave + HarnessOffset;
            // push rsi; push rdi; mov rsi,rcx; mov rdi,rdx; jmp routine
            var h = new List<byte> { 0x56, 0x57, 0x48, 0x89, 0xCE, 0x48, 0x89, 0xD7, 0xE9 };
            long toRoutine = (long)(cave + 0x40) - (long)(harness + (ulong)h.Count + 4);
            h.AddRange(BitConverter.GetBytes((int)toRoutine));
            ulong resume = harness + (ulong)h.Count;
            h.AddRange([0x5F, 0x5E, 0xC3]);                         // resume: pop rdi; pop rsi; ret

            var block = GoldenGenerationHook.BuildBlock(cave, resume - 7, GameSiteBytes[..7], Profile());
            Marshal.Copy(block, 0, Block, block.Length);
            Marshal.Copy(h.ToArray(), 0, (nint)harness, h.Count);
            run = Marshal.GetDelegateForFunctionPointer<Harness>((nint)harness);
        }

        static nint Alloc(int size)
        {
            nint at = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(at, i, 0);
            return at;
        }

        public nint Player => player;

        public void Settings(bool enabled, int min, int max, int wonderkid = 0, GoldenGenerationScope scope = GoldenGenerationScope.All, params int[] ids)
        {
            Marshal.WriteByte(Block + 0x28, enabled ? (byte)1 : (byte)0);
            Marshal.WriteInt16(Block + 0x2A, (short)min);
            Marshal.WriteInt16(Block + 0x2C, (short)(max - min + 1));
            Marshal.WriteByte(Block + 0x2E, (byte)wonderkid);
            Marshal.WriteByte(Block + 0x2F, (byte)scope);
            for (int i = 0; i < ids.Length; i++) Marshal.WriteInt32(Block + 0x300 + i * 4, ids[i]);
            Marshal.WriteInt32(Block + 0x30, ids.Length);
        }

        /// <summary>Gives the fake player CA and PA, runs the routine; returns (eax, CA, PA) afterwards.</summary>
        public (int Loaded, int Ca, int Pa) Run(short ca, short pa)
        {
            Marshal.WriteInt16(player, Ca, ca);
            Marshal.WriteInt16(player, Pa, pa);
            int loaded = run(player, record);
            return (loaded, Marshal.ReadInt16(player, Ca), Marshal.ReadInt16(player, Pa));
        }

        public long Calls => Marshal.ReadInt64(Block + 0x08);
        public long Raised => Marshal.ReadInt64(Block + 0x10);
        public int Wonderkids => Marshal.ReadInt32(Block + 0x3C);
        public long RingFirst => Marshal.ReadInt64(Block + 0x400);
        public long RingFirstClub => Marshal.ReadInt64(Block + 0x400 + 0x38);
        public nint Club => club;

        public void Dispose()
        {
            VirtualFree(Block, 0, 0x8000);
            Marshal.FreeHGlobal(player); Marshal.FreeHGlobal(record); Marshal.FreeHGlobal(club); Marshal.FreeHGlobal(nation); Marshal.FreeHGlobal(continent);
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern nint VirtualAlloc(nint address, nint size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFree(nint address, nint size, uint type);
    }

    [Fact]
    public void Switched_off_it_only_counts_and_logs_and_the_stolen_load_still_reads_PA()
    {
        using var r = new NativeRoutine();
        Assert.Equal((97, 60, 97), r.Run(60, 97));
        Assert.Equal(1, r.Calls);
        Assert.Equal(0, r.Raised);
        Assert.Equal((long)r.Player, r.RingFirst);
        Assert.Equal((long)r.Club, r.RingFirstClub);
    }

    [Fact]
    public void Switched_on_the_new_player_gets_a_PA_from_the_range_and_the_game_reads_the_new_value()
    {
        using var r = new NativeRoutine();
        r.Settings(true, 180, 180);
        Assert.Equal((180, 60, 180), r.Run(60, 97));
        Assert.Equal(1, r.Raised);
    }

    [Fact]
    public void Drawn_PAs_stay_inside_the_range()
    {
        using var r = new NativeRoutine();
        r.Settings(true, 150, 170);
        var seen = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            int pa = r.Run(40, 80).Pa;
            Assert.InRange(pa, 150, 170);
            seen.Add(pa);
        }
        Assert.True(seen.Count > 1, "the draw never varied");
    }

    [Fact]
    public void PA_is_never_set_below_CA()
    {
        using var r = new NativeRoutine();
        r.Settings(true, 100, 100);
        Assert.Equal(150, r.Run(150, 160).Pa);
    }

    [Theory]
    [InlineData(GoldenGenerationScope.Club, new[] { ClubId }, 190)]
    [InlineData(GoldenGenerationScope.Club, new[] { 1, 2, ClubId }, 190)]
    [InlineData(GoldenGenerationScope.Club, new[] { NationId }, 90)]      // a nation id is not a club id
    [InlineData(GoldenGenerationScope.Nation, new[] { NationId }, 190)]
    [InlineData(GoldenGenerationScope.Nation, new[] { ClubId }, 90)]
    [InlineData(GoldenGenerationScope.Continent, new[] { ContinentId }, 190)]
    [InlineData(GoldenGenerationScope.Continent, new[] { 5 }, 90)]
    public void A_scope_limits_which_regens_are_raised(GoldenGenerationScope scope, int[] ids, int expectedPa)
    {
        using var r = new NativeRoutine();
        r.Settings(true, 190, 190, 0, scope, ids);
        Assert.Equal(expectedPa, r.Run(50, 90).Pa);
    }

    [Theory]
    [InlineData(50, 180, 141)]   // gap 130 x 70% = 91
    [InlineData(50, 160, 110)]   // gap 110 x 55% = 60.5 -> 60
    [InlineData(40, 130, 76)]    // gap 90 x 40% = 36
    [InlineData(30, 100, 51)]    // gap 70 x 30% = 21
    [InlineData(130, 180, 130)]  // CA already at 70% of PA (126): left alone
    public void A_sure_wonderkid_moves_CA_toward_PA_by_the_band_rate(short ca, int pa, int expectedCa)
    {
        using var r = new NativeRoutine();
        r.Settings(true, pa, pa, wonderkid: 100);
        Assert.Equal(expectedCa, r.Run(ca, 10).Ca);
    }

    [Fact]
    public void Without_a_wonderkid_chance_CA_is_left_alone()
    {
        using var r = new NativeRoutine();
        r.Settings(true, 180, 180);
        Assert.Equal(50, r.Run(50, 90).Ca);
        Assert.Equal(0, r.Wonderkids);
    }

    // ---------- Install / settings on fake memory ----------

    const ulong Module = 0x7FF0_0000_0000, CodeRva = 0x1000, SiteRva = 0x1500;

    sealed class FakeHost(FakeMemory memory) : ICodePatchHost
    {
        public ulong Next = 0x7FF0_0100_0000;
        public int Pauses;
        public ulong AllocateNear(ulong near, int bytes) { ulong at = Next; memory.Map(at, bytes); Next += 0x100000; return at; }
        public uint Protect(ulong address, int bytes, uint protect) => 0x20;
        public IDisposable Pause() { Pauses++; return new Done(); }
        public void FlushCode(ulong address, int bytes) { }
        sealed class Done : IDisposable { public void Dispose() { } }
    }

    static (FakeMemory Memory, OffsetProfile Profile) Module_(params int[] siteRvas)
    {
        var memory = new FakeMemory();
        var header = memory.Map(Module, 0x400);
        BitConverter.GetBytes(0x80).CopyTo(header, 0x3C);
        BitConverter.GetBytes((ushort)1).CopyTo(header, 0x80 + 6);
        BitConverter.GetBytes((ushort)0).CopyTo(header, 0x80 + 20);
        int s = 0x80 + 24;
        BitConverter.GetBytes(0x2000u).CopyTo(header, s + 8);
        BitConverter.GetBytes((uint)CodeRva).CopyTo(header, s + 12);
        BitConverter.GetBytes(0x60000020u).CopyTo(header, s + 36);
        var code = memory.Map(Module + CodeRva, 0x2000);
        foreach (int rva in siteRvas) GameSiteBytes.CopyTo(code, rva - (int)CodeRva);
        return (memory, Profile());
    }

    [Fact]
    public void Install_puts_a_jump_at_the_site_switched_off_and_uninstall_puts_the_bytes_back()
    {
        var (memory, profile) = Module_((int)SiteRva);
        var host = new FakeHost(memory);
        var hook = new GoldenGenerationHook(memory, host, profile, Module, 0x10000);

        var state = hook.Install();
        ulong site = Module + SiteRva;
        var patched = memory.Read(site, 7);
        Assert.Equal(0xE9, patched[0]);
        Assert.Equal(state.Cave + 0x40, site + 5 + (ulong)(long)BitConverter.ToInt32(patched, 1));
        Assert.Equal(GameSiteBytes[7..], memory.Read(site + 7, 5));
        Assert.False(state.Settings.Enabled);
        Assert.Equal(state.Cave, hook.Install().Cave);            // a second install reuses the block

        hook.Uninstall();
        Assert.Equal(GameSiteBytes, memory.Read(site, GameSiteBytes.Length));
        Assert.Null(hook.Read());
    }

    [Fact]
    public void Configure_writes_the_settings_and_reads_them_back()
    {
        var (memory, profile) = Module_((int)SiteRva);
        var hook = new GoldenGenerationHook(memory, new FakeHost(memory), profile, Module, 0x10000);
        var state = hook.Configure(true, 150, 190, 25, GoldenGenerationScope.Club, [680, 1]);
        Assert.Equal((true, 150, 190, 25, GoldenGenerationScope.Club), (state.Settings.Enabled, state.Settings.MinPa, state.Settings.MaxPa, state.Settings.WonderkidChance, state.Settings.Scope));
        Assert.Equal([680, 1], state.Settings.Ids);
        Assert.False(hook.Configure(false, 150, 190, 25, GoldenGenerationScope.All, []).Settings.Enabled);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(150, 201, 0)]
    [InlineData(190, 150, 0)]
    [InlineData(100, 150, 101)]
    public void Bad_settings_are_refused_before_anything_is_written(int min, int max, int wonderkid)
    {
        var (memory, profile) = Module_((int)SiteRva);
        var hook = new GoldenGenerationHook(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<ArgumentOutOfRangeException>(() => hook.Configure(true, min, max, wonderkid, GoldenGenerationScope.All, []));
        Assert.Null(hook.Read());
    }

    [Theory]
    [InlineData(GoldenGenerationScope.All, 680)]        // a list without a scope
    [InlineData(GoldenGenerationScope.Nation)]          // a scope without a list
    public void A_scope_and_its_list_go_together(GoldenGenerationScope scope, params int[] ids)
    {
        var (memory, profile) = Module_((int)SiteRva);
        var hook = new GoldenGenerationHook(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<ArgumentException>(() => hook.Configure(true, 150, 190, 0, scope, ids));
        Assert.Null(hook.Read());
    }

    [Fact]
    public void A_site_that_appears_twice_is_refused()
    {
        var (memory, profile) = Module_((int)SiteRva, (int)SiteRva + 0x100);
        var hook = new GoldenGenerationHook(memory, new FakeHost(memory), profile, Module, 0x10000);
        Assert.Throws<InvalidOperationException>(() => hook.Install());
        Assert.Equal(GameSiteBytes, memory.Read(Module + SiteRva, GameSiteBytes.Length));
    }
}
