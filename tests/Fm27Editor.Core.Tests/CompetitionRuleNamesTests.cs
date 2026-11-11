using Fm27Editor.Core.Competitions;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The rule-type names come from the FM26 Cheat Engine selectionRulesTypes table; the low codes that
/// used to be unknown must resolve, and codes the table does not carry must stay null.
/// </summary>
public class CompetitionRuleNamesTests
{
    [Theory]
    [InlineData(0x02, "Foreign Players In Playing 11")]
    [InlineData(0x04, "Foreign Players In Match Squad")]
    [InlineData(0x09, "Max Squad Size")]
    [InlineData(0x12, "Non-EU Players In Squad")]
    [InlineData(0x29, "U23 Players In Squad")]
    [InlineData(0x3E, "Loan Players In Match Squad")]
    [InlineData(0x40, "Goalkeepers On Bench")]
    [InlineData(0xCF, "Loan Players From Same Club In Match Squad")]
    [InlineData(0x12F, "Youth Or Amateur Players In Squad")]
    [InlineData(0x134, "First-Year Senior Players In Squad")]
    public void A_named_rule_resolves_to_the_cheat_engine_name(int code, string expected) =>
        Assert.Equal(expected, CompetitionRuleNames.Name(code));

    [Theory]
    [InlineData(0x1B3)]
    [InlineData(0x1D3)]
    [InlineData(0xA3)]
    public void A_code_the_table_does_not_name_stays_unknown(int code) =>
        Assert.Null(CompetitionRuleNames.Name(code));
}
