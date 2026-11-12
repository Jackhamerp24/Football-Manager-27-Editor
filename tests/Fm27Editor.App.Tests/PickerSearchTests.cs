namespace Fm27Editor.App.Tests;

/// <summary>
/// The picker windows must accept the same shorthand the universal search accepts, so "man utd" finds Manchester
/// United through its short name and a club id finds the club by number.
/// </summary>
public sealed class PickerSearchTests
{
    [Theory]
    [InlineData("man utd", true)]
    [InlineData("Man Utd", true)]
    [InlineData("manchester united", true)]
    [InlineData("manchester", true)]
    [InlineData("utd", true)]
    [InlineData("united", true)]
    [InlineData("real madrid", false)]
    [InlineData("zzz", false)]
    public void Short_name_and_words_match_like_the_universal_search(string query, bool expected) =>
        Assert.Equal(expected, PickerSearch.Hit(query, "Manchester United", "Man Utd", 680));

    [Theory]
    [InlineData("680", true)]
    [InlineData("681", false)]
    public void Numeric_query_matches_the_id(string query, bool expected) =>
        Assert.Equal(expected, PickerSearch.Hit(query, "Manchester United", "Man Utd", 680));

    [Theory]
    [InlineData("le havre", true)]
    [InlineData("havre", true)]
    [InlineData("le havre ac", true)]
    public void Leading_article_is_tolerated(string query, bool expected) =>
        Assert.Equal(expected, PickerSearch.Hit(query, "Havre AC", null, 1));

    [Fact]
    public void Empty_query_lists_everything() => Assert.True(PickerSearch.Hit("", "Anything"));
}
