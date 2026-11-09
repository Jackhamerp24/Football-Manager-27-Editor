using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

public class ClubSearchTests
{
    static ClubEntry Club(string name) => new(0x1000, 1, name, null, 0, []);

    [Fact]
    public void NameMatchesCaseInsensitivelyInEitherName()
    {
        var club = new ClubEntry(0x1000, 1, "Manchester United", "Man Utd", 0, []);
        Assert.True(ClubSearch.Matches(club, new ClubSearchCriteria(NameContains: "united"), _ => null));
        Assert.True(ClubSearch.Matches(club, new ClubSearchCriteria(NameContains: "man utd"), _ => null));
        Assert.False(ClubSearch.Matches(club, new ClubSearchCriteria(NameContains: "liverpool"), _ => null));
    }

    [Fact]
    public void EveryRangeIsInclusiveAndUnknownValuesExclude()
    {
        var values = new Dictionary<ClubField, long> { [ClubField.Balance] = 50_000_000, [ClubField.Training] = 18 };
        var club = Club("Arsenal");
        var criteria = new ClubSearchCriteria("Arsenal", new Dictionary<ClubField, (long, long)>
        {
            [ClubField.Balance] = (50_000_000, 50_000_000),
            [ClubField.Training] = (18, 20),
        });
        Assert.True(ClubSearch.Matches(club, criteria, field => values.TryGetValue(field, out long v) ? v : null));

        var strict = criteria with { Ranges = new Dictionary<ClubField, (long, long)> { [ClubField.Training] = (19, 20) } };
        Assert.False(ClubSearch.Matches(club, strict, field => values.TryGetValue(field, out long v) ? v : null));

        values.Remove(ClubField.Training);
        Assert.False(ClubSearch.Matches(club, criteria, field => values.TryGetValue(field, out long v) ? v : null));
    }

    [Fact]
    public void EmptyCriteriaMatchesEverything()
    {
        Assert.True(ClubSearch.Matches(Club("Anyone"), new ClubSearchCriteria(), _ => null));
    }
}
