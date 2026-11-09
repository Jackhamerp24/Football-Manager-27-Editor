using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// One loan laid out with the Game Pass 27.0.0+2493426 offsets: player +0x130 playing team, +0x330 contract,
/// +0x338 loan holder, contract team +0x10 / senior team +0x18, squad vector +0x38; loan contract vtable 0x4D78AF8
/// with wage +0x20, started +0x44, expires +0x48, sign date +0x4C and money at +0xC0/+0xC8/+0xCC/+0xD0.
/// Every expected byte is worked out by hand; the loan contract values are the ones read from Mark Shelton's loan
/// (York → Grimsby) on 8 November 2026: wage 2250, 13/7/2026 to 31/5/2027, -1 agreed, -1 buy-back, monthly 2222.
/// </summary>
public class LoanEditorTests
{
    const uint TeamVt = 0x4C90A78, ClubVt = 0x4CDA1F8, ContractVt = 0x4AC05F8, LoanVt = 0x4D78AF8;
    const ulong ParentClub = 0x6000_0000, ParentTeam = 0x6002_0000, ParentSquad = 0x6003_0000;
    const ulong BorrowClub = 0x6100_0000, BorrowTeam = 0x6102_0000, BorrowSquad = 0x6103_0000;
    const ulong Holder = 0x6200_0000, Loan = 0x6200_1000, Contract = 0x6300_0000;
    const ulong Other = 0x6400_0000, NewBuffer = 0x6500_0000;
    const int PlayerTeam = 0x130, PlayerContract = 0x330, LoanHolder = 0x338;

