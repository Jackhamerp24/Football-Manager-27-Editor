using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Values come from FMRTE 27's Income Statement for Manchester United on 25/11/2026 (this month / last month / this season)
/// and are written here at the offsets the profile declares, so a wrong offset or a wrong block stride fails the test.
/// </summary>
public class ClubFinancialStatementsTests
{
    const ulong Club = 0x6000_0000, Finance = 0x6001_0000;

    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));

    ClubStatementOffsets Offsets => profile.Club!.FinanceLists!.Statements!;

    void Build()
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + profile.Club!.ClubVtableRva);
        TestWorld.Set64(club, 0x150, Finance);
        financeBytes = memory.Map(Finance, 0x1000);
    }

    byte[] financeBytes = [];

    void Write(int period, int fieldOffset, int value)
    {
        var o = Offsets;
        BitConverter.GetBytes(value).CopyTo(financeBytes, o.Base + o.Stride * period + fieldOffset);
    }

    void WriteIncome(int period, int fieldOffset, int value) => Write(period, Offsets.IncomeBase + fieldOffset, value);

    ClubFinancialStatements Reader() => new(memory, profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_this_month_last_month_and_this_season_blocks()
    {
        Build();
        var o = Offsets;
        int gate = o.Income[Array.IndexOf(ClubStatementOffsets.IncomeKeys, "gateReceipts")];
        int wages = o.Expenditure[Array.IndexOf(ClubStatementOffsets.ExpenditureKeys, "playerWages")];
        int vat = o.Expenditure[Array.IndexOf(ClubStatementOffsets.ExpenditureKeys, "vat")];

        WriteIncome(0, gate, 2428554);
        Write(0, wages, 14756724);
        Write(0, vat, 807546);
        WriteIncome(1, gate, 3746183);
        Write(1, wages, 14726683);
        WriteIncome(2, gate, 9472204);
        Write(2, wages, 68679405);

        var periods = Reader().Read(Club);

        Assert.Equal(3, periods.Count);
        Assert.Equal(2428554, periods[0].GateReceipts);
        Assert.Equal(14756724, periods[0].PlayerWages);
        Assert.Equal(807546, periods[0].Vat);
        Assert.Equal(3746183, periods[1].GateReceipts);
        Assert.Equal(14726683, periods[1].PlayerWages);
        Assert.Equal(9472204, periods[2].GateReceipts);
        Assert.Equal(68679405, periods[2].PlayerWages);
    }

    [Fact]
    public void Totals_sum_every_declared_income_and_expenditure_field()
    {
        Build();
        var o = Offsets;
        for (int i = 0; i < o.Income.Count; i++) WriteIncome(0, o.Income[i], 100);
        for (int i = 0; i < o.Expenditure.Count; i++) Write(0, o.Expenditure[i], 10);

        var period = Reader().Read(Club)[0];

        Assert.Equal(o.Income.Count * 100, period.TotalIncome);
        Assert.Equal(o.Expenditure.Count * 10, period.TotalExpenditure);
        Assert.Equal(o.Income.Count * 100 - o.Expenditure.Count * 10, period.TotalIncome - period.TotalExpenditure);
    }

    [Fact]
    public void Set_writes_the_figure_and_rejects_a_stale_snapshot()
    {
        Build();
        var o = Offsets;
        int gate = o.Income[Array.IndexOf(ClubStatementOffsets.IncomeKeys, "gateReceipts")];
        WriteIncome(0, gate, 500);
        var reader = Reader();
        Assert.Equal(500, reader.Read(Club)[0].GateReceipts);

        Assert.Throws<StaleObjectException>(() => reader.Set(Club, 0, true, "gateReceipts", 400, 700));

        reader.Set(Club, 0, true, "gateReceipts", 500, 700);
        Assert.Equal(700, reader.Read(Club)[0].GateReceipts);
    }

    [Fact]
    public void Set_rejects_a_club_whose_vtable_changed()
    {
        Build();
        var reader = Reader();
        Assert.True(reader.IsClub(Club));
        TestWorld.Set64(memory.Map(0x6002_0000, 0x170), 0, TestWorld.ModuleBase + 0x1234567);
        var impostor = new ClubFinancialStatements(memory, profile, TestWorld.ModuleBase);

        Assert.False(impostor.IsClub(0x6002_0000));
        Assert.Throws<StaleObjectException>(() =>
            impostor.Set(0x6002_0000, 0, true, "gateReceipts", 0, 1));
        Assert.True(reader.IsClub(Club));
    }

    [Fact]
    public void Set_rejects_a_rebuilt_finance_object()
    {
        Build();
        var reader = Reader();
        ulong current = reader.FinancePointer(Club)!.Value;
        reader.Set(Club, 0, true, "gateReceipts", 0, 5, expectedFinance: current);

        Assert.Throws<StaleObjectException>(() =>
            reader.Set(Club, 0, true, "gateReceipts", 5, 6, expectedFinance: current + 0x1000));
        Assert.Equal(5, reader.Read(Club)[0].GateReceipts);
    }

    [Fact]
    public void Missing_finance_pointer_reads_nothing()
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + profile.Club!.ClubVtableRva);

        Assert.Empty(Reader().Read(Club));
    }
}
