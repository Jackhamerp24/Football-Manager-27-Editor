using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Relations of Oliver Arblaster as dumped from FM27 RAM (player + 0x300 → holder; inline 16-byte records; type at +0xB,
/// code at +0xC). Target pointers are re-pointed at fake objects. Expected values are FMRTE 27's Relationships page
/// (lane D, re/squad/statuses.md): registered with Sheffield United, Domestic (8); formed at club Sheffield United,
/// periods 1 ("3 Years (15-21)") and 10 ("Done"); formed at country England (1).
/// </summary>
public class PlayerStatusReaderTests
{
    const uint ClubVt = 0x4CDA1F8, TeamVt = 0x4CE1108, NationVt = 0x4CDF658;
    const ulong Holder = 0x5000_0000, Records = 0x5001_0000, Club = 0x5002_0000, Team = 0x5003_0000, Nation = 0x5004_0000, Other = 0x5005_0000;

    // Arblaster's 7 records: what the 8-byte target points at, then bytes +0x8..+0xF as dumped.
    static readonly (string Target, string Tail)[] Arblaster =
    [
        ("other",  "0A00030164 4F00FF"), ("club", "0100010364 4E00FF"), ("other", "6901071212 5A00FF"),
        ("club",   "0000014801 FF00FF"), ("club", "000001480A FF00FF"), ("nation", "0000084601 FF00FF"),
        ("team",   "0000041908 0000FF"),
    ];
    readonly TestWorld _w = new();

    public PlayerStatusReaderTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x300, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x18);
        TestWorld.Set64(h, 0, Records); TestWorld.Set64(h, 8, Records + 7 * 16); TestWorld.Set64(h, 0x10, Records + 7 * 16);
        var r = _w.Memory.Map(Records, 7 * 16);
        for (int i = 0; i < Arblaster.Length; i++)
        {
            ulong target = Arblaster[i].Target switch { "club" => Club, "team" => Team, "nation" => Nation, _ => Other };
            TestWorld.Set64(r, i * 16, target);
            Convert.FromHexString(Arblaster[i].Tail.Replace(" ", "")).CopyTo(r, i * 16 + 8);
        }
        var c = _w.Memory.Map(Club, 0xD0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(c, 0xC0, Club + 0x1000); TestWorld.Set64(c, 0xC8, Club + 0x1000);
        _w.Memory.MapString(Club + 0x1000, "Sheffield United");
        var t = _w.Memory.Map(Team, 0x40);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + TeamVt);
        TestWorld.Set64(t, 0x30, Club);
        var n = _w.Memory.Map(Nation, 0x30);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + NationVt);
        TestWorld.Set64(n, 0x18, Nation + 0x1000); TestWorld.Set64(n, 0x28, Nation + 0x1000);
        _w.Memory.MapString(Nation + 0x1000, "England");
        _w.Memory.Map(Other, 8);
    }

    PlayerStatuses Read() => new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase)
        .Read(new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!);

    [Fact]
    public void Reads_registration_and_formed_at_relations_as_FMRTE_shows()
    {
        var s = Read();
        var reg = Assert.Single(s.Registrations);
        Assert.Equal("Sheffield United", reg.Club!.Name);
        Assert.Equal(8, reg.Kind); // Domestic
        Assert.Equal([1, 10], s.TrainedAtClubs.Select(x => (int)x.Period)); // two rows, not merged
        Assert.All(s.TrainedAtClubs, x => Assert.Equal("Sheffield United", x.Club!.Name));
        var nation = Assert.Single(s.TrainedAtNations);
        Assert.Equal("England", nation.Nation!.Name);
        Assert.Equal(1, nation.Period);
    }

    [Fact]
    public void Wrong_target_class_is_skipped_and_a_bad_vector_gives_nothing()
    {
        // A type-72 record whose target is not a club must not be reported as a club.
        Assert.True(_w.Memory.TryWrite(Records + 3 * 16, BitConverter.GetBytes(Other)));
        Assert.Single(Read().TrainedAtClubs);
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Records + 7 * 16 + 1)));
        Assert.Same(PlayerStatuses.None, Read());
    }

    [Fact]
    public void SearchTrainingUsesObservedClubAndNationAndReportsInvalidTargets()
    {
        var player = new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var search = new PlayerRelationSearch(new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase));
        Assert.Equal(new[] { player.Address }, search.Find([player], new(TrainedAtClub: Club, TrainedAtNation: Nation)).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], new(TrainedAtNation: Other)).Matches);
        Assert.True(_w.Memory.TryWrite(Records + 5 * 16, BitConverter.GetBytes(Other)));
        var unknown = search.Find([player], new(TrainedAtNation: Nation));
        Assert.Empty(unknown.Matches);
        Assert.Equal(new[] { player.Address }, unknown.Unavailable.Select(p => p.Address));
    }
}
