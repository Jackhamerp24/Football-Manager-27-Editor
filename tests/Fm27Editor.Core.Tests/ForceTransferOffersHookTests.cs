using System.Runtime.InteropServices;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Exercises <see cref="ForceTransferOffersHook"/> both on the host x64 CPU (running the assembled cave routine in
/// executable memory via a small Win64 harness) and across fake PE sections for install, configure, read and uninstall.
/// </summary>
public class ForceTransferOffersHookTests
{
    static readonly byte[] GameSiteBytes =
    [
        0x44, 0x0F, 0xB6, 0xAF, 0x00, 0x08, 0x00, 0x00,
        0x4C, 0x8B, 0x74, 0x24, 0x68,
        0x4D, 0x85, 0xF6,
    ];

    static OffsetProfile SteamProfile() =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2503222-steam.json"));

    static OffsetProfile GamePassProfile() =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2494687-gamepass.json"));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate int Harness(nint ctx, nint proposal);

    sealed class NativeRoutine : IDisposable
    {
        const int HarnessOffset = ForceTransferOffersHook.BlockSize;
        public readonly nint Block;
        readonly nint ctx, proposal, clubTeam, intlTeam, matchClub, otherClub;
        readonly Harness run;
        readonly OffsetProfile profile = SteamProfile();

        public NativeRoutine()
        {
            Block = VirtualAlloc(0, HarnessOffset + 0x1000, 0x3000, 0x40);
            if (Block == 0) throw new InvalidOperationException("VirtualAlloc failed");

            ctx = Alloc(0x900);
            proposal = Alloc(0x100);
            clubTeam = Alloc(0x80);
            intlTeam = Alloc(0x80);
            matchClub = Alloc(0x40);
            otherClub = Alloc(0x40);

            int buyerOff = profile.Proposals!.Buyer;
            int teamClubOff = profile.Contract!.TeamClub;
            int clubIdOff = profile.Club!.Id!.Value;

            Marshal.WriteInt32(matchClub, clubIdOff, 680);
            Marshal.WriteInt32(otherClub, clubIdOff, 999);

            // Normal club team: byte[team+0x2F] == 0
            Marshal.WriteByte(clubTeam, 0x2F, 0);
            Marshal.WriteInt64(clubTeam, teamClubOff, matchClub);

            // International / nation team: byte[team+0x2F] == 1, yet its nation+ClubIdOffset happens to equal 680
            Marshal.WriteByte(intlTeam, 0x2F, 1);
            Marshal.WriteInt64(intlTeam, teamClubOff, matchClub);

            Marshal.WriteInt64(proposal, buyerOff, clubTeam);

            ulong cave = (ulong)Block;
            ulong harness = cave + HarnessOffset;
            // push rdi; push r13; push rsi; push rbx; sub rsp,0x70; mov [rsp+0x68],rdx; mov rdi,rcx; jmp routine
            var h = new List<byte>
            {
                0x57, 0x41, 0x55, 0x56, 0x53,
                0x48, 0x83, 0xEC, 0x70,
                0x48, 0x89, 0x54, 0x24, 0x68,
                0x48, 0x89, 0xCF,
                0xE9,
            };
            long toRoutine = (long)(cave + 0x40) - (long)(harness + (ulong)h.Count + 4);
            h.AddRange(BitConverter.GetBytes((int)toRoutine));
            ulong resume = harness + (ulong)h.Count;
            // mov eax, r13d; add rsp,0x70; pop rbx; pop rsi; pop r13; pop rdi; ret
            h.AddRange([0x44, 0x89, 0xE8, 0x48, 0x83, 0xC4, 0x70, 0x5B, 0x5E, 0x41, 0x5D, 0x5F, 0xC3]);

            var block = ForceTransferOffersHook.BuildBlock(cave, resume - 8, GameSiteBytes[..8], profile);
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

        public nint Proposal => proposal;
        public nint ClubTeam => clubTeam;
        public nint IntlTeam => intlTeam;
        public nint MatchClub => matchClub;
        public nint OtherClub => otherClub;

        public void Configure(bool enabled, params int[] ids)
        {
            Marshal.WriteByte(Block + 0x28, enabled ? (byte)1 : (byte)0);
            Marshal.WriteInt32(Block + 0x2C, ids.Length);
            for (int i = 0; i < ids.Length; i++)
                Marshal.WriteInt32(Block + 0x300 + i * 4, ids[i]);
        }

        public void SetBuyerTeam(nint team) => Marshal.WriteInt64(proposal, profile.Proposals!.Buyer, team);
        public void SetTeamClub(nint club) => Marshal.WriteInt64(clubTeam, profile.Contract!.TeamClub, club);
        public void SetClubId(nint club, int id) => Marshal.WriteInt32(club, profile.Club!.Id!.Value, id);

        public int Invoke(byte initialStatus, nint proposalPtr)
        {
            Marshal.WriteByte(ctx, 0x800, initialStatus);
            return run(ctx, proposalPtr);
        }

        public long Calls => Marshal.ReadInt64(Block + 0x08);
        public long Forced => Marshal.ReadInt64(Block + 0x10);
        public uint RingIndex => (uint)Marshal.ReadInt32(Block + 0x30);

        public (ulong Proposal, ulong Club, int ClubId, byte Orig, byte New, bool Forced) LastRing()
        {
            uint idx = (RingIndex - 1) % ForceTransferOffersHook.RingSize;
            nint slot = Block + 0x400 + (int)(idx * ForceTransferOffersHook.RecordSize);
            return (
                (ulong)Marshal.ReadInt64(slot + 0x00),
                (ulong)Marshal.ReadInt64(slot + 0x08),
                Marshal.ReadInt32(slot + 0x10),
                Marshal.ReadByte(slot + 0x14),
                Marshal.ReadByte(slot + 0x15),
                Marshal.ReadByte(slot + 0x16) != 0);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(ctx);
            Marshal.FreeHGlobal(proposal);
            Marshal.FreeHGlobal(clubTeam);
            Marshal.FreeHGlobal(intlTeam);
            Marshal.FreeHGlobal(matchClub);
            Marshal.FreeHGlobal(otherClub);
            VirtualFree(Block, 0, 0x8000);
        }

        [DllImport("kernel32.dll")]
        static extern nint VirtualAlloc(nint addr, nuint size, uint type, uint protect);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool VirtualFree(nint addr, nuint size, uint type);
    }

    [Fact]
    public void Matching_human_club_offer_forces_status_to_3_and_logs_hit()
    {
        using var r = new NativeRoutine();
        r.Configure(true, 680);

        int result = r.Invoke(2, r.Proposal);

        Assert.Equal(3, result);
        Assert.Equal(1, r.Calls);
        Assert.Equal(1, r.Forced);
        var hit = r.LastRing();
        Assert.Equal(((ulong)r.Proposal, (ulong)r.MatchClub, 680, (byte)2, (byte)3, true), hit);
    }

    [Fact]
    public void Disabled_hook_leaves_original_status_intact()
    {
        using var r = new NativeRoutine();
        r.Configure(false, 680);

        int result = r.Invoke(5, r.Proposal);

        Assert.Equal(5, result);
        Assert.Equal(1, r.Calls);
        Assert.Equal(0, r.Forced);
        Assert.False(r.LastRing().Forced);
        Assert.Equal((byte)5, r.LastRing().New);
    }

    [Fact]
    public void Ai_club_offer_is_never_forced()
    {
        using var r = new NativeRoutine();
        r.Configure(true, 680);
        r.SetTeamClub(r.OtherClub);

        int result = r.Invoke(4, r.Proposal);

        Assert.Equal(4, result);
        Assert.Equal(1, r.Calls);
        Assert.Equal(0, r.Forced);
        Assert.Equal((999, false), (r.LastRing().ClubId, r.LastRing().Forced));
    }

    [Fact]
    public void International_team_is_excluded_even_when_id_collides()
    {
        using var r = new NativeRoutine();
        r.Configure(true, 680);
        r.SetBuyerTeam(r.IntlTeam);

        int result = r.Invoke(2, r.Proposal);

        Assert.Equal(2, result);
        Assert.Equal(0, r.Forced);
        Assert.Equal((0, false), (r.LastRing().ClubId, r.LastRing().Forced));
    }

    [Fact]
    public void Null_pointers_and_sentinel_ids_safely_no_op()
    {
        using var r = new NativeRoutine();
        r.Configure(true, 680, -1, 0);

        Assert.Equal(7, r.Invoke(7, 0));

        r.SetBuyerTeam(0);
        Assert.Equal(6, r.Invoke(6, r.Proposal));

        r.SetBuyerTeam(r.ClubTeam);
        r.SetTeamClub(0);
        Assert.Equal(5, r.Invoke(5, r.Proposal));

        r.SetTeamClub(r.MatchClub);
        r.SetClubId(r.MatchClub, -1);
        Assert.Equal(4, r.Invoke(4, r.Proposal));

        r.SetClubId(r.MatchClub, 0);
        Assert.Equal(2, r.Invoke(2, r.Proposal));
        Assert.Equal(0, r.Forced);
    }

    [Fact]
    public void Multi_club_list_and_excess_count_clamp_work()
    {
        using var r = new NativeRoutine();
        int[] ids = [11, 22, 680, 44];
        r.Configure(true, ids);
        Assert.Equal(3, r.Invoke(1, r.Proposal));

        // Corrupt IdCount in cave above MaxIds to prove the assembly clamps to 32 rather than over-reading.
        var many = Enumerable.Range(0, 35).Select(i => i == ForceTransferOffersHook.MaxIds ? 680 : i + 1000).ToArray();
        Marshal.WriteByte(r.Block + 0x28, 1);
        Marshal.WriteInt32(r.Block + 0x2C, many.Length);
        for (int i = 0; i < many.Length; i++)
            Marshal.WriteInt32(r.Block + 0x300 + i * 4, many[i]);
        Assert.Equal(1, r.Invoke(1, r.Proposal)); // 33rd element (index 32) is ignored past MaxIds
    }

    // ---------- Install / configure / uninstall on fake PE memory ----------

    const ulong Module = 0x7FF0_0000_0000, CodeRva = 0x1000, SiteRva = 0x1500;

    sealed class FakeHost(FakeMemory memory) : ICodePatchHost
    {
        public ulong Next = 0x7FF0_0100_0000;
        public ulong AllocateNear(ulong near, int bytes) { ulong at = Next; memory.Map(at, bytes); Next += 0x100000; return at; }
        public uint Protect(ulong address, int bytes, uint protect) => 0x20;
        public IDisposable Pause() => new Done();
        public void FlushCode(ulong address, int bytes) { }
        sealed class Done : IDisposable { public void Dispose() { } }
    }

    static FakeMemory Module_(params int[] siteRvas)
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
        return memory;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Install_configure_read_and_uninstall_work_across_both_build_profiles(bool useSteam)
    {
        var profile = useSteam ? SteamProfile() : GamePassProfile();
        var memory = Module_((int)SiteRva);
        var hook = new ForceTransferOffersHook(memory, new FakeHost(memory), profile, Module, 0x10000);

        var installed = hook.Install();
        Assert.False(installed.Enabled);
        Assert.Empty(installed.ClubIds);

        ulong site = Module + SiteRva;
        var patched = memory.Read(site, 16);
        Assert.Equal(0xE9, patched[0]);
        Assert.Equal([0x90, 0x90, 0x90], patched[5..8]);
        Assert.Equal(GameSiteBytes[8..], patched[8..16]);
        Assert.Equal(installed.Cave + 0x40, site + 5 + (ulong)(long)BitConverter.ToInt32(patched, 1));
        Assert.Equal(installed.Cave, hook.Install().Cave);

        var configured = hook.Configure(true, [680, 680, 0, -1, 123]);
        Assert.True(configured.Enabled);
        Assert.Equal([680, 123], configured.ClubIds);

        var off = hook.Configure(false, [680]);
        Assert.False(off.Enabled);
        Assert.Equal([680], off.ClubIds);

        hook.Uninstall();
        Assert.Equal(GameSiteBytes, memory.Read(site, GameSiteBytes.Length));
        Assert.Null(hook.Read());
    }

    [Fact]
    public void Missing_or_duplicate_sites_and_foreign_bytes_are_refused()
    {
        var profile = SteamProfile();

        var emptyMem = Module_();
        var missing = new ForceTransferOffersHook(emptyMem, new FakeHost(emptyMem), profile, Module, 0x10000);
        Assert.Throws<InvalidOperationException>(() => missing.FindSite());

        var dupMem = Module_((int)SiteRva, (int)SiteRva + 0x100);
        var dup = new ForceTransferOffersHook(dupMem, new FakeHost(dupMem), profile, Module, 0x10000);
        Assert.Throws<InvalidOperationException>(() => dup.Install());

        // Patching the site with someone else's jump is detected on Read/Install.
        var oneMem = Module_((int)SiteRva);
        var foreign = new ForceTransferOffersHook(oneMem, new FakeHost(oneMem), profile, Module, 0x10000);
        Assert.Equal(Module + SiteRva, foreign.FindSite());
        oneMem.TryWrite(Module + SiteRva, [0xE9, 0x01, 0x02, 0x03, 0x04, 0x90, 0x90, 0x90]);
        Assert.Throws<InvalidOperationException>(() => foreign.Read());
    }

    [Fact]
    public void More_than_32_club_ids_is_refused_before_installing()
    {
        var memory = Module_((int)SiteRva);
        var hook = new ForceTransferOffersHook(memory, new FakeHost(memory), SteamProfile(), Module, 0x10000);
        var tooMany = Enumerable.Range(1, ForceTransferOffersHook.MaxIds + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => hook.Configure(true, tooMany));
        Assert.Null(hook.Read());
    }
}
