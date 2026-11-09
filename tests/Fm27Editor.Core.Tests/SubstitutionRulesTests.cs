using Fm27Editor.Core.Competitions;

namespace Fm27Editor.Core.Tests;

/// <summary>Values come from FMRTE 27 saving each Substitution Rules option on Premier League and a poller logging the byte at +0xFD (8/11/2026).</summary>
public class SubstitutionRulesTests
{
    [Fact]
    public void Every_FMRTE_option_has_its_own_value_and_label()
    {
        Assert.Equal(76, SubstitutionRules.All.Count);
        Assert.Equal(76, SubstitutionRules.All.Select(x => x.Value).Distinct().Count());
        Assert.Equal(76, SubstitutionRules.All.Select(x => x.Label).Distinct().Count());
    }

    [Theory]
    [InlineData(0xFF, "Do Not Override Sub Rules")]
    [InlineData(0x11, "5 from 9")]          // FMRTE wrote this on Premier League earlier
    [InlineData(0x13, "No Subs Allowed")]
    [InlineData(0x1E, "6 from 7")]          // selected at the moment the dropdown was read
    [InlineData(0x12, "15 from 15")]
    public void Observed_pairs(int value, string label) => Assert.Equal(label, SubstitutionRules.LabelOf((byte)value));
}
