using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Moving a free agent, checked against what FMRTE 27 build12 wrote on Steam 27.0.1+2503222 (Frida WriteProcessMemory log,
/// 14/11/2026, game date 15/12/2026 = day 349):
/// <list type="bullet">
/// <item>Move now: Lamine Guèye to Man City. FMRTE copied the contract of the first player in the Man City squad, then wrote
/// person +0x08, the start and signed dates +0x44/+0x4C (5d01 ea07), the player's contract pointer, teams +0x130/+0x138,
/// the dates +0x3A4/+0x3A8, and appended him to the squad.</item>
/// <item>Future transfer: Coutinho to Liverpool (wage 192,963) and Benzema to Arsenal (196,892), dated 16/12/2026.</item>
/// </list>
/// </summary>
public class FreeAgentMoveTests
{
    const ulong Club = 0x6000_0000, Team = 0x6002_0000, Squad = 0x6003_0000, TemplatePlayer = 0x1000_5000, TemplateContract = 0x6300_0000;
    const int PlayerTeam = 0x130, PlayerContract = 0x330;
    static readonly DateOnly Today = new(2026, 12, 15);

    readonly TestWorld _w = new();
    readonly OffsetProfile _profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2503222-steam.json"));
    ulong _next = 0x6600_0000;

    /// <summary>The contract FMRTE copied (Man City's first squad player), as it wrote it before patching; pointers replaced below.</summary>
    const string ManCityTemplate =
        "084bb2ddfa7f0000983e565ee9010000" + "d8c3f854e9010000d8c3f854e9010000" + "a08601000b0001000000000000000000" +
        "0000000000000000403d0000ffffffff" + "ffffffffc21aea07b600ec07c21aea07" + "01006c071111110000003cfb1a1c000a" +
        "000a0000000000000000000000000000" + "00000000000000000000000000000000" + "00000000000000000000000000000000" +
        "00000000000000000000000000000000" + "00000000000000008067180000000000" + "0000000001006c07000000006c070000" +
        "00000001ff000000";

    public FreeAgentMoveTests()
    {
        _w.Memory.Map(Club, 0x160);
        var t = _w.Memory.Map(Team, 0x50);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + _profile.Team!.VtableRva);
        TestWorld.Set64(t, 0x30, Club);
        TestWorld.Set64(t, 0x38, Squad);
        TestWorld.Set64(t, 0x40, Squad + 16);
        TestWorld.Set64(t, 0x48, Squad + 32);
        var s = _w.Memory.Map(Squad, 32);
        TestWorld.Set64(s, 0, TemplatePlayer);
        TestWorld.Set64(s, 8, TestWorld.Yamal);

        var template = _w.Memory.Map(TemplatePlayer, 0x3C8);
        TestWorld.Set64(template, 0, TestWorld.ModuleBase + _profile.PlayerVtableRvas[0]);
        TestWorld.Set64(template, PlayerContract, TemplateContract);
        var c = _w.Memory.Map(TemplateContract, 200);
        Convert.FromHexString(ManCityTemplate).CopyTo(c, 0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + _profile.Contract!.ContractVtableRva);
        TestWorld.Set64(c, 0x10, Team);
        TestWorld.Set64(c, 0x18, Team);

        // Mainoo plays the free agent: no contract, no team.
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + _profile.PlayerVtableRvas[0])));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + PlayerContract, new byte[8]));
    }

    ClubEntry City(int squadCount = 2, int capacity = 4) =>
        new(Club, 43, "Manchester City", "Man City", 0, [new TeamEntry(Team, Club, Squad, squadCount, capacity, true)], Gender.Men);

    PlayerRecord Free() => new PlayerRepository(_w.Memory, _profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    FreeAgentSigning Signing() => new(_w.Memory, _profile, TestWorld.ModuleBase);
    ulong Q(ulong at) => BitConverter.ToUInt64(_w.Bytes(at, 8));
    string Hex(ulong at, int n) => Convert.ToHexString(_w.Bytes(at, n)).ToLowerInvariant();

    ulong Alloc(int bytes)
    {
        ulong at = _next;
        _next += 0x1_0000;
        _w.Memory.Map(at, Math.Max(bytes, 0x100));
        return at;
    }

    [Fact]
    public void Move_now_copies_the_first_squad_contract_and_changes_only_what_FMRTE_changed()
    {
        var plan = Signing().Plan(Free(), City(), Today);
        Assert.Equal(100_000, plan.Wage);                      // Man City's first player's wage, as FMRTE kept it
        Signing().Apply(plan, Alloc);

        ulong contract = Q(TestWorld.Mainoo + PlayerContract);
        Assert.Equal(0x6600_0000UL, contract);
        var expected = Convert.FromHexString(ManCityTemplate);
        BitConverter.GetBytes(TestWorld.ModuleBase + _profile.Contract!.ContractVtableRva).CopyTo(expected, 0);
        BitConverter.GetBytes(TestWorld.Mainoo + 0x288).CopyTo(expected, 0x08);
        BitConverter.GetBytes(Team).CopyTo(expected, 0x10);
        BitConverter.GetBytes(Team).CopyTo(expected, 0x18);
        Convert.FromHexString("5d01ea07").CopyTo(expected, 0x44);   // FMRTE: 5d01 ea07, the template's flag bits (c21a) dropped
        Convert.FromHexString("5d01ea07").CopyTo(expected, 0x4C);
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(_w.Bytes(contract, 200)));

        Assert.Equal((Team, Team), (Q(TestWorld.Mainoo + PlayerTeam), Q(TestWorld.Mainoo + 0x138)));
        Assert.Equal("5d01ea07", Hex(TestWorld.Mainoo + 0x3A4, 4));
        Assert.Equal("5d01ea07", Hex(TestWorld.Mainoo + 0x3A8, 4));
        Assert.Equal(new[] { TemplatePlayer, TestWorld.Yamal, TestWorld.Mainoo }, new[] { Q(Squad), Q(Squad + 8), Q(Squad + 16) });
        Assert.Equal(Squad + 24, Q(Team + 0x40));
        // The copied player's own contract is untouched.
        Assert.Equal(TemplateContract, Q(TemplatePlayer + PlayerContract));
        Assert.Equal(0x1E95E563E98UL, Q(TemplateContract + 0x08));
    }

    [Fact]
    public void Move_now_into_a_full_squad_moves_the_squad_to_a_bigger_buffer()
    {
        Assert.True(_w.Memory.TryWrite(Team + 0x48, BitConverter.GetBytes(Squad + 16)));
        var plan = Signing().Plan(Free(), City(capacity: 2), Today);
        Assert.True(plan.NeedsAllocation);
        Signing().Apply(plan, Alloc);
        ulong begin = Q(Team + 0x38);
        Assert.NotEqual(Squad, begin);
        Assert.Equal(new[] { TemplatePlayer, TestWorld.Yamal, TestWorld.Mainoo }, new[] { Q(begin), Q(begin + 8), Q(begin + 16) });
        Assert.Equal((begin + 24, begin + 48), (Q(Team + 0x40), Q(Team + 0x48)));
    }

    [Fact]
    public void A_player_with_a_contract_is_refused()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + PlayerContract, BitConverter.GetBytes(TemplateContract)));
        var ex = Assert.Throws<InvalidOperationException>(() => Signing().Plan(Free(), City(), Today));
        Assert.Contains("has a contract", ex.Message);
    }

    [Fact]
    public void Nothing_is_written_when_the_squad_changes_after_planning()
    {
        var plan = Signing().Plan(Free(), City(), Today);
        Assert.True(_w.Memory.TryWrite(Squad + 8, BitConverter.GetBytes(0x1000_9000UL)));
        int before = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Signing().Apply(plan, Alloc));
        Assert.Equal(before, _w.Memory.Writes);
        Assert.Equal(0UL, Q(TestWorld.Mainoo + PlayerContract));
    }

    /// <summary>The 200 bytes FMRTE wrote for Coutinho's and Benzema's future contracts, rebuilt from the wage alone.</summary>
    [Theory]
    [InlineData(0x1E95EA29BA8UL, 0x1E954F8C1B0UL, 192_963,
        "084bb2ddfa7f0000a89ba25ee9010000b0c1f854e9010000b0c1f854e9010000c3f10200000001000000000000200000000000000000000000000000ffffffffffffffff5e01ea07b500ed0701006c0701006c070300010000005e5c14ff00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005d01ea0700000000ea07000000000001ff000000")]
    [InlineData(0x1E95E49D290UL, 0x1E954F88D38UL, 196_892,
        "084bb2ddfa7f000090d2495ee9010000388df854e9010000388df854e90100001c010300000001000000000000200000000000000000000000000000ffffffffffffffff5e01ea07b500ed0701006c0701006c070300010000005e5c14ff00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005d01ea0700000000ea07000000000001ff000000")]
    public void Free_agent_future_contract_matches_FMRTE(ulong person, ulong team, int wage, string fmrte)
    {
        const ulong vtable = 0x00007FFADDB24B08;
        var source = FutureTransferCreator.FreeAgentContract(vtable, person, wage);
        var built = FutureTransferContractTemplate.Build(source, team, Today, new DateOnly(2026, 12, 16), 0, 0, 0, 0);
        Assert.Equal(fmrte, Convert.ToHexString(built).ToLowerInvariant());
    }

    [Fact]
    public void Free_agent_future_offer_has_no_seller()
    {
        var offer = FutureTransferRecordTemplate.Offer(0x7FFADDF71CD8, 0x1E95EA29BA8, 0x1E954F8C1B0, 0, 0, 0x22CB, 0x2842,
            0x1E96EE78CA0, Today, new DateOnly(2026, 12, 16));
        Assert.Equal(0UL, BitConverter.ToUInt64(offer, 0x50));
    }
}
