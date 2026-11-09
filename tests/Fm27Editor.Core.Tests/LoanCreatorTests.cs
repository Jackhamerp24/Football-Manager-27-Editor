using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Creating a loan, laid out with the Game Pass 27.0.0+2493426 offsets. Expected values are the bytes FMRTE 27 wrote
/// (WriteProcessMemory log, 8 November 2026, game date 26/11/2026 = day 330) when it loaned Phil Foden Man City → Chelsea
/// (wage 50% of 150,000, price 5,000,000, buy-back 7,000,000, monthly 20,000, purchase after 3 matches) and Matheus Cunha
/// Man Utd → Tottenham (all defaults, Can Be Recalled ticked), plus where it took the template contract from.
/// </summary>
public class LoanCreatorTests
{
    const uint TeamVt = 0x4C90A78, ClubVt = 0x4CDA1F8, ContractVt = 0x4AC05F8, LoanVt = 0x4D78AF8;
    const ulong ParentTeam = 0x6002_0000, ParentSquad = 0x6003_0000, ParentClub = 0x6000_0000;
    const ulong BorrowTeam = 0x6102_0000, BorrowSquad = 0x6103_0000, BorrowClub = 0x6100_0000;
    const ulong OtherTeam = 0x6202_0000, OtherSquad = 0x6203_0000, OtherClub = 0x6200_0000;
    const ulong Contract = 0x6300_0000, TemplateHolder = 0x6400_0000, TemplateLoan = 0x6400_1000;
    const ulong OtherHolder = 0x6500_0000, OtherLoan = 0x6500_1000, OtherPlayer = 0x1000_3000;
    const ulong NewHolder = 0x6600_0000, NewContract = 0x6601_0000, NewSquad = 0x6602_0000;
    const int PlayerTeam = 0x130, PlayerContract = 0x330, LoanHolder = 0x338;

    readonly TestWorld _w = new();
    readonly OffsetProfile _profile = OffsetProfile.LoadFile(
        Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
    ulong _next = NewHolder;

    public LoanCreatorTests()
    {
        foreach (var club in new[] { ParentClub, BorrowClub, OtherClub })
        {
            var c = _w.Memory.Map(club, 0x160);
            TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        }
        Team(ParentTeam, ParentClub, ParentSquad, [TestWorld.Mainoo, TestWorld.Yamal], capacity: 4);
        Team(BorrowTeam, BorrowClub, BorrowSquad, [OtherPlayer], capacity: 4);
        Team(OtherTeam, OtherClub, OtherSquad, [], capacity: 4);

        var contract = _w.Memory.Map(Contract, 0x60);
        TestWorld.Set64(contract, 0, TestWorld.ModuleBase + ContractVt);
        TestWorld.Set64(contract, 0x10, ParentTeam);
        TestWorld.Set64(contract, 0x18, ParentTeam);
        BitConverter.GetBytes(150000).CopyTo(contract, 0x20);

        foreach (var p in new[] { TestWorld.Mainoo, TestWorld.Yamal })
            Assert.True(_w.Memory.TryWrite(p, BitConverter.GetBytes(TestWorld.ModuleBase + _profile.PlayerVtableRvas[0])));
        Set(TestWorld.Mainoo + PlayerContract, Contract);
        Set(TestWorld.Mainoo + PlayerTeam, ParentTeam);
        // Another player already on loan at BorrowTeam: the template FMRTE copies for that destination.
        Template(TemplateHolder, TemplateLoan, BorrowTeam, marker: 0xA1);
        Set(TestWorld.Yamal + LoanHolder, TemplateHolder);
        // A player on loan elsewhere, listed after Yamal: only used when the destination has no loan of its own.
        var other = _w.Memory.Map(OtherPlayer, 0x3C8);
        TestWorld.Set64(other, 0, TestWorld.ModuleBase + _profile.PlayerVtableRvas[0]);
    }

    void Template(ulong holder, ulong loan, ulong team, byte marker)
    {
        var h = _w.Memory.Map(holder, 0x20);
        TestWorld.Set64(h, 0, loan);
        var l = _w.Memory.Map(loan, 0x100);
        for (int i = 0; i < l.Length; i++) l[i] = (byte)(marker + i);             // every byte distinguishable
        TestWorld.Set64(l, 0, TestWorld.ModuleBase + LoanVt);
        TestWorld.Set64(l, 0x10, team);
        TestWorld.Set64(l, 0x18, team);
    }

    void Set(ulong at, ulong value) => Assert.True(_w.Memory.TryWrite(at, BitConverter.GetBytes(value)));
    ulong Q(ulong at) => BitConverter.ToUInt64(_w.Bytes(at, 8), 0);
    int I32(ulong at) => BitConverter.ToInt32(_w.Bytes(at, 4), 0);
    string Hex(ulong at, int n) => Convert.ToHexString(_w.Bytes(at, n)).ToLowerInvariant();

    void Team(ulong team, ulong club, ulong squad, ulong[] players, int capacity)
    {
        var t = _w.Memory.Map(team, 0x50);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + TeamVt);
        TestWorld.Set64(t, 0x30, club);
        TestWorld.Set64(t, 0x38, squad);
        TestWorld.Set64(t, 0x40, squad + (ulong)players.Length * 8);
        TestWorld.Set64(t, 0x48, squad + (ulong)capacity * 8);
        var s = _w.Memory.Map(squad, capacity * 8);
        for (int i = 0; i < players.Length; i++) TestWorld.Set64(s, i * 8, players[i]);
    }

