using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class GamePassClubReadingTests
{
    [Fact]
    public void Reads_two_clubs_and_their_squads_from_game_pass_players()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000;
        void MapClub(ulong start, int playerId, string clubHeader, string name, string shortName, int count)
        {
            ulong player = start, contract = start + 0x1000, team = start + 0x2000, club = start + 0x3000,
                squad = start + 0x4000, namePtr = start + 0x5000, shortPtr = start + 0x6000;
            var p = memory.Map(player, 0x3C8);
            BitConverter.GetBytes(module + 0x4CD6318u).CopyTo(p, 0);
            BitConverter.GetBytes(playerId).CopyTo(p, 0x294);
            BitConverter.GetBytes(team).CopyTo(p, 0x130);
            BitConverter.GetBytes(contract).CopyTo(p, 0x330);
            var c = memory.Map(contract, 0x60);
            BitConverter.GetBytes(module + 0x4AC05F8u).CopyTo(c, 0);
            BitConverter.GetBytes(team).CopyTo(c, 0x10);
            var t = memory.Map(team, 0x70);
            BitConverter.GetBytes(module + 0x4C90A78u).CopyTo(t, 0);
            BitConverter.GetBytes(club).CopyTo(t, 0x30);
            BitConverter.GetBytes(squad).CopyTo(t, 0x38);
            BitConverter.GetBytes(squad + (ulong)(count * 8)).CopyTo(t, 0x40);
            BitConverter.GetBytes(squad + (ulong)(count * 8)).CopyTo(t, 0x48);
            BitConverter.GetBytes(player).CopyTo(memory.Map(squad, count * 8), 0);
            var cb = memory.Map(club, 0xD0);
            Convert.FromHexString(clubHeader).CopyTo(cb, 0);
            BitConverter.GetBytes(module + 0x4C89B68u).CopyTo(cb, 0);
            BitConverter.GetBytes(namePtr).CopyTo(cb, 0xC0);
            BitConverter.GetBytes(shortPtr).CopyTo(cb, 0xC8);
            memory.MapString(namePtr, name);
            memory.MapString(shortPtr, shortName);
        }
        // IDs, names and squad counts from the two Game Pass clubs on 4/11/2026.
        MapClub(0x10000000, 19334410, "689B7EDDFA7F000099010000A8020000", "Manchester United", "Man Utd", 30);
        MapClub(0x20000000, 2000256231, "689B7EDDFA7F000080040000AC060000", "Barcelona", "Barcelona", 31);
        var repo = new PlayerRepository(memory, profile, module);
        var players = new[] { repo.Reload(0x10000000)!, repo.Reload(0x20000000)! };
        Assert.NotNull(players[0].Club);
        Assert.NotNull(players[1].Club);
        Assert.Equal("Man Utd", players[0].Club!.DisplayName);
        Assert.Equal("Barcelona", players[1].Club!.DisplayName);
        var clubs = ClubDirectory.FromPlayers(memory, players, profile, module);
        var united = Assert.Single(clubs, c => c.Id == 680);
        var barcelona = Assert.Single(clubs, c => c.Id == 1708);
        Assert.Equal("Manchester United", united.Name);
        Assert.Equal("Barcelona", barcelona.Name);
        Assert.Equal(30, united.FirstTeam!.SquadCount);
        Assert.Equal(31, barcelona.FirstTeam!.SquadCount);
    }
}
