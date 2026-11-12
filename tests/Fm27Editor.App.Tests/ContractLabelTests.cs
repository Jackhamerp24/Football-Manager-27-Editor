using Fm27Editor.App;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

/// <summary>Names as FMRTE 26's contract screen showed them for Shea Lacey and Acerbi's offer screen in FM27.</summary>
public class ContractLabelTests
{
    [Theory]
    [InlineData(32, "Appearance Fee")]
    [InlineData(39, "Assist Bonus")]
    [InlineData(38, "Unused Substitute Fee")]
    [InlineData(13, "Wage After Reaching Club League Games")]
    [InlineData(22, "Optional Contract Extension By Club")]
    [InlineData(11, "One-Year Extension After League Games (Final Season)")]
    [InlineData(34, "Clean Sheet Bonus")]
    [InlineData(99, "Clause 99")]
    public void Term_names(short type, string expected) => Assert.Equal(expected, ContractLabels.Term(type));

    [Fact]
    public void Parameters_are_games_or_years_and_option_clauses_have_no_value()
    {
        Assert.Equal("Games", ContractLabels.TermParameter(13));
        Assert.Equal("Years", ContractLabels.TermParameter(22));
        Assert.Null(ContractLabels.TermParameter(32));
        Assert.False(ContractLabels.TermHasValue(22));
        Assert.True(ContractLabels.TermHasValue(13));
    }

    [Fact]
    public void Playing_time_and_type_read_like_the_game()
    {
        Assert.Equal("Breakthrough Prospect", ContractLabels.Of(PlayingTime.BreakthroughProspect));
        Assert.Equal("Impact Sub", ContractLabels.Of(PlayingTime.ImpactSub));
        Assert.Equal("Any", ContractLabels.Of(PlayingTime.Unset));
        Assert.Equal("Full Time Contract", ContractLabels.Of(ContractType.FullTime));
        Assert.Equal("Youth Contract", ContractLabels.Of(ContractType.Youth));
    }

    /// <summary>
    /// As FMRTE 27 lists them: Arribas 50% (Almería), Uche 15% signed for £429,515 (Getafe), Marcos Luna 42,951 after 5,
    /// Horta until 30/6/2029, Cooper's Sky Bet Championship bonus (Sheffield United, competition index 8).
    /// </summary>
    [Fact]
    public void Transfer_clauses_show_percentage_or_amount_and_progress()
    {
        var sellOn = new TransferClause(1, TransferClauseKind.SellOn, 67277465, null, null, 50, null, null, null, null, null, null);
        var uche = sellOn with { Percentage = 15, SignedFor = 429515 };
        var apps = new TransferClause(2, TransferClauseKind.AppearanceFee, 2000111004, null, 42951, null, null, 5, 0, null, null, null);
        var bonus = new TransferClause(3, TransferClauseKind.CompetitionBonus, 55082342, null, 429515, null, null, null, null, 52, null, new DateOnly(2029, 6, 30));
        var cooper = bonus with { CompetitionIndex = 8, Competition = new(0, "Sky Bet Championship", null), Ends = null };

        Assert.Equal("Sell-On", ContractLabels.Of(sellOn.Kind));
        Assert.Equal("Appearance Fee", ContractLabels.Of(apps.Kind));
        Assert.Equal("Competition Bonus", ContractLabels.Of(bonus.Kind));
        Assert.Equal("50%", ContractLabels.Amount(sellOn));
        Assert.Equal("42,951", ContractLabels.Amount(apps));
        Assert.Equal("0 / 5 apps", ContractLabels.Detail(apps));
        Assert.Equal("Competition 52  until 2029-06-30", ContractLabels.Detail(bonus));
        Assert.Equal("Sky Bet Championship", ContractLabels.Detail(cooper));
        Assert.Equal("", ContractLabels.Detail(sellOn));
        Assert.Equal("signed for 429,515", ContractLabels.Detail(uche));
    }

    /// <summary>
    /// Bryan Mbeumo's clauses with Brentford as FM27's own screen lists them (15/12/2026): "Goals Money £500K … after
    /// another 6 league goals" is the record with condition 3, 14 of 20; "Appearance Money £400K … after another 13
    /// league appearances" has condition 0, 47 of 60. FMRTE 27 calls both "Appearance Money".
    /// </summary>
    [Fact]
    public void Appearance_fee_records_counting_goals_are_goals_fees()
    {
        var goals = new TransferClause(4, TransferClauseKind.AppearanceFee, 1, null, 500000, null, null, 20, 14, null, null, null)
            { Condition = TransferClauseCondition.LeagueGoals };
        var apps = goals with { Amount = 400000, Appearances = 60, AppearancesSoFar = 47, Condition = TransferClauseCondition.LeagueAppearances };
        var unknown = goals with { Condition = 2 };

        Assert.Equal(3, TransferClauseCondition.LeagueGoals);
        Assert.Equal("Goals Fee", ContractLabels.Of(goals));
        Assert.Equal("14 / 20 goals", ContractLabels.Detail(goals));
        Assert.Equal("Appearance Fee", ContractLabels.Of(apps));
        Assert.Equal("47 / 60 apps", ContractLabels.Detail(apps));
        Assert.Equal("Appearance Fee (type 2)", ContractLabels.Of(unknown));
        Assert.Equal("14 / 20", ContractLabels.Detail(unknown));
        Assert.Equal("Appearance Fee", ContractLabels.Of(goals with { Condition = null }));
    }

    [Fact]
    public void Clause_save_input_distinguishes_unchanged_cleared_and_populated_fields()
    {
        Assert.Equal(new ClauseSaveValues(40, false, null, null),
            ClauseSaveInput.Parse(TransferClauseKind.SellOn, true, "40", false, null));
        Assert.Equal(new ClauseSaveValues(null, true, null, null),
            ClauseSaveInput.Parse(TransferClauseKind.SellOn, false, null, true, " "));
        Assert.Equal(new ClauseSaveValues(50_000, true, 10, null),
            ClauseSaveInput.Parse(TransferClauseKind.AppearanceFee, true, "50000", true, "10"));
        Assert.Equal(new ClauseSaveValues(1_000_000, true, null, new DateOnly(2030, 6, 30)),
            ClauseSaveInput.Parse(TransferClauseKind.CompetitionBonus, true, "1000000", true, "2030-06-30"));
        Assert.Equal(new ClauseSaveValues(null, true, null, null),
            ClauseSaveInput.Parse(TransferClauseKind.CompetitionBonus, false, null, true, ""));
        Assert.Throws<FormatException>(() => ClauseSaveInput.Parse(TransferClauseKind.AppearanceFee, true, "50000", true, ""));
        Assert.Throws<FormatException>(() => ClauseSaveInput.Parse(TransferClauseKind.CompetitionBonus, false, null, true, "30/06/2030"));
    }
}
