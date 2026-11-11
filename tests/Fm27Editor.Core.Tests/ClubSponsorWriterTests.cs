using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubSponsorWriterTests
{
    const ulong Club = 0x5000_0000, Holder = 0x5001_0000, List = 0x5002_0000, Records = 0x5003_0000;
    // 0x18-byte sponsor records: start date, end date, annual value, eight bytes of other data, income type.
    // Row 1 start date carries flag bit 0x200 above the 9-bit day, which the writer has to keep.
    static readonly string[] Rows =
    [
        "B500EB07B600E907C0C62D00000000000000000116010000",
        "B502EB07B600E90740420F00000000000000000004010000",
    ];

    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));

    void Build(string[] rows)
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + profile.Club!.ClubVtableRva);
        TestWorld.Set64(club, 0x130, Holder);
        var holder = memory.Map(Holder, 0x18);
        TestWorld.Set64(holder, 0, List);
        TestWorld.Set64(holder, 8, List + (ulong)rows.Length * 8);
        TestWorld.Set64(holder, 0x10, List + (ulong)rows.Length * 8);
        var list = memory.Map(List, Math.Max(8, rows.Length * 8));
        var records = memory.Map(Records, rows.Length * 0x100);
        for (int i = 0; i < rows.Length; i++)
        {
            TestWorld.Set64(list, i * 8, Records + (ulong)(i * 0x100));
            Convert.FromHexString(rows[i]).CopyTo(records, i * 0x100);
        }
    }

    ClubListReader Reader() => new(memory, profile, TestWorld.ModuleBase);
    ClubSponsorWriter Writer() => new(memory, profile, TestWorld.ModuleBase);

    [Fact]
    public void Reader_decodes_two_sponsor_records()
    {
        Build(Rows);
        var sponsors = Reader().ReadSponsors(Club);
        Assert.Equal(2, sponsors.Count);
        Assert.Equal(3_000_000, sponsors[0].TotalValue);
        Assert.Equal(22, sponsors[0].IncomeTypeCode);
        Assert.Equal(new DateOnly(2027, 6, 30), sponsors[0].StartDate);
        Assert.Equal(new DateOnly(2025, 7, 1), sponsors[0].EndDate);
        Assert.Equal(1_000_000, sponsors[1].TotalValue);
        Assert.Equal(4, sponsors[1].IncomeTypeCode);
    }

    [Fact]
    public void Writer_changes_the_value_or_the_two_dates_and_leaves_the_rest_alone()
    {
        Build(Rows);
        var entry = Reader().ReadSponsors(Club)[0];
        byte[] before = memory.Read(entry.Address, 0x18);
        Writer().SetTotal(Club, entry, 901_234_567);
        byte[] after = memory.Read(entry.Address, 0x18);
        Assert.Equal(BitConverter.GetBytes(901_234_567), after[0x8..0xC]);
        Assert.Equal(before[0..0x8], after[0..0x8]);
        Assert.Equal(before[0xC..], after[0xC..]);

        var fresh = Reader().ReadSponsors(Club)[0];
        Writer().SetDates(Club, fresh, new DateOnly(2025, 8, 1), new DateOnly(2036, 6, 30));
        var result = Reader().ReadSponsors(Club)[0];
        Assert.Equal(new DateOnly(2025, 8, 1), result.StartDate);
        Assert.Equal(new DateOnly(2036, 6, 30), result.EndDate);
        Assert.Equal(901_234_567, result.TotalValue);
        Assert.Equal(22, result.IncomeTypeCode);
    }

    [Fact]
    public void Writer_preserves_flag_bits_above_the_day()
    {
        Build(Rows);
        var entry = Reader().ReadSponsors(Club)[1];
        Assert.Equal(0x0200, BitConverter.ToUInt16(memory.Read(entry.Address, 4), 0) & 0xFE00);
        Writer().SetDates(Club, entry, new DateOnly(2026, 6, 30), new DateOnly(2025, 7, 1));
        Assert.Equal(0x0200, BitConverter.ToUInt16(memory.Read(entry.Address, 4), 0) & 0xFE00);
        Assert.Equal(181, BitConverter.ToUInt16(memory.Read(entry.Address, 4), 0) & 0x1FF);
        Assert.Equal(2026, BitConverter.ToInt16(memory.Read(entry.Address, 4), 2));
    }

    [Fact]
    public void Writer_sets_the_two_flag_bytes_FMRTE_wrote()
    {
        Build(Rows);
        var entry = Reader().ReadSponsors(Club)[0];
        Assert.Equal((true, false), (entry.RenewIncome, entry.FixedValue));
        Writer().SetFlags(Club, entry, renewIncome: false, fixedValue: true);
        byte[] after = memory.Read(entry.Address, 0x18);
        Assert.Equal((byte)0, after[0x15]);
        Assert.Equal((byte)1, after[0x16]);
        Assert.Equal((byte)0x16, after[0x14]); // income type untouched
        Assert.Equal((false, true), (Reader().ReadSponsors(Club)[0].RenewIncome, Reader().ReadSponsors(Club)[0].FixedValue));
    }

    [Fact]
    public void Writer_refuses_a_record_that_changed_after_the_read()
    {
        Build(Rows);
        var entry = Reader().ReadSponsors(Club)[0];
        Assert.True(memory.TryWrite(Records + 8, BitConverter.GetBytes(3_500_000)));

        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetTotal(Club, entry, 1));
        Assert.Equal(writes, memory.Writes);
        Assert.Equal(3_500_000, BitConverter.ToInt32(memory.Read(Records + 8, 4)));
    }
}
