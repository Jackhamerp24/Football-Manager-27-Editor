using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Competition loading when the game-wide list slot is missing or points at something else. Measured on Game Pass
/// 27.0.0+2493426 (PID 8776, 8/11/2026): the profile's list slot held 502 squad-rule containers, so the list path returned 0
/// competitions, while taking [team + 0x50] of every team and walking neighbours at 0x1B0 (what FM Studio 1.1.1 does) found
/// 8,213, Premier League (index 7, ID 11) among them. Index/ID pairs below are FMRTE 27's (Premier League 7/11, Championship
/// 8/12, Süper Lig 224/130286).
/// </summary>
public class CompetitionDiscoveryTests
{
    const ulong Block = 0x7000_0000, TeamAddress = 0x7100_0000, Beyond = Block + 3 * 0x1B0, WrongList = 0x7200_0000, WrongRoot = 0x7300_0000;
    const ulong ListRootObject = 0x7400_0000, ListArray = 0x7500_0000;

    readonly TestWorld _w = new();

    ulong CompetitionVtable => TestWorld.ModuleBase + _w.Profile.Competition!.VtableRva;

    void Competition(ulong at, int index, int id, string name)
    {
        var o = _w.Memory.Map(at, 0x1B0);
        TestWorld.Set64(o, 0, CompetitionVtable);
        BitConverter.GetBytes(index).CopyTo(o, 8);
        BitConverter.GetBytes(id).CopyTo(o, 0xC);
        TestWorld.Set64(o, 0x40, at + 0x4000); _w.Memory.MapString(at + 0x4000, name);
        TestWorld.Set64(o, 0x48, at + 0x4100); _w.Memory.MapString(at + 0x4100, name);
    }

    /// <summary>Three neighbours at the object size; the team of Mainoo points at the middle one, so both directions are needed.</summary>
    void ThreeNeighboursReachedFromAPlayersTeam()
    {
        Competition(Block, 7, 11, "Premier League");
        Competition(Block + 0x1B0, 8, 12, "Sky Bet Championship");
        Competition(Block + 2 * 0x1B0, 224, 130286, "Trendyol Süper Lig");
        // The next slot is some other kind of object: the walk must stop there.
        var other = _w.Memory.Map(Beyond, 0x1B0);
        TestWorld.Set64(other, 0, TestWorld.ModuleBase + TestWorld.OtherVtable);
        var team = _w.Memory.Map(TeamAddress, 0x100);
        TestWorld.Set64(team, _w.Profile.Competition!.TeamCompetition, Block + 0x1B0);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + (ulong)_w.Profile.Team!.PlayerTeam, BitConverter.GetBytes(TeamAddress)));
    }

    CompetitionRepository Repository => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    void ListSlotPointsAtOtherObjects()
    {
        var c = _w.Profile.Competition!;
        var other = _w.Memory.Map(WrongList, 0x10);
        TestWorld.Set64(other, 0, WrongRoot);
        TestWorld.Set64(other, 8, WrongRoot + 0x100);
        var wrong = _w.Memory.Map(WrongRoot, 0x100);
        TestWorld.Set64(wrong, 0, TestWorld.ModuleBase + TestWorld.OtherVtable);
        var root = _w.Memory.Map(ListRootObject, 0x100);
        TestWorld.Set64(root, c.ListOffset!.Value, ListArray);
        TestWorld.Set64(root, c.ListOffset.Value + 8, ListArray + 8);
        TestWorld.Set64(_w.Memory.Map(ListArray, 8), 0, WrongRoot);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + (ulong)c.ListRootRva!.Value, 8), 0, ListRootObject);
    }

    [Fact]
    public void A_list_slot_holding_other_objects_falls_back_to_the_teams_and_their_neighbours()
    {
        ThreeNeighboursReachedFromAPlayersTeam();
        ListSlotPointsAtOtherObjects();
        var found = Repository.LoadAll();
        Assert.Equal(new[] { 7, 8, 224 }, found.Select(c => c.Index));
        Assert.Equal(new[] { 11, 12, 130286 }, found.Select(c => c.Id));
        Assert.Equal(["Premier League", "Sky Bet Championship", "Trendyol Süper Lig"], found.Select(c => c.Name));
    }

    [Fact]
    public void An_empty_list_slot_also_falls_back()
    {
        ThreeNeighboursReachedFromAPlayersTeam();
        var found = Repository.LoadAll();
        Assert.Equal(new[] { 11, 12, 130286 }, found.Select(c => c.Id));
    }

    [Fact]
    public void A_working_list_wins_over_the_team_walk()
    {
        ThreeNeighboursReachedFromAPlayersTeam();
        WorkingList();
        var found = Repository.LoadAll();
        Assert.Equal(new[] { 130286 }, found.Select(x => x.Id));
    }

    /// <summary>
    /// The player list is the expensive part of the team fallback (the app already holds it), so the repository must
    /// not ask for it when the game-wide list works. Two working views used to reload it once per view.
    /// </summary>
    [Fact]
    public void A_working_list_does_not_ask_for_the_players()
    {
        ThreeNeighboursReachedFromAPlayersTeam();
        WorkingList();
        int loads = 0;
        var found = Repository.LoadAll(() =>
        {
            loads++;
            return new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).LoadAll();
        });
        Assert.Equal(0, loads);
        Assert.Equal(new[] { 130286 }, found.Select(x => x.Id));
    }

    [Fact]
    public void The_team_fallback_uses_the_players_it_is_given()
    {
        ThreeNeighboursReachedFromAPlayersTeam();
        int loads = 0;
        var found = Repository.LoadAll(() =>
        {
            loads++;
            return new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).LoadAll();
        });
        Assert.Equal(1, loads);
        Assert.Equal(new[] { 11, 12, 130286 }, found.Select(x => x.Id));
    }

    void WorkingList()
    {
        var c = _w.Profile.Competition!;
        var root = _w.Memory.Map(ListRootObject, 0x100);
        TestWorld.Set64(root, c.ListOffset!.Value, ListArray);
        TestWorld.Set64(root, c.ListOffset.Value + 8, ListArray + 8);
        TestWorld.Set64(_w.Memory.Map(ListArray, 8), 0, Block + 2 * 0x1B0);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + (ulong)c.ListRootRva!.Value, 8), 0, ListRootObject);
    }
}
