namespace Fm27Editor.Core.Players;

/// <summary>
/// Tells regens (players the game created: youth intakes and the players it generates at the start of a save) from the
/// players of the database. The game gives every person it creates the next id after the database's highest id plus
/// 1,000,000, so the ids of created people form one block above a gap of about a million.
/// </summary>
/// <remarks>
/// Measured on Steam 27.0.1 (save at 14 Mar 2027): the database's highest player id is 2,001,040,995 and the first created
/// person has 2,002,040,995; above it 2,891 players with gaps of at most 11,463 between neighbours (staff take ids from the
/// same counter). All 64 regens the Wonderkid Academy hook caught as the game created them are in that block; Kobbie Mainoo
/// (2,000,123,586, a database player) is below it. The first id of the block is found per save, not fixed.
/// </remarks>
public static class RegenIds
{
    /// <summary>A gap in ids at least this wide, walking down from the highest id, ends the block of created people.</summary>
    public const long MinGap = 500_000;

    /// <summary>The lowest id of the created block, or null when no such gap exists (no created players yet).</summary>
    public static int? FirstCreatedId(IEnumerable<int> ids)
    {
        var sorted = ids.Where(id => id > 0).Distinct().OrderByDescending(id => id).ToList();
        for (int i = 1; i < sorted.Count; i++)
            if ((long)sorted[i - 1] - sorted[i] >= MinGap) return sorted[i - 1];
        return null;
    }

    /// <summary>The block's first id for the player list loaded last; null until a list was loaded or when it has no regens.</summary>
    public static int? Current { get; set; }

    /// <summary>Whether the player is a regen by <see cref="Current"/>; null when that is not known.</summary>
    public static bool? IsRegen(PlayerRecord player) =>
        Current is not { } first || player.Id is not { } id ? null : id >= first;
}
