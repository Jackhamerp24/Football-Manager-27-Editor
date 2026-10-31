using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

public sealed class ContractYearInputTests
{
    [Fact]
    public void Sentinel_year_is_blank_but_a_real_year_is_shown()
    {
        Assert.Equal("", ContractYearInput.Format(1900));
        Assert.Equal("", ContractYearInput.Format(null));
        Assert.Equal("2029", ContractYearInput.Format(2029));
    }

    [Theory]
    [InlineData(1900, "")]
    [InlineData(2029, "2029")]
    public void Clearing_year_removes_the_year_edit_and_preserves_snapshot_on_save(int original, string display)
    {
        Assert.Equal(display, ContractYearInput.NormalizeDraftValue("", original));
        Assert.Equal(display, ContractYearInput.NormalizeDraftValue("  ", original));
        Assert.Equal(original, ContractYearInput.Resolve("", original));
        Assert.Equal(original, ContractYearInput.Resolve(null, original));
        Assert.Equal(original, ContractYearInput.Resolve("  ", original));
    }

    [Fact]
    public void Explicit_year_is_kept_and_validated_before_save()
    {
        Assert.Equal("2031", ContractYearInput.NormalizeDraftValue("2031", 2029));
        Assert.Equal(2031, ContractYearInput.Resolve("2031", 2029));
        Assert.Equal(1901, ContractYearInput.Resolve("1901", 1900));
        Assert.Equal(2300, ContractYearInput.Resolve("2300", 1900));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractYearInput.Resolve("1900", 2029));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractYearInput.Resolve("2301", 2029));
        Assert.Throws<FormatException>(() => ContractYearInput.Resolve("invalid", 2029));
    }
}
