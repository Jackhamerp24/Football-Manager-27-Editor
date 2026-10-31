using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Finances → Sponsors and Other / Debt and Loans. Record bytes are dumped from FM27 27.0.0 RAM (PID 30568,
/// 2026-10-31; club +0x130 → holder, vector at +0; club +0x100 → detail, vector at +0x50). Expected values are the
/// FMRTE 27 screens: Sheffield United in docs/club-plan.md, Everton as checked by lane D (re/club/lists.md).
/// </summary>
public class ClubFinanceListTests
{
    const uint ClubVt = 0x4CDA1F8;
    const ulong Club = 0x5000_0000, Holder = 0x5001_0000, Detail = 0x5002_0000, SponsorList = 0x5003_0000, DebtList = 0x5004_0000;
    const ulong Records = 0x5100_0000;

    static readonly string[] SheffieldSponsors =
    [
        "1300E807B500EB0748E80100C2A200000000381817010001",
        "B600EA07B500EB0750C3000050C300000000381817010001",
        "B600EA07B500EB07A0860100A08601000000381817010001",
        "B700E807B500E90771E57E0071E57E000000381812000001",
        "9800EA07B500EB0720A1070020A107000000381801010001",
        "B600EA07B500EB07A0860100A08601000000381817010001",
        "B600EA07B500EB0750C3000050C300000000381817010001",
        "B600EA07B500EB07A0860100A08601000000381817010001",
        "B600EA07B500EB07B8880000B88800000000381817010001",
        "B600EA07B500EB0790D0030090D00300000038180B010001",
        "B600EA07B500ED0720AA440060E316000000381816010001",
        "B600EA07B500EB07A0860100A08601000000381813010001",
        "4F01E8074F01EB07DF9CA100F5DE35000000381804010001",
    ];
    static readonly string[] SheffieldDebts =
    [
        "C0D8A70093A904000000000000000000B600EC07B600E9070A010000",
        "46856D01FE1803000000000000000000B500F307B600E90701010000",
        "C01F480145A805000000000000000000B500EE07B600E90701010000",
    ];
    static readonly string[] EvertonSponsors =
    [
        "9800E9079700EA0780F9370380F937030000F41A12000000",
        "B700E807B500EB0700879303002D31010000F41A16010000",
        "B700E807B500EA0760E31600B0710B000000F41A13000000",
        "8700E907B500EE07C00E1602C0CF6A000000F41A03010000",
        "9800EA07B500ED07C0320A03406603010000F41A01010000",
        "B600EA07B600EC0780841E0040420F000000F41A13010000",
    ];
    static readonly string[] EvertonDebts =
    [
        "60FF2F02D4EE010000D9000000000000B50009087900DE070F010030",
        "403F5314EB5324003755150000000000B5000608B500E90701010080",
        "0020A1079A9227000778060000000000B500EE07B500E90701010000",
    ];

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void Build(string[] sponsors, string[] debts)
    {
        var club = _m.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(club, 0x100, Detail);
        TestWorld.Set64(club, 0x130, Holder);
        Vector(Holder, 0, SponsorList, sponsors, 0x18, Records);
        Vector(Detail, 0x50, DebtList, debts, 0x1C, Records + 0x10000);
    }

    void Vector(ulong owner, int headerAt, ulong list, string[] records, int size, ulong recordBase)
    {
        var o = _m.Map(owner, headerAt + 0x18);
        TestWorld.Set64(o, headerAt, list);
        TestWorld.Set64(o, headerAt + 8, list + (ulong)records.Length * 8);
        TestWorld.Set64(o, headerAt + 0x10, list + (ulong)records.Length * 8);
        var l = _m.Map(list, Math.Max(8, records.Length * 8));
        for (int i = 0; i < records.Length; i++)
        {
            ulong r = recordBase + (ulong)i * 0x100;
            TestWorld.Set64(l, i * 8, r);
            var bytes = Convert.FromHexString(records[i]);
            Assert.Equal(size, bytes.Length);
            bytes.CopyTo(_m.Map(r, size), 0);
        }
    }

    ClubListReader Reader() => new(_m, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Sheffield_sponsors_match_FMRTE()
    {
        Build(SheffieldSponsors, SheffieldDebts);
        var s = Reader().ReadSponsors(Club);

        // 23 Stadium, 18 Total Commercial Income, 1 Kit Sponsor, 11 Back of Shirt, 22 Kit Manufacturer, 19 Shirt Sleeve, 4 General Sponsor.
        Assert.Equal(new byte[] { 23, 23, 23, 18, 1, 23, 23, 23, 23, 11, 22, 19, 4 }, s.Select(x => x.IncomeTypeCode));
        Assert.Equal([125000, 50000, 100000, 8316273, 500000, 100000, 50000, 100000, 35000, 250000, 4500000, 100000, 10591455],
            s.Select(x => x.TotalValue));
        Assert.Equal(new DateOnly(2024, 1, 19), s[0].StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), s[0].EndDate);
        Assert.Equal(new DateOnly(2024, 7, 1), s[3].StartDate);
        Assert.Equal(new DateOnly(2025, 6, 30), s[3].EndDate);
        Assert.Equal(new DateOnly(2026, 6, 1), s[4].StartDate);
        Assert.Equal(new DateOnly(2029, 6, 30), s[10].EndDate);
        Assert.Equal(new DateOnly(2024, 11, 30), s[12].StartDate);
        Assert.Equal(new DateOnly(2027, 12, 1), s[12].EndDate);
    }

    [Fact]
    public void Sheffield_debts_match_FMRTE()
    {
        Build(SheffieldSponsors, SheffieldDebts);
        var d = Reader().ReadDebts(Club);

        // 10 Loan Repayable On Promotion To Top Division, 1 Bank.
        Assert.Equal(new byte[] { 10, 1, 1 }, d.Select(x => x.SourceCode));
        Assert.Equal([11000000, 23954758, 21503936], d.Select(x => x.OriginalDebt));
        Assert.All(d, x => Assert.Equal(new DateOnly(2025, 7, 1), x.StartDate));
        Assert.Equal([new DateOnly(2028, 6, 30), new DateOnly(2035, 6, 30), new DateOnly(2030, 6, 30)], d.Select(x => x.EndDate!.Value));
    }

    [Fact]
    public void Everton_sponsor_types_and_debt_sources_match_FMRTE_order()
    {
        Build(EvertonSponsors, EvertonDebts);
        // Lane D: Total Commercial Income, Kit Manufacturer, Shirt Sleeve, Stadium Sponsor, Kit Sponsor, Shirt Sleeve;
        // debts Miscellaneous, Bank, Bank.
        Assert.Equal(new byte[] { 18, 22, 19, 3, 1, 19 }, Reader().ReadSponsors(Club).Select(x => x.IncomeTypeCode));
        Assert.Equal([54000000, 60000000, 1500000, 35000000, 51000000, 2000000], Reader().ReadSponsors(Club).Select(x => x.TotalValue));
        Assert.Equal(new byte[] { 15, 1, 1 }, Reader().ReadDebts(Club).Select(x => x.SourceCode));
        Assert.Equal([36700000, 341000000, 128000000], Reader().ReadDebts(Club).Select(x => x.OriginalDebt));
    }

    [Fact]
    public void Not_a_club_or_no_profile_section_gives_empty_lists_and_never_writes()
    {
        Build(SheffieldSponsors, SheffieldDebts);
        int writes = _m.Writes;
        Assert.True(_m.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        Assert.Empty(Reader().ReadSponsors(Club));
        Assert.Empty(Reader().ReadDebts(Club));
        Assert.Equal(writes + 1, _m.Writes);
    }
}