    PlayerRecord Player(ulong address) => new PlayerRepository(_w.Memory, _profile, TestWorld.ModuleBase).Reload(address)!;
    LoanCreator Creator(IProcessMemory? memory = null) => new(memory ?? _w.Memory, _profile, TestWorld.ModuleBase);
    PlayerRecord[] All() => [Player(TestWorld.Mainoo), Player(TestWorld.Yamal), Player(OtherPlayer)];

    ulong Alloc(int bytes)
    {
        ulong at = _next;
        _next += 0x1_0000;
        _w.Memory.Map(at, Math.Max(bytes, 0x100));
        return at;
    }

    /// <summary>26/11/2026 is day 330, written as 4a01 + the year (FMRTE's bytes); 26/11/2027 is 4a01 eb07.</summary>
    static readonly DateOnly Today = new(2026, 11, 26), NextYear = new(2027, 11, 26);

    [Fact]
    public void Foden_to_Chelsea_style_loan_writes_the_contract_FMRTE_wrote()
    {
        var creator = Creator();
        var offer = new LoanOffer(LoanCreator.WageFromPercent(150000, 50), Today, NextYear, AgreedPrice: 5_000_000,
            BuyBackFee: 7_000_000, MonthlyFee: 20_000, PurchaseAfterMatches: 3);
        var plan = creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, offer, All());
        creator.Apply(plan, Alloc);

        ulong holder = Q(TestWorld.Mainoo + LoanHolder), contract = Q(holder);
        Assert.Equal(NewHolder, holder);
        Assert.Equal(NewContract, contract);
        Assert.Equal(TestWorld.ModuleBase + LoanVt, Q(contract));
        Assert.Equal(TestWorld.Mainoo + 0x288, Q(contract + 0x08));                  // person
        Assert.Equal((BorrowTeam, BorrowTeam), (Q(contract + 0x10), Q(contract + 0x18)));
        Assert.Equal("f8240100", Hex(contract + 0x20, 4));                           // wage 75,000
        Assert.Equal("f8240100", Hex(contract + 0xD4, 4));
        Assert.Equal("4a01ea07", Hex(contract + 0x44, 4));                           // started 26/11/2026
        Assert.Equal("4a01eb07", Hex(contract + 0x48, 4));                           // expires 26/11/2027
        Assert.Equal("4a01ea07", Hex(contract + 0x4C, 4));                           // signed
        Assert.Equal("404b4c00", Hex(contract + 0xC0, 4));                           // 5,000,000
        Assert.Equal("c0cf6a00", Hex(contract + 0xC8, 4));                           // 7,000,000
        Assert.Equal("204e0000", Hex(contract + 0xCC, 4));                           // monthly 20,000
        Assert.Equal("204e0000", Hex(contract + 0xD0, 4));
        Assert.Equal("03", Hex(contract + 0xF7, 1));
        Assert.Equal("02000000", Hex(contract + 0xB0, 4));
        Assert.Equal("00000000", Hex(contract + 0xB4, 4));