    readonly TestWorld _w = new();
    readonly OffsetProfile _profile = OffsetProfile.LoadFile(
        Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));

    public LoanEditorTests()
    {
        Club(ParentClub);
        Club(BorrowClub);
        Team(ParentTeam, ParentClub, ParentSquad, [Other, Other + 8], capacity: 4);
        Team(BorrowTeam, BorrowClub, BorrowSquad, [Other, TestWorld.Mainoo, Other + 8], capacity: 4);
        var contract = _w.Memory.Map(Contract, 0x60);
        TestWorld.Set64(contract, 0, TestWorld.ModuleBase + ContractVt);
        TestWorld.Set64(contract, 0x10, ParentTeam);
        TestWorld.Set64(contract, 0x18, ParentTeam);
        BitConverter.GetBytes(3000).CopyTo(contract, 0x20);

        var loan = _w.Memory.Map(Loan, 0xD4);
        TestWorld.Set64(loan, 0, TestWorld.ModuleBase + LoanVt);
        BitConverter.GetBytes(2250).CopyTo(loan, 0x20);                  // wage paid while on loan
        PutDate(loan, 0x44, new DateOnly(2026, 7, 13));
        PutDate(loan, 0x48, new DateOnly(2027, 5, 31));
        PutDate(loan, 0x4C, new DateOnly(2026, 7, 13));                  // signed
        BitConverter.GetBytes(-1).CopyTo(loan, 0xC0);                    // agreed price: none
        BitConverter.GetBytes(-1).CopyTo(loan, 0xC8);                    // buy-back: none
        BitConverter.GetBytes(2222).CopyTo(loan, 0xCC);                  // playing monthly fee
        BitConverter.GetBytes(2222).CopyTo(loan, 0xD0);                  // unused monthly fee

        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + _profile.PlayerVtableRvas[0])));
        Set(TestWorld.Mainoo + PlayerContract, Contract);
        Set(TestWorld.Mainoo + PlayerTeam, BorrowTeam);
        Set(TestWorld.Mainoo + LoanHolder, Holder);
        var holder = _w.Memory.Map(Holder, 8);
        TestWorld.Set64(holder, 0, Loan);
    }

    void Set(ulong at, ulong value) => Assert.True(_w.Memory.TryWrite(at, BitConverter.GetBytes(value)));
    ulong Q(ulong at) => BitConverter.ToUInt64(_w.Bytes(at, 8), 0);
    int I32(ulong at) => BitConverter.ToInt32(_w.Bytes(at, 4), 0);

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    LoanEditor Editor() => new(_w.Memory, _profile, TestWorld.ModuleBase);

    void Club(ulong club)
    {
        var c = _w.Memory.Map(club, 0x160);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        BitConverter.GetBytes(1).CopyTo(c, 0xC);
    }

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

    static void PutDate(byte[] b, int at, DateOnly d)
    {
        short raw = BitConverter.ToInt16(b, at);
        BitConverter.GetBytes((short)((raw & ~0x1FF) | d.DayOfYear)).CopyTo(b, at);
        BitConverter.GetBytes((short)d.Year).CopyTo(b, at + 2);
    }

    [Fact]
    public void Read_returns_the_terms_and_endpoints_of_the_active_loan()
    {
        int writes = _w.Memory.Writes;
        var loan = Editor().Read(Player())!;
        Assert.Equal((Contract, Holder, Loan), (loan.ContractAddress, loan.Holder, loan.LoanContractAddress));
        Assert.Equal(TestWorld.Mainoo + (ulong)LoanHolder, loan.HolderSlot);
        Assert.Equal((BorrowTeam, ParentTeam), (loan.BorrowingTeam, loan.ParentTeam));
        Assert.Equal(2250, loan.Terms.Wage);
        Assert.Equal((new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 13), new DateOnly(2027, 5, 31)),
            (loan.Terms.Started, loan.Terms.Signed, loan.Terms.Expires));
        Assert.Equal((-1, -1, 2222, 2222),
            (loan.Terms.AgreedPrice, loan.Terms.BuyBackFee, loan.Terms.MonthlyFee, loan.Terms.UnusedMonthlyFee));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Read_is_null_when_the_holder_or_its_vtable_is_wrong()
    {
        Set(TestWorld.Mainoo + LoanHolder, 0);
        Assert.Null(Editor().Read(Player()));
        Set(TestWorld.Mainoo + LoanHolder, Holder);
        Assert.True(_w.Memory.TryWrite(Loan, BitConverter.GetBytes(TestWorld.ModuleBase + 0x123456UL)));
        Assert.Null(Editor().Read(Player()));
    }

    [Fact]
    public void SetTerms_writes_wage_dates_and_money_and_keeps_the_flag_bits()
    {
        // A flag in the started date's upper bits, as the game keeps them (0x2000 seen on offered contracts).
        Assert.True(_w.Memory.TryWrite(Loan + 0x44, BitConverter.GetBytes((short)(194 | 0x2000))));
        var editor = Editor();
        var loan = editor.Read(Player())!;

        editor.SetTerms(loan, new LoanTermsEdit(Wage: 1800, Started: new DateOnly(2026, 8, 1),
            Expires: new DateOnly(2027, 1, 31), Signed: new DateOnly(2026, 7, 20),
            AgreedPrice: 500000, BuyBackFee: 750000, MonthlyFee: 0, UnusedMonthlyFee: 0));

        var raw = _w.Bytes(Loan, 0xD4);
        Assert.Equal(1800, BitConverter.ToInt32(raw, 0x20));
        Assert.Equal(2026, BitConverter.ToInt16(raw, 0x46));
        Assert.Equal(213 | 0x2000, BitConverter.ToInt16(raw, 0x44));      // 1 August is day 213, flag kept
        Assert.Equal(2027, BitConverter.ToInt16(raw, 0x4A));
        Assert.Equal(31, BitConverter.ToInt16(raw, 0x48) & 0x1FF);         // 31 January
        Assert.Equal(201, BitConverter.ToInt16(raw, 0x4C) & 0x1FF);        // 20 July
        Assert.Equal((500000, 750000, 0, 0),
            (BitConverter.ToInt32(raw, 0xC0), BitConverter.ToInt32(raw, 0xC8), BitConverter.ToInt32(raw, 0xCC), BitConverter.ToInt32(raw, 0xD0)));
        Assert.Equal(TestWorld.ModuleBase + LoanVt, BitConverter.ToUInt64(raw, 0));

        var after = editor.Read(Player())!;
        Assert.Equal((1800, new DateOnly(2026, 8, 1), new DateOnly(2027, 1, 31)),
            (after.Terms.Wage, after.Terms.Started, after.Terms.Expires));
    }

    [Fact]
    public void SetTerms_refuses_a_changed_contract_and_writes_nothing()
    {
        var editor = Editor();
        var loan = editor.Read(Player())!;
        Assert.True(_w.Memory.TryWrite(Loan + 0x80, BitConverter.GetBytes(1)));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => editor.SetTerms(loan, new LoanTermsEdit(Wage: 1)));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(2250, I32(Loan + 0x20));
    }

    [Fact]
    public void SetTerms_rejects_an_end_before_the_start_and_a_negative_wage()
    {
        var editor = Editor();
        var loan = editor.Read(Player())!;
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.SetTerms(loan, new LoanTermsEdit(Started: new DateOnly(2027, 1, 1), Expires: new DateOnly(2026, 1, 1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetTerms(loan, new LoanTermsEdit(Wage: -1)));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Recall_plays_for_the_contract_team_and_swaps_the_squad_vectors()
    {
        var session = Editor();
        var plan = session.PlanRecall(Player());
        Assert.False(plan.NeedsAllocation);
        Assert.Equal((ParentTeam, BorrowTeam, 1), (plan.ParentTeam, plan.BorrowingTeam, plan.SquadIndex));
        session.ApplyRecall(plan);

        Assert.Equal(ParentTeam, Q(TestWorld.Mainoo + PlayerTeam));
        Assert.Equal(0UL, Q(TestWorld.Mainoo + LoanHolder));
        Assert.Equal(new[] { Other, Other + 8, TestWorld.Mainoo },
            new[] { Q(ParentSquad), Q(ParentSquad + 8), Q(ParentSquad + 16) });
        Assert.Equal(ParentSquad + 24, Q(ParentTeam + 0x40));
        Assert.Equal(new[] { Other, Other + 8 }, new[] { Q(BorrowSquad), Q(BorrowSquad + 8) });
        Assert.Equal(BorrowSquad + 16, Q(BorrowTeam + 0x40));
        Assert.Null(session.Read(Player()));
    }

    [Fact]
    public void Recall_grows_a_full_parent_squad_through_the_allocator()
    {
        Set(ParentTeam + 0x40, ParentSquad + 16);                          // two players, capacity two
        Set(ParentTeam + 0x48, ParentSquad + 16);
        var session = Editor();
        var plan = session.PlanRecall(Player());
        Assert.True(plan.NeedsAllocation);
        session.ApplyRecall(plan, bytes => { _w.Memory.Map(NewBuffer, bytes); return NewBuffer; });

        Assert.Equal(NewBuffer, Q(ParentTeam + 0x38));
        Assert.Equal(NewBuffer + 24, Q(ParentTeam + 0x40));
        Assert.Equal(NewBuffer + 48, Q(ParentTeam + 0x48));
        Assert.Equal(new[] { Other, Other + 8, TestWorld.Mainoo }, new[] { Q(NewBuffer), Q(NewBuffer + 8), Q(NewBuffer + 16) });
        Assert.Equal(ParentTeam, Q(TestWorld.Mainoo + PlayerTeam));
    }

    [Fact]
    public void Recall_refuses_a_player_who_is_not_in_the_borrowing_squad()
    {
        Set(BorrowSquad + 8, Other + 16);
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Editor().PlanRecall(Player()));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Recall_refuses_a_player_who_already_plays_for_his_contract_team()
    {
        Set(TestWorld.Mainoo + PlayerTeam, ParentTeam);
        Assert.Throws<InvalidOperationException>(() => Editor().PlanRecall(Player()));
    }

    [Fact]
    public void Recall_writes_nothing_when_an_object_changed_after_planning()
    {
        var session = Editor();
        var plan = session.PlanRecall(Player());
        Set(TestWorld.Mainoo + PlayerTeam, Other);
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => session.ApplyRecall(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(BorrowSquad + 24, Q(BorrowTeam + 0x40));
        Assert.Equal(Holder, Q(TestWorld.Mainoo + LoanHolder));
    }
}
