using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PersonalityClassifierTests
{
    public static IEnumerable<object[]> All39PersonalitiesAndDefault()
    {
        // (id, name, isNegative, isRegen, age, amb, loy, pre, pro, spo, tem, det, lea)
        yield return [1, "Balanced", false, false, 22, 10, 10, 10, 10, 10, 10, 10, 10];
        yield return [12, "Fairly Professional", false, false, 22, 10, 10, 10, 16, 10, 10, 10, 10];
        yield return [12, "Fairly Professional", false, false, 22, 10, 10, 10, 18, 10, 9, 10, 10];
        yield return [11, "Fairly Loyal", false, false, 22, 10, 16, 10, 10, 10, 10, 10, 10];
        yield return [9, "Fairly Ambitious", false, false, 22, 16, 10, 10, 10, 10, 10, 10, 10];
        yield return [9, "Fairly Ambitious", false, false, 22, 15, 5, 10, 10, 10, 10, 10, 10];
        yield return [10, "Fairly Determined", false, true, 22, 10, 10, 10, 10, 10, 10, 16, 10];
        yield return [10, "Fairly Determined", false, false, 22, 10, 10, 10, 10, 10, 10, 16, 10];
        yield return [13, "Fairly Sporting", false, false, 22, 10, 10, 10, 10, 16, 10, 10, 10];
        yield return [32, "Spineless", true, true, 20, 10, 10, 1, 10, 10, 10, 5, 10];
        yield return [21, "Low Self Belief", false, true, 20, 10, 10, 2, 10, 10, 10, 5, 10];
        yield return [30, "Resolute", false, false, 22, 10, 10, 10, 16, 10, 10, 16, 10];
        yield return [33, "Spirited", false, false, 22, 10, 10, 16, 15, 10, 12, 10, 10];
        yield return [17, "Jovial", false, false, 22, 10, 10, 16, 8, 10, 12, 10, 10];
        yield return [19, "Light-Hearted", false, false, 22, 10, 10, 16, 12, 16, 12, 10, 10];
        yield return [20, "Low Determination", false, true, 20, 5, 10, 10, 10, 10, 10, 3, 10];
        yield return [29, "Resilient", false, false, 22, 10, 10, 18, 10, 10, 10, 16, 10];
        yield return [16, "Iron Willed", false, false, 22, 10, 10, 20, 10, 10, 10, 16, 10];
        yield return [22, "Loyal", false, false, 22, 6, 18, 10, 10, 10, 10, 10, 10];
        yield return [6, "Very Loyal", false, false, 22, 6, 20, 10, 10, 10, 10, 10, 10];
        yield return [15, "Honest", false, false, 22, 10, 10, 10, 10, 20, 10, 5, 10];
        yield return [34, "Sporting", false, false, 22, 10, 10, 10, 10, 18, 10, 5, 10];
        yield return [37, "Unsporting", true, true, 20, 10, 10, 10, 12, 1, 10, 12, 10];
        yield return [28, "Realist", false, true, 20, 10, 10, 10, 12, 3, 10, 12, 10];
        yield return [8, "Easily Discouraged", false, true, 20, 10, 10, 10, 12, 10, 10, 1, 10];
        yield return [36, "Unambitious", false, true, 20, 4, 15, 10, 12, 10, 10, 10, 10];
        yield return [0, "Ambitious", false, false, 22, 18, 5, 10, 12, 10, 10, 10, 10];
        yield return [5, "Determined", false, false, 22, 10, 10, 10, 12, 10, 10, 19, 10];
        yield return [7, "Driven", false, false, 22, 15, 10, 10, 12, 10, 10, 19, 10];
        yield return [38, "Very Ambitious", false, false, 22, 20, 5, 10, 12, 10, 10, 10, 10];
        yield return [23, "Mercenary", false, true, 20, 18, 2, 10, 12, 10, 10, 10, 10];
        yield return [14, "Fickle", false, true, 20, 18, 5, 10, 12, 10, 10, 10, 10];
        yield return [25, "Model Professional", false, false, 24, 10, 10, 10, 20, 10, 12, 10, 10];
        yield return [18, "Leader", false, false, 24, 10, 10, 10, 10, 10, 10, 10, 19];
        yield return [4, "Charismatic Leader", false, false, 24, 10, 10, 10, 10, 19, 19, 10, 19];
        yield return [2, "Born Leader", false, false, 24, 10, 10, 10, 10, 10, 10, 20, 20];
        yield return [27, "Professional", false, false, 20, 10, 10, 10, 20, 10, 12, 10, 10];
        yield return [27, "Professional", false, false, 24, 10, 10, 10, 18, 10, 12, 10, 10];
        yield return [24, "Model Citizen", false, false, 24, 15, 16, 15, 16, 16, 16, 15, 10];
        yield return [26, "Perfectionist", false, false, 24, 16, 10, 10, 16, 10, 8, 16, 10];
        yield return [3, "Casual", true, true, 20, 10, 10, 10, 3, 10, 10, 5, 10];
        yield return [31, "Slack", true, true, 20, 10, 10, 10, 1, 10, 10, 5, 10];
        yield return [35, "Temperamental", true, true, 20, 10, 10, 10, 8, 10, 3, 10, 10];
        // amb=5 fails Fairly Loyal (5 < amb), loy=15 fails Balanced (loy < 15), det=10, pro=10 -> hits fallback 255
        yield return [255, "Unclassified", false, false, 20, 5, 15, 10, 10, 10, 10, 10, 10];
    }

    [Theory]
    [MemberData(nameof(All39PersonalitiesAndDefault))]
    public void ClassifiesExpectedPersonality(
        int expectedId, string expectedName, bool expectedNegative,
        bool isRegen, int age, int amb, int loy, int pre, int pro, int spo, int tem, int det, int lea)
    {
        var result = PersonalityClassifier.Classify(isRegen, age, amb, loy, pre, pro, spo, tem, det, lea);
        Assert.Equal(expectedId, result.Id);
        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedNegative, result.IsNegative);
    }

    [Fact]
    public void NegativePersonalitiesRequireRegenFlag()
    {
        int[] negativeIds = [3, 31, 32, 35, 37];
        Assert.Equal(negativeIds, PersonalityClassifier.NegativePersonalityIds);

        // Identical attributes on non-regen must never produce negative personality IDs
        var inputs = new[]
        {
            (10, 10, 10, 3, 10, 10, 5, 10),   // Casual on regen
            (10, 10, 10, 1, 10, 10, 5, 10),   // Slack on regen
            (10, 10, 1, 10, 10, 10, 5, 10),   // Spineless on regen
            (10, 10, 10, 8, 10, 3, 10, 10),   // Temperamental on regen
            (10, 10, 10, 12, 1, 10, 12, 10),  // Unsporting on regen
        };
        foreach (var (amb, loy, pre, pro, spo, tem, det, lea) in inputs)
        {
            var regenRes = PersonalityClassifier.Classify(true, 20, amb, loy, pre, pro, spo, tem, det, lea);
            Assert.True(regenRes.IsNegative);
            var realRes = PersonalityClassifier.Classify(false, 20, amb, loy, pre, pro, spo, tem, det, lea);
            Assert.False(realRes.IsNegative);
        }
    }

    [Fact]
    public void Age24BoundaryControlsProfessionalVsModelProfessional()
    {
        var young = PersonalityClassifier.Classify(false, 23, 10, 10, 10, 20, 10, 12, 10, 10);
        var mature = PersonalityClassifier.Classify(false, 24, 10, 10, 10, 20, 10, 12, 10, 10);
        Assert.Equal("Professional", young.Name);
        Assert.Equal("Model Professional", mature.Name);
    }
}
