using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Omar Marmoush's offer as dumped on 15/8/2026 (0x98 bytes; person and team pointers re-pointed at fakes). FMRTE 27 lists it
/// as Transfer, Manchester City → Tottenham Hotspur, £20,000,000, Confirmed; his id list ([player+0x320]) holds 21. FMRTE 27
/// writing Alejandro Balde's Value 150,000 → 150,001 changed +0x58 only (2/11/2026).
/// </summary>
public class TransferProposalTests
{
    const string Marmoush = "E8924F09FB7F0000010000001B0008010000000000000000000000000000000000000000000000000000000000000000002100000000000000B23FE7C7010000589F4FDDC7010000589F4FDDC701000090C24FDDC7010000002D31011500000016000000FFFFFFFFFFFFFFFF01006C0701006C074F01E8076D01E8070100E90701006C070000010000FF0000001301000000000000000000";
    const uint TransferVt = 0x4F392E8, LoanVt = 0x4E195B8, TeamVt = 0x4CE1108, ClubVt = 0x4CDA1F8;
    const ulong Offer = 0x5000_0000, Other = 0x5000_1000, List = 0x5001_0000, Root = 0x5002_0000, Ids = 0x5003_0000, Header = 0x5003_1000, Objects = 0x5100_0000;
    readonly TestWorld _w = new();
    ulong _next = Objects;

    ulong Text(string s) { ulong at = _next; _next += 0x100; _w.Memory.MapString(at, s); return at; }

    ulong Team(string club)
    {
        ulong c = _next; _next += 0x1000;
        TestWorld.Set64(_w.Memory.Map(c, 0xD0), 0, TestWorld.ModuleBase + ClubVt);
        Assert.True(_w.Memory.TryWrite(c + 0xC0, BitConverter.GetBytes(Text(club))));
        ulong t = _next; _next += 0x1000;
        var b = _w.Memory.Map(t, 0x50);
        TestWorld.Set64(b, 0, TestWorld.ModuleBase + TeamVt); TestWorld.Set64(b, 0x30, c);
        return t;
    }

