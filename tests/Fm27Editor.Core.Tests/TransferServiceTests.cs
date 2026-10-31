using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Two clubs laid out with the FM27 27.0.0 offsets: club id +0xC, finance +0x150 (balance +0xC, transfer spend
/// this month +0x29C, players sold +0x32C, spent this season +0x4CC, transfer budget +0x7C4, wage bill +0x810),
/// team club +0x30 and squad vector +0x38, player team +0x130, contract team +0x10, wage +0x20.
/// Mainoo (wage 135000) moves from the seller to the buyer. Expected values are worked out by hand.
/// </summary>
public class TransferServiceTests
{
    const uint ContractVt = 0x4B00AE8, ClubVt = 0x4CDA1F8, TeamVt = 0x4CE1108, FullFinanceVt = 0x4CF0CC8, CompactFinanceVt = 0x4CF07F8;
    const ulong SellerClub = 0x5000_0000, SellerFin = 0x5001_0000, SellerTeam = 0x5002_0000, SellerSquad = 0x5003_0000;
    const ulong BuyerClub = 0x6000_0000, BuyerFin = 0x6001_0000, BuyerTeam = 0x6002_0000, BuyerSquad = 0x6003_0000;
    const ulong MainooContract = 0x7000_0000, YamalContract = 0x7000_1000, Other = 0x7100_0000;
    readonly TestWorld _w = new();

    public TransferServiceTests()
    {
        Club(SellerClub, SellerFin, 680, balance: 100_000_000, budget: 20_000_000, wageBill: 3_000_000);
        Club(BuyerClub, BuyerFin, 868, balance: 50_000_000, budget: 5_000_000, wageBill: 2_000_000);
        Team(SellerTeam, SellerClub, SellerSquad, [Other, TestWorld.Mainoo, Other + 8], capacity: 4);
        Team(BuyerTeam, BuyerClub, BuyerSquad, [TestWorld.Yamal], capacity: 2);
        Contract(MainooContract, SellerTeam, 135000);
        Contract(YamalContract, BuyerTeam, 50000);
        Set(TestWorld.Mainoo + 0x330, MainooContract); Set(TestWorld.Mainoo + 0x130, SellerTeam);
        Set(TestWorld.Yamal + 0x330, YamalContract); Set(TestWorld.Yamal + 0x130, BuyerTeam);
    }

    void Set(ulong a, ulong v) => Assert.True(_w.Memory.TryWrite(a, BitConverter.GetBytes(v)));
    int I32(ulong a) => BitConverter.ToInt32(_w.Bytes(a, 4));
    ulong Q(ulong a) => BitConverter.ToUInt64(_w.Bytes(a, 8));

    void Club(ulong club, ulong fin, int id, int balance, int budget, int wageBill)
    {
        var c = _w.Memory.Map(club, 0x160);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        BitConverter.GetBytes(id).CopyTo(c, 0xC);
        TestWorld.Set64(c, 0x150, fin);
        var f = _w.Memory.Map(fin, 0x820);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + FullFinanceVt);
        BitConverter.GetBytes(balance).CopyTo(f, 0xC);
        BitConverter.GetBytes(budget).CopyTo(f, 0x7C4);
        BitConverter.GetBytes(wageBill).CopyTo(f, 0x810);
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

    void Contract(ulong contract, ulong team, int wage)
    {
        var c = _w.Memory.Map(contract, 0x60);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ContractVt);
        TestWorld.Set64(c, 0x10, team);
        BitConverter.GetBytes(wage).CopyTo(c, 0x20);
    }

    (TransferService service, IReadOnlyList<ClubEntry> clubs, PlayerRecord mainoo) Setup()
    {
        var repo = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var players = new[] { repo.Reload(TestWorld.Mainoo)!, repo.Reload(TestWorld.Yamal)! };
        return (new TransferService(_w.Memory, _w.Profile, TestWorld.ModuleBase),
                ClubDirectory.FromPlayers(_w.Memory, players, _w.Profile, TestWorld.ModuleBase), players[0]);
    }

    [Fact]
    public void Directory_finds_both_clubs_their_ids_and_first_teams()
    {
        var (_, clubs, _) = Setup();
        Assert.Equal(new[] { 680, 868 }, clubs.Select(c => c.Id!.Value).Order());
        var buyer = clubs.Single(c => c.Id == 868);
        Assert.Equal(BuyerTeam, buyer.FirstTeam!.Address);
        Assert.Equal((1, 2), (buyer.FirstTeam.SquadCount, buyer.FirstTeam.SquadCapacity));
    }

    [Fact]
    public void Moves_money_wages_contract_team_playing_team_and_both_squads()
    {
        var (service, clubs, mainoo) = Setup();
        var plan = service.Plan(mainoo, clubs, clubs.Single(c => c.Id == 868), 12_345_678);
        Assert.False(plan.NeedsAllocation);
        service.Apply(plan);

        Assert.Equal(50_000_000 - 12_345_678, I32(BuyerFin + 0xC));
        Assert.Equal(12_345_678, I32(BuyerFin + 0x29C));
        Assert.Equal(12_345_678, I32(BuyerFin + 0x4CC));
        Assert.Equal(0, I32(BuyerFin + 0x7C4));                       // 5M budget minus 12.3M stops at 0
        Assert.Equal(2_000_000 + 135_000, I32(BuyerFin + 0x810));
        Assert.Equal(100_000_000 + 12_345_678, I32(SellerFin + 0xC));
        Assert.Equal(12_345_678, I32(SellerFin + 0x32C));
        Assert.Equal(20_000_000 + 12_345_678, I32(SellerFin + 0x7C4));
        Assert.Equal(3_000_000 - 135_000, I32(SellerFin + 0x810));

        Assert.Equal(BuyerTeam, Q(MainooContract + 0x10));
        Assert.Equal(BuyerTeam, Q(TestWorld.Mainoo + 0x130));
        Assert.Equal(SellerSquad + 16, Q(SellerTeam + 0x40));           // 3 players -> 2
        Assert.Equal(new[] { Other, Other + 8 }, new[] { Q(SellerSquad), Q(SellerSquad + 8) });
        Assert.Equal(BuyerSquad + 16, Q(BuyerTeam + 0x40));             // 1 player -> 2
        Assert.Equal(TestWorld.Mainoo, Q(BuyerSquad + 8));
    }

    /// <summary>
    /// Small clubs (Oxford United 695 in FM27) have a 0x38-byte finance object, vtable +0x4CF07F8, laid out back to back
    /// with the next club's. Only balance and embargo fit; budget/spend/wage offsets would write into the neighbour.
    /// </summary>
    [Fact]
    public void Small_club_finance_gets_only_its_balance_changed()
    {
        Assert.True(_w.Memory.TryWrite(BuyerFin, BitConverter.GetBytes(TestWorld.ModuleBase + CompactFinanceVt)));
        // Stand-in for the next club's compact object right after the 0x38 bytes.
        Assert.True(_w.Memory.TryWrite(BuyerFin + 0x38, BitConverter.GetBytes(TestWorld.ModuleBase + CompactFinanceVt)));
        var before = _w.Bytes(BuyerFin + 0x38, 0x820 - 0x38);
        var (service, clubs, mainoo) = Setup();

        var plan = service.Plan(mainoo, clubs, clubs.Single(c => c.Id == 868), 1_000_000);
        Assert.DoesNotContain(plan.Writes, w => w.Address >= BuyerFin + 0x38 && w.Address < BuyerFin + 0x820);
        service.Apply(plan);

        Assert.Equal(50_000_000 - 1_000_000, I32(BuyerFin + 0xC));
        Assert.Equal(before, _w.Bytes(BuyerFin + 0x38, 0x820 - 0x38));
        Assert.Equal(20_000_000 + 1_000_000, I32(SellerFin + 0x7C4)); // the full-object seller still gets its budget
    }

    [Fact]
    public void Unknown_finance_object_stops_the_plan()
    {
        Assert.True(_w.Memory.TryWrite(BuyerFin, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        var (service, clubs, mainoo) = Setup();
        Assert.Throws<StaleObjectException>(() => service.Plan(mainoo, clubs, clubs.Single(c => c.Id == 868), 1_000_000));
    }

    [Fact]
    public void Full_squad_gets_a_new_buffer_with_double_capacity()
    {
        Set(BuyerTeam + 0x48, BuyerSquad + 8);                          // capacity 1: full
        var (service, clubs, mainoo) = Setup();
        var plan = service.Plan(mainoo, clubs, clubs.Single(c => c.Id == 868), 1_000);
        Assert.True(plan.NeedsAllocation);
        const ulong NewBuffer = 0x6100_0000;
        service.Apply(plan, bytes => { _w.Memory.Map(NewBuffer, bytes); return NewBuffer; });

        Assert.Equal(NewBuffer, Q(BuyerTeam + 0x38));
        Assert.Equal(NewBuffer + 16, Q(BuyerTeam + 0x40));
        Assert.Equal(NewBuffer + 32, Q(BuyerTeam + 0x48));              // 2 entries, capacity 4
        Assert.Equal(new[] { TestWorld.Yamal, TestWorld.Mainoo }, new[] { Q(NewBuffer), Q(NewBuffer + 8) });
    }

    [Fact]
    public void Writes_nothing_when_a_value_changed_after_planning()
    {
        var (service, clubs, mainoo) = Setup();
        var plan = service.Plan(mainoo, clubs, clubs.Single(c => c.Id == 868), 1_000);
        Assert.True(_w.Memory.TryWrite(SellerFin + 0xC, BitConverter.GetBytes(1)));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => service.Apply(plan));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(SellerTeam, Q(MainooContract + 0x10));
    }
}
