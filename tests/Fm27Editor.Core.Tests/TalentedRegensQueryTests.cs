using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class TalentedRegensQueryTests
{
    static PlayerRecord MakeCandidate(
        int id, short pa, short ca, int age, bool hasContract, Gender gender = Gender.Men, string name = "Regen")
    {
        var gameDate = new DateOnly(2027, 7, 1);
        var dob = new DateOnly(2027 - age, 1, 1);
        return new PlayerRecord
        {
            Address = (ulong)id,
            VtableRva = 1,
            Id = id,
            FirstName = name,
            LastName = $"#{id}",
            CurrentAbility = ca,
            PotentialAbility = pa,
            HomeReputation = 100,
            CurrentReputation = 100,
            WorldReputation = 100,
            Height = 178,
            DateOfBirth = dob,
            GameDate = gameDate,
            Gender = gender,
            Contract = hasContract ? new ContractInfo(0x9000, 1000, new DateOnly(2030, 6, 30), null, null) : null,
            RawAttributes = Enumerable.Repeat((byte)50, 64).ToArray(),
            AttributeScale = 5,
            Positions = new byte[14],
            Personality = Enumerable.Repeat((byte)10, 8).ToArray(),
        };
    }

    [Fact]
    public void UnavailableWhenRegenFloorIsMissing()
    {
        int? previous = RegenIds.Current;
        try
        {
            RegenIds.Current = null;
            var res = TalentedRegensQuery.Find([MakeCandidate(2_002_000_001, 180, 120, 18, false)], new TalentedRegenCriteria());
            Assert.True(res.RegenIdentificationUnavailable);
            Assert.Empty(res.Matches);
        }
        finally
        {
            RegenIds.Current = previous;
        }
    }

    [Fact]
    public void FiltersByRegenFloorPaAgeFreeAgentAndGenderAndSortsByPotential()
    {
        const int floor = 2_002_000_000;
        var list = new[]
        {
            MakeCandidate(2_001_000_500, 190, 140, 19, false),               // real db player (below floor)
            MakeCandidate(2_002_000_001, 149, 100, 17, false),               // PA below default 150
            MakeCandidate(2_002_000_002, 170, 110, 24, false),               // Age > 23
            MakeCandidate(2_002_000_003, 175, 115, 23, true),                // contracted regen age 23
            MakeCandidate(2_002_000_004, 185, 130, 18, false),               // free-agent regen
            MakeCandidate(2_002_000_005, 185, 130, 17, false, Gender.Women), // younger women's free-agent regen
        };

        var all = TalentedRegensQuery.Find(list, new TalentedRegenCriteria(MinPotentialAbility: 150, MaxAge: 23), floor);
        Assert.False(all.RegenIdentificationUnavailable);
        Assert.Equal([2_002_000_005, 2_002_000_004, 2_002_000_003], all.Matches.Select(m => m.Player.Id!.Value));

        var freeMen = TalentedRegensQuery.Find(
            list,
            new TalentedRegenCriteria(MinPotentialAbility: 150, MaxAge: 23, FreeAgentsOnly: true, Gender: Gender.Men),
            floor);
        var only = Assert.Single(freeMen.Matches);
        Assert.Equal(2_002_000_004, only.Player.Id);
    }
}
