using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class CurrentLoanTests
{
    // Live Game Pass loan records for Sekou Kone and Pape Matar Sarr, 8 November 2026.
    [Theory]
    [InlineData("F88A85F1FA7F000030921A462402000060842E372402000060842E3724020000801F0000000001000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFC21AEA07B500EB07C21AEA0701006C07", 8064)]
    [InlineData("F88A85F1FA7F000000C6194624020000580F2D3724020000580F2D3724020000CA660100000001000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFC21AEA07B500EB07C21AEA0701006C07", 91850)]
    public void Reads_existing_loan_from_captured_contract(string hex, int wage)
    {
        var w = new TestWorld();
        var player = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + profile.PlayerVtableRvas[0])));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x338, BitConverter.GetBytes(0x50000000UL)));
        TestWorld.Set64(w.Memory.Map(0x50000000, 8), 0, 0x50001000);
        var contract = w.Memory.Map(0x50001000, 0xD4);
        Convert.FromHexString(hex).CopyTo(contract, 0);
        TestWorld.Set64(contract, 0, TestWorld.ModuleBase + 0x4D78AF8);
        int writes = w.Memory.Writes;
        var read = new CurrentLoanReader(w.Memory, profile, TestWorld.ModuleBase).Read(player);
        Assert.Equal(CurrentLoanStatus.Present, read.Status);
        Assert.Equal(wage, read.Contract!.Wage);
        Assert.Equal(new DateOnly(2026, 7, 13), read.Contract.Started);
        Assert.Equal(new DateOnly(2027, 6, 30), read.Contract.Expires);
        Assert.NotEqual(0UL, read.BorrowingTeamAddress);
        Assert.Equal(writes, w.Memory.Writes);
        TestWorld.Set64(contract, 0, TestWorld.ModuleBase + 0x123456);
        Assert.Equal(CurrentLoanStatus.Unavailable, new CurrentLoanReader(w.Memory, profile, TestWorld.ModuleBase).Read(player).Status);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x338, BitConverter.GetBytes(0UL)));
        Assert.Equal(CurrentLoanStatus.Absent, new CurrentLoanReader(w.Memory, profile, TestWorld.ModuleBase).Read(player).Status);
    }

    [Fact]
    public void Reads_financial_terms_from_Mainoo_loan_captured_after_FMRTE_move()
    {
        const string snapshot = "F88A85F1FA7F0000282DB54724020000B0772C3724020000B0772C3724020000AC070100000001000000000000000000000000000000000000000000FFFFFFFFFFFFFFFF4A01EA07B600EC074A01EA0701006C07040004000000342922060000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF00000000B00C0000000000008C0C00000000000002000000000000000000000000000000CECA23000B00000015BF340040E2010040E20100AC0701003400000068010000FFFFFFFF800000000010000001006C0701006C071D02FF0700020A08FFFF0000";
        var w = new TestWorld();
        var player = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + profile.PlayerVtableRvas[0])));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x338, BitConverter.GetBytes(0x50000000UL)));
        TestWorld.Set64(w.Memory.Map(0x50000000, 8), 0, 0x50001000);
        var raw = w.Memory.Map(0x50001000, 0x100);
        Convert.FromHexString(snapshot).CopyTo(raw, 0);
        TestWorld.Set64(raw, 0, TestWorld.ModuleBase + 0x4D78AF8);
        var loan = new CurrentLoanReader(w.Memory, profile, TestWorld.ModuleBase).Read(player);
        Assert.Equal(CurrentLoanStatus.Present, loan.Status);
        Assert.Equal(new DateOnly(2026, 11, 26), loan.Signed);
        Assert.Equal((2345678, 3456789, 123456, 123456),
            (loan.Amounts!.AgreedPrice, loan.Amounts.BuyBackFee, loan.Amounts.MonthlyFee, loan.Amounts.UnusedMonthlyFee));
    }
}