        // Bytes FMRTE does not touch come from the template contract (marker 0xA1 + offset).
        foreach (int at in new[] { 0x28, 0x54, 0x58, 0x5C, 0x60, 0xE0, 0xE8, 0xF4, 0xF8 })
            Assert.Equal((byte)(0xA1 + at), _w.Bytes(contract + (ulong)at, 1)[0]);

        Assert.Equal((BorrowTeam, ParentTeam), (Q(TestWorld.Mainoo + PlayerTeam), Q(TestWorld.Mainoo + 0x138)));
        Assert.Equal("4a01ea07", Hex(TestWorld.Mainoo + 0x3A4, 4));
        Assert.Equal("4a01ea07", Hex(TestWorld.Mainoo + 0x3A8, 4));
        Assert.Equal(new[] { OtherPlayer, TestWorld.Mainoo }, new[] { Q(BorrowSquad), Q(BorrowSquad + 8) });
        Assert.Equal(BorrowSquad + 16, Q(BorrowTeam + 0x40));
        // The parent squad and the player's own contract are left alone.
        Assert.Equal(new[] { TestWorld.Mainoo, TestWorld.Yamal }, new[] { Q(ParentSquad), Q(ParentSquad + 8) });
        Assert.Equal(ParentSquad + 16, Q(ParentTeam + 0x40));
        Assert.Equal(150000, I32(Contract + 0x20));
        Assert.Equal(ParentTeam, Q(Contract + 0x10));

