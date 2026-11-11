using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubDebtPaymentsTests
{
    const ulong Club = 0x5000_0000, Holder = 0x5001_0000, List = 0x5002_0000, Records = 0x5003_0000;
    // Captured from build 2493426, PID 15744; FMRTE showed Sheffield 305555/0 and 203006/0,
    // Everton 126676/55552 and 2380779/1398071 respectively.
    static readonly string[] Sheffield =
    ["C0D8A70093A904000000000000000000B600EC07B600E9070A010000", "46856D01FE1803000000000000000000B500F307B600E90701010000"];
    static readonly string[] Everton =
    ["60FF2F02D4EE010000D9000000000000B50009087900DE070F010000", "403F5314EB5324003755150000000000B5000608B500E90701010000"];
    static readonly string[] Vasco =
    ["88CD2607E77E0B00DBA00700000000006D01FF070100EA0705010100"];
    static readonly string[] SaoPaulo =
    ["6D37930007600200F825010000000000D432F407D632EA0701010000"];
    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));

    void Build(string[] rows)
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + 0x4CDC1F8);
        TestWorld.Set64(club, 0x100, Holder);
        var holder = memory.Map(Holder, 0x68);
        TestWorld.Set64(holder, 0x50, List);
        TestWorld.Set64(holder, 0x58, List + (ulong)rows.Length * 8);
        TestWorld.Set64(holder, 0x60, List + (ulong)rows.Length * 8);
        var list = memory.Map(List, rows.Length * 8);
        for (int i = 0; i < rows.Length; i++)
        {
            ulong address = Records + (ulong)(i * 0x100);
            TestWorld.Set64(list, i * 8, address);
            Convert.FromHexString(rows[i]).CopyTo(memory.Map(address, 0x1C), 0);
        }
    }

    ClubListReader Reader() => new(memory, profile, TestWorld.ModuleBase);
    ClubDebtWriter Writer() => new(memory, profile, TestWorld.ModuleBase);

    [Fact]
    public void Repayment_and_interest_match_FMRTE_for_sheffield_and_everton()
    {
        Build(Sheffield);
        Assert.Equal([305555, 203006], Reader().ReadDebts(Club).Select(x => x.MonthlyRepayment));
        Assert.Equal([0, 0], Reader().ReadDebts(Club).Select(x => x.MonthlyInterestRepayment));
        var other = new ClubDebtPaymentsTests();
        other.Build(Everton);
        Assert.Equal([126676, 2380779], other.Reader().ReadDebts(Club).Select(x => x.MonthlyRepayment));
        Assert.Equal([55552, 1398071], other.Reader().ReadDebts(Club).Select(x => x.MonthlyInterestRepayment));
    }

    [Fact]
    public void Writer_changes_only_the_two_fields_and_refuses_stale_record_or_vector()
    {
        Build(Everton);
        var row = Reader().ReadDebts(Club)[0];
        Writer().SetPayments(Club, row, 126677, 55553);
        Assert.Equal(Convert.FromHexString("60FF2F02D5EE010001D9000000000000B50009087900DE070F010000"), memory.Read(Records, 0x1C));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPayments(Club, row, 1, 2));
        Assert.Equal(writes, memory.Writes);
        var fresh = Reader().ReadDebts(Club)[0];
        Assert.True(memory.TryWrite(Holder + 0x58, BitConverter.GetBytes(List + 8)));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPayments(Club, fresh, 1, 2));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Writer_refuses_replaced_club_and_replaced_slot()
    {
        Build(Sheffield);
        var row = Reader().ReadDebts(Club)[0];
        Assert.True(memory.TryWrite(List, BitConverter.GetBytes(Records + 0x100)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPayments(Club, row, 1, 2));
        Assert.Equal(writes, memory.Writes);
        Assert.True(memory.TryWrite(List, BitConverter.GetBytes(Records)));
        Assert.True(memory.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPayments(Club, row, 1, 2));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Interest_only_matches_FMRTE_and_guarded_write_preserves_other_bytes()
    {
        Build(Vasco);
        var row = Assert.Single(Reader().ReadDebts(Club));
        Assert.True(row.InterestOnly);
        var everton = new ClubDebtPaymentsTests();
        everton.Build(Everton);
        Assert.False(everton.Reader().ReadDebts(Club)[0].InterestOnly);

        Writer().SetInterestOnly(Club, row, false);
        Assert.Equal(Convert.FromHexString("88CD2607E77E0B00DBA00700000000006D01FF070100EA0705010000"), memory.Read(Records, 0x1C));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetInterestOnly(Club, row, true));
        Assert.Equal(writes, memory.Writes);
        Assert.True(memory.TryWrite(Records + 0x1A, [2]));
        var unknown = Assert.Single(Reader().ReadDebts(Club));
        Assert.Null(unknown.InterestOnly);
        writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => Writer().SetInterestOnly(Club, unknown, true));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Debt_source_matches_FMRTE_and_guarded_write_preserves_the_record()
    {
        Build(Vasco);
        var row = Assert.Single(Reader().ReadDebts(Club));
        Assert.Equal(5, row.SourceCode); // State in FMRTE, Vasco 339.
        var everton = new ClubDebtPaymentsTests();
        everton.Build(Everton);
        Assert.Equal(15, everton.Reader().ReadDebts(Club)[0].SourceCode); // Miscellaneous in FMRTE, Everton 650.
        Writer().SetSource(Club, row, 15);
        Assert.Equal(Convert.FromHexString("88CD2607E77E0B00DBA00700000000006D01FF070100EA070F010100"), memory.Read(Records, 0x1C));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetSource(Club, row, 5));
        Assert.Equal(writes, memory.Writes);
        var fresh = Assert.Single(Reader().ReadDebts(Club));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetSource(Club, fresh, 6));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Older_profile_does_not_enable_new_source_writes()
    {
        Build(Vasco);
        var oldProfile = OffsetProfile.LoadFile(TestWorld.ProfilePath);
        Assert.True(memory.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CDA1F8UL)));
        var row = Assert.Single(new ClubListReader(memory, oldProfile, TestWorld.ModuleBase).ReadDebts(Club));
        var oldWriter = new ClubDebtWriter(memory, oldProfile, TestWorld.ModuleBase);
        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => oldWriter.SetSource(Club, row, 15));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Original_debt_matches_FMRTE_and_writer_checks_the_snapshot()
    {
        Build(Vasco);
        var row = Assert.Single(Reader().ReadDebts(Club));
        Assert.Equal(119983496, row.OriginalDebt);
        var other = new ClubDebtPaymentsTests();
        other.Build(Everton);
        Assert.Equal(36700000, other.Reader().ReadDebts(Club)[0].OriginalDebt);
        Writer().SetOriginalDebt(Club, row, 119983497);
        Assert.Equal(Convert.FromHexString("89CD2607E77E0B00DBA00700000000006D01FF070100EA0705010100"), memory.Read(Records, 0x1C));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetOriginalDebt(Club, row, 1));
        Assert.Equal(writes, memory.Writes);

        var oldProfile = OffsetProfile.LoadFile(TestWorld.ProfilePath);
        Assert.True(memory.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CDA1F8UL)));
        var oldRow = Assert.Single(new ClubListReader(memory, oldProfile, TestWorld.ModuleBase).ReadDebts(Club));
        writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => new ClubDebtWriter(memory, oldProfile, TestWorld.ModuleBase).SetOriginalDebt(Club, oldRow, 1));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Debt_dates_match_FMRTE_and_preserve_real_high_flag_bits()
    {
        Build(SaoPaulo);
        var row = Assert.Single(Reader().ReadDebts(Club));
        Assert.Equal(new DateOnly(2026, 8, 2), row.StartDate);
        Assert.Equal(new DateOnly(2036, 7, 30), row.EndDate);
        var other = new ClubDebtPaymentsTests();
        other.Build(Vasco);
        var otherRow = Assert.Single(other.Reader().ReadDebts(Club));
        Assert.Equal(new DateOnly(2026, 1, 1), otherRow.StartDate);
        Assert.Equal(new DateOnly(2047, 12, 31), otherRow.EndDate);

        Writer().SetDates(Club, row, new DateOnly(2026, 8, 3), new DateOnly(2036, 7, 31));
        Assert.Equal(Convert.FromHexString("6D37930007600200F825010000000000D532F407D732EA0701010000"), memory.Read(Records, 0x1C));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetDates(Club, row, new DateOnly(2026, 8, 2), new DateOnly(2036, 7, 30)));
        Assert.Equal(writes, memory.Writes);
        var fresh = Assert.Single(Reader().ReadDebts(Club));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetDates(Club, fresh, new DateOnly(2301, 1, 1), new DateOnly(2301, 1, 2)));
        Assert.Equal(writes, memory.Writes);
    }
}
