using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Ids measured on Steam 27.0.1 (14 Mar 2027): database players up to 2,001,040,995 (Kobbie Mainoo 2,000,123,586, Senne
/// Lammens 18,107,436); the first created person 2,002,040,995; regens the hook caught as the game created them
/// 2,002,087,633–2,002,087,637.
/// </summary>
public class RegenIdsTests
{
    static readonly int[] Database = [18_107_436, 48_042_658, 2_000_123_586, 2_001_040_995];
    static readonly int[] Created = [2_002_040_995, 2_002_072_571, 2_002_087_633, 2_002_087_637];

    [Fact]
    public void The_created_block_starts_above_the_million_gap()
    {
        Assert.Equal(2_002_040_995, RegenIds.FirstCreatedId([.. Database, .. Created]));
    }

    [Fact]
    public void A_save_with_no_created_players_has_no_block()
    {
        Assert.Null(RegenIds.FirstCreatedId([18_107_436, 18_200_000, 18_300_000]));
    }

    [Fact]
    public void Players_are_regens_by_their_id()
    {
        RegenIds.Current = RegenIds.FirstCreatedId([.. Database, .. Created]);
        try
        {
            PlayerRecord P(int id) => new() { Address = 1, VtableRva = 0, Id = id, CurrentAbility = 1, PotentialAbility = 1, HomeReputation = 1,
                CurrentReputation = 1, WorldReputation = 1, Height = 1, RawAttributes = [], AttributeScale = 5, Positions = [], Personality = [] };
            Assert.False(RegenIds.IsRegen(P(2_000_123_586)));
            Assert.True(RegenIds.IsRegen(P(2_002_087_633)));
        }
        finally { RegenIds.Current = null; }
    }
}
