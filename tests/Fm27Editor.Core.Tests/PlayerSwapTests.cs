using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Two clubs on the Game Pass 27.0.0+2493426 vtables: Mainoo (wage 135,000) at club 680, Yamal (wage 50,000) at club
/// 868, each team with its own squad and each club with its own finance object. A swap must exchange the two players,
/// their teams and the two wage bills; every expected number is worked out by hand.
/// </summary>
public class PlayerSwapTests
{
    const ulong SellerClub = 0x5000_0000, SellerFin = 0x5001_0000, SellerTeam = 0x5002_0000, SellerSquad = 0x5003_0000;
    const ulong BuyerClub = 0x6000_0000, BuyerFin = 0x6001_0000, BuyerTeam = 0x6002_0000, BuyerSquad = 0x6003_0000;
    const ulong MainooContract = 0x7000_0000, YamalContract = 0x7000_1000;
    const ulong Holder = 0x7100_0000, Loan = 0x7100_1000;
    const ulong Other = 0x7200_0000;

    readonly TestWorld _w = new();
    readonly OffsetProfile _profile = OffsetProfile.LoadFile(
        Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));

    public PlayerSwapTests()
    {
        Set(TestWorld.Mainoo, TestWorld.ModuleBase + _profile.PlayerVtableRvas[0]);
        Set(TestWorld.Yamal, TestWorld.ModuleBase + _profile.PlayerVtableRvas[0]);
        Club(SellerClub, SellerFin, 680, balance: 100_000_000, budget: 20_000_000, wageBill: 3_000_000);
        Club(BuyerClub, BuyerFin, 868, balance: 50_000_000, budget: 5_000_000, wageBill: 2_000_000);
        Team(SellerTeam, SellerClub, SellerSquad, [Other, TestWorld.Mainoo, Other + 8], capacity: 4);
        Team(BuyerTeam, BuyerClub, BuyerSquad, [TestWorld.Yamal], capacity: 2);
        Contract(MainooContract, SellerTeam, 135000);
        Contract(YamalContract, BuyerTeam, 50000);
        Set(TestWorld.Mainoo + 0x330, MainooContract);
        Set(TestWorld.Mainoo + 0x130, SellerTeam);
        Set(TestWorld.Yamal + 0x330, YamalContract);
        Set(TestWorld.Yamal + 0x130, BuyerTeam);
    }

    void Set(ulong at, ulong value) => Assert.True(_w.Memory.TryWrite(at, BitConverter.GetBytes(value)));
    int I32(ulong at) => BitConverter.ToInt32(_w.Bytes(at, 4), 0);
    ulong Q(ulong at) => BitConverter.ToUInt64(_w.Bytes(at, 8), 0);

    void Club(ulong club, ulong fin, int id, int balance, int budget, int wageBill)
    {
        var c = _w.Memory.Map(club, 0x160);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + _profile.Club!.ClubVtableRva);
        BitConverter.GetBytes(id).CopyTo(c, 0xC);
        TestWorld.Set64(c, 0x150, fin);
        var f = _w.Memory.Map(fin, 0x820);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + _profile.Club.Finance!.VtableRva!.Value);
        BitConverter.GetBytes(balance).CopyTo(f, 0xC);
        BitConverter.GetBytes(budget).CopyTo(f, 0x7C4);
        BitConverter.GetBytes(wageBill).CopyTo(f, 0x810);
    }

    void Team(ulong team, ulong club, ulong squad, ulong[] players, int capacity)
    {
        var t = _w.Memory.Map(team, 0x50);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + _profile.Team!.VtableRva);
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
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + _profile.Contract!.ContractVtableRva);
        TestWorld.Set64(c, 0x10, team);
        TestWorld.Set64(c, 0x18, team);
        BitConverter.GetBytes(wage).CopyTo(c, 0x20);
    }

    IReadOnlyList<PlayerRecord> Players()
    {
        var repo = new PlayerRepository(_w.Memory, _profile, TestWorld.ModuleBase);
        return [repo.Reload(TestWorld.Mainoo)!, repo.Reload(TestWorld.Yamal)!];
    }

    IReadOnlyList<ClubEntry> Clubs() => ClubDirectory.FromPlayers(_w.Memory, Players(), _profile, TestWorld.ModuleBase);

    PlayerSwapService Service() => new(_w.Memory, _profile, TestWorld.ModuleBase);

    [Fact]
    public void Swaps_the_two_players_their_teams_and_their_wage_bills()
    {
        var swap = Service();
        var result = swap.Swap(Players()[0], Players()[1], Players, Clubs);

        Assert.Equal((868, 680), (result.First.Buyer.Id, result.Second.Buyer.Id));
        Assert.Equal(BuyerTeam, Q(MainooContract + 0x10));
        Assert.Equal(BuyerTeam, Q(MainooContract + 0x18));
        Assert.Equal(SellerTeam, Q(YamalContract + 0x10));
        Assert.Equal(SellerTeam, Q(YamalContract + 0x18));
        // Each player joins the team the other one played for.
        Assert.Equal(BuyerTeam, Q(TestWorld.Mainoo + 0x130));
        Assert.Equal(SellerTeam, Q(TestWorld.Yamal + 0x130));

        Assert.Equal(new[] { Other, Other + 8, TestWorld.Yamal },
            new[] { Q(SellerSquad), Q(SellerSquad + 8), Q(SellerSquad + 16) });
        Assert.Equal(SellerSquad + 24, Q(SellerTeam + 0x40));
        Assert.Equal(TestWorld.Mainoo, Q(BuyerSquad));
        Assert.Equal(BuyerSquad + 8, Q(BuyerTeam + 0x40));

        Assert.Equal(3_000_000 - 135_000 + 50_000, I32(SellerFin + 0x810));
        Assert.Equal(2_000_000 - 50_000 + 135_000, I32(BuyerFin + 0x810));
        Assert.Equal((100_000_000, 50_000_000), (I32(SellerFin + 0xC), I32(BuyerFin + 0xC)));
        Assert.Equal((0, 0), (I32(SellerFin + 0x29C), I32(BuyerFin + 0x29C)));
    }

    [Fact]
    public void Refuses_two_players_at_the_same_club()
    {
        Set(YamalContract + 0x10, SellerTeam);
        Set(YamalContract + 0x18, SellerTeam);
        Set(TestWorld.Yamal + 0x130, SellerTeam);
        int writes = _w.Memory.Writes;
        Assert.Throws<InvalidOperationException>(() => Service().Swap(Players()[0], Players()[1], Players, Clubs));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Refuses_a_player_who_is_on_loan()
    {
        // A loan shape: the playing team is not the contract team, the holder points at a loan contract.
        Set(TestWorld.Mainoo + 0x130, BuyerTeam);
        Set(TestWorld.Mainoo + 0x338, Holder);
        TestWorld.Set64(_w.Memory.Map(Holder, 8), 0, Loan);
        var loan = _w.Memory.Map(Loan, 0xD4);
        TestWorld.Set64(loan, 0, TestWorld.ModuleBase + _profile.FutureTransfers!.LoanContractVtable);
        TestWorld.Set64(loan, 0x10, BuyerTeam);
        var swap = Service();
        int writes = _w.Memory.Writes;
        var error = Assert.Throws<InvalidOperationException>(() => swap.Plan(Players()[0], swap.Target(Players()[1]), Clubs()));
        Assert.Contains("on loan", error.Message);
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Refuses_a_free_agent()
    {
        Set(TestWorld.Mainoo + 0x330, 0);
        Assert.Throws<InvalidOperationException>(() => Service().Plan(Players()[0], Service().Target(Players()[1]), Clubs()));
    }

    [Fact]
    public void Refuses_a_destination_team_that_is_not_in_the_destination_club()
    {
        var swap = Service();
        var target = swap.Target(Players()[1]) with { TeamAddress = SellerTeam };
        Assert.Throws<InvalidOperationException>(() => swap.Plan(Players()[0], target, Clubs()));
    }

    [Fact]
    public void A_failure_after_the_first_move_puts_the_first_player_back()
    {
        int reads = 0;
        IReadOnlyList<PlayerRecord> FailsOnce() => ++reads == 1 ? throw new IOException("reload failed") : Players();

        var error = Assert.Throws<InvalidOperationException>(() => Service().Swap(Players()[0], Players()[1], FailsOnce, Clubs));

        Assert.True(error.Message.Contains("was moved back"), error.Message);
        // Mainoo is at his old team again and both wage bills are what they were before the swap.
        Assert.Equal(SellerTeam, Q(MainooContract + 0x10));
        Assert.Equal(SellerTeam, Q(TestWorld.Mainoo + 0x130));
        Assert.Equal(BuyerTeam, Q(YamalContract + 0x10));
        Assert.Equal(3_000_000, I32(SellerFin + 0x810));
        Assert.Equal(2_000_000, I32(BuyerFin + 0x810));
        Assert.Equal(TestWorld.Yamal, Q(BuyerSquad));
        Assert.Equal(BuyerSquad + 8, Q(BuyerTeam + 0x40));
    }
}