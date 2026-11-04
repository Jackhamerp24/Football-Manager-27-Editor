using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// National squads as found on 15/8/2026: team objects with vtable +0x4DC2288 in the team list at module+0x5703A60, nation
/// football object at +0x30 (Portugal, index 0xA2), squad vector at +0x38. FMRTE 27's Swap National Call Up of Bruno Fernandes
/// with Fábio Vieira (not called up) rewrote only Bruno's slot, with Vieira's pointer (2/11/2026).
/// </summary>
public class NationalCallUpTests
{
    const uint NationalVt = 0x4DC2288, ClubTeamVt = 0x4CE1108;
    const ulong List = 0x5000_0000, Portugal = 0x5001_0000, England = 0x5001_1000, ClubTeam = 0x5001_2000;
    const ulong PortugalSquad = 0x5002_0000, EnglandSquad = 0x5002_1000, ClubSquad = 0x5002_2000;
    const ulong PortugalFootball = 0x5003_0000, EnglandFootball = 0x5003_1000;
    readonly TestWorld _w = new();

    void Team(ulong at, uint vt, ulong football, ulong squad, params ulong[] players)
    {
        var t = _w.Memory.Map(at, 0x50);
        TestWorld.Set64(t, 0, TestWorld.ModuleBase + vt);
        TestWorld.Set64(t, 0x30, football);
        ulong end = squad + (ulong)players.Length * 8;
        TestWorld.Set64(t, 0x38, squad); TestWorld.Set64(t, 0x40, end); TestWorld.Set64(t, 0x48, end);
        var s = _w.Memory.Map(squad, Math.Max(8, players.Length * 8));
        for (int i = 0; i < players.Length; i++) TestWorld.Set64(s, i * 8, players[i]);
    }

    public NationalCallUpTests()
    {
        // Mainoo plays the role of Bruno (in Portugal's squad), Yamal of England's player; a club team holding Yamal is ignored.
        Team(Portugal, NationalVt, PortugalFootball, PortugalSquad, 0x9999_0000, TestWorld.Mainoo);
        Team(England, NationalVt, EnglandFootball, EnglandSquad, TestWorld.Yamal);
        Team(ClubTeam, ClubTeamVt, 0, ClubSquad, TestWorld.Yamal);
        var l = _w.Memory.Map(List, 24);
        TestWorld.Set64(l, 0, Portugal); TestWorld.Set64(l, 8, England); TestWorld.Set64(l, 16, ClubTeam);
        var root = _w.Memory.Map(TestWorld.ModuleBase + 0x5703A60, 16);
        TestWorld.Set64(root, 0, List); TestWorld.Set64(root, 8, List + 24);
    }

    PlayerRecord Load(ulong a) => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(a)!;
    NationalCallUpReader Reader() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    NationalCallUpWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Finds_national_squads_only()
    {
        var all = Reader().All();
        Assert.Equal((Portugal, PortugalFootball, PortugalSquad + 8), (all[TestWorld.Mainoo].Team, all[TestWorld.Mainoo].NationFootball, all[TestWorld.Mainoo].Slot));
        Assert.Equal(England, all[TestWorld.Yamal].Team);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Swapping_with_an_uncalled_player_rewrites_one_slot_like_FMRTE()
    {
        var callUps = Reader().All();
        const ulong uncalled = 0x1000_3000;
        _w.Memory.Map(uncalled, 0x10);
        Assert.True(_w.Memory.TryWrite(uncalled, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.PlayerVtable)));
        var vieira = new PlayerRecord
        {
            Address = uncalled, VtableRva = TestWorld.PlayerVtable, CurrentAbility = 1, PotentialAbility = 1, HomeReputation = 0, CurrentReputation = 0,
            WorldReputation = 0, Height = 0, RawAttributes = new byte[64], AttributeScale = 5, Positions = new byte[14], Personality = new byte[8],
        };
        var before = _w.Bytes(PortugalSquad, 16);
        Writer().Swap(Load(TestWorld.Mainoo), callUps[TestWorld.Mainoo], vieira, null);
        BitConverter.GetBytes(uncalled).CopyTo(before, 8);
        Assert.Equal(before, _w.Bytes(PortugalSquad, 16));
        Assert.Equal(TestWorld.Yamal, BitConverter.ToUInt64(_w.Bytes(EnglandSquad, 8)));
    }

    [Fact]
    public void Swapping_two_called_players_exchanges_their_slots_and_refuses_stale_ones()
    {
        var callUps = Reader().All();
        Writer().Swap(Load(TestWorld.Mainoo), callUps[TestWorld.Mainoo], Load(TestWorld.Yamal), callUps[TestWorld.Yamal]);
        Assert.Equal(TestWorld.Yamal, BitConverter.ToUInt64(_w.Bytes(PortugalSquad + 8, 8)));
        Assert.Equal(TestWorld.Mainoo, BitConverter.ToUInt64(_w.Bytes(EnglandSquad, 8)));
        Assert.Throws<StaleObjectException>(() => Writer().Swap(Load(TestWorld.Mainoo), callUps[TestWorld.Mainoo], Load(TestWorld.Yamal), callUps[TestWorld.Yamal]));
        Assert.Throws<InvalidOperationException>(() => Writer().Swap(Load(TestWorld.Mainoo), null, Load(TestWorld.Yamal), null));
    }
}
