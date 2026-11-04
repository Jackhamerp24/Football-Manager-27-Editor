using Fm27Editor.Core.Clubs;

namespace Fm27Editor.App.Tests;

/// <summary>
/// Expected strings are the FMRTE 27 Club → Relationships screens (Manchester United and Sheffield United, 3/11/2026).
/// FMRTE sorts by descending type code and shows unknown types as "Unknown (code)".
/// </summary>
public class ClubRelationshipLabelsTests
{
    static ClubRelationship Row(ClubRelationshipKind kind, int type, string? name, int value, int reason = 0, bool permanent = false) =>
        new(0x1000, kind, type, name, value, reason, permanent, "", 0x1000, 0x1010, 0x1010);

    [Fact]
    public void Rows_are_ordered_like_FMRTE_by_descending_type_with_ram_order_inside_a_type()
    {
        // RAM order of Sheffield United; FMRTE shows Unknown (29), Rival x7, Dislikes x3, Likes x5.
        var ram = new[]
        {
            Row(ClubRelationshipKind.LikesPerson, 1, "Chris Wilder", 100), Row(ClubRelationshipKind.Rival, 5, "Sheffield Wednesday", 100),
            Row(ClubRelationshipKind.DislikesPerson, 2, "Steve Bruce", 75), Row(ClubRelationshipKind.Other, 29, "Sheffield United", 8),
            Row(ClubRelationshipKind.Rival, 5, "Leeds United", 90), Row(ClubRelationshipKind.LikesPerson, 1, "Alan Kelly", 50),
        };
        var table = ClubRelationshipLabels.BuildTable(ClubRelationshipLabels.Ordered(ram));
        Assert.Equal(
            ["Unknown (29)|Sheffield United|8", "Rival|Sheffield Wednesday|100", "Rival|Leeds United|90",
             "Dislikes Person|Steve Bruce|75", "Likes Person|Chris Wilder|100", "Likes Person|Alan Kelly|50"],
            table.Rows.Select(r => string.Join('|', r.Cells.Select(c => c.Text))));
        Assert.Equal(["Type", "Name", "Value"], table.Columns.Select(c => c.Header));
        Assert.True(table.HasReader);
    }

    [Fact]
    public void Reason_names_follow_FMRTE_dropdowns()
    {
        Assert.Equal(["No Reason", "Local", "Competitive", "Historic"], ClubRelationshipLabels.Reasons(ClubRelationshipKind.Rival));
        Assert.Equal(["No Reason", "Player", "Manager", "Backroom Staff", "Chairperson", "Player And Manager", "Owner", "Club Staff", "Founder"],
            ClubRelationshipLabels.Reasons(ClubRelationshipKind.LikesPerson));
        Assert.Equal("Historic", ClubRelationshipLabels.ReasonName(ClubRelationshipKind.Rival, 3));
        Assert.Equal("Manager", ClubRelationshipLabels.ReasonName(ClubRelationshipKind.LikesPerson, 2));
        Assert.Equal("Founder", ClubRelationshipLabels.ReasonName(ClubRelationshipKind.LikesPerson, 8));
        Assert.Equal("77", ClubRelationshipLabels.ReasonName(ClubRelationshipKind.LikesPerson, 77));
        // The writer accepts exactly the codes the dropdown offers.
        Assert.Equal(4, ClubRelationshipWriter.ReasonCount(ClubRelationshipKind.Rival));
        Assert.Equal(9, ClubRelationshipWriter.ReasonCount(ClubRelationshipKind.LikesPerson));
    }
}