        // The loan reads back through the loan editor.
        var read = new LoanEditor(_w.Memory, _profile, TestWorld.ModuleBase).Read(Player(TestWorld.Mainoo))!;
        Assert.Equal((75000, Today, NextYear, 5_000_000, 7_000_000, 20_000), (read.Terms.Wage, read.Terms.Started, read.Terms.Expires,
            read.Terms.AgreedPrice, read.Terms.BuyBackFee, read.Terms.MonthlyFee));
    }

    [Fact]
    public void Defaults_store_minus_one_prices_and_0xFE_purchase_matches_and_recall_sets_bit_two()
    {
        var creator = Creator();
        var plan = creator.Plan(Player(TestWorld.Mainoo), BorrowTeam,
            new LoanOffer(150000, Today, NextYear, CanBeRecalled: true), All());
        creator.Apply(plan, Alloc);
        ulong contract = Q(Q(TestWorld.Mainoo + LoanHolder));
        Assert.Equal("ffffffff", Hex(contract + 0xC0, 4));
        Assert.Equal("ffffffff", Hex(contract + 0xC8, 4));
        Assert.Equal("00000000", Hex(contract + 0xCC, 4));
        Assert.Equal("00000000", Hex(contract + 0xD0, 4));
        Assert.Equal("fe", Hex(contract + 0xF7, 1));
        Assert.Equal("06000000", Hex(contract + 0xB0, 4));
    }

    [Fact]
    public void Template_is_the_first_loan_of_the_destination_else_the_first_loan_of_the_game()
    {
        // OtherPlayer's loan sits after Yamal's in the player order and borrows for OtherTeam.
        Template(OtherHolder, OtherLoan, OtherTeam, marker: 0x33);
        Set(OtherPlayer + LoanHolder, OtherHolder);
        var creator = Creator();

        var forBorrow = creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, Today, NextYear), All());
        Assert.Equal((TemplateLoan, true), (forBorrow.TemplateContract, forBorrow.TemplateFromBorrowingTeam));
        var forOther = creator.Plan(Player(TestWorld.Mainoo), OtherTeam, new LoanOffer(1, Today, NextYear), All());
        Assert.Equal((OtherLoan, true), (forOther.TemplateContract, forOther.TemplateFromBorrowingTeam));

        // A team with no loan at all (Chelsea in the capture) copies the first loan found: Yamal's, listed first.
        var third = 0x6700_0000UL;
        Team(third, BorrowClub, 0x6703_0000, [], 2);
        var fallback = creator.Plan(Player(TestWorld.Mainoo), third, new LoanOffer(1, Today, NextYear), All());
        Assert.Equal((TemplateLoan, false), (fallback.TemplateContract, fallback.TemplateFromBorrowingTeam));
        Assert.Equal(third, BitConverter.ToUInt64(fallback.Contract, 0x10));
        Assert.Equal((byte)(0xA1 + 0x28), fallback.Contract[0x28]);
    }

    [Fact]
    public void A_full_squad_gets_a_new_buffer_through_the_allocator()
    {
        var full = 0x6800_0000UL;
        Team(full, BorrowClub, 0x6803_0000, [Mainoo(1), Mainoo(2)], capacity: 2);
        var creator = Creator();
        var plan = creator.Plan(Player(TestWorld.Mainoo), full, new LoanOffer(1, Today, NextYear), All());
        Assert.True(plan.NeedsAllocation);
        creator.Apply(plan, Alloc);
        ulong buffer = Q(full + 0x38);
        Assert.NotEqual(0x6803_0000UL, buffer);
        Assert.Equal(buffer + 24, Q(full + 0x40));
        Assert.True(Q(full + 0x48) >= Q(full + 0x40));
        Assert.Equal(new[] { Mainoo(1), Mainoo(2), TestWorld.Mainoo }, new[] { Q(buffer), Q(buffer + 8), Q(buffer + 16) });
        Assert.Equal(full, Q(TestWorld.Mainoo + PlayerTeam));

        static ulong Mainoo(int n) => 0x7000_0000UL + (ulong)n;
    }

    [Fact]
    public void Refuses_a_player_who_already_has_a_loan_record_the_contract_team_and_an_empty_game()
    {
        var creator = Creator();
        Set(TestWorld.Mainoo + LoanHolder, TemplateHolder);
        Assert.Throws<InvalidOperationException>(() => creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, Today, NextYear), All()));
        Set(TestWorld.Mainoo + LoanHolder, 0);
        Assert.Throws<InvalidOperationException>(() => creator.Plan(Player(TestWorld.Mainoo), ParentTeam, new LoanOffer(1, Today, NextYear), All()));
        Assert.Throws<StaleObjectException>(() => creator.Plan(Player(TestWorld.Mainoo), 0x6900_0000, new LoanOffer(1, Today, NextYear), All()));
        Assert.Throws<ArgumentOutOfRangeException>(() => creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, NextYear, Today), All()));
        Set(TestWorld.Yamal + LoanHolder, 0);
        Assert.Throws<InvalidOperationException>(() => creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, Today, NextYear), All()));
    }

    [Fact]
    public void Apply_writes_nothing_when_the_game_changed_after_the_plan()
    {
        var creator = Creator();
        var plan = creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, Today, NextYear), All());
        Set(TestWorld.Mainoo + PlayerTeam, OtherTeam);                               // the player moved meanwhile
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => creator.Apply(plan, Alloc));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(0UL, Q(TestWorld.Mainoo + LoanHolder));
    }

    [Fact]
    public void A_failed_write_puts_back_what_was_already_replaced()
    {
        var failing = new FailingMemory(_w.Memory, TestWorld.Mainoo + LoanHolder);
        var creator = Creator(failing);
        var plan = creator.Plan(Player(TestWorld.Mainoo), BorrowTeam, new LoanOffer(1, Today, NextYear), All());
        Assert.Throws<MemoryAccessException>(() => creator.Apply(plan, Alloc));
        Assert.Equal(0UL, Q(TestWorld.Mainoo + LoanHolder));
        Assert.Equal(ParentTeam, Q(TestWorld.Mainoo + PlayerTeam));
        Assert.Equal(0UL, Q(TestWorld.Mainoo + 0x138));
        Assert.Equal("00000000", Hex(TestWorld.Mainoo + 0x3A4, 4));
        Assert.Equal(BorrowSquad + 8, Q(BorrowTeam + 0x40));
    }

    sealed class FailingMemory(IProcessMemory inner, ulong failAt) : IProcessMemory
    {
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);
        public bool TryWrite(ulong address, ReadOnlySpan<byte> data) => address != failAt && inner.TryWrite(address, data);
    }
}
