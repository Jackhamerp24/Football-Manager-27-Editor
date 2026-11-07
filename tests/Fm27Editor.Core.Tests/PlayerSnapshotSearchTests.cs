using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerSnapshotSearchTests
{
    static PlayerRecord Player(ulong address, int central, int attacking, ulong? moves)
    {
        var positions = new byte[14]; positions[(int)PlayerPosition.MidfielderCentral] = (byte)central; positions[(int)PlayerPosition.AttackingMidCentral] = (byte)attacking;
        var attributes = new byte[57]; attributes[(int)PlayerAttribute.Passing] = 85;
        var personality = new byte[8]; personality[(int)PersonalityAttribute.Professionalism] = 16;
        return new PlayerRecord { Address = address, VtableRva = 1, CurrentAbility = 140, PotentialAbility = 151,
            HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 180,
            DateOfBirth = new DateOnly(2006, 1, 1), GameDate = new DateOnly(2026, 1, 1), InternationalCaps = 10, InternationalGoals = 2, AskingPrice = 3000000000,
            RawAttributes = attributes, AttributeScale = 5, Positions = positions, Personality = personality, PreferredMoveBits = moves };
    }

    [Fact]
    public void CombinesIndividualPositionRangesAttributesAndPreferredMoves()
    {
        var criteria = new PlayerSnapshotCriteria
        {
            Positions = new Dictionary<PlayerPosition, PlayerSearchRange> { [PlayerPosition.MidfielderCentral] = new(14, 14), [PlayerPosition.AttackingMidCentral] = new(16, 20) },
            Attributes = new Dictionary<PlayerAttribute, PlayerSearchRange> { [PlayerAttribute.Passing] = new(17, 17) },
            Personality = new Dictionary<PersonalityAttribute, PlayerSearchRange> { [PersonalityAttribute.Professionalism] = new(16, 20) },
            Fields = new Dictionary<PlayerSnapshotField, PlayerSearchRange> { [PlayerSnapshotField.Caps] = new(10, 10), [PlayerSnapshotField.Goals] = new(2, 2), [PlayerSnapshotField.AskingPrice] = new(3000000000, 3000000000) },
            RequiredPreferredMoves = 0b101, Wonderkid = true
        };
        var result = PlayerSnapshotSearch.Find([Player(1, 14, 16, 0b101), Player(2, 13, 16, 0b101), Player(3, 14, 16, null)], criteria);
        Assert.Equal(new ulong[] { 1 }, result.Matches.Select(x => x.Address));
        Assert.Equal(new ulong[] { 3 }, result.Unavailable.Select(x => x.Address));
        var any = criteria with { AnyPosition = true, RequiredPreferredMoves = 0 };
        Assert.Equal(new ulong[] { 1, 2 }, PlayerSnapshotSearch.Find([Player(1, 14, 16, 5), Player(2, 13, 16, 5)], any).Matches.Select(x => x.Address));
    }

    [Fact]
    public void IntersectionKeepsUnknownUnlessAnotherStageRulesItOut()
    {
        var players = new[] { Player(1, 14, 16, 5), Player(2, 14, 16, 5), Player(3, 14, 16, 5), Player(4, 14, 16, 5) };
        PlayerSearchStage First(IReadOnlyList<PlayerRecord> rows, CancellationToken _) => new(rows.Where(p => p.Address is 1 or 3).ToArray(), rows.Where(p => p.Address is 2 or 4).ToArray());
        PlayerSearchStage Second(IReadOnlyList<PlayerRecord> rows, CancellationToken _) => new(rows.Where(p => p.Address is 3 or 4).ToArray(), rows.Where(p => p.Address == 1).ToArray());
        var result = PlayerSearchIntersection.Find(players, [First, Second]);
        Assert.Equal(new ulong[] { 3 }, result.Matches.Select(p => p.Address));
        Assert.Equal(new ulong[] { 1, 4 }, result.Unavailable.Select(p => p.Address));
    }
}
