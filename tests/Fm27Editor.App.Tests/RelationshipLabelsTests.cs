using Fm27Editor.App;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

public class RelationshipLabelsTests
{
    /// <summary>Reasons FMRTE 27 showed for Mbappé's rows (raw codes from RAM).</summary>
    [Theory]
    [InlineData(RelationshipKind.LikesPerson, 2, "Sibling")]   // Ethan Mbappé
    [InlineData(RelationshipKind.LikesPerson, 5, "Teammate")]  // Michael Olise, Neymar
    [InlineData(RelationshipKind.LikesPerson, 6, "Manager")]   // Frédéric Barilaro
    [InlineData(RelationshipKind.LikesPerson, 9, "Idol")]      // Thierry Henry
    [InlineData(RelationshipKind.LikesTeam, 1, "Supporter")]   // Real Madrid
    [InlineData(RelationshipKind.LikesTeam, 2, "Player")]      // Monaco
    [InlineData(RelationshipKind.LikesPerson, 8, "Backroom Staff")] // FMRTE wrote 8 for Backroom Staff (Kane → Saka)
    [InlineData(RelationshipKind.LikesTeam, 3, "Manager")]           // FMRTE wrote 3 for Manager (Kane → Tottenham)
    [InlineData(RelationshipKind.LikesTeam, 4, "Backroom Staff")]    // last entry of FMRTE's team dropdown
    [InlineData(RelationshipKind.LikesTeam, 5, "Code 5")]
    public void Reason_names_match_FMRTE(RelationshipKind kind, int code, string name) => Assert.Equal(name, RelationshipLabels.ReasonName(kind, code));
}
