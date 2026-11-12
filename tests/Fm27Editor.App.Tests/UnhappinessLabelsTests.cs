using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

/// <summary>Expected names are FMRTE 27's own (its language file and its Contract tab on Bruno Fernandes and Eduardo Camavinga).</summary>
public class UnhappinessLabelsTests
{
    [Fact]
    public void Motives_carry_FMRTE_names_and_whether_they_name_a_person()
    {
        Assert.Equal(471, UnhappinessMotives.All.Count);
        Assert.Equal("Unhappy about club finances", UnhappinessLabels.Motive(500));
        Assert.False(UnhappinessLabels.NeedsPerson(500));
        Assert.Equal("Unhappy at being stripped of the captaincy", UnhappinessLabels.Motive(600));
        Assert.Equal("Thinks 'Person' should be given the playing time proposed to him", UnhappinessLabels.Motive(802));
        Assert.True(UnhappinessLabels.NeedsPerson(802));
        Assert.Equal("Motive 9549", UnhappinessLabels.Motive(9549));
    }

    /// <summary>
    /// Codes are 1-based: FMRTE shows Concerned for Tahith Chong's two motives, whose type byte is 1 (Steam 27.0.1,
    /// 15/12/2026). The earlier "Internal at 1" came from reading records back to back, which picked up other records.
    /// </summary>
    [Fact]
    public void Types_follow_FMRTE_order_from_code_1()
    {
        Assert.Equal(["Concerned", "Internal", "Public", "Public", "Asked to leave", "Alienated", "Slight Concern"],
            UnhappinessLabels.TypeCodes.Select(UnhappinessLabels.Type));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], UnhappinessLabels.TypeCodes.Select(c => (int)c));
        Assert.Equal("Type 0", UnhappinessLabels.Type(0));
        Assert.Equal("Type 9", UnhappinessLabels.Type(9));
    }
}
