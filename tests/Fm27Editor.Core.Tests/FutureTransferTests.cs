using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Offered contracts and transfer dates as dumped on 15/8/2026, pointers re-pointed at fakes. Omar Marmoush's future contract
/// (FMRTE 27: Full Time Contract, £260,000, 1/7/2027 – 30/6/2031, not rolling; transfer date 1/7/2027) and Alejandro Balde's loan
/// contract (£43,982, 13/8/2026 – 30/6/2027; its dates carry flag bits 0x2A00). Expected writes are FMRTE 27's own (2/11/2026):
/// wages 260,000 → 260,001 at +0x20, expiry 30/6/2031 → 30/6/2032 as B6 00 F0 07, Rolling 00 → 08 at +0x2B, transfer date
/// 1/7/2027 → 3/7/2027 as B8 00 EB 07.
/// </summary>
public class FutureTransferTests
{
    const string MarmoushContract = "E80A0C09FB7F000000B23FE7C7010000589F4FDDC7010000589F4FDDC7010000A0F703000000010000000000000000000000000000000000238E0000FFFFFFFFFFFFFFFFB600EB07B500EF071700E907";
    const string BaldeContract = "F8913809FB7F0000B88B71E7C7010000D8C14FDDC7010000D8C14FDDC7010000CEAB0000000001000000000000000000000000000000000000000000FFFFFFFFFFFFFFFFE12AEA07B52AEB0701006C07";
    const uint FullVt = 0x4B00AE8, LoanVt = 0x4DC91F8;
    const ulong Holder = 0x5000_0000, Vector = 0x5000_1000, Rec1 = 0x5001_0000, Rec2 = 0x5001_1000, C1 = 0x5002_0000, C2 = 0x5002_1000;
    const ulong Root = 0x5003_0000, Dates = 0x5003_1000, DateRec = 0x5003_2000, OtherDate = 0x5003_3000;
    readonly TestWorld _w = new();

    // Type is the byte at +0xC3, read on the reloaded save: Marmoush 1 (FMRTE: Full Time), Balde 9 (FMRTE: blank).
    void Contract(ulong at, string hex, uint vt, byte type)
    {
        var b = Convert.FromHexString(hex);
        BitConverter.GetBytes(TestWorld.ModuleBase + vt).CopyTo(b, 0);
        _w.Memory.Map(at, 0xD0);
        Assert.True(_w.Memory.TryWrite(at, b));
        Assert.True(_w.Memory.TryWrite(at + 0xC3, [type]));
    }

    void Record(ulong at, ulong contract, int id)
    {
        _w.Memory.Map(at, 0x48);
        Assert.True(_w.Memory.TryWrite(at, Convert.FromHexString("01000000C7010000")));
        Assert.True(_w.Memory.TryWrite(at + 0x10, BitConverter.GetBytes(contract)));
        Assert.True(_w.Memory.TryWrite(at + 0x28, BitConverter.GetBytes(id)));
    }

