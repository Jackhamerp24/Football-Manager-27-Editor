namespace Fm27Editor.Core.Players;

/// <summary>Editor convention supplied by the user; not the game's media description.</summary>
public static class WonderkidSearch
{
    public static bool? IsWonderkid(PlayerRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.PotentialAbility <= 150) return false;
        return player.Age is { } age ? age <= 20 : null;
    }
}
