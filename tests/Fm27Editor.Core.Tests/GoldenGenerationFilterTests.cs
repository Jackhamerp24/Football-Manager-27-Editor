using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// How the filter combines its rows. The rule under test: rows inside one category are OR-ed, categories are AND-ed,
/// and a uid is only a category of its own — it never overrules the club, competition, nation or continent beside it.
/// </summary>
public class GoldenGenerationFilterTests
{
    static readonly ClubInfo Reds = GoldenGenerationFixtures.Club("Reds", 0x3000_0000);
    static readonly ClubInfo Blues = GoldenGenerationFixtures.Club("Blues", 0x3000_1000);
    static readonly NationInfo England = GoldenGenerationFixtures.Nation("England", 765, 0x4000_0000);
    static readonly NationInfo France = GoldenGenerationFixtures.Nation("France", 50, 0x4000_1000);

    static GoldenGenerationFilter Filter(GoldenGenerationFilterItem[] items) => new(items);

    static GoldenGenerationMatch Evaluate(GoldenGenerationFilter filter, PlayerRecord player, IGoldenGenerationFacts? facts = null) =>
        filter.Evaluate(player, facts);

    [Fact]
    public void An_empty_filter_lets_every_player_through() =>
        Assert.Equal(GoldenGenerationMatch.Yes, GoldenGenerationFilter.None.Evaluate(GoldenGenerationFixtures.Player(1)));

    [Fact]
    public void Rows_of_the_same_category_are_ored()
    {
        var clubs = Filter([new(GoldenGenerationFilterKind.Club, Name: "Reds"), new(GoldenGenerationFilterKind.Club, Name: "Blues")]);
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(clubs, GoldenGenerationFixtures.Player(1, club: Reds)));
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(clubs, GoldenGenerationFixtures.Player(2, club: Blues)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(clubs, GoldenGenerationFixtures.Player(3, club: GoldenGenerationFixtures.Club("Greens", 0x3000_2000))));
    }

    [Fact]
    public void Different_categories_are_anded()
    {
        var both = Filter([new(GoldenGenerationFilterKind.Club, Name: "Reds"), new(GoldenGenerationFilterKind.Nation, Name: "England")]);
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(both, GoldenGenerationFixtures.Player(1, club: Reds, nation: England)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(both, GoldenGenerationFixtures.Player(2, club: Reds, nation: France)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(both, GoldenGenerationFixtures.Player(3, club: Blues, nation: England)));
    }

    [Fact]
    public void A_uid_row_does_not_overrule_the_rows_next_to_it()
    {
        var withUid = Filter([new(GoldenGenerationFilterKind.Uid, Id: 1), new(GoldenGenerationFilterKind.Club, Name: "Reds")]);
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(withUid, GoldenGenerationFixtures.Player(1, club: Reds)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(withUid, GoldenGenerationFixtures.Player(1, club: Blues)));   // uid matches, club does not
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(withUid, GoldenGenerationFixtures.Player(2, club: Reds)));   // club matches, uid does not
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(withUid, GoldenGenerationFixtures.Player(3, club: Blues)));
    }

    [Fact]
    public void Data_the_player_does_not_expose_is_unknown_rather_than_a_match_or_a_miss()
    {
        var clubs = Filter([new(GoldenGenerationFilterKind.Club, Name: "Reds")]);
        Assert.Equal(GoldenGenerationMatch.Unknown, Evaluate(clubs, GoldenGenerationFixtures.Player(1)));   // no club at all

        var both = Filter([new(GoldenGenerationFilterKind.Club, Name: "Reds"), new(GoldenGenerationFilterKind.Nation, Name: "England")]);
        Assert.Equal(GoldenGenerationMatch.Unknown, Evaluate(both, GoldenGenerationFixtures.Player(1, club: Reds)));   // club yes, nation unknown
        // One category already saying no is enough, even while another cannot be decided.
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(both, GoldenGenerationFixtures.Player(2, club: Blues)));
    }

    [Fact]
    public void Rows_can_name_an_id_an_address_or_a_name()
    {
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(Filter([new(GoldenGenerationFilterKind.Nation, Id: 765)]), GoldenGenerationFixtures.Player(1, nation: England)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(Filter([new(GoldenGenerationFilterKind.Nation, Id: 765)]), GoldenGenerationFixtures.Player(2, nation: France)));
        Assert.Equal(GoldenGenerationMatch.Unknown, Evaluate(Filter([new(GoldenGenerationFilterKind.Nation, Id: 765)]),
            GoldenGenerationFixtures.Player(3, nation: GoldenGenerationFixtures.Nation("England", null, 0x4000_2000))));
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(Filter([new(GoldenGenerationFilterKind.Club, Address: 0x3000_0000)]), GoldenGenerationFixtures.Player(4, club: Reds)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(Filter([new(GoldenGenerationFilterKind.Club, Address: 0x3000_0000)]), GoldenGenerationFixtures.Player(5, club: Blues)));
    }

    [Fact]
    public void A_continent_row_needs_a_source_of_facts_and_uses_it()
    {
        var europe = Filter([new(GoldenGenerationFilterKind.Continent, Name: "Europe")]);
        var player = GoldenGenerationFixtures.Player(1, nation: England);
        Assert.Equal(GoldenGenerationMatch.Unknown, Evaluate(europe, player));                    // no facts given
        Assert.Equal(GoldenGenerationMatch.Yes, Evaluate(europe, player, new StubFacts("Europe", null)));
        Assert.Equal(GoldenGenerationMatch.No, Evaluate(europe, player, new StubFacts("Asia", null)));
        Assert.Equal(GoldenGenerationMatch.Unknown, Evaluate(europe, player, new StubFacts(null, null)));   // continent not readable
    }

    [Theory]
    [InlineData(GoldenGenerationFilterKind.Club)]
    [InlineData(GoldenGenerationFilterKind.Competition)]
    [InlineData(GoldenGenerationFilterKind.Nation)]
    public void A_row_that_names_nothing_is_refused(GoldenGenerationFilterKind kind) =>
        Assert.Throws<ArgumentException>(() => Filter([new(kind)]));

    [Fact]
    public void A_uid_row_needs_a_positive_id_and_a_continent_row_needs_a_name()
    {
        Assert.Throws<ArgumentException>(() => Filter([new(GoldenGenerationFilterKind.Uid, Name: "Player")]));
        Assert.Throws<ArgumentException>(() => Filter([new(GoldenGenerationFilterKind.Uid, Id: 0)]));
        Assert.Throws<ArgumentException>(() => Filter([new(GoldenGenerationFilterKind.Continent, Address: 0x4000_0000)]));
        Assert.Throws<ArgumentException>(() => Filter([new(GoldenGenerationFilterKind.Club, Id: 7)]));   // a club row has no id to compare
    }
}