    public FutureTransferTests()
    {
        Contract(C1, MarmoushContract, FullVt, 1);
        Contract(C2, BaldeContract, LoanVt, 9);
        Record(Rec1, C1, 21);
        Record(Rec2, C2, 5624);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x328, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x80);
        TestWorld.Set64(h, 0, Vector); TestWorld.Set64(h, 8, Vector + 16); TestWorld.Set64(h, 16, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, Rec1); TestWorld.Set64(v, 8, Rec2);
        // Transfer dates: Marmoush's (person, 1/7/2027, id 21) and someone else's.
        _w.Memory.Map(DateRec, 16);
        Assert.True(_w.Memory.TryWrite(DateRec, [.. BitConverter.GetBytes(TestWorld.Mainoo + 0x288), .. Convert.FromHexString("B600EB0715000000")]));
        _w.Memory.Map(OtherDate, 16);
        Assert.True(_w.Memory.TryWrite(OtherDate, [.. BitConverter.GetBytes(0x1234UL), .. Convert.FromHexString("01004B070F000000")]));
        var d = _w.Memory.Map(Dates, 16);
        TestWorld.Set64(d, 0, OtherDate); TestWorld.Set64(d, 8, DateRec);
        var root = _w.Memory.Map(Root, 0x50);
        TestWorld.Set64(root, 0x38, Dates); TestWorld.Set64(root, 0x40, Dates + 16);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F2EF0, 8), 0, Root);
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    FutureTransferReader Reader() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    FutureTransferWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    static TransferProposal Offer(int id) => new(0, ProposalType.Transfer, id, "Manchester City", "Tottenham Hotspur", 20_000_000, 19);

    [Fact]
    public void Reads_offered_contracts_as_FMRTE_shows_them()
    {
        var c = Reader().OfferContracts(Player());
        Assert.Equal((260_000, 1, false, new DateOnly(2027, 7, 1), new DateOnly(2031, 6, 30), false),
            (c[21].Wage, c[21].Type, c[21].Rolling, c[21].Started, c[21].Expires, c[21].Loan));
        Assert.Equal((43_982, 9, new DateOnly(2026, 8, 13), new DateOnly(2027, 6, 30), true), (c[5624].Wage, c[5624].Type, c[5624].Started, c[5624].Expires, c[5624].Loan));
    }

    [Fact]
    public void Contract_writes_match_FMRTEs_bytes_and_keep_date_flags()
    {
        var c = Reader().OfferContracts(Player());
        var before = _w.Bytes(C1, 0x50);
        Writer().SetContract(c[21], new(260_001, ContractType.FullTime, true, new DateOnly(2027, 7, 1), new DateOnly(2032, 6, 30)));
        BitConverter.GetBytes(260_001).CopyTo(before, 0x20);
        before[0x2B] = 0x08;
        Convert.FromHexString("B600F007").CopyTo(before, 0x48);
        Assert.Equal(before, _w.Bytes(C1, 0x50));
        // FMRTE's Part Time write left +0xC3 = 0.
        Writer().SetContract(Reader().OfferContracts(Player())[21], new(260_001, ContractType.PartTime, true, new DateOnly(2027, 7, 1), new DateOnly(2032, 6, 30)));
        Assert.Equal(0, _w.Bytes(C1 + 0xC3, 1)[0]);
        Assert.Equal(before, _w.Bytes(C1, 0x50));

        Writer().SetContract(c[5624], new(43_982, ContractType.FullTime, false, new DateOnly(2026, 8, 14), new DateOnly(2027, 6, 30)));
        Assert.Equal(Convert.FromHexString("E22AEA07B52AEB07"), _w.Bytes(C2 + 0x44, 8));
        Assert.Throws<StaleObjectException>(() => Writer().SetContract(c[21], new(1, ContractType.PartTime, false, new DateOnly(2027, 7, 1), new DateOnly(2028, 1, 1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetContract(Reader().OfferContracts(Player())[21], new(1, ContractType.PartTime, false, new DateOnly(2028, 7, 1), new DateOnly(2027, 1, 1))));
    }

    [Fact]
    public void Transfer_date_is_found_by_person_and_written_like_FMRTE()
    {
        var f = Assert.Single(Reader().Read(Player(), [Offer(21)]));
        Assert.Equal(new DateOnly(2027, 7, 1), f.Date);
        Writer().SetDate(f, new DateOnly(2027, 7, 3));
        Assert.Equal(Convert.FromHexString("B800EB0715000000"), _w.Bytes(DateRec + 8, 8));
        Assert.Throws<StaleObjectException>(() => Writer().SetDate(f, new DateOnly(2027, 7, 4)));
        Assert.Empty(Reader().Read(Player(), [Offer(22)]));
    }

    // FMRTE 27's Future Contract tab is the full contract editor on the future transfer's contract (Ibrahim Dosso → Aston Villa).
    [Fact]
    public void Contract_editor_works_on_an_offered_full_contract_and_refuses_others()
    {
        var future = new ContractWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, C1);
        var s = future.Read(Player())!;
        Assert.Equal((C1, 260_000, ContractType.FullTime, new DateOnly(2031, 6, 30)), (s.Address, s.Wage, s.Type!.Value, s.Expires!.Value));
        var before = _w.Bytes(C1, 0xD0);
        future.SetLoyaltyBonus(Player(), s, 1234);
        BitConverter.GetBytes(1234).CopyTo(before, 0x38);
        Assert.Equal(before, _w.Bytes(C1, 0xD0));

        // Balde's loan contract has another class; a contract no longer in the player's offers is refused too.
        Assert.Throws<StaleObjectException>(() => new ContractWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, C2).Read(Player()));
        Assert.True(_w.Memory.TryWrite(Vector, BitConverter.GetBytes(Rec2)));
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Vector + 8)));
        Assert.Throws<StaleObjectException>(() => future.SetLoyaltyBonus(Player(), s, 99));
        Assert.Throws<StaleObjectException>(() => new ContractWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, C1).Read(Player()));
    }
}