    public TransferProposalTests()
    {
        var o = Convert.FromHexString(Marmoush);
        BitConverter.GetBytes(TestWorld.ModuleBase + TransferVt).CopyTo(o, 0);
        BitConverter.GetBytes(TestWorld.Mainoo + 0x288).CopyTo(o, 0x38);
        ulong spurs = Team("Tottenham Hotspur"), city = Team("Manchester City");
        BitConverter.GetBytes(spurs).CopyTo(o, 0x40); BitConverter.GetBytes(spurs).CopyTo(o, 0x48); BitConverter.GetBytes(city).CopyTo(o, 0x50);
        _w.Memory.Map(Offer, o.Length);
        Assert.True(_w.Memory.TryWrite(Offer, o));
        // A second offer (loan, id 22, rejected) the player's list does not name.
        var l = (byte[])o.Clone();
        BitConverter.GetBytes(TestWorld.ModuleBase + LoanVt).CopyTo(l, 0); BitConverter.GetBytes(22).CopyTo(l, 0x5C); l[0x8D] = 2;
        _w.Memory.Map(Other, l.Length);
        Assert.True(_w.Memory.TryWrite(Other, l));
        var list = _w.Memory.Map(List, 16);
        TestWorld.Set64(list, 0, Other); TestWorld.Set64(list, 8, Offer);
        var root = _w.Memory.Map(Root, 0x20);
        TestWorld.Set64(root, 8, List); TestWorld.Set64(root, 0x10, List + 16);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F2EF0, 8), 0, Root);
        Assert.True(_w.Memory.Map(Ids, 4) is not null && _w.Memory.TryWrite(Ids, BitConverter.GetBytes(21)));
        var h = _w.Memory.Map(Header, 24);
        TestWorld.Set64(h, 0, Ids); TestWorld.Set64(h, 8, Ids + 4); TestWorld.Set64(h, 16, Ids + 4);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x320, BitConverter.GetBytes(Header)));
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    TransferProposalReader Reader() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_players_offers_as_FMRTE_shows_them()
    {
        var p = Assert.Single(Reader().Read(Player()));
        Assert.Equal((ProposalType.Transfer, "Manchester City", "Tottenham Hotspur", 20_000_000, 19, 21), (p.Type, p.From, p.To, p.Value, p.Status, p.Id));
        Assert.Equal(ProposalType.Loan, Reader().ReadOffer(Other)!.Type);
    }

    [Theory]
    [InlineData(8, true)]   // Kalimuendo, Player accepted
    [InlineData(10, true)]  // Wilson-Esbrand, Unknown status 1
    [InlineData(12, true)]  // Balde, Ready to sign
    [InlineData(19, true)]  // Marmoush, Confirmed
    [InlineData(2, false)]  // Leny Yoro: FMRTE lists nothing
    [InlineData(3, false)]  // Ohgihara
    [InlineData(15, false)] // Vasco Sousa 2000268521
    [InlineData(20, false)] // Ferran Fernández
    public void Filter_matches_what_FMRTE_lists(int status, bool shown) => Assert.Equal(shown, TransferProposalReader.ShownByFmrte(status));

    [Fact]
    public void Fee_write_touches_four_bytes_and_refuses_stale_offers()
    {
        var p = Reader().Read(Player())[0];
        var before = _w.Bytes(Offer, 0x98);
        new TransferProposalWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetValue(p, 20_000_001);
        BitConverter.GetBytes(20_000_001).CopyTo(before, 0x58);
        Assert.Equal(before, _w.Bytes(Offer, 0x98));
        Assert.Throws<StaleObjectException>(() => new TransferProposalWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetValue(p, 5));
        Assert.Equal(20_000_001, BitConverter.ToInt32(_w.Bytes(Offer + 0x58, 4)));
    }

    [Fact]
    public void Status_write_touches_one_byte_and_refuses_stale_offers()
    {
        var p = Reader().Read(Player())[0];
        var before = _w.Bytes(Offer, 0x98);
        new TransferProposalWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetStatus(p, 5);
        before[0x8D] = 5;
        Assert.Equal(before, _w.Bytes(Offer, 0x98));
        Assert.Throws<StaleObjectException>(() => new TransferProposalWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).SetStatus(p, 12));
        Assert.Equal(5, _w.Bytes(Offer + 0x8D, 1)[0]);
    }

    [Fact]
    public void Club_offers_include_staff_and_players_and_hide_completed_offers()
    {
        // Julio Figueroa → Man Utd, £0, status 10 (FMRTE 27, 3/11/2026); pointers remapped to fixture objects.
        const string figueroa = "E892411BFB7F000001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000800000040CA34F6D30100005047AAEED30100005047AAEED3010000000000000000000000000000DE0F0000CC140000FFFFFFFFFFFFFFFF01006C0701006C07D402EA0701006C07D402EA0701006C070203010100FF0000000A01000000000000000000";
        const ulong person = 0x5300_0000;
        var bytes = Convert.FromHexString(figueroa);
        BitConverter.GetBytes(TestWorld.ModuleBase + TransferVt).CopyTo(bytes, 0);
        BitConverter.GetBytes(person).CopyTo(bytes, 0x38);
        ulong team = BitConverter.ToUInt64(_w.Bytes(Offer + 0x40, 8));
        ulong club = BitConverter.ToUInt64(_w.Bytes(team + 0x30, 8));
        BitConverter.GetBytes(team).CopyTo(bytes, 0x40);
        Assert.True(_w.Memory.TryWrite(Other, bytes));
        var staff = new StaffRecord { Address = person, StaffAddress = person - 0x100, CommonName = "Julio Figueroa", Id = 123, Attributes = [], Personality = [], Kind = _w.Profile.Staff!.Kinds[0] };
        var found = Reader().ReadForClub(club, [Player()], [staff]);
        Assert.Equal(new[] { "Julio Figueroa", "Kobbie Mainoo" }, found.Select(x => x.PersonName));
        Assert.Equal(new[] { 4062, 21 }, found.Select(x => x.Proposal.Id));
        Assert.Equal(new[] { 0, 20_000_000 }, found.Select(x => x.Proposal.Value));
        Assert.True(_w.Memory.TryWrite(Other + 0x8D, new byte[] { 20 }));
        Assert.Equal("Kobbie Mainoo", Assert.Single(Reader().ReadForClub(club, [Player()], [staff])).PersonName);
    }
}
