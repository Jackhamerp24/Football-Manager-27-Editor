using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

/// <summary>Starring a player only remembers his ID; the favourites filter keeps exactly the starred players and a star never touches game memory.</summary>
public class FavoriteTests : IDisposable
{
    const int Starred = 1_900_000_001, Plain = 1_900_000_002;

    public void Dispose()
    {
        FavoriteStore.Set(Starred, false);
        FavoriteStore.Set(Plain, false);
    }

    static PlayerRecord Player(int id) => new()
    {
        Address = 0x1000, VtableRva = 1, Id = id, CommonName = "Test", CurrentAbility = 100, PotentialAbility = 120,
        HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 0, RawAttributes = new byte[64], AttributeScale = 5,
        Positions = new byte[20], Personality = new byte[10],
    };

    [Fact]
    public void A_star_is_remembered_and_can_be_removed()
    {
        Assert.False(FavoriteStore.Contains(Starred));
        FavoriteStore.Set(Starred, true);
        Assert.True(FavoriteStore.Contains(Starred));
        Assert.False(FavoriteStore.Contains(Plain));
        FavoriteStore.Set(Starred, false);
        Assert.False(FavoriteStore.Contains(Starred));
        Assert.False(FavoriteStore.Contains(null));
    }

    [Fact]
    public void The_file_holds_the_ids_so_a_new_session_sees_them()
    {
        FavoriteStore.Set(Starred, true);
        var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "favorites.json")))!;
        Assert.Contains(Starred, ids);
    }
}
