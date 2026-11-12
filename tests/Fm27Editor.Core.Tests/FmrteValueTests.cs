using Fm27Editor.Core.Calibration;
using Xunit;

namespace Fm27Editor.Core.Tests;

public class FmrteValueTests
{
    // Inputs are as FMRTE 27 shows them (Ivan Perišić's proposal: "6/30/2028", "£742,685", "1/1/2027").
    [Theory]
    [InlineData("6/30/2028", "2028-06-30")]
    [InlineData("1/1/2027", "2027-01-01")]
    [InlineData("12/10/2026", "2026-12-10")]
    [InlineData("£742,685", "742685")]
    [InlineData("£69,241", "69241")]
    [InlineData("£1,000", "1000")]
    [InlineData("1,250", "1250")]
    [InlineData("true", "1")]
    [InlineData("False", "0")]
    [InlineData("Listed", "Listed")]
    [InlineData("150", "150")]
    [InlineData("13/30/2028", "13/30/2028")]
    [InlineData("2/30/2028", "2/30/2028")]
    public void Normalize_turns_fmrte_text_into_comparable_values(string fmrte, string expected) =>
        Assert.Equal(expected, FmrteValue.Normalize(fmrte));
}
